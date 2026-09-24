using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Infrastructure.Json;

namespace HYWDebugAssistant.Core.Persistence;

/// <summary>组合件读取结果（**失败原因分类**：面向用户的原因必须准确，不能一律"读不了"）。</summary>
public enum CompositeReadOutcome
{
    /// <summary>读取成功。</summary>
    Success = 0,

    /// <summary>文件不存在。</summary>
    NotFound = 1,

    /// <summary>超过单文件大小上限。</summary>
    TooLarge = 2,

    /// <summary>超过结构深度上限。</summary>
    TooDeep = 3,

    /// <summary>语法非法（**不可解析**）。</summary>
    Malformed = 4,

    /// <summary>文件不可读（占用/权限/IO）。</summary>
    Unavailable = 5,
}

/// <summary>
/// 组合件文件的读写：**大小/深度上限 + 原子写 + 拒绝即保护原文件**（R4-6）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>上限</b>（§14 资源耗尽护栏）：单文件 ≤ <see cref="MaxFileSizeBytes"/>（16 MB）、结构深度 ≤ <see cref="MaxStructureDepth"/>（8 层）；
///     超限 = **拒绝**而非截断（"拒绝而非猜测"）；</item>
///   <item><b>原子写</b>：同目录临时文件 → <see cref="File.Replace(string, string, string?)"/>，防半写损坏（§9.2）；</item>
///   <item><b>保护原文件</b>：解析失败/版本过新时**不写任何内容**——加载绝不覆盖用户文件；</item>
///   <item>读写失败一律**返回原因不抛异常**（面向用户的失败路径）。</item>
/// </list>
/// </remarks>
public static class CompositeStore
{
    /// <summary>单文件大小上限（字节）。</summary>
    public const long MaxFileSizeBytes = 16L * 1024 * 1024;

    /// <summary>结构深度上限（层）。</summary>
    public const int MaxStructureDepth = 8;

    private static readonly JsonDocumentOptions ReadDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 读取组合件文本为 JSON 对象（含上限校验）。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="root">读到的根对象。</param>
    /// <param name="outcome">失败分类。</param>
    /// <param name="error">失败原因（成功时为 <see langword="null"/>）。</param>
    /// <remarks>
    /// 本方法自行完成读取与解析（不经 <c>JsonFileStore</c>）：需要把"不存在 / 过大 / 过深 /
    /// 语法非法 / 不可读"分开报告，才能给出准确原因（"拒绝而非猜测"）。
    /// </remarks>
    public static bool TryRead(string filePath, out JsonObject? root, out CompositeReadOutcome outcome, out string? error)
    {
        root = null;
        error = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            outcome = CompositeReadOutcome.NotFound;
            error = "文件路径为空";
            return false;
        }

        var fullPath = Path.GetFullPath(filePath);

        long length;
        try
        {
            if (!File.Exists(fullPath))
            {
                outcome = CompositeReadOutcome.NotFound;
                error = "文件不存在";
                return false;
            }

            length = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome = CompositeReadOutcome.Unavailable;
            error = $"{ex.GetType().Name}：{ex.Message}";
            return false;
        }

        if (length > MaxFileSizeBytes)
        {
            outcome = CompositeReadOutcome.TooLarge;
            error = $"文件过大：{length} 字节（上限 {MaxFileSizeBytes} 字节）";
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome = CompositeReadOutcome.Unavailable;
            error = $"{ex.GetType().Name}：{ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            outcome = CompositeReadOutcome.Malformed;
            error = "文件内容为空";
            return false;
        }

        try
        {
            root = JsonNode.Parse(text, nodeOptions: null, documentOptions: ReadDocumentOptions) as JsonObject;
        }
        catch (JsonException ex)
        {
            outcome = CompositeReadOutcome.Malformed;
            error = ex.Message;
            return false;
        }

        if (root is null)
        {
            outcome = CompositeReadOutcome.Malformed;
            error = "文件根节点不是 JSON 对象";
            return false;
        }

        if (!TryMeasureDepth(root, out var depth))
        {
            outcome = CompositeReadOutcome.Malformed;
            error = "结构深度无法确定（文件不是合法 JSON 结构）";
            return false;
        }

        if (depth > MaxStructureDepth)
        {
            outcome = CompositeReadOutcome.TooDeep;
            error = $"结构深度超限：{depth} 层（上限 {MaxStructureDepth} 层）";
            return false;
        }

        outcome = CompositeReadOutcome.Success;
        return true;
    }

    /// <summary>原子写入组合件文本。</summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="root">要写入的根对象。</param>
    /// <param name="error">失败原因（成功时为 <see langword="null"/>）。</param>
    public static bool TryWrite(string filePath, JsonObject root, out string? error)
    {
        ArgumentNullException.ThrowIfNull(root);
        return JsonFileStore.TryWriteObjectAtomic(filePath, root, out error);
    }

    /// <summary>测量 JSON 结构深度（标量 = 1）。</summary>
    public static bool TryMeasureDepth(JsonNode node, out int depth)
    {
        ArgumentNullException.ThrowIfNull(node);

        try
        {
            depth = Measure(node);
            return true;
        }
        catch (InvalidOperationException)
        {
            depth = 0;
            return false;
        }
    }

    private static int Measure(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var objectDepth = 1;
                foreach (var (_, child) in obj)
                {
                    if (child is not null)
                    {
                        objectDepth = Math.Max(objectDepth, 1 + Measure(child));
                    }
                }

                return objectDepth;

            case JsonArray array:
                var arrayDepth = 1;
                foreach (var child in array)
                {
                    if (child is not null)
                    {
                        arrayDepth = Math.Max(arrayDepth, 1 + Measure(child));
                    }
                }

                return arrayDepth;

            default:
                return 1;
        }
    }
}
