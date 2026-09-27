using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Binding;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Diagnostics;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Core.Workspace;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Registry;
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

    /// <summary>绑定的速率计（键 = 绑定身份键）——**框架侧唯一的速率口径来源**（ADR-0046 D8）。</summary>
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

    /// <summary>本进程启动时刻（判定"包在本次启动后重新构建过"用 —— ADR-0036 D4）。</summary>
    private readonly DateTime _processStartUtc = DateTime.UtcNow;

    /// <summary>创建服务。</summary>
    /// <param name="runtime">运行时。</param>
    /// <param name="settings">设置服务（缺省：应用目录下的 <c>settings.json</c>，未加载时使用默认值）。</param>
    public RuntimeServices(ModuleRuntime runtime, SettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _runtime = runtime;
        _settings = settings ?? new SettingsService(runtime, Path.Combine(AppContext.BaseDirectory, "settings.json"));
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
    /// 扫描**模块来源根列表**并装入其中的模块包（ADR-0036 D1）。
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
    public CommandResult RescanModuleSources()
    {
        var roots = NormalizeRoots([.. _settings.Current.ModuleSourceDirectories]);
        var installed = ScanModuleSources(roots);

        var failed = _moduleSources.Sum(s => s.FailedPackages);
        var message = installed > 0
            ? $"重扫完成：新装入 {installed} 个模块包"
            : "重扫完成：没有发现新的模块包（已装入的模块若要更新代码，需重启应用）";

        return CommandResult.Ok(failed > 0 ? [$"{message}；{failed} 个包装入失败（见 Issue）"] : [message]);
    }

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
        [.. _runtime.Content.Networks.Select(n => new NetworkView(
            n.Name,
            IsAnonymous: !n.IsNamed,
            n.IsAutoGenerated,
            n.IsStalled,
            n.Source.ToString(),
            [.. n.Targets.Select(t => t.ToString())],
            [.. n.UnresolvedMembers.Select(m => m.ToString())])
        {
            // 【P40】**网络身份键**：视图层靠它配对（网络名不唯一：匿名网络名为空串）
            Key = ModuleRuntime.DescribeKey(n),

            // 【ADR-0050 M2/M5】多源是一等组合：暴露**全部声明的源**（在场的在前 + 悬空的声明在后，
            // 各组内字典序稳定）；**在场与否**看 IsStalled 与逐源诊断（R6 呈现时再细分）
            Sources = [.. n.Sources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal),
                       .. n.UnresolvedSources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal)],
        })];

    /// <inheritdoc />
    public IReadOnlyList<BindingDiagnosticView> GetDiagnostics()
    {
        var now = Stopwatch.GetTimestamp();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var views = new List<BindingDiagnosticView>(_runtime.Bindings.Bindings.Count);

        foreach (var b in _runtime.Bindings.Bindings)
        {
            // 【ADR-0050 M2/M7】诊断/绑定的键 = **单源 → 宿子集**（多源网络下**按源归属**）
            var key = ModuleRuntime.DescribeSourceKey(b.Source, b.Targets);
            _ = seen.Add(key);
            var meters = MetersOf(key);

            var payloadSize = b.Metering.PayloadSizeInBytes ?? b.Layout.SizeInBytes;

            views.Add(new BindingDiagnosticView(
                b.Network.Name,
                b.Diagnostics.Delivered,
                b.Diagnostics.Dropped,
                b.Diagnostics.LastDataTime)
            {
                Key = key,

                // 【ADR-0046 D8 / ADR-0050 Q5】丢弃按原因分类（停流未投递 ≠ 传输丢弃）
                DropStalled = b.Diagnostics.DroppedByReason(DropReason.Stalled),
                DropQueueFull = b.Diagnostics.DroppedByReason(DropReason.QueueFull),
                DropNoCarrier = b.Diagnostics.DroppedByReason(DropReason.NoCarrier),
                DropInvalidPayload = b.Diagnostics.DroppedByReason(DropReason.InvalidPayload),

                // **三类之一：传输丢弃**（队列满 + 无承载 + 载荷非法；不含"停流未投递"）
                DropTransport = b.Diagnostics.DroppedByReason(DropReason.QueueFull)
                    + b.Diagnostics.DroppedByReason(DropReason.NoCarrier)
                    + b.Diagnostics.DroppedByReason(DropReason.InvalidPayload),

                // 【ADR-0046 D8】**四档速率由框架给出**（UI 不得自行差分估算）
                Published = b.Diagnostics.Published,
                NetworkKey = ModuleRuntime.DescribeKey(b.Network),
                UnitRateHz = meters.Unit.Observe(b.Diagnostics.Published, now),
                BusByteRate = meters.Bus.Observe(b.Diagnostics.Delivered * b.Layout.SizeInBytes, now),
                PayloadByteRate = meters.Payload.Observe(b.Diagnostics.Published * payloadSize, now),
                SampleRateHz = meters.Unit.Rate * b.Metering.SamplesPerUnit,
                ChannelSampleRateHz = meters.Unit.Rate * b.Metering.SamplesPerUnit * b.Metering.ChannelCount,
                NominalRateHz = b.Metering.NominalRateHz,
            });
        }

        // 绑定被重建/删除后，旧键的速率计一并清掉（否则长期运行会积累无主状态）
        foreach (var stale in _rateMeters.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _ = _rateMeters.Remove(stale);
        }

        return views;
    }

    /// <summary>
    /// **每 (实例, 口) 队列**的深度与吞吐【ADR-0050 Q2 / ADR-0046 D8"水位"】。
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
                    // 【ADR-0046 D1 ③】解码器句柄随视图交给呈现层（框架只回调，不内建解码）
                    Decoder = buffer.Decoder,
                    DataType = buffer.DataType,
                });
            }
        }

        return sources;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public IReadOnlyList<RoleView> GetRoles(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (!_runtime.Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return [];   // 不存在 / 占位实例 ⇒ 无角色（叠加层不画方块）
        }

        var networks = _runtime.Content.Networks.ToList();

        // **按网络聚合的通路诊断**（键 = `NetworkKey`）——角色维度的速率/投递/丢弃由它聚合而来（ADR-0053）。
        // 【实测踩坑】`BindingDiagnosticView.Key` 是**逐源键**（`源 → 宿子集`，多源下按源归属），
        // 与 **`NetworkKey`**（= `DescribeKey(网络)`）**不是同一个键**：早先按 `Key` 建表、用网络键查 ⇒ 永远查不到
        // ⇒ 角色的速率恒为 0（界面上恒显示占位符）。⇒ 这里一律用 `NetworkKey` 建表（一个网络把所有源加起来）。
        var byNetwork = new Dictionary<string, (double Rate, long Delivered, long Dropped)>(StringComparer.Ordinal);

        foreach (var diagnostic in GetDiagnostics())
        {
            if (diagnostic.NetworkKey is not { Length: > 0 } networkKey)
            {
                continue;
            }

            byNetwork.TryGetValue(networkKey, out var sum);
            byNetwork[networkKey] = (
                sum.Rate + diagnostic.BusByteRate,
                sum.Delivered + diagnostic.Delivered,
                sum.Dropped + diagnostic.DropTransport);
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
            var metricPorts = role.Outputs.Count > 0 ? role.Outputs : role.Inputs;

            var rate = 0.0;
            long delivered = 0;
            long dropped = 0;

            foreach (var networkKey in RoleModel.NetworksOf(instance, metricPorts, networks, ModuleRuntime.DescribeKey))
            {
                if (byNetwork.TryGetValue(networkKey, out var sum))
                {
                    rate += sum.Rate;
                    delivered += sum.Delivered;
                    dropped += sum.Dropped;
                }
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
            var geometry = _runtime.Content.Layout.GetPort(instance.InstanceId, port.Name);
            ports.Add(new PortView(
                port.Name,
                port.Direction.ToString(),
                port.Declaration.Shape.SizeInBytes,
                connected,
                stalled,
                geometry.Offset,
                geometry.IsLocked,
                geometry.Label,
                port.Declaration.Shape.Describe())
            {
                // 【ADR-0048 R2 / ADR-0049 D6】**类型身份**（色板键）：已声明 ⇒ `标识@v版本`；未声明 ⇒ 结构描述。
                // 与角色键**同一来源**（`DescribePortTypeKey`）⇒ 端口标记的窄色条与角色方块填充色永远一致。
                TypeKey = _runtime.DescribePortTypeKey(instance.InstanceId, port),
            });
        }

        return new ModuleInstanceView(
            instance.InstanceId,
            instance.Definition.Id.ToString(),
            instance.Definition.DisplayName,
            instance.State.ToString(),
            instance.View is not null,
            ports,
            IsPlaceholder: false,
            instance.View);
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
    /// 口是否**已加入网络**（ADR-0048/0049 的**唯一来源** = 网络成员关系）。
    /// </summary>
    /// <remarks>
    /// **为什么不用绑定**：停流网络（全部源未锚定）**不建立绑定**，但它在**声明上仍然是"连着"的**
    /// —— 用户需要看到"本来连给谁"（ADR-0050 M5）。用绑定判会出现"停流 ⇒ 显示未连"，与 UI
    /// （按 `GetNetworks()` 判定）**两处口径不一致**；用户裁定"**所有数据指标必须保证来源唯一**"
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
