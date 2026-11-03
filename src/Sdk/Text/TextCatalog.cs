using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using HYWDebugAssistant.Sdk.Resources;

namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **界面文本表**：按 `resources/i18n/&lt;程序集简名&gt;/` 下的数据文件解析并缓存文案；
/// 当前语言 + 回退链 + 缺失可见。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>表住哪里</b>：拥有者 = 程序集简名；根与拥有者目录由 <see cref="ResourceLayout"/>
///     统一解析（与图标表同构的两级投递：框架资源根、程序集旁）。基础表 <c>&lt;culture&gt;.json</c>，
///     可选分片 <c>&lt;culture&gt;.&lt;area&gt;.json</c>；同一目录里**基础表排在最前**，其余按文件名序合并，
///     **先者胜**（分片出现重复键 ⇒ 该分片整份跳过并报诊断）；</item>
///   <item><b>根解析（惰性一次）</b>：<see cref="UseRoot"/> → <c>&lt;AppContext.BaseDirectory&gt;/resources/i18n</c> →
///     从 BaseDirectory 向上找到含 <c>HYWDebugAssistant.slnx</c> 的仓库根 + <c>resources/i18n</c> → 找不到任何表；</item>
///   <item><b>模块无需注册目录</b>：包内文本就在实现程序集旁的 `i18n/&lt;程序集简名&gt;/`，
///     加载后自动命中第二级；框架拥有者住在 `resources/i18n/`，命中第一级；</item>
///   <item><b>坏表降级</b>：读取失败 / JSON 坏 / 版本不认识 / 分片键重复 ⇒ **跳过该文件**并经
///     <see cref="Diagnostics"/> 报一条，界面不崩；**整张表目录缺失**（登记目录或 `&lt;根&gt;/&lt;拥有者&gt;` 不存在，
///     且确实发生了查表）⇒ **每个拥有者报一次**；缺键不报（界面的标记已经可见，缺失由审计在构建期拦）；</item>
///   <item><b>回退链</b>：精确 culture → 语言中立（<c>zh</c>）→ <c>zh-Hans</c> → 可见缺失标记 <c>⟦key⟧</c>。
///     缺表与缺键都**不返回空白**：空白会被当成"这个控件天生没有文字"，排查时完全看不见；</item>
///   <item><b>语言只定一次</b>：<see cref="Apply"/> 设 UI 文化，取值方在载入界面时解析一次即可
///     （设置项承诺"切换后重启完全生效"，所以不做运行期重新绑定）；</item>
///   <item><b>只动 UI 文化</b>：数值/日期格式仍按用户的区域设置（<c>CurrentCulture</c> 不碰）。</item>
/// </list>
/// </remarks>
public static class TextCatalog
{
    /// <summary>缺失键的可见标记（左右双括号）。</summary>
    private const string MissingLeft = "\u27E6";

    private const string MissingRight = "\u27E7";


    /// <summary>已解析的表缓存；键里的代号随根变更递增（旧根下迟到的加载不会污染新根）。</summary>
    private static readonly ConcurrentDictionary<(string Owner, string Language, int Generation), IReadOnlyDictionary<string, string>> _tables = new();

    /// <summary>已经报过"整张表目录缺失"的拥有者（每个拥有者每个代号一次；根变更后代号变了自然再报）。</summary>
    private static readonly ConcurrentDictionary<(string Owner, int Generation), bool> _missingDirectories = new();

    private static readonly object _gate = new();

    /// <summary>当前语言；初值 = 进程启动时的 UI 文化。</summary>
    private static CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    /// <summary>已发现的受支持语言（缓存；根或目录变更后重扫）。</summary>
    private static string[]? _supportedLanguages;

    /// <summary>上面那份语言清单对应的资源代号（代号变了就要重扫）。</summary>
    private static int _supportedLanguagesGeneration = -1;

    /// <summary>**当前语言**发生变化后触发（同一语言重复 <see cref="Apply"/> 不触发）。</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// 文本表加载诊断（坏文件 / 版本不认识 / 分片键重复）；缺省 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 只报**数据/落点问题**：这一份表被跳过，或整张表目录缺失；缺键不报（界面上的
    /// <c>⟦key⟧</c> 暴露）。生产由组合根接到 Issue 中心，并按消息去重。
    /// </remarks>
    public static Action<string>? Diagnostics { get; set; }

