using System.Diagnostics;
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
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk;
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

    /// <summary>创建服务。</summary>
    /// <param name="runtime">运行时。</param>
    /// <param name="settings">设置服务（缺省：用户数据层下的 `settings.json`，见 `StoragePaths`；未加载时使用默认值）。</param>
    public RuntimeServices(ModuleRuntime runtime, SettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _runtime = runtime;
        _settings = settings ?? new SettingsService(runtime, StoragePaths.SettingsFile);
        _installer = new ModuleInstaller(_registry, runtime.Issues);
        _composites = new CompositeService(runtime, _registry, runtime.Issues);
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

        if (_installer.TryInstall(packageDirectory, out _))
        {
            return CommandResult.Ok();
        }

        var recent = _runtime.Issues.Recent(1);
        return CommandResult.Fail([recent.Count > 0 ? recent[0].Issue.Message : "模块包加载失败"]);
    }

    /// <summary>
    /// 扫描**模块来源根列表**并装入其中的模块包。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>按列表**顺序**逐根扫描；每根只扫**直接子目录**中含模块清单文件者（**不递归、不扫任意位置**）；</item>
    ///   <item>同 ID 同版本重复 → **先到者胜**，后者记 **Info**（不刷 Warning）；同 ID 不同版本 → 共存；</item>
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
                results.Add(new ModuleSourceView(root, Exists: false, 0, 0, 0, [$"目录不存在（已跳过）"]));
                continue;
            }

            var messages = new List<string>();
            var found = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!File.Exists(Path.Combine(directory, "module.json")))
                {
                    skipped++;
                    continue;
                }

                found++;
                var name = Path.GetFileName(directory);

                if (!_installer.TryInstall(directory, out var definition) || definition is null)
                {
                    // 失败原因已由安装器记 Issue 并归属 package:<目录>
                    failed++;
                    messages.Add($"✗ {name}：装入失败（见 Issue）");
                    continue;
                }

                var packageDirectory = Path.GetFullPath(directory);
                _modulePackages[definition.Id.ToString()] = new ModulePackageOrigin(root, packageDirectory, NeedsRestart(packageDirectory));
                installed++;

                var restart = NeedsRestart(directory) ? "（**该包在本次启动后重新构建，重启应用才生效**）" : string.Empty;
                messages.Add($"✓ {name} → {definition.Id}{restart}");
            }

            results.Add(new ModuleSourceView(root, Exists: true, found, skipped, failed, messages));
        }

        _moduleSources = results;
        return installed;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleSourceView> GetModuleSources() => _moduleSources;

    /// <inheritdoc />
    public IReadOnlyList<ConnectionCandidateView> PreviewConnections(IReadOnlyList<ModuleLayoutView>? provisionalLayouts = null) =>
        [.. _runtime.CollectDockPairs(provisionalLayouts)
            .Select(pair => new ConnectionCandidateView(pair.SourceLiteral, pair.TargetLiteral))];

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
            ? $"重扫完成：新装入 {installed} 个模块包"
            : "重扫完成：没有发现新的模块包（已装入的模块若要更新代码，需重启应用）";

        return CommandResult.Ok(failed > 0 ? [$"{message}；{failed} 个包装入失败（见 Issue）"] : [message]);
    }

    /// <summary>
    /// **本次进程扫描的根列表**（唯一来源）：内置包目录（若已注入）**排首位**，其后是设置里的用户根。
    /// </summary>
    /// <remarks>
    /// 启动与「重新加载」**必须**都走这里 —— 各写一份 ⇒ 重扫会把内置根丢掉（见
    /// <see cref="RescanModuleSources"/> 的说明）。
    /// </remarks>
    public IReadOnlyList<string> ResolveScanRoots()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(BuiltInModuleDirectory))
        {
            roots.Add(BuiltInModuleDirectory);
        }

        roots.AddRange(_settings.Current.ModuleSourceDirectories);

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

    /// <summary>去重并保持顺序（忽略空白项）。</summary>
    private static List<string> NormalizeRoots(IReadOnlyList<string>? sourceDirectories)
    {
        var roots = new List<string>();
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

    /// <summary>该包是否在**本进程启动之后**被重新构建过（决定是否提示"需重启"）。</summary>
    private bool NeedsRestart(string packageDirectory)
    {
        try
        {
            var newest = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return newest > _processStartUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public CommandResult AddModule(string instanceId, string moduleIdLiteral)
    {
        if (!ModuleId.TryParse(moduleIdLiteral, out var moduleId)
            || !_registry.TryResolve(moduleId, out var definition)
            || definition is null)
        {
            return CommandResult.Fail([$"模块未注册：{moduleIdLiteral}（请先加载模块包）"]);
        }

        return Map(_runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, definition))));
    }

    /// <inheritdoc />
    public CommandResult RemoveModule(string instanceId) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new RemoveModuleOperation(instanceId))));

    /// <inheritdoc />
    public CommandResult SetModuleEnabled(string instanceId, bool enabled) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new SetModuleEnabledOperation(instanceId, enabled))));

    /// <inheritdoc />
    public CommandResult Connect(
        PortRef source,
        IReadOnlyList<PortRef> targets,
        string? name = null,
        bool allowUnverifiedTypeMatch = false)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var sourceMember = new NetworkMember(source.InstanceId, source.PortName, PortDirection.Send);
        var targetMembers = targets
            .Select(t => new NetworkMember(t.InstanceId, t.PortName, PortDirection.Receive))
            .ToList();

        return Map(_runtime.Apply(ChangeSet.OfOne(
            new ConnectNetworkOperation(sourceMember, targetMembers, name, allowUnverifiedTypeMatch))));
    }

    /// <inheritdoc />
    public CommandResult Disconnect(string networkName) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new DisconnectNetworkOperation(networkName))));

    /// <inheritdoc />
    public CommandResult UpdateConfiguration(string instanceId, object configuration) =>
        Map(_runtime.Apply(ChangeSet.OfOne(new UpdateConfigurationOperation(instanceId, configuration))));

    /// <inheritdoc />
    public CommandResult UpdateSettings(ApplicationSettings settings) => _settings.Update(settings);

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
        _composites.Save(filePath, name, instanceIds);

    /// <inheritdoc />
    public CommandResult LoadComposite(string filePath) => _composites.Load(filePath);

    /// <inheritdoc />
    public CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral) =>
        _composites.ResolvePlaceholder(instanceId, moduleIdLiteral);

    /// <inheritdoc />
    public CommandResult UpdateConfigurationJson(string instanceId, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return CommandResult.Fail([$"实例不存在：{instanceId}"]);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return CommandResult.Fail(["配置 JSON 为空"]);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException ex)
        {
            return CommandResult.Fail([$"配置 JSON 无法解析：{ex.Message}"]);
        }

        if (root is null)
        {
            return CommandResult.Fail(["配置 JSON 的根必须是对象（键 = 配置类型完整名）"]);
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
                failures.Add($"配置类型无法解析：{typeName}");
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
                    failures.Add($"配置项 {typeName} 反序列化为空");
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                failures.Add($"配置项 {typeName} 无法解析：{ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return CommandResult.Fail(failures);
        }

        if (pending.Count == 0)
        {
            return CommandResult.Ok(["配置为空，无变更"]);
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
        var views = new List<ModuleInstanceView>(_runtime.Content.InstanceCount + _runtime.Content.PlaceholderCount);
        views.AddRange(_runtime.Content.Instances.Select(ToView));
        views.AddRange(_runtime.Content.Placeholders.Select(ToView));
        return views;
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
    public IReadOnlyList<NetworkView> GetNetworks() =>
        [.. _runtime.Content.Networks.Select(n =>
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
                n.IsAutoGenerated,
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
        })];

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
            DropQueueFull = b.Diagnostics.DroppedByReason(DropReason.QueueFull),
            DropNoCarrier = b.Diagnostics.DroppedByReason(DropReason.NoCarrier),
            DropInvalidPayload = b.Diagnostics.DroppedByReason(DropReason.InvalidPayload),

            // **三类之一：传输丢弃**（队列满 + 无承载 + 载荷非法；不含"停流未投递"）
            DropTransport = b.Diagnostics.DroppedByReason(DropReason.QueueFull)
                + b.Diagnostics.DroppedByReason(DropReason.NoCarrier)
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
    /// **每 (实例, 口) 队列**的深度与吞吐（框架速率计的「水位」）。
    /// </summary>
    /// <remarks>搬运字节速率**按宿计**（第 2 档）：只有每口一条队列才能给出这个口径。</remarks>
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
                });
            }
        }

        return views;
    }

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
    public IReadOnlyList<ModuleCatalogEntryView> GetModuleCatalog() =>
        [.. _registry.Definitions.Select(d => new ModuleCatalogEntryView(
            d.Id.ToString(),
            d.DisplayName,
            d.Description,
            d.Capabilities.ToString(),
            _modulePackages.TryGetValue(d.Id.ToString(), out var package) ? package.Root : null,
            package?.NeedsRestart ?? false,
            d.Tags))];

    /// <summary>
    /// 模块包的来源信息（**NeedsRestart 在扫描时算一次并缓存**）：
    /// 该判定要访问文件系统，若每次查询都现算，界面每帧刷新就会变成每秒数百次目录枚举。
    /// </summary>
    private sealed record ModulePackageOrigin(string Root, string PackageDirectory, bool NeedsRestart);

    /// <inheritdoc />
    public ApplicationSettings GetSettings() => _settings.Current.Clone();

    /// <inheritdoc />
    public IReadOnlyList<ModuleLayoutView> GetLayout() => _runtime.Content.Layout.Modules;

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
    public IReadOnlyList<AppearanceView> GetAppearance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        // 不存在 / 占位实例 ⇒ 空表（没设过外观就没有条目 —— "不设 = 全按主题"）
        return !_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null
            ? []
            : [.. instance.Context.AppearanceStore.Snapshot()
                .Select(e => new AppearanceView(
                    e.SourceName, e.Outline, e.Text, e.TextSize, e.Revision, e.ReadOnly, e.OutlineRole, e.TextRole))];
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
            var typeKey = $"{role.Type.TypeId}@v{role.Type.Version}";
            var declaration = instance.FindDataType(role.Type);

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
            ? instance.FindPort(member.PortName) ?? throw new InvalidOperationException($"口不存在：{member}")
            : throw new InvalidOperationException($"实例不存在：{member.InstanceId}");

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
                schema = new ConfigurationSchemaView(instanceId, [], "占位实例：模块定义缺失，无法生成配置表单");
                return true;
            }

            return false;
        }

        schema = ConfigurationSchemaBuilder.Build(instanceId, instance.Context.ConfigurationSnapshot);
        return true;
    }

    /// <inheritdoc />
    public CommandResult UpdateConfigurationField(string instanceId, string fieldName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(value);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return CommandResult.Fail([$"实例不存在或为占位实例：{instanceId}"]);
        }

        if (!ConfigurationSchemaBuilder.TryCreateUpdated(
                instance.Context.ConfigurationSnapshot,
                fieldName,
                value,
                out var updated,
                out var failure) || updated is null)
        {
            return CommandResult.Fail([failure ?? "按字段写回失败"]);
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
                // **类型身份**（色板键）：已声明 ⇒ `标识@v版本`；未声明 ⇒ 结构描述。
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
                instance.Definition.MinHeight));
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
