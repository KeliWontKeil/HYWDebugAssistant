using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Binding;
using HYWDebugAssistant.Core.Changes;
// `Binding` 既是**命名空间**又是**类型名**（与 `ModuleRuntime` 同一手法）：显式别名，避免歧义
using RuntimeBinding = HYWDebugAssistant.Core.Binding.Binding;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Diagnostics;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Core.Workspace;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// 运行时服务：<b>同时实现查询面与命令面</b>，由组合根（<c>App</c>）注入 UI。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>UI **只经本服务**读写框架（Q1-4/C4-4）；</item>
///   <item>每个命令对应一次 <c>ChangeSet</c>：**原子应用或整体拒绝**（拒绝时零副作用）；</item>
///   <item>本类不缓存状态：查询每次都取**当下快照**（绑定集本就是不可变快照）。</item>
/// </list>
/// </remarks>
public sealed class RuntimeServices : IRuntimeQuery, IRuntimeCommand
{
    private readonly ModuleRuntime _runtime;
    private readonly SettingsService _settings;
    private readonly ModuleRegistry _registry = new();
    private readonly ModuleInstaller _installer;
    private readonly CompositeService _composites;

    /// <summary>模块标识字面量 → 装入它的来源根与包目录（诊断用）。</summary>
    private readonly Dictionary<string, ModulePackageOrigin> _modulePackages = new(StringComparer.Ordinal);

    /// <summary>绑定的速率计（键 = 绑定身份键）——**框架侧唯一的速率口径来源**。</summary>
    private readonly Dictionary<string, BindingRateMeters> _rateMeters = new(StringComparer.Ordinal);

    /// <summary>每 (实例, 口) 队列的速率计（键 = `实例.口名`）。</summary>
    private readonly Dictionary<string, BindingRateMeters> _portRateMeters = new(StringComparer.Ordinal);

    /// <summary>实例视图的缓存（键 = 现场/布局/壁纸修订号；命中即复用同一份对象）。</summary>
    private readonly Lock _viewCacheGate = new();
    private IReadOnlyList<Api.ModuleInstanceView>? _instancesCache;
    private (long Content, long Layout, long Wallpaper) _instancesKey;

    /// <summary>网络视图的缓存（键 = 现场修订号 + 绑定快照引用）。</summary>
    private IReadOnlyList<Api.NetworkView>? _networksCache;
    private (long Content, object? Bindings) _networksKey;

    /// <summary>同网络名不兼容端口的缓存（键 = 现场/布局修订号；只读查询）。</summary>
    private IReadOnlyList<Api.PortContractConflictView>? _portConflictsCache;
    private (long Content, long Layout) _portConflictsKey;

    /// <summary>布局视图的缓存（键 = 布局修订号）。</summary>
    private IReadOnlyList<Api.ModuleLayoutView>? _layoutCache;
    private long _layoutKey = -1;

    /// <summary>配置 Schema 缓存（键 = 实例；失效依据 = 配置变更号 + UI 语言）。</summary>
    private readonly Lock _schemaCacheGate = new();
    private readonly Dictionary<string, (long Version, string Language, long ChoicesRevision, ConfigurationSchemaView Schema)> _schemaCache =
        new(StringComparer.Ordinal);

    /// <summary>模块目录视图缓存（只随扫描/注册变化；UI 每帧刷新不再逐项建行）。</summary>
    private readonly Lock _catalogGate = new();
    private IReadOnlyList<ModuleCatalogEntryView>? _catalogCache;

    /// <summary>一条连接 / 一个口用的四个速率计（单元 · 搬运字节 · 有效载荷字节；样本档由单元档乘系数）。</summary>
    private sealed class BindingRateMeters
    {
        public RateMeter Unit { get; } = new();

        public RateMeter Bus { get; } = new();

        public RateMeter Payload { get; } = new();
    }

    private BindingRateMeters MetersOf(string key) =>
        _rateMeters.TryGetValue(key, out var meters)
            ? meters
            : _rateMeters[key] = new BindingRateMeters();

    private BindingRateMeters PortMetersOf(string key) =>
        _portRateMeters.TryGetValue(key, out var meters)
            ? meters
            : _portRateMeters[key] = new BindingRateMeters();

    /// <summary>上次来源根扫描的结果（`GetModuleSources` 的数据源）。</summary>
    private IReadOnlyList<ModuleSourceView> _moduleSources = [];

    /// <summary>本进程启动时刻（判定「包在本次启动后重新构建过」用）。</summary>
    private readonly DateTime _processStartUtc = DateTime.UtcNow;

    /// <summary>扫描期已报告过的“重复/内容变化”包（同一次扫描重复不刷 Issue）。</summary>
    private readonly HashSet<string> _reportedScanNotices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>一次性重启恢复快照路径（生产 = `StoragePaths.RestartFile`；测试可注入临时路径）。</summary>
    private readonly string _restartSnapshotPath;

    /// <summary>本次启动从工作区恢复的外壳矩形。</summary>
    private WorkspacePlacementView? _workspacePlacement;

    /// <summary>
    /// 本次启动的工作区**没有成功载入**。退出保存前必须先备份原文件，避免用空现场覆盖它。
    /// </summary>
    private bool _workspaceLoadFailed;

    /// <summary>创建服务。</summary>
    /// <param name="runtime">运行时。</param>
    /// <param name="settings">设置服务（缺省：`StoragePaths.SettingsFile`；未加载时使用默认值）。</param>
    /// <param name="restartSnapshotPath">重启恢复快照路径（缺省 = `StoragePaths.RestartFile`；测试可注入临时路径）。</param>
    public RuntimeServices(ModuleRuntime runtime, SettingsService? settings = null, string? restartSnapshotPath = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _runtime = runtime;
        _settings = settings ?? new SettingsService(runtime, StoragePaths.SettingsFile);
        _installer = new ModuleInstaller(_registry, runtime.Issues, _processStartUtc);
        _composites = new CompositeService(runtime, _registry, runtime.Issues);
        _restartSnapshotPath = Path.GetFullPath(restartSnapshotPath ?? StoragePaths.RestartFile);
    }

    /// <summary>底层运行时（组合根与测试用；UI 不应使用）。</summary>
    public ModuleRuntime Runtime => _runtime;

    /// <summary>模块注册表（组合根与测试用：装包后按标识加入实例；UI 不应使用）。</summary>
    public ModuleRegistry Registry => _registry;

    // ---------------------------------------------------------------- 命令面

    /// <inheritdoc />
    public CommandResult InstallModulePackage(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var result = _installer.Install(packageDirectory);
        if (result.Outcome != ModuleInstallOutcome.Failed)
        {
            // 已载入/重复/内容变化都不是失败：把真实状态告诉用户，不写 Error Issue。
            return CommandResult.Ok([result.Message]);
        }

        var recent = _runtime.Issues.Recent(1);
        return CommandResult.Fail([recent.Count > 0 ? recent[0].Issue.Message : CoreText.Get("core.services.modulePackageLoadFailed")]);
    }

    /// <summary>
    /// 扫描**模块来源根列表**并装入其中的模块包。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>按列表**顺序**逐根扫描；每根只扫**直接子目录**中含模块清单文件者（**不递归、不扫任意位置**）；</item>
    ///   <item>同 ID 重复 → **先到者胜**，后者记 **Info**（不刷 Warning）；</item>
    ///   <item>每个包独立处理，失败只记 Issue 并计入诊断结果；</item>
    ///   <item>结果写入 <see cref="GetModuleSources"/> 的数据源（供「模块清单」弹窗诊断）。</item>
    /// </list>
    /// </remarks>
    /// <param name="sourceDirectories">来源根（顺序即优先级；空项被忽略）。</param>
    /// <returns>装入的包数。</returns>
    public int ScanModuleSources(IReadOnlyList<string>? sourceDirectories)
    {
        var roots = NormalizeRoots(sourceDirectories);
        var results = new List<ModuleSourceView>(roots.Count);
        var installed = 0;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                results.Add(new ModuleSourceView(root, Exists: false, 0, 0, 0, [CoreText.Get("core.services.directoryMissingSkipped")]));
                continue;
            }

            var isExternal = IsExternalRoot(root);
            var messages = new List<string>();
            var found = 0;
            var skipped = 0;
            var failed = 0;
            var alreadyLoaded = 0;
            var duplicates = 0;
            var changed = 0;

