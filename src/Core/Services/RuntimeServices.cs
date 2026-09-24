using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
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
    public CommandResult Connect(PortRef source, IReadOnlyList<PortRef> targets, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var sourceMember = new NetworkMember(source.InstanceId, source.PortName, PortDirection.Send);
        var targetMembers = targets
            .Select(t => new NetworkMember(t.InstanceId, t.PortName, PortDirection.Receive))
            .ToList();

        return Map(_runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(sourceMember, targetMembers, name))));
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
            [.. n.UnresolvedMembers.Select(m => m.ToString())]))];

    /// <inheritdoc />
    public IReadOnlyList<BindingDiagnosticView> GetDiagnostics() =>
        [.. _runtime.Bindings.Bindings.Select(b => new BindingDiagnosticView(
            b.Network.Name,
            b.Diagnostics.Delivered,
            b.Diagnostics.Dropped,
            b.Diagnostics.LastDataTime))];

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
                    buffer.Dropped));
            }
        }

        return sources;
    }

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
            var geometry = _runtime.Content.Layout.GetPort(instance.InstanceId, port.Name);
            ports.Add(new PortView(
                port.Name,
                port.Direction.ToString(),
                port.Declaration.Shape.SizeInBytes,
                connected,
                port.IsStalled,
                geometry.Offset,
                geometry.IsLocked,
                geometry.Label,
                port.Declaration.Shape.Describe()));
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

    private bool IsPortConnected(string instanceId, string portName)
    {
        foreach (var binding in _runtime.Bindings.Bindings)
        {
            foreach (var member in binding.Network.Members)
            {
                if (string.Equals(member.InstanceId, instanceId, StringComparison.Ordinal)
                    && string.Equals(member.PortName, portName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static CommandResult Map(ChangeSetResult result) =>
        result.Applied ? CommandResult.Ok(result.Notices) : CommandResult.Fail(result.Failures);
}