    /// <summary>
    /// 文本表里实际存在的语言（含兜底的中文简体；序数排序）。
    /// </summary>
    /// <remarks>
    /// 判定口径是"表目录里出现过这个语言"，所以它随出厂文本包提供的内容自动生长，
    /// 不需要第二份"支持哪些语言"的清单。
    /// </remarks>
    public static IReadOnlyList<string> SupportedLanguages
    {
        get
        {
            lock (_gate)
            {
                var generation = ResourceLayout.Generation(ResourceKinds.Text);

                if (_supportedLanguages is null || _supportedLanguagesGeneration != generation)
                {
                    _supportedLanguages = ScanLanguages();
                    _supportedLanguagesGeneration = generation;
                }

                return _supportedLanguages;
            }
        }
    }

    /// <summary>
    /// **显式指定文本包根**（宿主、测试与预览可用）；传 <c>null</c> 恢复默认解析。
    /// </summary>
    /// <param name="path">
    /// 资源根下 `i18n/` 目录的路径（其下是 `&lt;程序集简名&gt;/` 目录）；<c>null</c>/空 = 清掉显式根。
    /// </param>
    /// <remarks>设定后清空表缓存与语言扫描结果（下一次取值按新根重读）。</remarks>
    public static void UseRoot(string? path)
    {
        ResourceLayout.UseRoot(ResourceKinds.Text, path);

        lock (_gate)
        {
            _tables.Clear();
            _missingDirectories.Clear();
            _supportedLanguages = null;
            _supportedLanguagesGeneration = -1;
        }
    }

