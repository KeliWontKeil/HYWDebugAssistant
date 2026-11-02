using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Persistence;

/// <summary>
/// 组合件的保存与加载服务（L4 持久化面）：文件 IO ↔ 运行时 ↔ Issue 报告。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>保存</b>：捕获现场 → 合并未知字段 → **原子写**；</item>
///   <item><b>加载</b>：读文件（大小/深度上限）→ 解析（记录级容错）→ **Schema 版本判定** → 降级式应用；</item>
///   <item><b>拒绝即保护</b>：不可解析 / 版本过新 → 只报 Issue，**不写任何内容**（§9.3）；</item>
///   <item><b>组合件永不自动执行代码</b>：加载只重建声明结构（§14）。</item>
/// </list>
/// </remarks>
public sealed class CompositeService
{
    private readonly ModuleRuntime _runtime;
    private readonly ModuleRegistry _registry;
    private readonly IIssueSink _issues;

    /// <summary>
    /// **当前屏幕缩放**（供旧格式（&lt; v3）布局的单位归一用；≤0 或没设 ⇒ 1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>静态</b>是有意的：它是**进程级事实**（这台机器当前那块屏的缩放），不是某个组合件或某个服务的属性；
    /// 而 <c>Core</c> 拿不到屏幕 ⇒ 由 UI 层在窗口建好之后设置一次（见 <c>App</c>）。
    /// </para>
    /// <para>
    /// 它只影响**读旧文件**那一次换算：v3 起的组合件里四个数已经是 DIP，本值无用。
    /// 旧文件若在与保存时不同缩放的屏上打开，尺寸会差一个比例（一次性近似，第一次保存后固化）。
    /// </para>
    /// </remarks>
    /// <summary>创建服务。</summary>
    /// <param name="runtime">运行时（捕获与加载的目标）。</param>
    /// <param name="registry">模块注册表（加载时按标识 + 版本重锚）。</param>
    /// <param name="issues">框架侧上报口（缺省用运行时自带的 Issue 中心）。</param>
    public CompositeService(ModuleRuntime runtime, ModuleRegistry registry, IIssueSink? issues = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(registry);

        _runtime = runtime;
        _registry = registry;
        _issues = issues ?? runtime.Issues;
    }

    // ---------------------------------------------------------------- 保存

