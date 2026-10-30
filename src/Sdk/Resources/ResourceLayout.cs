using System.Reflection;
using System.Text.Json;

namespace HYWDebugAssistant.Sdk.Resources;

/// <summary>
/// **可外部修改的资源布局**：根解析、显式根、拥有者目录的两级投递、缓存代号。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>两套根，彼此不串</b>：**框架**拥有者只查程序目录的 <c>resources/&lt;kind&gt;/&lt;owner&gt;/</c>；
///     **模块**拥有者只查模块目录（<c>module.json</c> 旁边）的 <c>resources/&lt;kind&gt;/&lt;owner&gt;/</c>。
///     模块资源落在框架 <c>resources/</c> 下不算数，框架拥有者也不会去模块包里翻；</item>
///   <item><b>模块判定</b>：程序集目录里有 <c>module.json</c>，且要么清单的 <c>entryAssembly</c> 点名本程序集、
///     要么本拥有者的 <c>resources/&lt;kind&gt;/&lt;owner&gt;/</c> 就在清单旁边 ——
///     后者是多模块共享输出目录（测试 / 预览宿主）的实情：那里的 <c>module.json</c> 只能有一个；</item>
///   <item><b>框架根解析（按类别惰性一次）</b>：显式根 → <c>&lt;AppContext.BaseDirectory&gt;/resources/&lt;kind&gt;</c> →
///     从 BaseDirectory 向上找到含 <c>HYWDebugAssistant.slnx</c> 的仓库根 + <c>resources/&lt;kind&gt;</c> → 无；</item>
///   <item><b>显式根不是排他隔离</b>：显式根没有该拥有者目录时仍回退默认根——并行用例临时改根时，
///     框架自己的表不能因此消失（模块拥有者不受显式根影响：它的数据在包里）；</item>
///   <item><b>代号</b>：每次 <see cref="UseRoot"/> 递增一次，读取方用 (拥有者, 代号) 做缓存键，
///     旧根下算出的表自然失效，不需要跨类回调清缓存。</item>
/// </list>
/// </remarks>
public static class ResourceLayout
{
    /// <summary>出厂资源目录名（与 `StoragePaths.ResourcesDirectoryName` 同值；两层零依赖，靠审计钉住）。</summary>
    public const string ResourcesDirectoryName = "resources";

    /// <summary>模块清单文件名（与 `ModuleManifestReader.FileName` 同值；零依赖层自己写一份，靠审计钉住）。</summary>
    public const string ModuleManifestFileName = "module.json";

    /// <summary>仓库根的标志文件（用于从输出目录向上找开发期的资源根）。</summary>
    public const string RepositoryMarkerFile = "HYWDebugAssistant.slnx";

    /// <summary>单份表文件的体积上限（病态输入防护；正常表是几 KB）。</summary>
    public const long MaxTableFileBytes = 4L * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, KindState> States = new(StringComparer.Ordinal);

    /// <summary>
    /// 资源层共享告警（坏表 / 缺表）；缺省 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 只报**数据/落点问题**，不报关表本身：缺键靠界面上的可见标记暴露，构建期由审计拦。
    /// 文本表另有自己的 <c>TextCatalog.Diagnostics</c>（公开面不变），两类表因此可以各自接线。
    /// </remarks>
    public static Action<string>? Diagnostics { get; set; }

    /// <summary>某个类别的资源根（类型惰性解析；<c>null</c> = 找不到任何资源）。</summary>
    /// <param name="kind">资源类别（见 <see cref="ResourceKinds"/>）。</param>
    public static string? Root(string kind)
    {
        lock (Gate)
        {
            return ResolveRootLocked(kind);
        }
    }

    /// <summary>
    /// **显式指定某个类别的资源根**（宿主、测试与预览可用）；传 <c>null</c> 恢复默认解析。
    /// </summary>
    /// <param name="kind">资源类别。</param>
    /// <param name="path">资源根下 `<kind>/` 目录的路径；<c>null</c>/空 = 清掉显式根。</param>
    /// <remarks>设定后代号递增：所有以代号为缓存键的读取方都会重读（见类型备注）。</remarks>
    public static void UseRoot(string kind, string? path)
    {
        lock (Gate)
        {
            var state = State(kind);
            state.ExplicitRoot = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
            state.Root = null;
            state.RootResolved = false;
            state.Generation++;
        }
    }