            // **依赖顺序无关**：先把带 module.json 的包排成队列，按"依赖已就位"分轮安装；
            // 这一轮装不了的留到下一轮。目录枚举顺序不再决定成败（旧写法里"被依赖的包排在后面"
            // 会让先遇到的包整包失败，用户看到的是随机失败）。
            var pending = new List<string>();
            var manifests = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!File.Exists(Path.Combine(directory, "module.json")))
                {
                    skipped++;
                    continue;
                }

                found++;
                pending.Add(directory);
                _ = ModuleManifestReader.TryRead(directory, out var manifest, out _);
                manifests[directory] = manifest?.DependsOn ?? [];
            }

            var progress = true;
            while (pending.Count > 0 && progress)
            {
                progress = false;

                for (var index = pending.Count - 1; index >= 0; index--)
                {
                    if (!DependenciesReady(manifests[pending[index]]))
                    {
                        continue;   // 留到下一轮
                    }

                    var directory = pending[index];
                    pending.RemoveAt(index);
                    progress = true;
                    InstallOne(directory);
                }
            }

            // 依赖始终不满足：仍走同一条安装路径，让安装器给出**明确原因**（不静默跳过）。
            foreach (var directory in pending)
            {
                InstallOne(directory);
            }

            bool DependenciesReady(string[] dependsOn)
            {
                foreach (var dependency in dependsOn)
                {
                    if (ModuleId.TryParse(dependency, out var dependencyId)
                        && !_registry.TryResolve(dependencyId, out _))
                    {
                        return false;
                    }
                }

                return true;
            }

            void InstallOne(string directory)
            {
                var name = Path.GetFileName(directory);
                var result = _installer.Install(directory);

                if (result.Outcome == ModuleInstallOutcome.Failed || result.Definition is null)
                {
                    // 失败原因已由安装器记 Issue 并归属 package:<目录>
                    failed++;
                    messages.Add(CoreText.Get("core.services.rescan.installFailed", name));
                    return;
                }

                var packageDirectory = Path.GetFullPath(directory);
                var key = result.Definition.Id.ToString();

                switch (result.Outcome)
                {
                    case ModuleInstallOutcome.Installed:
                        installed++;
                        _modulePackages[key] = new ModulePackageOrigin(root, packageDirectory, NeedsRestart: false, isExternal);
                        messages.Add($"✓ {name} → {result.Definition.Id}");
                        break;

                    case ModuleInstallOutcome.AlreadyLoaded:
                        alreadyLoaded++;
                        if (_modulePackages.TryGetValue(key, out var origin))
                        {
                            _modulePackages[key] = origin with { NeedsRestart = false };
                        }
                        else
                        {
                            _modulePackages[key] = new ModulePackageOrigin(root, packageDirectory, NeedsRestart: false, isExternal);
                        }

                        messages.Add(CoreText.Get("core.services.rescan.unchanged", name));
                        break;

                    case ModuleInstallOutcome.DuplicatePackage:
                        duplicates++;
                        messages.Add(CoreText.Get("core.services.rescan.duplicateSource", name));
                        if (_reportedScanNotices.Add($"duplicate:{packageDirectory}"))
                        {
                            _runtime.Issues.Report($"package:{packageDirectory}", new IssueReport(
                                IssueCategory.ModuleError,
                                IssueSeverity.Info,
                                result.Message));
                        }

                        break;

                    case ModuleInstallOutcome.ContentChanged:
                        changed++;
                        if (_modulePackages.TryGetValue(key, out var changedOrigin))
                        {
                            _modulePackages[key] = changedOrigin with { NeedsRestart = true };
                        }
                        else
                        {
                            _modulePackages[key] = new ModulePackageOrigin(root, packageDirectory, NeedsRestart: true, isExternal);
                        }

                        messages.Add(CoreText.Get("core.services.rescan.contentChanged", name));
                        if (_reportedScanNotices.Add($"changed:{packageDirectory}"))
                        {
                            _runtime.Issues.Report($"package:{packageDirectory}", new IssueReport(
                                IssueCategory.ModuleError,
                                IssueSeverity.Warning,
                                result.Message));
                        }

                        break;

                    default:
                        break;
                }
            }

            results.Add(new ModuleSourceView(
                root,
                Exists: true,
                found,
                skipped,
                failed,
                messages,
                AlreadyLoadedPackages: alreadyLoaded,
                DuplicatePackages: duplicates,
                ChangedPackages: changed));
        }

        _moduleSources = results;

        lock (_catalogGate)
        {
            _catalogCache = null;
        }

        return installed;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleSourceView> GetModuleSources() => _moduleSources;

    /// <inheritdoc />
    /// <remarks>
    /// **只返回会被真正建立的连接**：几何候选再经 L4 的唯一接受判定（方向 + 契约）过滤，
    /// 与落地同一规则——否则预览会比实际多（"预告能连、松手不连"）。
    /// </remarks>
    public IReadOnlyList<ConnectionCandidateView> PreviewConnections(IReadOnlyList<ModuleLayoutView>? provisionalLayouts = null) =>
        [.. _runtime.CollectAcceptedDockPairs(provisionalLayouts)
            .Select(pair => new ConnectionCandidateView(pair.SourceLiteral, pair.TargetLiteral))];

    /// <inheritdoc />
    public IReadOnlyList<PortContractConflictView> GetPortContractConflicts()
    {
        var content = _runtime.Content;
        var key = (content.Revision, content.Layout.Revision);

        lock (_viewCacheGate)
        {
            if (_portConflictsCache is { } cached && _portConflictsKey == key)
            {
                return cached;
            }

            var views = _runtime.DescribeLabelContractConflicts()
                .Select(conflict => new PortContractConflictView(
                    conflict.InstanceId,
                    conflict.PortName,
                    conflict.PeerInstanceId,
                    conflict.PeerPortName,
                    conflict.NetworkName,
                    conflict.Reason))
                .ToList();

            _portConflictsKey = key;
            _portConflictsCache = views;
            return views;
        }
    }

    /// <inheritdoc />
    public AnchorDockHintView? SuggestAnchorDock(
        string instanceId,
        string portName,
        HYWDebugAssistant.Sdk.Ports.PortEdge edge,
        double offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        return double.IsFinite(offset)
            ? _runtime.SuggestAnchorDock(instanceId, portName, edge, offset)
            : null;
    }

    /// <inheritdoc />
    public CommandResult RescanModuleSources()
    {
        // 点「重新加载」后模块来源目录消失、也没重扫 —— 真因：**「扫哪些根」有两个来源**：
        // 启动路径（组合根）扫 `内置 modules\ + 设置里的根`，而这里只扫 `设置里的根`；
        // 偏偏组合根**刻意把内置目录排除在设置之外**（它「不可移除、不显示为可编辑项」）
        // ⇒ 用户没有自配来源根时（默认情形）这里的根列表是**空的** ⇒ 扫描结果表被覆盖成空表
        // ⇒ 「模块来源目录」列表**清空**（看起来像「目录被清掉了」），而且**什么都没重扫**。
        // 两条路径**共用同一个根列表**（`ResolveScanRoots`）⇒ 重扫永远不会丢掉内置根。
        // 本方法**不删任何文件**，消失的只是「列表内容」。
        var roots = ResolveScanRoots();
        var installed = ScanModuleSources(roots);

        var failed = _moduleSources.Sum(s => s.FailedPackages);
        var message = installed > 0
            ? CoreText.Get("core.services.rescan.installed", installed)
            : CoreText.Get("core.services.rescan.nothingNew");

        return CommandResult.Ok(failed > 0
            ? [CoreText.Get("core.services.rescan.partialFailure", message, failed)]
            : [message]);
    }

    /// <summary>
    /// **本次进程扫描的根列表**（唯一来源）：内置模块目录 + 外部导入目录；不再读取用户来源根。
    /// </summary>
    /// <remarks>
    /// 外部目录可能尚不存在（首次运行）；导入时创建。扫描结果仍会记录“目录不存在（已跳过）”，
    /// 但不会阻断启动。
    /// </remarks>
    public IReadOnlyList<string> ResolveScanRoots()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(BuiltInModuleDirectory))
        {
            roots.Add(BuiltInModuleDirectory);
        }

        if (!string.IsNullOrWhiteSpace(ExternalModuleDirectory)
            && !string.Equals(BuiltInModuleDirectory, ExternalModuleDirectory, StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(ExternalModuleDirectory);
        }

        return NormalizeRoots(roots);
    }

    /// <summary>
    /// **内置包目录**（应用目录下的 `modules\`）——由**组合根**在启动时注入。
    /// </summary>
    /// <remarks>
    /// 由外部注入而不是写在这里：Core **不认识**"应用目录"（那是交付形态的事），
    /// 而"内置目录永远参与扫描"这条语义必须在**扫描侧**（本类）兑现。
    /// </remarks>
    public string? BuiltInModuleDirectory { get; set; }

    /// <summary>外部导入包目录（`modules\external`）——由组合根注入；来源标记按此路径判定。</summary>
    public string? ExternalModuleDirectory { get; set; }

    /// <summary>
    /// 单一来源目录扫描（兼容入口）：等价于把该目录作为唯一根扫描。
    /// </summary>
    /// <param name="sourceDirectory">来源目录（为空/不存在 = 不扫描，返回 false）。</param>
    /// <param name="installed">装入的包数。</param>
    /// <param name="failed">失败的包数。</param>
    public bool TryScanSourceDirectory(string? sourceDirectory, out int installed, out int failed)
    {
        installed = 0;
        failed = 0;

        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return false;
        }

        installed = ScanModuleSources([sourceDirectory]);
        failed = _moduleSources.Sum(s => s.FailedPackages);
        return true;
    }

    /// <inheritdoc />
    public CommandResult ImportModuleDirectory(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        if (string.IsNullOrWhiteSpace(ExternalModuleDirectory))
        {
            return CommandResult.Fail([CoreText.Get("core.services.externalDirectoryNotConfigured")]);
        }

        if (!Directory.Exists(sourceDirectory))
        {
            return CommandResult.Fail([CoreText.Get("core.services.directoryMissing", sourceDirectory)]);
        }

        var packages = FindImportPackages(sourceDirectory);
        if (packages.Count == 0)
        {
            return CommandResult.Fail([CoreText.Get("core.services.noModulePackagesInDirectory")]);
        }

        var notices = new List<string>();
        var failures = new List<string>();
        var imported = 0;

        _ = Directory.CreateDirectory(ExternalModuleDirectory);

        foreach (var package in packages)
        {
            var packageName = Path.GetFileName(Path.TrimEndingDirectorySeparator(package));
            var load = ModulePackageLoader.Load(package);
            if (!load.Succeeded)
            {
                failures.Add($"{packageName}：{load.FailureReason}");
                continue;
            }

            ModuleDefinition definition;
            try
            {
                definition = ModuleDefinition.FromPackage(load.Package!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                failures.Add($"{packageName}：{ex.Message}");
                continue;
            }

            if (_registry.TryResolve(definition.Id, out var existing))
            {
                failures.Add(CoreText.Get(
                    "core.services.import.alreadyExists",
                    definition.Id,
                    existing!.PackageDirectory));
                continue;
            }

            var target = CreateUniqueDirectory(ExternalModuleDirectory, packageName);
            try
            {
                CopyDirectory(package, target);
                imported++;
                notices.Add(CoreText.Get("core.services.import.importedToTarget", definition.Id, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDeleteDirectory(target);
                failures.Add(CoreText.Get("core.services.import.copyFailed", packageName, ex.Message));
            }
        }

        if (imported > 0)
        {
            var rescan = RescanModuleSources();
            notices.AddRange(rescan.Notices);
        }

        foreach (var failure in failures)
        {
            _runtime.Issues.Report("module:import", new IssueReport(
                IssueCategory.ModuleError, IssueSeverity.Error, failure));
        }

        return imported > 0
            ? new CommandResult(true, failures, notices)
            : failures.Count > 0
                ? CommandResult.Fail(failures)
                : CommandResult.Ok(notices);
    }

    /// <summary>外部导入时扫描：目录自身是包，或取直接子目录中的包。</summary>
    private static List<string> FindImportPackages(string sourceDirectory)
    {
        if (File.Exists(Path.Combine(sourceDirectory, "module.json")))
        {
            return [Path.GetFullPath(sourceDirectory)];
        }

        return [.. Directory.EnumerateDirectories(sourceDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, "module.json")))
            .Select(Path.GetFullPath)];
    }

    private static string CreateUniqueDirectory(string root, string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "module";
        }

        var target = Path.Combine(root, clean);
        for (var suffix = 2; Directory.Exists(target); suffix++)
        {
            target = Path.Combine(root, $"{clean}-{suffix}");
        }

        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        _ = Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool IsExternalRoot(string root) =>
        !string.IsNullOrWhiteSpace(ExternalModuleDirectory)
        && string.Equals(Path.GetFullPath(root), Path.GetFullPath(ExternalModuleDirectory), StringComparison.OrdinalIgnoreCase);

    /// <summary>去重并保持顺序（忽略空白项）。</summary>
    private static List<string> NormalizeRoots(IReadOnlyList<string>? sourceDirectories)
    {        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in sourceDirectories ?? [])
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var full = Path.GetFullPath(directory.Trim());
            if (seen.Add(full))
            {
                roots.Add(full);
            }
        }

        return roots;
    }

    /// <inheritdoc />
    public CommandResult AddModule(string instanceId, string moduleIdLiteral)
    {
        if (!ModuleId.TryParse(moduleIdLiteral, out var moduleId)
            || !_registry.TryResolve(moduleId, out var definition)
            || definition is null)
        {
            return CommandResult.Fail([CoreText.Get("core.services.moduleNotRegistered", moduleIdLiteral)]);
        }

        return Map(_runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, definition))));
    }

    /// <inheritdoc />
    public CommandResult RemoveModule(string instanceId)
    {
        var result = Map(_runtime.Apply(ChangeSet.OfOne(new RemoveModuleOperation(instanceId))));

        lock (_schemaCacheGate)
        {
            _ = _schemaCache.Remove(instanceId);
        }

        return result;
    }

    /// <inheritdoc />
    public CommandResult SetModuleEnabled(string instanceId, bool enabled) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new SetModuleEnabledOperation(instanceId, enabled))));

    /// <inheritdoc />
    public CommandResult UpdateConfiguration(string instanceId, object configuration) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new UpdateConfigurationOperation(instanceId, configuration))));

    /// <inheritdoc />
    public CommandResult UpdateSettings(ApplicationSettings settings) => _settings.Update(settings);

    /// <inheritdoc />
    public ModuleWallpaperView? GetModuleWallpaper(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        return _runtime.GetWallpaper(instanceId) is { } asset
            ? new ModuleWallpaperView(asset.Revision, asset.Png)
            : null;
    }

    /// <inheritdoc />
    public CommandResult SetModuleWallpaper(string instanceId, byte[]? png)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.SetWallpaper(instanceId, png))
        {
            return CommandResult.Fail([CoreText.Get("core.runtime.instanceMissing", instanceId)]);
        }

        return CommandResult.Ok([png is { Length: > 0 }
            ? CoreText.Get("core.services.wallpaperSet")
            : CoreText.Get("core.services.wallpaperCleared")]);
    }

    /// <inheritdoc />
    public CommandResult SetWorkspaceExitBehavior(string behavior)
    {
        if (behavior is not ("Ask" or "Save" or "Discard"))
        {
            return CommandResult.Fail([CoreText.Get("core.services.exitBehaviorInvalid")]);
        }

        var settings = _settings.Current.Clone();
        settings.WorkspaceExitBehavior = behavior;
        return _settings.UpdateStrict(settings);
    }

    /// <inheritdoc />
    public IReadOnlyList<ThemeFileView> GetThemeFiles() => _settings.Themes.LoadAll();

    /// <inheritdoc />
    public bool TryGetThemeWallpaper(string themeId, out byte[]? png) =>
        _settings.Themes.TryReadWallpaper(themeId, out png);

    /// <inheritdoc />
    public CommandResult SaveTheme(ThemeSaveRequest request) => _settings.Themes.Save(request);

    /// <inheritdoc />
    public CommandResult DeleteTheme(string themeId) => _settings.Themes.Delete(themeId);

    /// <inheritdoc />
    public CommandResult UpdateModuleLayout(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Map(_runtime.Apply(ChangeSet.OfOne(new UpdateModuleLayoutOperation(layout))));
    }

    /// <inheritdoc />
    public CommandResult UpdateModuleLayouts(IReadOnlyList<ModuleLayoutView> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        if (layouts.Count == 0)
        {
            return CommandResult.Ok();
        }

        // 一次 ChangeSet：整体拖动要么全部生效、要么整体拒绝（C5-1 约束 1）
        return Map(_runtime.Apply(ChangeSet.Of([.. layouts.Select(l => (ChangeOperation)new UpdateModuleLayoutOperation(l))])));
    }

    /// <inheritdoc />
    public CommandResult UpdatePortGeometry(string instanceId, PortGeometryView geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return Map(_runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(instanceId, geometry))));
    }

    /// <inheritdoc />
    public CommandResult UpdatePortNetwork(string instanceId, string portName, string? networkName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        return Map(_runtime.Apply(ChangeSet.OfOne(new UpdatePortNetworkOperation(instanceId, portName, networkName))));
    }

    /// <inheritdoc />
    public CommandResult SendCommand(string instanceId, string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        return Map(_runtime.Apply(ChangeSet.OfOne(new SendCommandOperation(instanceId, commandName))));
    }

    /// <inheritdoc />
    public CommandResult SubmitInput(string instanceId, string source, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(text);

        return Map(_runtime.Apply(ChangeSet.OfOne(new SubmitInputOperation(instanceId, source, text))));
    }

    /// <inheritdoc />
    public CommandResult SaveComposite(string filePath, string? name = null, IReadOnlyList<string>? instanceIds = null) =>
        filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? _composites.Save(filePath, name, instanceIds)
            : _composites.SavePackage(filePath, name, instanceIds);

    /// <inheritdoc />
    public CompositeImportResult ImportCompositeFile(string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
        {
            return FailCompositeImport(CoreText.Get("core.services.composite.fileMissing"));
        }

        var inspection = _composites.Inspect(sourceFile);
        if (inspection.Error is { Length: > 0 } error)
        {
            return FailCompositeImport(CoreText.Get("core.services.composite.importFailed", error));
        }

        try
        {
            var root = StoragePaths.CompositesRoot;
            _ = Directory.CreateDirectory(root);

            var sourcePath = Path.GetFullPath(sourceFile);
            var sourceDirectory = Path.GetDirectoryName(sourcePath);
            var isPackage = string.Equals(Path.GetFileName(sourcePath), StoragePaths.CompositeDocumentFileName, StringComparison.OrdinalIgnoreCase)
                && sourceDirectory is { Length: > 0 };

            if (isPackage)
            {
                var packageName = Path.GetFileName(sourceDirectory!);
                var targetDirectory = UniqueDirectory(root, packageName);
                CopyCompositePackage(sourceDirectory!, targetDirectory);
                var targetDocument = CompositeFileStore.DocumentPathFor(targetDirectory);
                var packageNotices = new List<string> { CoreText.Get("core.services.composite.imported", targetDirectory) };
                if (inspection.MissingModules.Count > 0)
                {
                    packageNotices.Add(CoreText.Get("core.services.composite.missingModules", inspection.MissingModules.Count));
                }

                return new CompositeImportResult(true, targetDocument, packageNotices, []);
            }

            var name = Path.GetFileName(sourcePath);
            var target = Path.Combine(root, name);

            if (string.Equals(sourcePath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                return new CompositeImportResult(true, target, [CoreText.Get("core.services.composite.inDefaultDirectory")], []);
            }

            if (File.Exists(target))
            {
                if (FilesEqual(sourcePath, target))
                {
                    return new CompositeImportResult(true, target, [CoreText.Get("core.services.composite.defaultDirectoryReused")], []);
                }

                target = UniqueCompositePath(root, name);
            }

            File.Copy(sourcePath, target, overwrite: false);

            // 外部组合若带同名 `.preview.png` 封面，一并复制到默认目录。
            var sourceCover = CompositeFileStore.CoverPathFor(sourcePath);
            if (File.Exists(sourceCover))
            {
                File.Copy(sourceCover, CompositeFileStore.CoverPathFor(target), overwrite: true);
            }

            var notices = new List<string> { CoreText.Get("core.services.composite.imported", target) };
            if (inspection.MissingModules.Count > 0)
            {
                notices.Add(CoreText.Get("core.services.composite.missingModules", inspection.MissingModules.Count));
            }

            return new CompositeImportResult(true, target, notices, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FailCompositeImport(CoreText.Get("core.services.composite.copyFailed", ex.Message));
        }
    }

    private CompositeImportResult FailCompositeImport(string reason)
    {
        _runtime.Issues.Report("composite:import", new IssueReport(
            IssueCategory.ConfigurationError, IssueSeverity.Error, reason));
        return new CompositeImportResult(false, null, [], [reason]);
    }

    /// <inheritdoc />
    public CommandResult DeleteCompositeFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return CommandResult.Fail([CoreText.Get("core.services.composite.pathEmpty")]);
        }

        try
        {
            var root = Path.GetFullPath(StoragePaths.CompositesRoot);
            var full = Path.GetFullPath(filePath);
            var parent = Path.GetDirectoryName(full);

            var isPackage = string.Equals(Path.GetFileName(full), StoragePaths.CompositeDocumentFileName, StringComparison.OrdinalIgnoreCase)
                && parent is { Length: > 0 }
                && string.Equals(Path.GetDirectoryName(parent), root, StringComparison.OrdinalIgnoreCase);
            var isLegacy = string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase);

            if (!isPackage && !isLegacy)
            {
                return FailCompositeDelete(full, CoreText.Get("core.services.composite.deleteOnlyDefaultDirectory"));
            }

            if (!File.Exists(full))
            {
                return FailCompositeDelete(full, CoreText.Get("core.services.composite.fileMissing"));
            }

            if (isPackage)
            {
                Directory.Delete(parent!, recursive: true);
                return CommandResult.Ok([CoreText.Get("core.services.composite.deleted", Path.GetFileName(parent))]);
            }

            File.Delete(full);
            var cover = CompositeFileStore.CoverPathFor(full);
            if (File.Exists(cover))
            {
                try
                {
                    File.Delete(cover);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _runtime.Issues.Report("composite:delete", new IssueReport(
                        IssueCategory.ConfigurationError, IssueSeverity.Warning, CoreText.Get("core.services.composite.coverDeleteFailed", ex.Message)));
                }
            }

            return CommandResult.Ok([CoreText.Get("core.services.composite.deleted", Path.GetFileName(full))]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FailCompositeDelete(filePath, CoreText.Get("core.services.composite.deleteFailed", ex.Message));
        }
    }

    private CommandResult FailCompositeDelete(string filePath, string reason)
    {
        _runtime.Issues.Report("composite:delete", new IssueReport(
            IssueCategory.ConfigurationError, IssueSeverity.Error, reason));
        return CommandResult.Fail([reason]);
    }

    /// <inheritdoc />
    public CompositeRenameResult RenameCompositeFile(string filePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(newName))
        {
            return FailCompositeRename(CoreText.Get("core.services.composite.rename.nameEmpty"));
        }

        try
        {
            var root = Path.GetFullPath(StoragePaths.CompositesRoot);
            var source = Path.GetFullPath(filePath);
            var parent = Path.GetDirectoryName(source);
            var isPackage = string.Equals(Path.GetFileName(source), StoragePaths.CompositeDocumentFileName, StringComparison.OrdinalIgnoreCase)
                && parent is { Length: > 0 }
                && string.Equals(Path.GetDirectoryName(parent), root, StringComparison.OrdinalIgnoreCase);
            var isLegacy = string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase);

            if ((!isPackage && !isLegacy) || !File.Exists(source))
            {
                return FailCompositeRename(CoreText.Get("core.services.composite.rename.onlyDefaultDirectory"));
            }

            var stem = newName.Trim();
            if (stem.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^5];
            }

            var invalid = Path.GetInvalidFileNameChars();
            if (stem.Length == 0
                || stem.Any(ch => invalid.Contains(ch))
                || !string.Equals(Path.GetFileName(stem), stem, StringComparison.Ordinal))
            {
                return FailCompositeRename(CoreText.Get("core.services.composite.rename.invalidChars"));
            }

            if (isPackage)
            {
                var targetDirectory = Path.Combine(root, stem);
                if (string.Equals(parent, targetDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return FailCompositeRename(CoreText.Get("core.services.composite.rename.sameName"));
                }

                if (Directory.Exists(targetDirectory) || File.Exists(Path.Combine(root, stem + ".json")))
                {
                    return FailCompositeRename(CoreText.Get("core.services.composite.rename.targetExists"));
                }

                Directory.Move(parent!, targetDirectory);
                return new CompositeRenameResult(true, CompositeFileStore.DocumentPathFor(targetDirectory), []);
            }

            var target = Path.Combine(root, stem + ".json");
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                return FailCompositeRename(CoreText.Get("core.services.composite.rename.sameName"));
            }

            if (File.Exists(target))
            {
                return FailCompositeRename(CoreText.Get("core.services.composite.rename.targetExists"));
            }

            var sourceCover = CompositeFileStore.CoverPathFor(source);
            var targetCover = CompositeFileStore.CoverPathFor(target);
            if (File.Exists(targetCover))
            {
                return FailCompositeRename(CoreText.Get("core.services.composite.rename.coverExists"));
            }

            File.Move(source, target);
            if (File.Exists(sourceCover))
            {
                try
                {
                    File.Move(sourceCover, targetCover);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _runtime.Issues.Report("composite:rename", new IssueReport(
                        IssueCategory.ConfigurationError, IssueSeverity.Warning, CoreText.Get("core.services.composite.rename.coverMoveFailed", ex.Message)));
                }
            }

            return new CompositeRenameResult(true, target, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FailCompositeRename(CoreText.Get("core.services.composite.rename.failed", ex.Message));
        }
    }

    /// <inheritdoc />
    public CommandResult SetCompositeCover(string filePath, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)
            || string.IsNullOrWhiteSpace(imagePath)
            || !File.Exists(imagePath))
        {
            return FailCompositeCover(CoreText.Get("core.services.composite.cover.fileMissing"));
        }

        try
        {
            return SetCompositeCoverPng(filePath, File.ReadAllBytes(imagePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FailCompositeCover(CoreText.Get("core.services.composite.cover.changeFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public CommandResult SetCompositeCoverPng(string filePath, byte[] png)
    {
        if (string.IsNullOrWhiteSpace(filePath) || png is not { Length: > 0 })
        {
            return FailCompositeCover(CoreText.Get("core.services.composite.cover.empty"));
        }

        try
        {
            var root = Path.GetFullPath(StoragePaths.CompositesRoot);
            var source = Path.GetFullPath(filePath);
            if (!File.Exists(source))
            {
                return FailCompositeCover(CoreText.Get("core.services.composite.fileMissing"));
            }

            var isPackage = string.Equals(Path.GetFileName(source), StoragePaths.CompositeDocumentFileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(source)), root, StringComparison.OrdinalIgnoreCase);
            var isLegacy = string.Equals(Path.GetDirectoryName(source), root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase);

            if (!isPackage && !isLegacy)
            {
                return FailCompositeCover(CoreText.Get("core.services.composite.cover.onlyDefaultDirectory"));
            }

            var target = CompositeFileStore.CoverPathFor(source);
            File.WriteAllBytes(target, png);
            return CommandResult.Ok([CoreText.Get("core.services.composite.cover.changed", Path.GetFileName(target))]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return FailCompositeCover(CoreText.Get("core.services.composite.cover.changeFailed", ex.Message));
        }
    }

    private CommandResult FailCompositeCover(string reason)
    {
        _runtime.Issues.Report("composite:cover", new IssueReport(
            IssueCategory.ConfigurationError, IssueSeverity.Error, reason));
        return CommandResult.Fail([reason]);
    }

    private CompositeRenameResult FailCompositeRename(string reason)
    {
        _runtime.Issues.Report("composite:rename", new IssueReport(
            IssueCategory.ConfigurationError, IssueSeverity.Error, reason));
        return new CompositeRenameResult(false, null, [reason]);
    }

    private static bool FilesEqual(string left, string right)    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }

    /// <summary>复制一个组合件包的已知文件（JSON/封面/资产），不跟随链接。</summary>
    private static void CopyCompositePackage(string source, string target)
    {
        _ = Directory.CreateDirectory(target);

        var document = Path.Combine(source, StoragePaths.CompositeDocumentFileName);
        File.Copy(document, Path.Combine(target, StoragePaths.CompositeDocumentFileName), overwrite: false);

        var cover = Path.Combine(source, StoragePaths.CompositeCoverFileName);
        if (File.Exists(cover))
        {
            File.Copy(cover, Path.Combine(target, StoragePaths.CompositeCoverFileName), overwrite: true);
        }

        var assets = Path.Combine(source, StoragePaths.CompositeAssetsDirectoryName);
        if (Directory.Exists(assets))
        {
            var targetAssets = Path.Combine(target, StoragePaths.CompositeAssetsDirectoryName);
            _ = Directory.CreateDirectory(targetAssets);
            foreach (var file in Directory.EnumerateFiles(assets, "*.png", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(targetAssets, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    private static string UniqueDirectory(string root, string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "composite" : name;
        var candidate = Path.Combine(root, baseName);
        for (var suffix = 2; Directory.Exists(candidate) || File.Exists(candidate + ".json"); suffix++)
        {
            candidate = Path.Combine(root, $"{baseName}-{suffix}");
        }

        return candidate;
    }

    private static string UniqueCompositePath(string root, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(root, $"{stem}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <inheritdoc />
    public CommandResult LoadComposite(string filePath) => _composites.Load(filePath);

    /// <inheritdoc />
    public CommandResult SaveWorkspace(WorkspacePlacementView placement)
    {
        if (_workspaceLoadFailed && !TryBackupUnreadableWorkspace(out var backupFailure))
        {
            _runtime.Issues.Report("workspace:save", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Error, backupFailure));
            return CommandResult.Fail([backupFailure]);
        }

        var result = _composites.SavePackage(
            StoragePaths.WorkspaceRoot,
            "workspace",
            instanceIds: null,
            workspacePlacement: placement,
            documentFileName: StoragePaths.WorkspaceFileName);

        if (result.Succeeded)
        {
            _workspaceLoadFailed = false;
        }

        return result;
    }

    /// <summary>
    /// 工作区载入失败后，首次保存前把原文件另存一份；返回 <see langword="false"/> 时调用方**不得**写新文件。
    /// </summary>
    private bool TryBackupUnreadableWorkspace(out string failure)
    {
        failure = string.Empty;
        var document = StoragePaths.WorkspaceFile;
        if (!File.Exists(document))
        {
            return true;
        }

        var backup = document + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
        try
        {
            File.Copy(document, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = CoreText.Get("core.workspace.backupFailed", document, ex.Message);
            return false;
        }

        _runtime.Issues.Report("workspace:load", new IssueReport(
            IssueCategory.ConfigurationError, IssueSeverity.Warning,
            CoreText.Get("core.workspace.unreadableBackedUp", backup)));
        return true;
    }

    /// <summary>启动时恢复工作区（存在才做；失败不阻断启动）。</summary>
    public CommandResult LoadWorkspaceIfAny()
    {
        if (!Directory.Exists(StoragePaths.WorkspaceRoot))
        {
            return CommandResult.Ok();
        }

        var document = StoragePaths.WorkspaceFile;
        if (!File.Exists(document))
        {
            return CommandResult.Ok();
        }

        var result = _composites.Load(document);
        _workspaceLoadFailed = !result.Succeeded;
        _workspacePlacement = _composites.LastLoadedWorkspacePlacement;
        return result;
    }

    /// <inheritdoc />
    public WorkspacePlacementView? GetWorkspacePlacement() => _workspacePlacement;

    /// <inheritdoc />
    public CommandResult SaveRestartSnapshot()
    {
        var directory = Path.GetDirectoryName(_restartSnapshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                _ = Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var failure = CoreText.Get("core.services.restartSnapshot.createDirFailed", directory, ex.Message);
                _runtime.Issues.Report("restart", new IssueReport(
                    IssueCategory.ConfigurationError, IssueSeverity.Error, failure));
                return CommandResult.Fail([failure]);
            }
        }

        // 快照就是“当前全部模块的关系”——复用组合件保存；加载端只看 ID 冲突决定是否重映射。
        return _composites.Save(_restartSnapshotPath, "restart", instanceIds: null);
    }

    /// <inheritdoc />
    public CommandResult DeleteRestartSnapshot()
    {
        try
        {
            if (File.Exists(_restartSnapshotPath))
            {
                File.Delete(_restartSnapshotPath);
            }

            return CommandResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = CoreText.Get("core.services.restartSnapshot.deleteFailed", ex.Message, _restartSnapshotPath);
            _runtime.Issues.Report("restart", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Warning, failure));
            return CommandResult.Fail([failure]);
        }
    }

    /// <summary>
    /// 启动时恢复一次性重启快照（存在才做）：加载后删除；损坏/版本过新也删除，避免每次启动重复失败。
    /// </summary>
    /// <remarks>由组合根在模块扫描之后、显示窗口之前调用；不是会话系统。</remarks>
    public CommandResult RestoreRestartSnapshotIfAny()
    {
        if (!File.Exists(_restartSnapshotPath))
        {
            return CommandResult.Ok();
        }

        var result = _composites.Load(_restartSnapshotPath);

        try
        {
            File.Delete(_restartSnapshotPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _runtime.Issues.Report("restart", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                CoreText.Get("core.services.restartSnapshot.loadedButNotDeleted", ex.Message, _restartSnapshotPath)));
        }

        return result;
    }

    /// <inheritdoc />
    public CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral) =>
        _composites.ResolvePlaceholder(instanceId, moduleIdLiteral);

    /// <inheritdoc />
    public CommandResult UpdateConfigurationJson(string instanceId, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return CommandResult.Fail([CoreText.Get("core.runtime.instanceMissing", instanceId)]);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return CommandResult.Fail([CoreText.Get("core.services.config.jsonEmpty")]);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException ex)
        {
            return CommandResult.Fail([CoreText.Get("core.services.config.jsonParseFailed", ex.Message)]);
        }

        if (root is null)
        {
            return CommandResult.Fail([CoreText.Get("core.services.config.jsonRootNotObject")]);
        }

        // 全部解析成功后才提交（整体应用或整体拒绝，经变更入口）
        var pending = new List<object>();
        var failures = new List<string>();

        foreach (var (typeName, node) in root)
        {
            if (node is null)
            {
                continue;
            }

            var type = instance.Definition.EntryType.Assembly.GetType(typeName, throwOnError: false)
                ?? Type.GetType(typeName, throwOnError: false);

            if (type is null)
            {
                failures.Add(CoreText.Get("core.services.config.typeUnresolved", typeName));
                continue;
            }

            try
            {
                if (node.Deserialize(type, ConfigurationJson.Options) is { } value)
                {
                    pending.Add(value);
                }
                else
                {
                    failures.Add(CoreText.Get("core.services.config.itemDeserializedEmpty", typeName));
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                failures.Add(CoreText.Get("core.services.config.itemUnresolved", typeName, ex.Message));
            }
        }

        if (failures.Count > 0)
        {
            return CommandResult.Fail(failures);
        }

        if (pending.Count == 0)
        {
            return CommandResult.Ok([CoreText.Get("core.services.config.emptyNoChange")]);
        }

        // 走变更入口：一次 ChangeSet、只触发一次配置变更通知
        return Map(_runtime.Apply(ChangeSet.OfOne(new UpdateConfigurationsOperation(instanceId, pending))));
    }

    /// <inheritdoc />
    public CommandResult ResetModuleConfiguration(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return Map(_runtime.Apply(ChangeSet.OfOne(new ResetModuleConfigurationOperation(instanceId))));
    }

    // ---------------------------------------------------------------- 查询面

    /// <inheritdoc />
    public IReadOnlyList<ModuleInstanceView> GetInstances()
    {
        var content = _runtime.Content;
        var key = (content.Revision, content.Layout.Revision, _runtime.WallpaperVersion);

        lock (_viewCacheGate)
        {
            if (_instancesCache is { } cached && _instancesKey == key)
            {
                return cached;
            }

            var views = new List<ModuleInstanceView>(content.InstanceCount + content.PlaceholderCount);
            views.AddRange(content.Instances.Select(ToView));
            views.AddRange(content.Placeholders.Select(ToView));

            _instancesKey = key;
            _instancesCache = views;
            return views;
        }
    }

    /// <inheritdoc />
    public bool TryGetInstance(string instanceId, out ModuleInstanceView? view)
    {
        if (_runtime.Content.TryGetInstance(instanceId, out var instance) && instance is not null)
        {
            view = ToView(instance);
            return true;
        }

        if (_runtime.Content.TryGetPlaceholder(instanceId, out var placeholder) && placeholder is not null)
        {
            view = ToView(placeholder);
            return true;
        }

        view = null;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkView> GetNetworks()
    {
        var content = _runtime.Content;
        var bindings = _runtime.Bindings;
        var key = (content.Revision, (object?)bindings);

        lock (_viewCacheGate)
        {
            if (_networksCache is { } cached && _networksKey == key)
            {
                return cached;
            }

            var views = content.Networks.Select(n =>
            {
                // 多源是一等组合：**全部声明的源**（在场的在前 + 悬空的声明在后，各组内字典序稳定）。
                // `Source` 只是兼容访问器，必须与 `Sources[0]` 一致（旧实现取 n.Source，与排序后的
                // 视图列表可能不是同一个；多源成员判定一律用 `Sources`）。
                var presentSources = n.Sources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
                var unresolvedSources = n.UnresolvedSources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
                var sources = new List<string>(presentSources.Count + unresolvedSources.Count);
                sources.AddRange(presentSources);
                sources.AddRange(unresolvedSources);

                return new NetworkView(
                    n.Name,
                    IsAnonymous: !n.IsNamed,
                    n.IsStalled,
                    sources.Count > 0 ? sources[0] : string.Empty,
                    [.. n.Targets.Select(t => t.ToString())],
                    [.. n.UnresolvedMembers.Select(m => m.ToString())])
                {
                    // 【P40】**网络身份键**：视图层靠它配对（网络名不唯一：匿名网络名为空串）
                    Key = ModuleRuntime.DescribeKey(n),
                    Sources = sources,
                    UnresolvedSources = unresolvedSources,
                };
            }).ToList();

            _networksKey = key;
            _networksCache = views;
            return views;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleLayoutView> GetLayout()
    {
        var layout = _runtime.Content.Layout;

        lock (_viewCacheGate)
        {
            if (_layoutCache is { } cached && _layoutKey == layout.Revision)
            {
                return cached;
            }

            _layoutKey = layout.Revision;
            _layoutCache = layout.Modules;
            return _layoutCache;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BindingDiagnosticView> GetDiagnostics()
    {
        var now = Stopwatch.GetTimestamp();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var views = new List<BindingDiagnosticView>(_runtime.Bindings.Bindings.Count);

        foreach (var b in _runtime.Bindings.Bindings)
        {
            var view = BuildDiagnostic(b, now);
            _ = seen.Add(view.Key);
            views.Add(view);
        }

        // 绑定被重建/删除后，旧键的速率计一并清掉（否则长期运行会积累无主状态）
        PruneStaleRateMeters(seen);

        return views;
    }

    /// <summary>
    /// 把 <c>_rateMeters</c> 里**已经不对应任何绑定**的键清掉（长期运行不积累无主状态）。
    /// </summary>
    /// <remarks>
    /// <b>判据必须是"全部绑定的键"</b>（不是"本次查询用到的那些"）：否则按端口查询会把别的端口/别的绑定的
    /// 速率计一起删掉 ⇒ 那些口下次读到 0 并重建基准（表现为速率莫名归零）。故调用方一律传**全量**键集合。
    /// </remarks>
    /// <param name="liveKeys">当前全部绑定的身份键（`源 → 宿子集`）。</param>
    private void PruneStaleRateMeters(HashSet<string> liveKeys)
    {
        foreach (var stale in _rateMeters.Keys.Where(k => !liveKeys.Contains(k)).ToList())
        {
            _ = _rateMeters.Remove(stale);
        }
    }

    /// <summary>
    /// 构造**一条绑定**的逐源诊断视图（键 = `单源 → 宿子集`）—— **框架侧唯一的速率 / 丢弃口径实现**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么抽出来</b>：<see cref="GetDiagnostics"/>（逐源行）与 <see cref="GetPortMetrics"/>
    ///     （按端口汇总）必须读<b>同一份</b>实现、<b>同一批</b>速率计（<c>_rateMeters</c>）——
    ///     否则"统一口径"只是换个地方再分叉一次（那正是本次缺陷的成因）；</item>
    ///   <item><b>四档速率由框架给出</b>：UI 不得自行差分估算；</item>
    ///   <item><b>三类丢弃分开计</b>：停流未投递 ≠ 传输丢弃。</item>
    /// </list>
    /// </remarks>
    /// <param name="b">绑定（= 一条"源 → 宿子集"的流）。</param>
    /// <param name="now">本次观察时刻（<see cref="Stopwatch.GetTimestamp"/>；同一趟刷新共用一个）。</param>
    private BindingDiagnosticView BuildDiagnostic(RuntimeBinding b, long now)
    {
        // 诊断/绑定的键 = **单源 → 宿子集**（多源网络下**按源归属**）
        var key = ModuleRuntime.DescribeSourceKey(b.Source, b.Targets);
        var meters = MetersOf(key);

        return new BindingDiagnosticView(
            b.Network.Name,
            b.Diagnostics.Delivered,
            b.Diagnostics.Dropped,
            b.Diagnostics.LastDataTime)
        {
            Key = key,

            // 丢弃按原因分类（停流未投递 ≠ 传输丢弃）
            DropStalled = b.Diagnostics.DroppedByReason(DropReason.Stalled),

            // "队列满"是**两种事实的合计**：挤旧（本次投递成立、旧单元没送到）与拒新（本次被拒）。
            // 视图里它们是同一个格子的数，链路上仍分开记账——合并只发生在这里
            DropQueueFull = b.Diagnostics.DroppedByReason(DropReason.QueueFullEvicted)
                + b.Diagnostics.DroppedByReason(DropReason.QueueFullRejected),

            // 两个分项各自给：界面要区分"挤掉的旧数据"与"被拒的新数据"时不必再猜
            DropQueueFullEvicted = b.Diagnostics.DroppedByReason(DropReason.QueueFullEvicted),
            DropQueueFullRejected = b.Diagnostics.DroppedByReason(DropReason.QueueFullRejected),
            DropCarrierStopped = b.Diagnostics.DroppedByReason(DropReason.CarrierStopped),
            DropNoCarrier = b.Diagnostics.DroppedByReason(DropReason.NoCarrier),
            DropInvalidPayload = b.Diagnostics.DroppedByReason(DropReason.InvalidPayload),

            // **三类之一：传输丢弃**（队列满 + 无承载 + 承载已停 + 载荷非法；不含"停流未投递"）
            DropTransport = b.Diagnostics.DroppedByReason(DropReason.QueueFullEvicted)
                + b.Diagnostics.DroppedByReason(DropReason.QueueFullRejected)
                + b.Diagnostics.DroppedByReason(DropReason.NoCarrier)
                + b.Diagnostics.DroppedByReason(DropReason.CarrierStopped)
                + b.Diagnostics.DroppedByReason(DropReason.InvalidPayload),

            // **四档速率由框架给出**（UI 不得自行差分估算）
            Published = b.Diagnostics.Published,
            NetworkKey = ModuleRuntime.DescribeKey(b.Network),
            UnitRateHz = meters.Unit.Observe(b.Diagnostics.Published, now),
            BusByteRate = meters.Bus.Observe(b.Diagnostics.Delivered * b.Layout.SizeInBytes, now),

            // 第 3 档读的是**累计有效字节**（逐单元读类型声明的
            // 有效长度字段；未声明 ⇒ 静态 `PayloadSizeInBytes` ?? 契约尺寸）—— 不能拿「发布数 × 静态尺寸」现乘
            // （那样变长载荷会把补零也算成有效数据）。
            PayloadByteRate = meters.Payload.Observe(b.Diagnostics.PayloadBytes, now),
            SampleRateHz = meters.Unit.Rate * b.Metering.SamplesPerUnit,
            ChannelSampleRateHz = meters.Unit.Rate * b.Metering.SamplesPerUnit * b.Metering.ChannelCount,
            NominalRateHz = b.Metering.NominalRateHz,
        };
    }

    /// <inheritdoc />
    public PortMetricsView GetPortMetrics(string instanceId, string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        // **一趟刷新共用一个时刻**：与 GetDiagnostics 同一手法（RateMeter 只认"观察时刻"，
        // 同一时刻重复观察返回上次速率 ⇒ 本次汇总不会因为多读几次而互相清零）。
        var now = Stopwatch.GetTimestamp();

        long delivered = 0;
        long dropped = 0;
        long stalled = 0;
        long queueFull = 0;
        long queueFullEvicted = 0;
        long queueFullRejected = 0;
        long carrierStopped = 0;
        long noCarrier = 0;
        long invalidPayload = 0;
        long bytes = 0;
        long payloadBytes = 0;
        double bytesPerSecond = 0;
        double payloadBytesPerSecond = 0;
        double unitRate = 0;
        DateTimeOffset? lastData = null;
        var networkKeys = new HashSet<string>(StringComparer.Ordinal);

        // **全量绑定键**（不只是本口参与的那些）：用于结尾清理陈旧速率计，判据见 PruneStaleRateMeters
        var liveKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var b in _runtime.Bindings.Bindings)
        {
            _ = liveKeys.Add(ModuleRuntime.DescribeSourceKey(b.Source, b.Targets));

            // **"流" = 一条 (源成员 → 宿成员) 绑定**：本口是**源**或**宿**都算参与
            // ⇒ **宿口按网络把该网络的各源诊断加起来**（缺陷根因就在这里被挡住：
            //    绝不再拿宿口字面量去查只含"源"键的表）
            if (!IsPortInvolved(b, instanceId, portName))
            {
                continue;
            }

            var view = BuildDiagnostic(b, now);

            delivered += view.Delivered;

            // **只取"传输丢弃"**：停流未投递是合法状态，单独走 DropStalled（三类不混算）
            dropped += view.DropTransport;
            stalled += view.DropStalled;
            queueFull += view.DropQueueFull;
            queueFullEvicted += view.DropQueueFullEvicted;
            queueFullRejected += view.DropQueueFullRejected;
            carrierStopped += view.DropCarrierStopped;
            noCarrier += view.DropNoCarrier;
            invalidPayload += view.DropInvalidPayload;

            // 累计搬运字节与 BusByteRate **同一口径**（Delivered × 该源契约尺寸）
            bytes += view.Delivered * b.Layout.SizeInBytes;

            // 界面显示的**速率与累计流量**都取有效口径：
            // 累计有效字节 = 该绑定逐单元累计的有效字节之和（**按源计**，与 Published 同一次发布累加）。
            payloadBytes += b.Diagnostics.PayloadBytes;

            // 速率 = 逐源诊断的速率之和（**与角色方块同一批速率计**，不另起第二套）
            bytesPerSecond += view.BusByteRate;
            payloadBytesPerSecond += view.PayloadByteRate;
            unitRate += view.UnitRateHz;

            if (view.LastDataTime is { } time && (lastData is null || time > lastData))
            {
                lastData = time;
            }

            if (view.NetworkKey.Length > 0)
            {
                _ = networkKeys.Add(view.NetworkKey);
            }
        }

        // 与 `GetDiagnostics` 同一份清理判据（本查询可能**单独**被界面调用 ⇒ 不能依赖别人替它清）
        PruneStaleRateMeters(liveKeys);

        return new PortMetricsView(delivered, dropped, bytesPerSecond)
        {
            DropStalled = stalled,
            DropQueueFull = queueFull,
            DropQueueFullEvicted = queueFullEvicted,
            DropQueueFullRejected = queueFullRejected,
            DropCarrierStopped = carrierStopped,
            DropNoCarrier = noCarrier,
            DropInvalidPayload = invalidPayload,
            BytesDelivered = bytes,
            PayloadBytesDelivered = payloadBytes,
            PayloadBytesPerSecond = payloadBytesPerSecond,
            UnitRateHz = unitRate,
            LastDataTime = lastData,
            NetworkKeys = [.. networkKeys.OrderBy(k => k, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// 该口是否**参与**这条绑定（= 它是这条流的**源或宿**）—— "一个端口该读哪些流"的**唯一判据**。
    /// </summary>
    /// <param name="binding">一条绑定。</param>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    private static bool IsPortInvolved(RuntimeBinding binding, string instanceId, string portName) =>
        IsSamePort(binding.Source, instanceId, portName)
        || binding.Targets.Any(t => IsSamePort(t, instanceId, portName));

    /// <summary>该成员是否就是"该 (实例, 口)"（成员比对**唯一实现**）。</summary>
    /// <param name="member">网络成员。</param>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    private static bool IsSamePort(NetworkMember member, string instanceId, string portName) =>
        string.Equals(member.InstanceId, instanceId, StringComparison.Ordinal)
        && string.Equals(member.PortName, portName, StringComparison.Ordinal);

    /// <summary>
    /// **每 (实例, 口) 队列**的深度、吞吐与**原因账本**（框架速率计的「水位」）。
    /// </summary>
    /// <remarks>
    /// 搬运字节速率**按宿计**（第 2 档）：只有每口一条队列才能给出这个口径。
    /// 原因账本与队列自己读的是**同一次快照**（<c>TryGetFlowState</c>），不另算一套。
    /// </remarks>
    public IReadOnlyList<PortQueueDepthView> GetQueueDepths()
    {
        var now = Stopwatch.GetTimestamp();
        var views = new List<PortQueueDepthView>();

        foreach (var carrier in _runtime.Carriers.Carriers)
        {
            for (var index = 0; index < carrier.PortNames.Count; index++)
            {
                var port = carrier.PortNames[index];
                var queue = carrier.Queues[index];
                var meters = PortMetersOf($"{carrier.InstanceId}.{port}");

                // 契约尺寸：按该口所属网络的绑定反查（未建网 ⇒ 0 ⇒ 只给单元速率）
                var size = ResolveBusByteSize(carrier.InstanceId, port);

                _ = queue.TryGetFlowState(out var flow);

                views.Add(new PortQueueDepthView(
                    carrier.InstanceId,
                    port,
                    queue.Count,
                    queue.Capacity,
                    queue.Enqueued,
                    queue.Dropped)
                {
                    UnitRateHz = meters.Unit.Observe(queue.Enqueued, now),
                    BusByteRate = meters.Bus.Observe(queue.Enqueued * size, now),
                    PeakDepth = flow.PeakDepth,
                    DroppedEvictedUnits = queue.DroppedByReason(DropReason.QueueFullEvicted),
                    DroppedRejectedUnits = queue.DroppedByReason(DropReason.QueueFullRejected),
                    DroppedBytes = queue.DroppedBytes,
                    DiscardedOnStopUnits = queue.DroppedByReason(DropReason.DiscardedOnStop),
                    ModuleInternalDroppedUnits = queue.DroppedByReason(DropReason.ModuleInternal),
                    ExpiredUnits = queue.DroppedByReason(DropReason.Expired),
                    DroppedWhileSaturatedUnits = flow.DroppedWhileSaturatedUnits,
                    Saturated = flow.Saturated,
                    SaturatedFor = flow.SaturatedFor,
                    LastDropReason = flow.LastDropReason,
                    SinceLastDrop = flow.SinceLastDrop,
                });
            }
        }

        return views;
    }

    /// <inheritdoc />
    public PortFlowState GetPortFlowState(string instanceId, string portName) =>
        _runtime.TryGetPortFlow(instanceId, portName, out var state) ? state : PortFlowState.None;

    /// <summary>按接收口反查其所在绑定的契约尺寸（用于"按宿计"的搬运字节速率）。</summary>
    private int ResolveBusByteSize(string instanceId, string portName)
    {
        foreach (var binding in _runtime.Bindings.Bindings)
        {
            foreach (var target in binding.Targets)
            {
                if (string.Equals(target.InstanceId, instanceId, StringComparison.Ordinal)
                    && string.Equals(target.PortName, portName, StringComparison.Ordinal))
                {
                    return binding.Layout.SizeInBytes;
                }
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<IssueView> GetIssues(int maxCount) =>
        [.. _runtime.Issues.Recent(maxCount).Select(e => new IssueView(
            e.Sequence,
            e.ObjectPath,
            e.Issue.Category.ToString(),
            e.Issue.Severity.ToString(),
            e.Issue.Message,
            e.Timestamp,
            e.Issue.Source.ToString()))];

    /// <inheritdoc />
    public IssueSummaryView GetIssueSummary() => _runtime.Issues.Summary();

    /// <inheritdoc />
    public CommandResult RemoveIssues(IReadOnlyCollection<long> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);

        var removed = _runtime.Issues.Remove(sequences);
        return removed > 0
            ? CommandResult.Ok([CoreText.Get("core.services.issues.removed", removed)])
            : CommandResult.Ok();
    }

    /// <inheritdoc />
    public CommandResult ClearIssues()
    {
        _runtime.Issues.Clear();
        return CommandResult.Ok();
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleCatalogEntryView> GetModuleCatalog()
    {
        lock (_catalogGate)
        {
            if (_catalogCache is { } cached)
            {
                return cached;
            }

            var catalog = _registry.Definitions.Select(d =>
            {
                _ = _modulePackages.TryGetValue(d.Id.ToString(), out var package);
                var preview = package?.PackageDirectory is { Length: > 0 } directory
                    ? Path.Combine(directory, "preview.png")
                    : null;
                if (preview is not null && !File.Exists(preview))
                {
                    preview = null;
                }

                return new ModuleCatalogEntryView(
                    d.Id.ToString(),
                    d.DisplayName,
                    d.Description,
                    d.Capabilities.ToString(),
                    package?.Root,
                    package?.NeedsRestart ?? false,
                    d.Tags,
                    package?.IsExternal ?? false,
                    preview);
            }).ToList();

            _catalogCache = catalog;
            return catalog;
        }
    }
    /// <summary>
    /// 模块包的来源信息（**NeedsRestart 在扫描时算一次并缓存**）：
    /// 该判定要访问文件系统，若每次查询都现算，界面每帧刷新就会变成每秒数百次目录枚举。
    /// </summary>
    private sealed record ModulePackageOrigin(string Root, string PackageDirectory, bool NeedsRestart, bool IsExternal);

    /// <inheritdoc />
    public ApplicationSettings GetSettings() => _settings.Current.Clone();

    /// <inheritdoc />
    public StorageLocationsView GetStorageLocations() => new(
        StoragePaths.AppRoot,
        StoragePaths.ModulesRoot,
        StoragePaths.CompositesRoot,
        StoragePaths.SettingsFile,
        StoragePaths.WorkspaceRoot,
        StoragePaths.LogsRoot,
        StoragePaths.SeedsRoot);

    /// <inheritdoc />
    public IReadOnlyList<CompositeFileView> GetCompositeFiles() =>
        [.. CompositeFileStore.Scan(StoragePaths.CompositesRoot).Select(file =>
        {
            try
            {
                var inspection = _composites.Inspect(file.Path);
                return file with
                {
                    ModuleCount = inspection.ModuleCount,
                    MissingModules = inspection.MissingModules,
                    ReadError = inspection.Error,
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return file with { ReadError = CoreText.Get("core.services.composite.readFailed", ex.Message) };
            }
        })];

    /// <inheritdoc />
    public IReadOnlyList<SubscriptionSourceView> GetSubscriptionSources()
    {
        var sources = new List<SubscriptionSourceView>();
        foreach (var set in _runtime.Subscriptions)
        {
            foreach (var buffer in set.Buffers)
            {
                sources.Add(new SubscriptionSourceView(
                    buffer.InstanceId,
                    buffer.DataSourceName,
                    buffer.Kind,
                    buffer.Form == Views.SubscriptionBufferForm.Ring,
                    buffer.Capacity,
                    buffer.SampleSizeInBytes,
                    buffer.ValueType,
                    buffer.Written,
                    buffer.Overwritten,
                    buffer.Dropped)
                {
                    // 解码器句柄随视图交给呈现层（框架只回调，不内建解码）
                    Decoder = buffer.Decoder,
                    DataType = buffer.DataType,
                });
            }
        }

        return sources;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public long GetAppearanceRevision(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return _runtime.Content.TryGetInstance(instanceId, out var instance) && instance is not null
            ? instance.Context.AppearanceStore.Version
            : 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<AppearanceView> GetAppearance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        // 不存在 / 占位实例 ⇒ 空表（没设过外观就没有条目 —— "不设 = 全按主题"）
        return !_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null
            ? []
            : [.. instance.Context.AppearanceStore.Snapshot()
                .Select(e => new AppearanceView(
                    e.SourceName, e.Outline, e.Text, e.TextSize, e.Revision, e.ReadOnly,
                    e.OutlineRole, e.TextRole, e.Enabled))];
    }

    /// <inheritdoc />
    public IReadOnlyList<RoleView> GetRoles(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return [];   // 不存在 / 占位实例 ⇒ 无角色（叠加层不画方块）
        }

        var networks = _runtime.Content.Networks.ToList();

        // **角色指标的唯一来源 = 绑定集合**（不是网络合计）：
        // 多源网络里每条绑定按源归属；多宿绑定的 `PayloadBytes` 按源累计（速率不会被宿数放大）。
        // 早先按 `NetworkKey` 建 `byNetwork` 再让每个角色读同一份合计 ⇒ 同一网络里
        // 每个源角色都显示网络总和（空闲源与发送源完全相同），部分兼容时还会把
        // 没投到本宿的源也算进来。现在按“该角色选定侧的口所承载的绑定”求和。
        var now = Stopwatch.GetTimestamp();
        var bindings = _runtime.Bindings.Bindings;
        var liveKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            _ = liveKeys.Add(ModuleRuntime.DescribeSourceKey(binding.Source, binding.Targets));
        }

        string PeerRoleKeyOf(string peerInstanceId, string peerPortName)
        {
            _ = _runtime.Content.TryGetInstance(peerInstanceId, out var peer);
            var key = RoleModel.RoleKeyOf(peer, peerPortName);
            return key.Length > 0 ? key : $"{peerInstanceId}/?";
        }

        var views = new List<RoleView>(instance.Roles.Count);

        foreach (var role in instance.Roles)
        {
            var inbound = role.Inputs.Count == 0
                ? (int?)null
                : RoleModel.PeersOf(instance, role.Inputs, networks, PeerRoleKeyOf).Count;

            var outbound = role.Outputs.Count == 0
                ? (int?)null
                : RoleModel.PeersOf(instance, role.Outputs, networks, PeerRoleKeyOf).Count;

            var key = RoleModel.RoleKey(instanceId, role.Id);
            var typeKey = role.TypeId;
            var declaration = instance.FindDataType(role.TypeId);

            // **角色自身的量**：产出侧优先（有输出口 ⇒ 看它发出去多少；纯汇分块 ⇒ 看它收进来多少）。
            // **不把入/出两侧相加**：那是多条不同通路的和，会被读成"这块功能的总吞吐"（口径不清、无意义）。
            // 同一绑定只计一次：一个绑定投到同一角色的两个口时，不按口重复放大（速率按源计）。
            var outputSide = role.Outputs.Count > 0;
            var metricPorts = new HashSet<string>(outputSide ? role.Outputs : role.Inputs, StringComparer.Ordinal);

            var rate = 0.0;
            long delivered = 0;
            long dropped = 0;

            foreach (var binding in bindings)
            {
                var touches = outputSide
                    ? string.Equals(binding.Source.InstanceId, instanceId, StringComparison.Ordinal)
                        && metricPorts.Contains(binding.Source.PortName)
                    : binding.Targets.Any(t =>
                        string.Equals(t.InstanceId, instanceId, StringComparison.Ordinal)
                        && metricPorts.Contains(t.PortName));

                if (!touches)
                {
                    continue;
                }

                var diagnostic = BuildDiagnostic(binding, now);

                // 角色方块显示的是**有效载荷字节速率**（逐单元读有效长度字段；未声明 ⇒ 静态尺寸），
                // 与网络标签/端口标记同一口径。
                rate += diagnostic.PayloadByteRate;
                delivered += diagnostic.Delivered;
                dropped += diagnostic.DropTransport;
            }

            views.Add(new RoleView(
                key,
                role.Id,
                role.DisplayName ?? string.Empty,
                typeKey,
                RoleModel.TypeTextOf(role, instance),
                role.Inputs,
                role.Outputs,
                inbound,
                outbound,
                declaration is null ? string.Empty : string.Join(" / ", declaration.SemanticTags),
                declaration?.Metering.NominalRateHz,
                rate,
                delivered,
                dropped));
        }

        // 与 `GetDiagnostics` / `GetPortMetrics` 同一份清理判据（本查询可能单独被界面调用）。
        PruneStaleRateMeters(liveKeys);

        return views;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoleFlowView> GetRoleFlows(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return [];
        }

        string RoleKeyOf(string otherInstanceId, string portName)
        {
            _ = _runtime.Content.TryGetInstance(otherInstanceId, out var other);
            return RoleModel.RoleKeyOf(other, portName);
        }

        var flows = RoleModel.FlowsOf(instance, _runtime.DescribePortLinks(), RoleKeyOf);

        return [.. flows
            .OrderBy(f => f.Key.Local, StringComparer.Ordinal)
            .ThenBy(f => f.Key.PeerInstance, StringComparer.Ordinal)
            .ThenBy(f => f.Key.PeerRole, StringComparer.Ordinal)
            .Select(f => new RoleFlowView(
                f.Key.Local,
                f.Key.LocalIsSend,
                f.Key.PeerInstance,
                f.Key.PeerRole,
                f.Value.Count,
                [.. f.Value.Networks.OrderBy(k => k, StringComparer.Ordinal)]))];
    }

    /// <inheritdoc />
    public IReadOnlyList<RoleOrderView> GetRoleOrder(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return [];
        }

        return [.. instance.Flows.Select(f => new RoleOrderView(
            RoleModel.RoleKey(instanceId, f.FromRoleId),
            RoleModel.RoleKey(instanceId, f.ToRoleId),
            f.Label))];
    }

    /// <summary>取端口实例（连接枚举专用；调用前已确认该口存在）。</summary>
    private Hosting.Instances.PortInstance FindPortOrThrow(Composition.NetworkMember member) =>
        _runtime.Content.TryGetInstance(member.InstanceId, out var instance) && instance is not null
            ? instance.FindPort(member.PortName) ?? throw new InvalidOperationException(CoreText.Get("core.runtime.portMissing", member.InstanceId, member.PortName))
            : throw new InvalidOperationException(CoreText.Get("core.runtime.instanceMissing", member.InstanceId));

    /// <inheritdoc />
    public bool TryReadLatest(string instanceId, string dataSourceName, Span<byte> destination, out int length)
    {
        length = 0;
        return _runtime.TryGetSubscriptions(instanceId, out var set)
            && set is not null
            && set.TryGet(dataSourceName, out var buffer)
            && buffer is not null
            && buffer.TryReadLatest(destination, out length);
    }

    /// <inheritdoc />
    public int ReadSeries(string instanceId, string dataSourceName, Span<byte> destination, int maxSamples) =>
        _runtime.TryGetSubscriptions(instanceId, out var set) && set is not null
        && set.TryGet(dataSourceName, out var buffer) && buffer is not null
            ? buffer.ReadSeries(destination, maxSamples)
            : 0;

    /// <inheritdoc />
    public bool TryGetConfigurationSchema(string instanceId, out ConfigurationSchemaView? schema)
    {
        schema = null;

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            // 占位实例：定义缺失 → 没有可编辑字段（配置片段原样保留在组合件里）
            if (_runtime.Content.TryGetPlaceholder(instanceId, out _))
            {
                schema = new ConfigurationSchemaView(instanceId, [], CoreText.Get("core.services.config.placeholderSchemaUnavailable"));
                return true;
            }

            return false;
        }

        var version = instance.Context.ConfigurationVersion;
        var language = CultureInfo.CurrentUICulture.Name;
        var snapshot = instance.Context.ConfigurationSnapshot;
        var choicesRevision = ChoicesRevisionOf(snapshot);

        lock (_schemaCacheGate)
        {
            if (_schemaCache.TryGetValue(instanceId, out var cached)
                && cached.Version == version
                && string.Equals(cached.Language, language, StringComparison.Ordinal)
                && cached.ChoicesRevision == choicesRevision)
            {
                schema = cached.Schema;
                return true;
            }

            schema = ConfigurationSchemaBuilder.Build(
                instanceId,
                snapshot,
                instance.Module.GetType().Assembly);

            _schemaCache[instanceId] = (version, language, choicesRevision, schema);
            return true;
        }
    }

    /// <summary>动态可选项的版本号（无动态字段时为 0）。</summary>
    private static long ChoicesRevisionOf(IReadOnlyDictionary<string, object> snapshot)
    {
        long revision = 0;

        foreach (var configuration in snapshot.Values)
        {
            if (configuration is not IConfigurationChoices choices)
            {
                continue;
            }

            try
            {
                revision = unchecked((revision * 31) + choices.GetChoicesRevision());
            }
            catch (Exception)
            {
                // 动态提供者不得打断 Schema 读取；拿不到版本就按不变处理。
            }
        }

        return revision;
    }

    /// <inheritdoc />
    public CommandResult UpdateConfigurationField(string instanceId, string fieldName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(value);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return CommandResult.Fail([CoreText.Get("core.services.instanceMissingOrPlaceholder", instanceId)]);
        }

        if (!ConfigurationSchemaBuilder.TryCreateUpdated(
                instance.Context.ConfigurationSnapshot,
                fieldName,
                value,
                out var updated,
                out var failure) || updated is null)
        {
            return CommandResult.Fail([failure ?? CoreText.Get("core.services.config.fieldWriteFailed")]);
        }

        // 走变更入口：一次 ChangeSet、只触发一次配置变更通知
        return Map(_runtime.Apply(ChangeSet.OfOne(new UpdateConfigurationsOperation(instanceId, [updated]))));
    }

    /// <inheritdoc />
    public bool TryGetConfigurationJson(string instanceId, out string? json)
    {
        json = null;

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return false;
        }

        var root = new JsonObject();
        foreach (var (typeName, value) in instance.Context.ConfigurationSnapshot)
        {
            if (JsonSerializer.SerializeToNode(value, value.GetType(), ConfigurationJson.Options) is { } node)
            {
                root[typeName] = node;
            }
        }

        json = root.ToJsonString(ConfigurationJson.Options);
        return true;
    }

    // ---------------------------------------------------------------- 内部

    private ModuleInstanceView ToView(Hosting.Instances.ModuleInstance instance)
    {
        var ports = new List<PortView>(instance.Ports.Count);
        foreach (var port in instance.Ports)
        {
            var connected = IsPortConnected(instance.InstanceId, port.Name);
            var stalled = IsPortStalled(instance.InstanceId, port.Name);
            // **端口几何**：用户拖动覆盖 > 模块代码声明的默认位置（没有惯例回退）。
            // `recorded` 区分"工作区里有这个口的记录"与"从没动过"（后者才用声明的 offset）。
            var recorded = _runtime.Content.Layout.TryGetPort(instance.InstanceId, port.Name, out var stored);
            var geometry = Composition.PortGeometryResolution.Resolve(
                port.Declaration, stored, recorded);
            ports.Add(new PortView(
                port.Name,
                port.Direction.ToString(),
                port.Declaration.Shape.SizeInBytes,
                connected,
                stalled,
                geometry.Offset,
                // 【G5 · C5-7】**有效「固定」态**：用户设置过（三态里的具体值）优先；
                // 没设置过 ⇒ 看模块声明（默认固定）。
                Composition.PortGeometryResolution.ResolveAnchorFixed(
                    geometry.IsAnchorFixed,
                    port.Declaration.AnchorDraggable),
                geometry.Label,
                port.Declaration.Shape.Describe())
            {
                // 【G5 · C5-7】**有效挂载边**：用户拖动覆盖 > 包内声明（没有惯例回退）；
                // 已在上面一次解析完，这里直接用。
                Edge = geometry.Edge,
                // **类型身份**（色板键）：已声明 ⇒ 类型标识；未声明 ⇒ 结构描述。
                // 与角色键**同一来源**（`DescribePortTypeKey`）⇒ 端口标记的窄色条与角色方块填充色永远一致。
                TypeKey = _runtime.DescribePortTypeKey(instance.InstanceId, port),
            });
        }

        return new ModuleInstanceView(
            instance.InstanceId,
            instance.Definition.Id.ToString(),
            instance.Definition.DisplayName,
            instance.State.ToString(),
            instance.ViewType is not null,
            ports,
            IsPlaceholder: false,
            instance.ViewType,
            new ModuleWindowView(
                instance.Definition.WindowWidth,
                instance.Definition.WindowHeight,
                instance.Definition.MinWidth,
                instance.Definition.MinHeight),
            WallpaperRevision: _runtime.GetWallpaperRevision(instance.InstanceId));
    }

    /// <summary>占位实例视图（模块定义缺失/版本不兼容）：无活动、无口，只有 ID 与声明归属。</summary>
    private static ModuleInstanceView ToView(PlaceholderInstance placeholder) =>
        new(placeholder.InstanceId,
            placeholder.ModuleIdLiteral,
            placeholder.ModuleIdLiteral,
            RuntimeStateNames.Placeholder,
            HasView: false,
            Ports: [],
            IsPlaceholder: true);

    /// <summary>
    /// 口是否**已加入网络**（**唯一来源** = 网络成员关系）。
    /// </summary>
    /// <remarks>
    /// **为什么不用绑定**：停流网络（全部源未锚定）**不建立绑定**，但它在**声明上仍然是「连着」的**
    /// —— 界面需要看到「声明上连给谁」。用绑定判会出现「停流 ⇒ 显示未连」，与 UI
    /// （按 `GetNetworks()` 判定）**两处口径不一致**；**所有数据指标必须来源唯一**
    /// ⇒ 统一到**网络成员关系**这一处。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    private bool IsPortConnected(string instanceId, string portName) =>
        FindPortNetwork(instanceId, portName) is not null;

    /// <summary>口是否**停流**（唯一来源 = 所在网络的 <c>Network.IsStalled</c>，其本身是 `Sources.Count == 0` 的派生量）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    private bool IsPortStalled(string instanceId, string portName) =>
        FindPortNetwork(instanceId, portName)?.IsStalled ?? false;

    /// <summary>取口所在的网络（**声明成员关系**，含停流网络）；未入网 ⇒ <see langword="null"/>。</summary>
    /// <remarks>
    /// 一个口在**合法**情形下只属于一条网络（同名标签唯一 / 对齐建网唯一），但多源网络的宿成员可同时出现在
    /// 多条网络里 ⇒ 命中多条时取**第一条命中**（呈现口径与 `ModuleWindowsViewModel` 的标记刷新一致）。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    private Composition.Network? FindPortNetwork(string instanceId, string portName)
    {
        foreach (var network in _runtime.Content.Networks)
        {
            foreach (var member in network.Members)
            {
                if (string.Equals(member.InstanceId, instanceId, StringComparison.Ordinal)
                    && string.Equals(member.PortName, portName, StringComparison.Ordinal))
                {
                    return network;
                }
            }
        }

        return null;
    }

    private static CommandResult Map(ChangeSetResult result) =>
        result.Applied ? CommandResult.Ok(result.Notices) : CommandResult.Fail(result.Failures);
}
