using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace HYWDebugAssistant.Sdk.Resources;

/// <summary>
/// **一个图标资源**：SVG 里的路径数据 + 画布尺寸（`viewBox`）。
/// </summary>
/// <remarks>
/// 这里刻意只放**数据**（字符串 + 数值），不放 `Geometry`：`Sdk` 是零依赖契约层，
/// 不能引 Avalonia；把路径数据合成 `Geometry` 是界面层（`Sdk.Ui.Controls.Icon`）的事。
/// 一个图标允许多条 `&lt;path&gt;`（有些图形分片画），它们共用同一个填充规则。
/// </remarks>
/// <param name="PathData">每条 `&lt;path d&gt;` 的原始路径数据。</param>
/// <param name="ViewBoxWidth">画布宽（SVG `viewBox` 的第三个数）。</param>
/// <param name="ViewBoxHeight">画布高（SVG `viewBox` 的第四个数）。</param>
/// <param name="EvenOdd">填充规则是否 `evenodd`（否则按 `nonzero`）。</param>
public sealed record IconAsset(
    IReadOnlyList<string> PathData,
    double ViewBoxWidth,
    double ViewBoxHeight,
    bool EvenOdd)
{
    /// <summary>
    /// 是否**线条图标**：所有可用路径的有效填充都是 `none`。
    /// </summary>
    /// <remarks>
    /// 线条图标由界面层只描边、不填充；混合填充/描边的文件仍按填充图标处理。
    /// </remarks>
    public bool StrokeOnly { get; init; }
}

/// <summary>
/// **界面图标表**：每个拥有者一份 `icons.json`（键 → 相对路径的 `.svg`），
/// 目录布局与文本表同构（见 <see cref="ResourceLayout"/>）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>图标是显式文件</b>：manifest 只登记"键 → 相对路径"，图形在 `.svg` 里
///     （Fluent UI System Icons，MIT）；换图标只换文件，不改代码也不改界面；</item>
///   <item><b>每 owner 自己的表，没有跨 owner 回退</b>：别人的键在这里就是缺键，
///     不让"恰好同名"把图标来源变成隐式共享；</item>
///   <item><b>缺表与缺键都不返回空白</b>：缺键返回键名本身——界面上看得见是哪个键没配；</item>
///   <item><b>SVG 只支持子集</b>：`viewBox` + `&lt;path d&gt;`（+ `fill-rule`）。`&lt;use&gt;` / 脚本 /
///     外部引用 / `transform` 一律跳过并报诊断——不引第三方 SVG 引擎，也不执行文件里的任何东西；</item>
///   <item><b>缓存按 (owner, 代号)</b>：<see cref="UseRoot"/> 让代号递增，旧根下算出的表自然失效。</item>
/// </list>
/// </remarks>
public static class IconCatalog
{

    /// <summary>每个拥有者的清单文件名（一个拥有者一张表）。</summary>
    private const string ManifestFileName = "icons.json";

    /// <summary>图标键的前缀（与文本键共用命名纪律，但键空间不重叠）。</summary>
    private const string KeyPrefix = "icon.";

    /// <summary>认的图形扩展名：只读显式 SVG 文件。</summary>
    private const string SvgExtension = ".svg";

    /// <summary>SVG 没写 `viewBox` 时的兜底画布（Fluent 20px 图标的标准尺寸）。</summary>
    private const double DefaultViewBoxSize = 20d;

    private static readonly ConcurrentDictionary<(string Owner, int Generation), IReadOnlyDictionary<string, IconAsset>> _tables = new();
    private static readonly ConcurrentDictionary<(string Owner, int Generation), bool> _missingTables = new();

    private static readonly IReadOnlyDictionary<string, IconAsset> EmptyTable =
        new Dictionary<string, IconAsset>(StringComparer.Ordinal);