    /// <summary>保存组合件（物化选中范围为 1 个文件）。</summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="name">组合件名（空 = 取文件名）。</param>
    /// <param name="instanceIds">选中实例（为空 = **当前全部**）。</param>
    public CommandResult Save(string filePath, string? name = null, IReadOnlyList<string>? instanceIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var compositeName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(filePath)
            : name;

        var composite = _runtime.CaptureComposite(compositeName, instanceIds, out var notices);

        if (!CompositeStore.TryWrite(filePath, CompositeSerializer.ToJsonObject(composite), out var error))
        {
            var failure = CoreText.Get("core.composite.saveFailed", error);
            _issues.Report($"composite:{compositeName}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Error, failure));

            return CommandResult.Fail([failure]);
        }

        return CommandResult.Ok(notices);
    }

    /// <summary>
    /// 保存为**组合件包**（`&lt;dir&gt;/composite.json` + `assets/`）。
    /// </summary>
    /// <param name="packageDirectory">包目录（不存在则创建）。</param>
    /// <param name="name">组合名（空 = 目录名）。</param>
    /// <param name="instanceIds">选中实例（空 = 全部）。</param>
    public CommandResult SavePackage(
        string packageDirectory,
        string? name = null,
        IReadOnlyList<string>? instanceIds = null,
        WorkspacePlacementView? workspacePlacement = null,
        string? documentFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var directory = Path.GetFullPath(packageDirectory);
        var compositeName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : name;

        try
        {
            _ = Directory.CreateDirectory(directory);
            var composite = _runtime.CaptureComposite(compositeName, instanceIds, out var notices);
            composite.WorkspacePlacement = workspacePlacement;
            var document = string.IsNullOrWhiteSpace(documentFileName)
                ? CompositeFileStore.DocumentPathFor(directory)
                : Path.Combine(directory, documentFileName);

            if (!CompositeStore.TryWrite(document, CompositeSerializer.ToJsonObject(composite), out var error))
            {
                var failure = CoreText.Get("core.composite.saveFailed", error);
                _issues.Report($"composite:{compositeName}", new IssueReport(
                    IssueCategory.ConfigurationError, IssueSeverity.Error, failure));
                return CommandResult.Fail([failure]);
            }

            var assets = Path.Combine(directory, "assets");
            if (composite.Assets.Count > 0)
            {
                _ = Directory.CreateDirectory(assets);
                foreach (var (hash, png) in composite.Assets)
                {
                    var path = Path.Combine(assets, hash + ".png");
                    if (!File.Exists(path))
                    {
                        File.WriteAllBytes(path, png);
                    }
                }
            }

            return CommandResult.Ok(notices);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var failure = CoreText.Get("core.composite.saveFailed", ex.Message);
            _issues.Report($"composite:{compositeName}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Error, failure));
            return CommandResult.Fail([failure]);
        }
    }

    // ---------------------------------------------------------------- 加载

    /// <summary>加载组合件（实例化进当前工作区；异常加载为**降级式**）。</summary>
    /// <param name="filePath">组合件文件路径。</param>
    public CommandResult Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        filePath = Directory.Exists(filePath)
            ? CompositeFileStore.DocumentPathFor(filePath)
            : filePath;

        if (!CompositeStore.TryRead(filePath, out var root, out var outcome, out var readError))
        {
            // 语法非法 = **不可解析**（与"读不到"区分开：面向用户的原因必须准确）
            return Reject(filePath, CoreText.Get(
                "core.composite.load.prefixedError",
                CoreText.Get(outcome == CompositeReadOutcome.Malformed
                    ? "core.composite.load.malformedPrefix"
                    : "core.composite.load.readPrefix"),
                readError));
        }

        if (!CompositeSerializer.TryParse(root!, out var composite, out var warnings, out var parseFailure))
        {
            return Reject(filePath, CoreText.Get("core.composite.parseFailed", parseFailure));
        }

        LoadAssets(composite!, filePath);
        LastLoadedWorkspacePlacement = composite!.WorkspacePlacement;

        foreach (var warning in warnings)
        {
            _issues.Report($"composite:{Describe(composite)}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Warning,
                CoreText.Get("core.composite.warningWithRemainder", warning)));
        }

        var result = _runtime.LoadComposite(composite, ResolveDefinition);

        var notices = new List<string>(result.Notices) { Summarize(result) };
        return CommandResult.Ok(notices);
    }