    /// <summary>
    /// **设定界面语言**并切换 UI 文化。
    /// </summary>
    /// <param name="language">
    /// 语言标识；<see cref="TextLanguages.System"/>（或空）= 跟随系统 UI 文化。
    /// </param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>`system` ⇒ 系统 UI 文化落到**受支持语言**，落到不了就用中文简体；</item>
    ///   <item>未知 / 不受支持的语言 ⇒ 中文简体（缺表也显示中文，不是空白）；</item>
    ///   <item>只写 <c>DefaultThreadCurrentUICulture</c> 与当前线程的 <c>CurrentUICulture</c>，
    ///     **不动 <c>CurrentCulture</c>** —— 界面语言与数值格式是两件事。</item>
    /// </list>
    /// </remarks>
    public static void Apply(string? language)
    {
        var requested = string.IsNullOrWhiteSpace(language) ? TextLanguages.System : language.Trim();

        bool changed;
        lock (_gate)
        {
            var culture = ResolveCulture(requested);
            changed = !string.Equals(_uiCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase);
            _uiCulture = culture;

            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        // 事件在锁外发：订阅方若在回调里再调 Apply，不该卡在同一把锁上。
        if (changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>取一条文案（缺失 ⇒ 可见缺失标记）。</summary>
    /// <param name="owner">表的拥有者程序集（通常是这份文本所属的界面程序集）。</param>
    /// <param name="key">键（<c>area.group.element.role</c>）。</param>
    public static string Get(Assembly owner, string key)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return TryGet(owner, key, out var text) ? text : Missing(key);
    }

    /// <summary>取一条**带占位符**的文案，并按 <c>CurrentCulture</c> 格式化。</summary>
    /// <param name="owner">表的拥有者程序集。</param>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数（模板里的 <c>{0}</c> 等）。</param>
    /// <remarks>格式化走 <c>CurrentCulture</c>：数字与日期随用户的区域设置，界面文案随 UI 语言。</remarks>
    public static string Get(Assembly owner, string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var template = Get(owner, key);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }

    /// <summary>尝试取一条文案（不看缺失标记，供"有就用、没有就走别的路径"的调用方）。</summary>
    /// <param name="owner">表的拥有者程序集。</param>
    /// <param name="key">键。</param>
    /// <param name="text">命中的文案（未命中 ⇒ 空串）。</param>
    /// <returns>是否命中。</returns>
    public static bool TryGet(Assembly owner, string key, out string text)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var language in FallbackChain(_uiCulture))
        {
            if (GetTable(owner, language).TryGetValue(key, out var value))
            {
                text = value;
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    /// <summary>缺键时的可见标记（<c>⟦key⟧</c>；宁可在界面上露出键名，也不要一片空白）。</summary>
    /// <param name="key">未命中的键。</param>
    /// <remarks>测试与界面判断"文本是否解析到"时用它，不要在别处再抄一份标记格式。</remarks>
    public static string MissingValue(string key) => Missing(key);

    /// <summary>一段文本是否是缺键标记。</summary>
    /// <param name="text">要判断的文本（<see langword="null"/> ⇒ <see langword="false"/>）。</param>
    /// <returns>形如 <c>⟦key⟧</c> 时为 <see langword="true"/>。</returns>
    public static bool IsMissing(string? text) =>
        text is not null
        && text.StartsWith(MissingLeft, StringComparison.Ordinal)
        && text.EndsWith(MissingRight, StringComparison.Ordinal);

    private static string Missing(string key) => string.Concat(MissingLeft, key, MissingRight);

    // ---------------------------------------------------------------- 语言与文化

    /// <summary>把请求值落成一个**受支持**的 UI 文化。</summary>
    /// <param name="requested">语言标识或 <see cref="TextLanguages.System"/>。</param>
    private static CultureInfo ResolveCulture(string requested)
    {
        CultureInfo requestedCulture;

        if (string.Equals(requested, TextLanguages.System, StringComparison.OrdinalIgnoreCase))
        {
            // 系统 UI 文化：进程启动时的值；之后本类会把它收敛到受支持语言。
            requestedCulture = CultureInfo.CurrentUICulture;
        }
        else if (!TryGetCulture(requested, out requestedCulture))
        {
            return CultureInfo.GetCultureInfo(TextLanguages.ZhHans);
        }

        return TryResolveSupported(requestedCulture, out var supported)
            ? CultureInfo.GetCultureInfo(supported)
            : CultureInfo.GetCultureInfo(TextLanguages.ZhHans);
    }

    private static bool TryGetCulture(string name, out CultureInfo culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(name);
            return true;
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
            return false;
        }
    }

    /// <summary>在这条文化链上找第一个**真的有表**的语言。</summary>
    /// <param name="culture">候选文化。</param>
    /// <param name="language">命中的语言标识。</param>
    private static bool TryResolveSupported(CultureInfo culture, out string language)
    {
        foreach (var candidate in FallbackChain(culture))
        {
            if (SupportedLanguages.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }

        language = TextLanguages.ZhHans;
        return false;
    }

    /// <summary>查表顺序：精确 culture → 语言中立 → 中文简体（去重，空的与 <c>iv</c> 跳过）。</summary>
    /// <param name="culture">当前语言对应的文化。</param>
    private static List<string> FallbackChain(CultureInfo culture)
    {
        var chain = new List<string>(3);
        AddCandidate(chain, culture.Name);
        AddCandidate(chain, culture.TwoLetterISOLanguageName);
        AddCandidate(chain, TextLanguages.ZhHans);
        return chain;
    }

    private static void AddCandidate(List<string> chain, string name)
    {
        if (name.Length == 0
            || string.Equals(name, "iv", StringComparison.OrdinalIgnoreCase)
            || chain.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        chain.Add(name);
    }

    /// <summary>扫表目录里的语言名（拥有者目录 + 登记目录），得到受支持语言（含兜底语言）。</summary>
    private static string[] ScanLanguages()
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TextLanguages.ZhHans };

        if (ResourceLayout.Root(ResourceKinds.Text) is { } root)
        {
            foreach (var ownerDirectory in SafeDirectories(root))
            {
                CollectLanguages(ownerDirectory, languages);
            }
        }

        var result = languages.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    /// <summary>收集某个表目录里出现过的语言名（只看 `&lt;culture&gt;[.&lt;area&gt;].json` 的文件名首段）。</summary>
    /// <param name="directory">表目录。</param>
    /// <param name="languages">语言集合（就地追加）。</param>
    private static void CollectLanguages(string directory, HashSet<string> languages)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var separator = name.IndexOf('.', StringComparison.Ordinal);
            if (separator > 0)
            {
                _ = languages.Add(name[..separator]);
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ---------------------------------------------------------------- 表加载

    private static IReadOnlyDictionary<string, string> GetTable(Assembly owner, string language)
    {
        var ownerName = owner.GetName().Name;
        if (string.IsNullOrEmpty(ownerName))
        {
            return EmptyTable;
        }

        var generation = ResourceLayout.Generation(ResourceKinds.Text);
        return _tables.GetOrAdd((ownerName, language, generation), key => LoadTable(owner, key.Language));
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyTable =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>合并某个拥有者某个语言的全部表文件（基础表 + 分片，先者胜）。</summary>
    /// <param name="owner">拥有者程序集（表目录由简名决定）。</param>
    /// <param name="language">语言标识。</param>
    private static Dictionary<string, string> LoadTable(Assembly owner, string language)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerName = owner.GetName().Name ?? string.Empty;

        var directory = ResourceLayout.Directory(ResourceKinds.Text, owner);
        if (directory is null)
        {
            // 整张表缺失：**每个拥有者报一次**（否则界面满屏 ⟦key⟧ 却没有原因）。
            // 缺键不走这里：标记已经可见，构建期由审计拦。
            ReportMissingDirectory(ownerName, ExpectedDirectory(owner, ownerName));
            return table;
        }

        string[] files;
        try
        {
            files = [.. Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)];
        }
        catch (IOException)
        {
            return table;
        }
        catch (UnauthorizedAccessException)
        {
            return table;
        }

        var baseFileName = language + ".json";
        var ordered = files
            .Where(path => IsLanguageFile(Path.GetFileName(path), language))
            .OrderBy(path => string.Equals(Path.GetFileName(path), baseFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        foreach (var file in ordered)
        {
            if (!TryReadEntries(file, table, out var entries, out var failure))
            {
                Diagnostics?.Invoke($"{file}：{failure}（已跳过该文件；相关键显示 ⟦key⟧）");
                continue;
            }

            foreach (var entry in entries)
            {
                table[entry.Key] = entry.Value;
            }
        }

        return table;
    }

    /// <summary>两级都找不到时的"期望目录"（用于诊断定位）。</summary>
    /// <param name="owner">拥有者程序集。</param>
    /// <param name="ownerName">拥有者简名。</param>
    private static string ExpectedDirectory(Assembly owner, string ownerName)
    {
        if (ResourceLayout.Root(ResourceKinds.Text) is { } root)
        {
            return Path.Combine(root, ownerName);
        }

        if (owner.Location is { Length: > 0 } location
            && Path.GetDirectoryName(location) is { Length: > 0 } assemblyDirectory)
        {
            return Path.Combine(assemblyDirectory, ResourceKinds.Text, ownerName);
        }

        return Path.Combine("<resources-root unresolved>", ownerName);
    }

    /// <summary>报告"拥有者的整张表目录不存在"（每个拥有者每个代号一次）。</summary>
    /// <param name="owner">拥有者（程序集简名）。</param>
    /// <param name="expected">期望目录（两级候选里优先展示的那个）。</param>
    private static void ReportMissingDirectory(string owner, string expected)
    {
        var key = (owner, ResourceLayout.Generation(ResourceKinds.Text));
        if (!_missingDirectories.TryAdd(key, true))
        {
            return;
        }

        Diagnostics?.Invoke(
            $"文本表目录缺失：拥有者 {owner}；期望目录 {expected}（该拥有者的键显示 ⟦key⟧）");
    }

    /// <summary>读一份表文件并校验形状；失败给出**可行动**的原因（调用方负责报诊断）。</summary>
    /// <param name="path">表文件路径。</param>
    /// <param name="existing">本目录里先读到的表（用于判分片重复键）。</param>
    /// <param name="entries">本文件的条目（成功时）。</param>
    /// <param name="failure">失败原因（成功时为 <c>null</c>）。</param>
    /// <returns>本文件是否能整份并入。</returns>
    private static bool TryReadEntries(
        string path,
        Dictionary<string, string> existing,
        out Dictionary<string, string> entries,
        out string? failure)
    {
        entries = new Dictionary<string, string>(StringComparer.Ordinal);
        failure = null;

        byte[] bytes;
        try
        {
            var length = new FileInfo(path).Length;
            if (length > ResourceLayout.MaxTableFileBytes)
            {
                failure = $"超过单文件体积上限（{length} 字节 > {ResourceLayout.MaxTableFileBytes} 字节）";
                return false;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = "读取失败：" + exception.Message;
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            failure = "JSON 解析失败：" + exception.Message;
            return false;
        }

        using (document)
        {
            if (!TryGetEntries(document.RootElement, out var element))
            {
                failure = "entries 形状不对";
                return false;
            }

            foreach (var entry in element.EnumerateObject())
            {
                var value = entry.Value.GetString();
                if (value is null)
                {
                    failure = $"键 '{entry.Name}' 的值不是字符串";
                    return false;
                }

                if (!entries.TryAdd(entry.Name, value))
                {
                    failure = $"文件内重复键 '{entry.Name}'";
                    return false;
                }

                if (existing.ContainsKey(entry.Name))
                {
                    failure = $"分片重复键 '{entry.Name}'（前一份表已提供，先者胜）";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>文件名是不是这个语言的表（基础表或分片）。</summary>
    /// <param name="file">文件名（如 <c>zh-Hans.settings.json</c>）。</param>
    /// <param name="language">语言标识。</param>
    private static bool IsLanguageFile(string file, string language)
    {
        if (string.Equals(file, language + ".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return file.StartsWith(language + ".", StringComparison.OrdinalIgnoreCase)
            && file.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
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
}