    /// <summary>
    /// 图标表加载诊断（坏清单 / 坏 SVG / 缺表）；缺省 <c>null</c>。
    /// </summary>
    /// <remarks>与 <see cref="ResourceLayout.Diagnostics"/> 是同一份存储：资源层只有一条共享告警通道。</remarks>
    public static Action<string>? Diagnostics
    {
        get => ResourceLayout.Diagnostics;
        set => ResourceLayout.Diagnostics = value;
    }

    /// <summary>
    /// 尝试取一个图标资源（路径数据 + 画布尺寸）。
    /// </summary>
    /// <param name="owner">表的拥有者程序集（简名即目录名）。</param>
    /// <param name="key">键（以 <c>icon.</c> 开头）。</param>
    /// <param name="asset">命中的资源（未命中 ⇒ <see langword="null"/>）。</param>
    /// <returns>是否命中。</returns>
    public static bool TryGet(Assembly owner, string key, out IconAsset? asset)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        asset = null;

        if (!GetTable(owner).TryGetValue(key, out var value))
        {
            return false;
        }

        asset = value;
        return true;
    }

    /// <summary>取一个图标资源（缺失 ⇒ <see langword="null"/>；界面用 <see cref="MissingValue"/> 显示键名）。</summary>
    /// <param name="owner">表的拥有者程序集。</param>
    /// <param name="key">键。</param>
    public static IconAsset? Get(Assembly owner, string key)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return TryGet(owner, key, out var asset) ? asset : null;
    }

    /// <summary>缺键时的可见标记（就是键本身：界面上一眼能看出缺的是哪个键）。</summary>
    /// <param name="key">未命中的键。</param>
    public static string MissingValue(string key) => key;

    /// <summary>
    /// **显式指定图标表根**（宿主、测试与预览可用）；传 <c>null</c> 恢复默认解析。
    /// </summary>
    /// <param name="path">资源根下 `icons/` 目录的路径；<c>null</c>/空 = 清掉显式根。</param>
    public static void UseRoot(string? path) => ResourceLayout.UseRoot(ResourceKinds.Icons, path);

    // ---------------------------------------------------------------- 表加载

    private static IReadOnlyDictionary<string, IconAsset> GetTable(Assembly owner)
    {
        var ownerName = owner.GetName().Name;
        if (string.IsNullOrEmpty(ownerName))
        {
            return EmptyTable;
        }

        var generation = ResourceLayout.Generation(ResourceKinds.Icons);
        return _tables.GetOrAdd((ownerName, generation), _ => LoadTable(owner, ownerName));
    }

    /// <summary>读某个拥有者的清单与全部图标文件；缺表返回空表并报一次诊断。</summary>
    /// <param name="owner">拥有者程序集。</param>
    /// <param name="ownerName">拥有者简名。</param>
    private static Dictionary<string, IconAsset> LoadTable(Assembly owner, string ownerName)
    {
        var table = new Dictionary<string, IconAsset>(StringComparer.Ordinal);
        var directory = ResourceLayout.Directory(ResourceKinds.Icons, owner);

        if (directory is null)
        {
            ReportMissingOnce(
                ownerName,
                $"图标表目录缺失：拥有者 {ownerName}；期望目录 {ExpectedDirectory(owner, ownerName)}"
                + "（该拥有者的键显示键名）");
            return table;
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            ReportMissingOnce(
                ownerName,
                $"图标清单缺失：拥有者 {ownerName}；期望文件 {manifestPath}（该拥有者的键显示键名）");
            return table;
        }

        foreach (var (key, relativePath) in ReadManifest(manifestPath))
        {
            var svgPath = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!TryReadAsset(svgPath, out var asset, out var failure))
            {
                ReportEntry(manifestPath, key, $"{relativePath}：{failure}");
                continue;
            }

            if (asset is not null && !table.TryAdd(key, asset))
            {
                Diagnostics?.Invoke($"{manifestPath}：文件内重复键 '{key}'（已跳过后一条）");
            }
        }

        return table;
    }

    /// <summary>两级都找不到时的"期望目录"（用于诊断定位）。</summary>
    /// <param name="owner">拥有者程序集。</param>
    /// <param name="ownerName">拥有者简名。</param>
    private static string ExpectedDirectory(Assembly owner, string ownerName)
    {
        if (ResourceLayout.Root(ResourceKinds.Icons) is { } root)
        {
            return Path.Combine(root, ownerName);
        }

        if (owner.Location is { Length: > 0 } location
            && Path.GetDirectoryName(location) is { Length: > 0 } assemblyDirectory)
        {
            return Path.Combine(assemblyDirectory, ResourceKinds.Icons, ownerName);
        }

        return Path.Combine("<resources-root unresolved>", ownerName);
    }

    /// <summary>报告"拥有者的整张表不存在"（每个拥有者每个代号一次）。</summary>
    /// <param name="ownerName">拥有者简名。</param>
    /// <param name="message">诊断文本。</param>
    private static void ReportMissingOnce(string ownerName, string message)
    {
        var key = (ownerName, ResourceLayout.Generation(ResourceKinds.Icons));
        if (_missingTables.TryAdd(key, true))
        {
            Diagnostics?.Invoke(message);
        }
    }

    /// <summary>读清单：键 → 相对路径；坏条目只跳过该条（结构错误整份跳过）。</summary>
    /// <param name="path">清单路径。</param>
    private static List<(string Key, string RelativePath)> ReadManifest(string path)
    {
        var entries = new List<(string Key, string RelativePath)>();

        byte[] bytes;
        try
        {
            var length = new FileInfo(path).Length;
            if (length > ResourceLayout.MaxTableFileBytes)
            {
                ReportFile(path, $"超过单文件体积上限（{length} 字节 > {ResourceLayout.MaxTableFileBytes} 字节）");
                return entries;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportFile(path, "读取失败：" + exception.Message);
            return entries;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            ReportFile(path, "JSON 解析失败：" + exception.Message);
            return entries;
        }

        using (document)
        {
            if (!TryGetEntries(document.RootElement, out var element))
            {
                ReportFile(path, "entries 形状不对");
                return entries;
            }

            foreach (var entry in element.EnumerateObject())
            {
                if (!entry.Name.StartsWith(KeyPrefix, StringComparison.Ordinal))
                {
                    ReportEntry(path, entry.Name, $"不以 {KeyPrefix} 开头");
                    continue;
                }

                var value = entry.Value.GetString();
                if (value is null)
                {
                    ReportFile(path, $"键 '{entry.Name}' 的值不是字符串");
                    return entries;
                }

                if (!TryNormalizeRelativePath(value, out var normalized, out var failure))
                {
                    ReportEntry(path, entry.Name, failure!);
                    continue;
                }

                entries.Add((entry.Name, normalized));
            }
        }

        return entries;
    }

    /// <summary>
    /// 校验清单里的相对路径：必须相对、不得跳出 owner 目录、扩展名必须是 `.svg`。
    /// </summary>
    /// <param name="value">清单里的原值。</param>
    /// <param name="normalized">规范化后的相对路径（`/` 分隔）。</param>
    /// <param name="failure">拒绝原因（成功时为 <see langword="null"/>）。</param>
    private static bool TryNormalizeRelativePath(string value, out string normalized, out string? failure)
    {
        normalized = string.Empty;
        failure = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            failure = "路径为空";
            return false;
        }

        var candidate = value.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/') || candidate.Contains(':'))
        {
            failure = $"路径 '{value}' 不能是绝对路径";
            return false;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
        {
            failure = $"路径 '{value}' 不得包含 '.' / '..' 段";
            return false;
        }

        if (!string.Equals(Path.GetExtension(candidate), SvgExtension, StringComparison.OrdinalIgnoreCase))
        {
            failure = $"路径 '{value}' 必须以 {SvgExtension} 结尾";
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    /// <summary>读一个 SVG 文件并提取子集（`viewBox` + `&lt;path d&gt;` + `fill-rule`）。</summary>
    /// <param name="path">SVG 路径。</param>
    /// <param name="asset">解析结果（成功时）。</param>
    /// <param name="failure">失败原因（成功时为 <see langword="null"/>）。</param>
    private static bool TryReadAsset(string path, out IconAsset? asset, out string? failure)
    {
        asset = null;
        failure = null;

        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = "读取失败：" + exception.Message;
            return false;
        }

        if (!info.Exists)
        {
            failure = "文件不存在";
            return false;
        }

        if (info.Length > ResourceLayout.MaxTableFileBytes)
        {
            failure = $"超过单文件体积上限（{info.Length} 字节 > {ResourceLayout.MaxTableFileBytes} 字节）";
            return false;
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                // 图标是外部可替换的数据：禁止 DTD 与外部实体，绝不解析文件里指向别处的东西。
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            using var reader = XmlReader.Create(path, settings);
            document = XDocument.Load(reader);
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            failure = "XML 解析失败：" + exception.Message;
            return false;
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            failure = "根元素不是 <svg>";
            return false;
        }

        var (width, height) = ReadViewBox(root);
        var paths = new List<string>();
        var evenOdd = false;
        var strokeOnly = true;
        var unsupported = new List<string>();

        foreach (var element in root.Descendants())
        {
            var name = element.Name.LocalName;

            if (string.Equals(name, "path", StringComparison.OrdinalIgnoreCase))
            {
                if (element.Attribute("transform") is not null)
                {
                    unsupported.Add("<path transform>");
                    continue;
                }

                var data = element.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                if (string.Equals(element.Attribute("fill-rule")?.Value, "evenodd", StringComparison.OrdinalIgnoreCase))
                {
                    evenOdd = true;
                }

                // 有效填充按 path 自身 → 祖先继承；全部 none 才是线条图标。
                var fill = EffectiveFill(element);
                if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
                {
                    strokeOnly = false;
                }

                paths.Add(data);
            }
            else if (name is "use" or "script" or "image" or "text" or "foreignObject")
            {
                unsupported.Add($"<{name}>");
            }
        }

        if (paths.Count == 0)
        {
            failure = "没有可用的 <path d>";
            return false;
        }

        asset = new IconAsset(paths, width, height, evenOdd)
        {
            StrokeOnly = strokeOnly,
        };

        if (unsupported.Count > 0)
        {
            Diagnostics?.Invoke(
                $"{path}：跳过不支持的构造 {string.Join("、", unsupported.Distinct(StringComparer.Ordinal))}");
        }

        return true;
    }

    /// <summary>求某个 path 的有效填充：自身 `fill` 优先，缺省时沿祖先继承。</summary>
    /// <param name="element">路径元素。</param>
    private static string? EffectiveFill(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Attribute("fill") is { Value: var value })
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>读 `viewBox`（四段）；缺失/坏值退回 `width`/`height`，再退回 20×20。</summary>
    /// <param name="root">SVG 根元素。</param>
    private static (double Width, double Height) ReadViewBox(XElement root)
    {
        var viewBox = root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = viewBox.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
                && width > 0
                && height > 0)
            {
                return (width, height);
            }
        }

        var widthAttribute = root.Attribute("width")?.Value;
        var heightAttribute = root.Attribute("height")?.Value;

        if (double.TryParse(widthAttribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth)
            && double.TryParse(heightAttribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeight)
            && parsedWidth > 0
            && parsedHeight > 0)
        {
            return (parsedWidth, parsedHeight);
        }

        return (DefaultViewBoxSize, DefaultViewBoxSize);
    }

    /// <summary>表结构校验：`entries` 必须是对象。</summary>
    /// <param name="root">表根元素。</param>
    /// <param name="entries">条目表。</param>
    private static bool TryGetEntries(JsonElement root, out JsonElement entries)
    {
        entries = default;

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("entries", out entries)
            && entries.ValueKind == JsonValueKind.Object;
    }

    private static void ReportFile(string path, string failure) =>
        Diagnostics?.Invoke($"{path}：{failure}（已跳过该文件；相关键显示键名）");

    private static void ReportEntry(string path, string key, string failure) =>
        Diagnostics?.Invoke($"{path}：键 '{key}' {failure}（已跳过该条目）");
}