    /// <summary>为占位实例指定替代模块（"可手动指定替代版本"，§9.3）。</summary>
    /// <param name="instanceId">占位实例 ID。</param>
    /// <param name="moduleIdLiteral">替代模块标识字面量。</param>
    public CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleIdLiteral);

        if (!ResolveDefinition(moduleIdLiteral, out var definition, out var reason) || definition is null)
        {
            return CommandResult.Fail([reason ?? CoreText.Get("core.composite.moduleNotRegistered", moduleIdLiteral)]);
        }

        if (!_runtime.TryResolvePlaceholder(instanceId, definition, out var notices, out var failure))
        {
            return CommandResult.Fail([failure ?? CoreText.Get("core.composite.placeholderResolveFailed")]);
        }

        return CommandResult.Ok(notices);
    }

    // ---------------------------------------------------------------- 检查

    /// <summary>
    /// **只读检查**：实例数 + 缺失/不兼容的模块（不实例化、不迁移、不写文件）。
    /// </summary>
    /// <param name="filePath">组合文件路径。</param>
    /// <summary>最近一次加载的工作区外壳矩形（普通组合件为 null）。</summary>
    public WorkspacePlacementView? LastLoadedWorkspacePlacement { get; private set; }

    public CompositeInspection Inspect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!CompositeStore.TryRead(filePath, out var root, out var outcome, out var readError))
        {
            var prefix = CoreText.Get(outcome == CompositeReadOutcome.Malformed
                ? "core.composite.inspect.malformedPrefix"
                : "core.composite.inspect.readPrefix");
            return new CompositeInspection(0, [], CoreText.Get("core.composite.load.prefixedError", prefix, readError));
        }

        if (!CompositeSerializer.TryParse(root!, out var composite, out _, out var parseFailure))
        {
            return new CompositeInspection(0, [], CoreText.Get("core.composite.parseFailedShort", parseFailure));
        }

        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in composite!.Instances)
        {
            if (!ResolveDefinition(instance.ModuleId, out _, out _))
            {
                _ = missing.Add(instance.ModuleId);
            }
        }

        return new CompositeInspection(composite.Instances.Count, [.. missing], null);
    }

    /// <summary>从包目录读壁纸资产；缺失/非法只丢该资产并记 Warning，不影响组合其余内容。</summary>
    private void LoadAssets(Composite composite, string documentPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var record in composite.Instances)
        {
            if (record.Wallpaper is not { Length: > 0 } reference)
            {
                continue;
            }

            var hash = Path.GetFileName(reference);
            if (hash.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                hash = hash[..^4];
            }

            if (hash.Length != 64 || hash.Any(ch => !Uri.IsHexDigit(ch)))
            {
                _issues.Report($"composite:{composite.Name}", new IssueReport(
                    IssueCategory.ConfigurationError, IssueSeverity.Warning,
                    CoreText.Get("core.composite.wallpaperInvalidReference", reference)));
                continue;
            }

            var path = Path.Combine(directory, "assets", hash + ".png");
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length is > 0 and <= 32L * 1024 * 1024)
                {
                    assets[hash] = File.ReadAllBytes(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _issues.Report($"composite:{composite.Name}", new IssueReport(
                    IssueCategory.ConfigurationError, IssueSeverity.Warning,
                    CoreText.Get("core.composite.wallpaperReadFailed", path, ex.Message)));
            }
        }

        composite.Assets = assets;
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>拒绝加载：报 Issue 并**保持原文件不变**（绝不写入）。</summary>
    private CommandResult Reject(string filePath, string reason)
    {
        var message = CoreText.Get("core.composite.loadRejected", reason, Path.GetFullPath(filePath));
        _issues.Report("composite:load", new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Error, message));
        return CommandResult.Fail([message]);
    }


    /// <summary>按标识解析定义（未安装 → 缺失态 + 原因；没有版本判定）。</summary>
    private bool ResolveDefinition(string moduleIdLiteral, out ModuleDefinition? definition, out string? reason)
    {
        definition = null;

        if (!ModuleId.TryParse(moduleIdLiteral, out var moduleId))
        {
            reason = CoreText.Get("core.composite.moduleIdInvalid", moduleIdLiteral);
            return false;
        }

        if (!_registry.TryResolve(moduleId, out definition) || definition is null)
        {
            reason = CoreText.Get("core.composite.definitionMissing", moduleIdLiteral);
            return false;
        }

        reason = null;
        return true;
    }

    private static string Summarize(CompositeLoadResult result)
    {
        var summary = CoreText.Get(
            "core.composite.loadSummary",
            result.InstancesAdded,
            result.PlaceholdersCreated,
            result.NetworksRestored,
            result.UnresolvedMembers);

        return result.PlaceholdersCreated == 0 && result.UnresolvedMembers == 0
            ? summary
            : summary + CoreText.Get("core.composite.loadSummaryHint");
    }

    private static string Describe(Composite composite) =>
        composite.Name.Length > 0 ? composite.Name : CoreText.Get("core.composite.unnamed");
}

/// <summary>组合文件的只读检查结果（供列表标注）。</summary>
/// <param name="ModuleCount">实例数。</param>
/// <param name="MissingModules">缺失/不兼容的模块标识（去重）。</param>
/// <param name="Error">无法读取/解析的原因；空 = 可读取。</param>
public sealed record CompositeInspection(
    int ModuleCount,
    IReadOnlyList<string> MissingModules,
    string? Error);