    /// <summary>
    /// 某个拥有者在这个类别里的目录（模块 ⇒ 只查自己的包；框架 ⇒ 只查框架根）。
    /// </summary>
    /// <param name="kind">资源类别。</param>
    /// <param name="owner">拥有者程序集（简名即目录名）。</param>
    public static string? Directory(string kind, Assembly owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ownerName = owner.GetName().Name;
        if (string.IsNullOrEmpty(ownerName))
        {
            return null;
        }

        // 模块拥有者：只查模块包，**不回退**框架根（两边不串是这套布局的核心）。
        if (TryGetModuleDirectory(kind, owner, ownerName, out var moduleDirectory))
        {
            return moduleDirectory;
        }

        // 框架拥有者：显式根 → 产物 `resources/<kind>` → 仓库根 `resources/<kind>`，逐级找拥有者目录。
        // 逐级（而不是"取第一个存在的根"）是必要的：产物 `resources/` 可能只带了别的东西
        // （测试宿主会把模块包放进去），此时框架自己的表仍在仓库根。
        string? explicitRoot;

        lock (Gate)
        {
            explicitRoot = State(kind).ExplicitRoot;
        }

        foreach (var candidateRoot in new[]
                 {
                     explicitRoot,
                     ResolveProducedRoot(kind),
                     ResolveRepositoryRoot(kind),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidateRoot))
            {
                continue;
            }

            var frameworkDirectory = Path.Combine(candidateRoot, ownerName);
            if (System.IO.Directory.Exists(frameworkDirectory))
            {
                return frameworkDirectory;
            }
        }

        return null;
    }

    /// <summary>某个类别当前的缓存代号（每次 <see cref="UseRoot"/> 递增）。</summary>
    /// <param name="kind">资源类别。</param>
    public static int Generation(string kind)
    {
        lock (Gate)
        {
            return State(kind).Generation;
        }
    }

    // ---------------------------------------------------------------- 内部：根与目录

    /// <summary>类别状态（读写下都要求持有 <see cref="Gate"/>）。</summary>
    private sealed class KindState
    {
        public string? ExplicitRoot;

        public string? Root;

        public bool RootResolved;

        public int Generation;
    }

    private static KindState State(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (!States.TryGetValue(kind, out var state))
        {
            state = new KindState();
            States[kind] = state;
        }

        return state;
    }

    /// <summary>解析后的根（调用方持有 <see cref="Gate"/>）。</summary>
    private static string? ResolveRootLocked(string kind)
    {
        var state = State(kind);

        if (!state.RootResolved)
        {
            state.Root = state.ExplicitRoot ?? ResolveDefaultRoot(kind);
            state.RootResolved = true;
        }

        return state.Root;
    }

    /// <summary>默认根：输出目录的 `resources/&lt;kind&gt;` → 仓库根的 `resources/&lt;kind&gt;` → 无。</summary>
    private static string? ResolveDefaultRoot(string kind) =>
        ResolveProducedRoot(kind) ?? ResolveRepositoryRoot(kind);

    /// <summary>产物目录旁的根：<c>&lt;AppContext.BaseDirectory&gt;/resources/&lt;kind&gt;</c>（不存在 ⇒ null）。</summary>
    private static string? ResolveProducedRoot(string kind)
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        var produced = Path.Combine(baseDirectory, ResourcesDirectoryName, kind);
        return System.IO.Directory.Exists(produced) ? produced : null;
    }

    /// <summary>仓库根（从 BaseDirectory 向上找标志文件）下的 <c>resources/&lt;kind&gt;</c>（找不到 ⇒ null）。</summary>
    private static string? ResolveRepositoryRoot(string kind)
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        for (var current = new DirectoryInfo(baseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, RepositoryMarkerFile)))
            {
                return Path.Combine(current.FullName, ResourcesDirectoryName, kind);
            }
        }

        return null;
    }

    /// <summary>模块包 / 开发与测试输出侧的候选目录（程序集不在磁盘上 ⇒ false）。</summary>
    /// <param name="kind">资源类别。</param>
    /// <param name="owner">拥有者程序集。</param>
    /// <param name="ownerName">拥有者简名。</param>
    /// <param name="directory">模块里属于该拥有者的目录（判定为模块但目录缺失 ⇒ <see langword="null"/>）。</param>
    /// <returns>该拥有者是否是**模块**（是 ⇒ 调用方不得再查框架根）。</returns>
    private static bool TryGetModuleDirectory(string kind, Assembly owner, string ownerName, out string? directory)
    {
        directory = null;

        var location = owner.Location;
        if (string.IsNullOrWhiteSpace(location)
            || Path.GetDirectoryName(location) is not { Length: > 0 } assemblyDirectory)
        {
            return false;
        }

        var manifestPath = Path.Combine(assemblyDirectory, ModuleManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var moduleRoot = Path.Combine(assemblyDirectory, ResourcesDirectoryName);
        var candidate = Path.Combine(moduleRoot, kind, ownerName);
        var hasOwnDirectory = System.IO.Directory.Exists(candidate);

        // 共享输出目录（测试 / 预览宿主）里 `module.json` 只能有一个：
        // 清单点名本程序集，或本拥有者的目录就在清单旁边 —— 两者都算"这个拥有者是模块"。
        if (!NamesAssembly(manifestPath, ownerName) && !hasOwnDirectory)
        {
            return false;
        }

        directory = hasOwnDirectory ? candidate : null;
        return true;
    }

    /// <summary>清单的 `entryAssembly` 是否点名这个程序集（坏清单 ⇒ false，不抛）。</summary>
    /// <param name="manifestPath">清单路径。</param>
    /// <param name="ownerName">拥有者简名。</param>
    private static bool NamesAssembly(string manifestPath, string ownerName)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entryAssembly", out var entry)
                || entry.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return string.Equals(
                Path.GetFileNameWithoutExtension(entry.GetString()),
                ownerName,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
