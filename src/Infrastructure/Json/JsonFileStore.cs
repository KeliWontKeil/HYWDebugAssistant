using System.Text.Json;
using System.Text.Json.Nodes;

namespace HYWDebugAssistant.Infrastructure.Json;

/// <summary>
/// 文本 JSON 文件的读写（L0 横切支撑）：**可读、可 diff、原子写**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>以 <see cref="JsonObject"/> 为载体 → **未知字段天然保留**（更新已知字段时不动其余内容）；</item>
///   <item><b>原子写</b>：先写同目录临时文件，再 <see cref="File.Replace(string, string, string?)"/> 替换，避免半写损坏；</item>
///   <item>读取失败**不抛异常**：返回原因，由调用方决定"用默认值 + 报 Issue"（不阻断启动）。</item>
/// </list>
/// </remarks>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // 不转义中文与常见符号，保持文件可读可 diff
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>读取 JSON 对象；文件不存在或内容非法时返回原因。</summary>
    public static bool TryReadObject(string filePath, out JsonObject? root, out string? error)
    {
        root = null;
        error = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "文件路径为空";
            return false;
        }

        if (!File.Exists(filePath))
        {
            error = "文件不存在";
            return false;
        }

        try
        {
            var text = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "文件内容为空";
                return false;
            }

            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;

            if (root is null)
            {
                error = "文件根节点不是 JSON 对象";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"{ex.GetType().Name}：{ex.Message}";
            return false;
        }
    }

    /// <summary>原子写入 JSON 对象（同目录临时文件 → 替换）。</summary>
    public static bool TryWriteObjectAtomic(string filePath, JsonObject root, out string? error)
    {
        ArgumentNullException.ThrowIfNull(root);
        error = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "文件路径为空";
            return false;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = filePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, root.ToJsonString(WriteOptions));

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"{ex.GetType().Name}：{ex.Message}";

            // 清理临时文件（失败不影响结论）
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // 忽略清理失败
            }

            return false;
        }
    }
}
