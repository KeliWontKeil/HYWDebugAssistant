using HYWDebugAssistant.Core.Adapters;
using HYWDebugAssistant.Core.Binding;
using RuntimeBinding = HYWDebugAssistant.Core.Binding.Binding;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Core.Workspace;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Context;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Lifecycle;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Runtime;

/// <summary>
/// 模块运行时：把 **L2 宿主**（加载/实例/生命周期）、**L3 数据面**（承载/投递）与
/// **L4 管理面**（组合、绑定、变更、Issue）接在一起的门面。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>变更入口串行化</b>：<see cref="Apply"/> 内单闸门；**不与应用面争锁**（数据面投递读不可变快照）；</item>
///   <item><b>两阶段</b>：Prepare（校验 + 构建计划，**可失败且零副作用**）→ Commit（**无失败点**、先加后减）；</item>
///   <item><b>数据直通</b>：<see cref="Deliver"/> 把单元直接写入目标实例队列，Core 不经手数据（P2/E1）；</item>
///   <item><b>增量重建</b>：连接增删只改绑定快照，**模块实例与状态保留**。</item>
/// </list>
/// <para>持久化（组合件捕获与降级式加载）见 <c>ModuleRuntime.Persistence.cs</c>。</para>
/// </remarks>
public sealed partial class ModuleRuntime : IDisposable
{
    private readonly Lock _changeGate = new();
    private readonly Dictionary<string, ModuleCarrierAdapter> _adapters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Views.SubscriptionSet> _subscriptions = new(StringComparer.Ordinal);
    private readonly ModuleLifecycleController _lifecycle;

    private BindingSet _bindings = BindingSet.Empty;
    private bool _disposed;

    /// <summary>创建运行时。</summary>
    /// <param name="issues">Issue 中心（缺省新建）。</param>
    /// <param name="carrierOptions">承载参数（队列容量/丢弃策略/停止超时）。</param>
    public ModuleRuntime(
        IssueCenter? issues = null,
        CarrierOptions? carrierOptions = null)
    {
        Issues = issues ?? new IssueCenter();
        Carriers = new CarrierHost(options: carrierOptions);
        Content = new WorkspaceContent();
        _lifecycle = new ModuleLifecycleController(Issues);
    }

    /// <summary>Issue 中心（框架判定与模块报告的统一收集点）。</summary>
    public IssueCenter Issues { get; }

    /// <summary>数据面宿主（承载者与缓冲池）。</summary>
    public CarrierHost Carriers { get; }

    /// <summary>当前工作现场内容（实例 + 网络）。</summary>
    public WorkspaceContent Content { get; }

    /// <summary>
    /// **对齐自动建网**开关（由应用级设置驱动，即时生效）——ADR-0037：
    /// 开 = "接触且对齐"的未打标签口自动连上；关 = 只做"标签同名"的命名网络。
    /// </summary>
    public bool AlignAutoNetwork { get; private set; } = true;

    /// <summary>更新"对齐自动建网"开关（**即时生效**）。</summary>
    /// <param name="enabled">是否启用。</param>
    public void UpdateAlignAutoNetwork(bool enabled) => AlignAutoNetwork = enabled;

    /// <summary>当前绑定集（**不可变快照**；投递路径读它）。</summary>
    public BindingSet Bindings => Volatile.Read(ref _bindings);

    // ---------------------------------------------------------------- 变更（两阶段）

    /// <summary>应用一次变更集。</summary>
    public ChangeSetResult Apply(ChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_changeGate)
        {
            if (changeSet.IsEmpty)
            {
                return ChangeSetResult.Success();
            }

            // ① Prepare：可失败、零副作用
            var failures = Prepare(changeSet, out var plan);
            if (failures.Count > 0)
            {
                return ChangeSetResult.Rejected(failures);
            }

            // ② Commit：无失败点
            var notices = new List<string>();
            Commit(plan, notices);
            return ChangeSetResult.Success(notices);
        }
    }

    private List<string> Prepare(ChangeSet changeSet, out ApplyPlan plan)
    {
        plan = new ApplyPlan();
        var failures = new List<string>();
        var pendingInstanceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in changeSet.Operations)
        {
            switch (operation)
            {
                case AddModuleOperation add:
                    if (Content.IsInstanceIdTaken(add.InstanceId) || !pendingInstanceIds.Add(add.InstanceId))
                    {
                        failures.Add($"实例 ID 已存在：{add.InstanceId}");
                        break;
                    }

                    if (!typeof(IModule).IsAssignableFrom(add.Definition.EntryType))
                    {
                        failures.Add($"模块定义无效（入口类型未实现 IModule）：{add.InstanceId}");
                        break;
                    }

                    // 实例化在此完成（对象创建 + OnRegistered 声明）；**不启动线程、不改动现场**
                    var created = ModuleInstanceFactory.Create(add.InstanceId, add.Definition, Issues);
                    plan.AddInstances.Add((created, new ModuleCarrierAdapter(created, created.Context, Issues)));
                    break;

                case RemoveModuleOperation remove:
                    if (!Content.TryGetInstance(remove.InstanceId, out _) && !Content.TryGetPlaceholder(remove.InstanceId, out _))
                    {
                        failures.Add($"实例不存在：{remove.InstanceId}");
                    }
                    else
                    {
                        plan.RemoveInstances.Add(remove.InstanceId);
                    }

                    break;

                case SetModuleEnabledOperation toggle:
                    if (!Content.TryGetInstance(toggle.InstanceId, out var toggleTarget) || toggleTarget is null)
                    {
                        failures.Add($"实例不存在：{toggle.InstanceId}");
                    }
                    else if (toggle.Enabled && toggleTarget.State != ModuleInstanceState.Disabled)
                    {
                        failures.Add($"实例当前不处于禁用状态，无法启用：{toggle.InstanceId}");
                    }
                    else if (!toggle.Enabled && toggleTarget.State != ModuleInstanceState.Active)
                    {
                        failures.Add($"实例当前不处于活动状态，无法禁用：{toggle.InstanceId}");
                    }
                    else
                    {
                        plan.StateChanges.Add((toggle.InstanceId, toggle.Enabled));
                    }

                    break;

                case ConnectNetworkOperation connect:
                    var connectErrors = ValidateConnect(connect, out var network);
                    failures.AddRange(connectErrors);
                    if (connectErrors.Count == 0 && network is not null)
                    {
                        plan.AddNetworks.Add(network);
                    }

                    break;

                case DisconnectNetworkOperation disconnect:
                    if (string.IsNullOrWhiteSpace(disconnect.NetworkName))
                    {
                        failures.Add("断开网络必须给出网络名（匿名网络随实例移除级联断开）");
                    }
                    else if (!Content.Networks.Any(n => string.Equals(n.Name, disconnect.NetworkName, StringComparison.Ordinal)))
                    {
                        failures.Add($"网络不存在：{disconnect.NetworkName}（匿名网络请用移除实例的方式级联断开）");
                    }
                    else
                    {
                        plan.RemoveNetworks.Add(disconnect.NetworkName);
                    }

                    break;

                case UpdateConfigurationOperation update:
                    if (!Content.TryGetInstance(update.InstanceId, out _))
                    {
                        failures.Add($"实例不存在：{update.InstanceId}");
                    }
                    else
                    {
                        plan.ConfigChanges.Add((update.InstanceId, update.Configuration));
                    }

                    break;

                case UpdateConfigurationsOperation updateMany:
                    if (!Content.TryGetInstance(updateMany.InstanceId, out _))
                    {
                        failures.Add($"实例不存在：{updateMany.InstanceId}");
                    }
                    else if (updateMany.Configurations.Count == 0)
                    {
                        failures.Add($"配置集合为空：{updateMany.InstanceId}");
                    }
                    else
                    {
                        plan.MultiConfigChanges.Add((updateMany.InstanceId, updateMany.Configurations));
                    }

                    break;

                case UpdateModuleLayoutOperation layout:
                    var layoutFailures = ValidateLayout(layout.Layout);
                    if (layoutFailures.Count > 0)
                    {
                        failures.AddRange(layoutFailures);
                    }
                    else if (!Content.IsInstanceIdTaken(layout.Layout.InstanceId))
                    {
                        failures.Add($"实例不存在：{layout.Layout.InstanceId}");
                    }
                    else
                    {
                        plan.LayoutChanges.Add(layout.Layout);
                    }

                    break;

                case UpdatePortNetworkOperation portNetwork:
                    if (string.IsNullOrWhiteSpace(portNetwork.PortName))
                    {
                        failures.Add("端口标签操作缺少口名");
                    }
                    else if (!Content.TryGetInstance(portNetwork.InstanceId, out var labeledInstance) || labeledInstance is null)
                    {
                        failures.Add($"实例不存在：{portNetwork.InstanceId}");
                    }
                    else if (labeledInstance.FindPort(portNetwork.PortName) is null)
                    {
                        failures.Add($"口不存在：{portNetwork.InstanceId}.{portNetwork.PortName}");
                    }
                    else if (portNetwork.NetworkName is { Length: > 0 } label && !NetworkNaming.IsValidName(label, out var nameError))
                    {
                        failures.Add(nameError!);
                    }
                    else
                    {
                        plan.PortNetworkChanges.Add((portNetwork.InstanceId, portNetwork.PortName, portNetwork.NetworkName));
                    }

                    break;

                case SendCommandOperation command:
                    if (!Content.TryGetInstance(command.InstanceId, out var commandInstance) || commandInstance is null)
                    {
                        failures.Add($"实例不存在：{command.InstanceId}");
                    }
                    else if (commandInstance.Context.DeclaredView is not { } commandView
                        || !commandView.Commands.Any(c => string.Equals(c.Name, command.CommandName, StringComparison.Ordinal)))
                    {
                        // 未声明的命令**不静默**：直接拒绝，避免"点了没反应"
                        failures.Add($"模块未声明命令：{command.InstanceId}.{command.CommandName}");
                    }
                    else
                    {
                        plan.CommandDeliveries.Add((command.InstanceId, command.CommandName));
                    }

                    break;

                case SubmitInputOperation input:
                    if (!Content.TryGetInstance(input.InstanceId, out var inputInstance) || inputInstance is null)
                    {
                        failures.Add($"实例不存在：{input.InstanceId}");
                    }
                    else if (inputInstance.Context.DeclaredView is not { } inputView
                        || !inputView.Elements.Any(e => IsInputElement(e.Kind)
                            && string.Equals(e.DataSource, input.Source, StringComparison.Ordinal)))
                    {
                        failures.Add($"模块未声明该输入：{input.InstanceId}.{input.Source}");
                    }
                    else
                    {
                        plan.InputDeliveries.Add((input.InstanceId, input.Source, input.Text));
                    }

                    break;

                case UpdatePortGeometryOperation geometry:
                    var geometryFailures = ValidatePortGeometry(geometry.InstanceId, geometry.Geometry);
                    if (geometryFailures.Count > 0)
                    {
                        failures.AddRange(geometryFailures);
                    }
                    else if (!Content.IsInstanceIdTaken(geometry.InstanceId))
                    {
                        failures.Add($"实例不存在：{geometry.InstanceId}");
                    }
                    else
                    {
                        plan.PortGeometryChanges.Add((geometry.InstanceId, geometry.Geometry));
                    }

                    break;

                default:
                    failures.Add($"不支持的变更操作：{operation.GetType().Name}");
                    break;
            }
        }

        return failures;
    }

    private List<string> ValidateConnect(ConnectNetworkOperation connect, out Network? network)
    {
        network = null;
        var errors = new List<string>();

        if (!Content.TryGetInstance(connect.Source.InstanceId, out var sourceInstance) || sourceInstance is null)
        {
            errors.Add($"源实例不存在：{connect.Source.InstanceId}");
            return errors;
        }

        var sourcePort = sourceInstance.FindPort(connect.Source.PortName);
        if (sourcePort is null)
        {
            errors.Add($"源口不存在：{connect.Source}");
            return errors;
        }

        if (sourcePort.Direction != PortDirection.Send)
        {
            errors.Add($"源必须是发送口：{connect.Source}（当前为 {sourcePort.Direction}）");
        }

        var targets = new List<NetworkMember>();
        foreach (var target in connect.Targets)
        {
            if (!Content.TryGetInstance(target.InstanceId, out var targetInstance) || targetInstance is null)
            {
                errors.Add($"目标实例不存在：{target.InstanceId}");
                continue;
            }

            var targetPort = targetInstance.FindPort(target.PortName);
            if (targetPort is null)
            {
                errors.Add($"目标口不存在：{target}");
                continue;
            }

            if (targetPort.Direction != PortDirection.Receive)
            {
                errors.Add($"目标必须是接收口：{target}（当前为 {targetPort.Direction}）");
                continue;
            }

            // 兼容判定：接收契约 ⊇ 发送契约（不自动转换）
            var compatibility = ContractCompatibility.Check(
                sourcePort.Declaration.Shape,
                sourcePort.Declaration.Semantics,
                targetPort.Declaration.Shape,
                targetPort.Declaration.Semantics);

            if (!compatibility.IsCompatible)
            {
                errors.Add($"契约不兼容（{connect.Source} → {target}）：{compatibility.Reason}");
                continue;
            }

            targets.Add(target);
        }

        if (connect.Name is { Length: > 0 } name)
        {
            if (!NetworkNaming.IsValidName(name, out var nameError))
            {
                errors.Add(nameError!);
            }
            else if (Content.IsNameTaken(name))
            {
                errors.Add($"网络名已被占用：{name}");
            }
        }

        if (errors.Count == 0)
        {
            network = new Network(connect.Name ?? string.Empty, connect.Source, targets);
        }

        return errors;
    }

    private void Commit(ApplyPlan plan, List<string> notices)
    {
        // 先加（实例 → 网络/绑定 → 状态 → 配置），后减（网络 → 实例）
        foreach (var (instance, adapter) in plan.AddInstances)
        {
            Content.AddInstance(instance);
            _adapters[instance.InstanceId] = adapter;
            _subscriptions[instance.InstanceId] = new Views.SubscriptionSet(instance.InstanceId, instance.View);
            instance.Context.AttachDataPlane(new RuntimePortAccess(this, instance.InstanceId, instance.Context));

            if (!Carriers.TryActivate(adapter, out _, out var failure))
            {
                Issues.Report($"module:{instance.InstanceId}", new IssueReport(
                    IssueCategory.RuntimeError,
                    IssueSeverity.Error,
                    $"承载启动失败：{failure}（该实例已加入但无活动，可移除后重试）"));
                continue;
            }

            _lifecycle.Activate(instance);
        }

        foreach (var network in plan.AddNetworks)
        {
            AddNetworkCore(network);
        }

        foreach (var (instanceId, enabled) in plan.StateChanges)
        {
            ApplyEnabled(instanceId, enabled);
        }

        foreach (var (instanceId, configuration) in plan.ConfigChanges)
        {
            ApplyConfiguration(instanceId, configuration);
        }

        // 批量配置（同一实例）：全部写入后**只通知一次**
        foreach (var (instanceId, configurations) in plan.MultiConfigChanges)
        {
            foreach (var configuration in configurations)
            {
                ApplyConfiguration(instanceId, configuration, notify: false);
            }

            NotifyConfigurationChanged(instanceId);
        }

        foreach (var layout in plan.LayoutChanges)
        {
            Content.Layout.SetModule(layout);
        }

        foreach (var (instanceId, geometry) in plan.PortGeometryChanges)
        {
            Content.Layout.SetPort(instanceId, geometry);
        }

        // 端口标签变更：写入标签（网络由提交末尾的推导统一产生）
        foreach (var (instanceId, portName, networkName) in plan.PortNetworkChanges)
        {
            var current = Content.Layout.GetPort(instanceId, portName);
            Content.Layout.SetPort(instanceId, current with
            {
                Label = string.IsNullOrWhiteSpace(networkName) ? null : networkName.Trim(),
            });
        }

        foreach (var (instanceId, commandName) in plan.CommandDeliveries)
        {
            if (Content.TryGetInstance(instanceId, out var commandInstance) && commandInstance is not null)
            {
                commandInstance.Context.EnqueueCommand(commandName);

                var title = commandInstance.Context.DeclaredView?.Commands
                    .FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.Ordinal))?.Title ?? commandName;
                notices.Add($"已投递命令「{title}」→ {commandInstance.Definition.DisplayName}");
            }
        }

        foreach (var (instanceId, source, text) in plan.InputDeliveries)
        {
            if (Content.TryGetInstance(instanceId, out var inputInstance) && inputInstance is not null)
            {
                inputInstance.Context.EnqueueInput(source, text);
                notices.Add($"已写入输入「{source}」= {text} → {inputInstance.Definition.DisplayName}");
            }
        }

        foreach (var networkName in plan.RemoveNetworks)
        {
            RemoveNetworkCore(networkName);
        }

        foreach (var instanceId in plan.RemoveInstances)
        {
            RemoveInstanceCore(instanceId);
        }

        // **每次变更都重算派生网络**（ADR-0037）：布局（拖动/缩放）· 增删启停 · 标签变更 · 加载
        // 统一在"现场定局之后"重算一次，不做增量 → 不存在隐藏状态；派生网络 = 标签网络 + 对齐网络。
        ReconcileNetworks(notices, AlignAutoNetwork);
    }

    private static bool IsInputElement(HYWDebugAssistant.Sdk.Views.ViewElementKind kind) =>
        kind is HYWDebugAssistant.Sdk.Views.ViewElementKind.TextInput
            or HYWDebugAssistant.Sdk.Views.ViewElementKind.ToggleSlider
            or HYWDebugAssistant.Sdk.Views.ViewElementKind.EnumSelector;

    // ---------------------------------------------------------------- 增量重建与自动建网

    /// <summary>加入网络并**原子替换绑定快照**（模块实例与状态不受影响——增量重建单位 = 绑定）。</summary>
    private void AddNetworkCore(Network network)
    {
        Content.AddNetwork(network);

        if (network.IsStalled)
        {
            // 停流 = 源未锚定（加载产生的降级态）：**保留声明、不建绑定**，其余部分照常运行（§9.3）
            Issues.Report($"network:{network}", new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Warning,
                $"网络停流：源成员未锚定（{network.Source}）——声明保留，补齐模块或口后自动重锚"));
            return;
        }

        var layout = ResolveLayout(network.Source);
        if (layout is null)
        {
            Issues.Report($"network:{network}", new IssueReport(
                IssueCategory.InterfaceError, IssueSeverity.Error, "无法解析源口契约（绑定未建立）"));
            return;
        }

        ReplaceBinding(network, layout);
    }

    /// <summary>按契约布局建立/替换绑定（成员重锚时同名网络换新对象）。</summary>
    private void ReplaceBinding(Network network, ContractLayout layout)
    {
        var binding = new RuntimeBinding(network, layout);

        BindingSet current, updated;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.Without(network).With(binding);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));
    }

    /// <summary>移除与某实例相关的全部网络（含其**悬空成员**引用）——占位实例移除时用。</summary>
    private void RemoveNetworksReferencing(string instanceId)
    {
        foreach (var network in Content.Networks.ToArray())
        {
            var involved = network.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal));
            if (!involved)
            {
                continue;
            }

            _ = Content.RemoveNetwork(network);

            BindingSet current, updated;
            do
            {
                current = Volatile.Read(ref _bindings);
                updated = current.Without(network);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));
        }
    }

    private void RemoveNetworkCore(string networkName)
    {
        Content.RemoveNetwork(networkName);

        BindingSet current, updated;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.Without(networkName);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));
    }

    private void ApplyEnabled(string instanceId, bool enabled)
    {
        if (!Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return;
        }

        if (enabled)
        {
            _lifecycle.Enable(instance);
        }
        else
        {
            _lifecycle.Disable(instance);
        }

        // Disabled = 保留实例/拓扑/配置、**暂停数据流**（绑定保留，只停流）
        SetSuspended(instanceId, suspended: !enabled);
    }

    /// <summary>置某实例相关绑定为停流 / 恢复（禁用与加载降级态共用同一语义）。</summary>
    private void SetSuspended(string instanceId, bool suspended)
    {
        foreach (var binding in Bindings.Bindings)
        {
            if (binding.Network.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                binding.IsSuspended = suspended;
            }
        }
    }

    private void ApplyConfiguration(string instanceId, object configuration, bool notify = true)
    {
        if (!Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            return;
        }

        try
        {
            // 配置更新非热路径：用反射调用泛型写入（配置类型在编译期未知）
            var writeMethod = typeof(Sdk.Context.IConfigurationAccess)
                .GetMethod(nameof(Sdk.Context.IConfigurationAccess.Write))!
                .MakeGenericMethod(configuration.GetType());

            _ = writeMethod.Invoke(instance.Context.Configuration, [configuration]);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Reflection.TargetInvocationException)
        {
            Issues.Report($"module:{instanceId}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Error, $"配置写回失败：{ex.Message}"));
            return;
        }

        if (notify)
        {
            _lifecycle.NotifyConfigurationChanged(instance);
        }
    }

    /// <summary>配置变更通知（批量写入后统一触发一次）。</summary>
    private void NotifyConfigurationChanged(string instanceId)
    {
        if (Content.TryGetInstance(instanceId, out var instance) && instance is not null)
        {
            _lifecycle.NotifyConfigurationChanged(instance);
        }
    }

    private void RemoveInstanceCore(string instanceId)
    {
        if (!Content.TryGetInstance(instanceId, out var instance) || instance is null)
        {
            // 占位实例：无承载/无钩子，只需清网络声明、布局与配置片段（§9.3 占位可被用户清除）
            if (Content.TryGetPlaceholder(instanceId, out _))
            {
                RemoveNetworksReferencing(instanceId);
                _ = Content.RemovePlaceholder(instanceId);
                _ = Content.Layout.RemoveInstance(instanceId);
                _ = _configurationFragments.Remove(instanceId);
            }

            return;
        }

        // 级联：先拆绑定（快照），再停承载，最后钩子 + 移出现场
        BindingSet current, updated;
        IReadOnlyList<string> removedNetworks;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.WithoutMember(instanceId, out removedNetworks);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));

        foreach (var networkName in removedNetworks)
        {
            Content.RemoveNetwork(networkName);
        }

        if (Carriers.TryDeactivate(instanceId, out var result) && result == CarrierStopResult.TimedOut)
        {
            Issues.Report($"module:{instanceId}", new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Error,
                "模块未在期限内停止活动：框架已放弃等待（线程成为孤儿），该实例的队列与缓冲不再归还池"));
        }

        _lifecycle.Remove(instance);
        _adapters.Remove(instanceId);
        _ = _subscriptions.Remove(instanceId);
        _ = Content.RemoveInstance(instanceId);
        _ = Content.Layout.RemoveInstance(instanceId);
        _ = _configurationFragments.Remove(instanceId);
    }


    private bool IsPortConnected(string instanceId, string portName)
    {
        foreach (var binding in Bindings.Bindings)
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

    private ContractLayout? ResolveLayout(NetworkMember source)
    {
        if (!Content.TryGetInstance(source.InstanceId, out var instance) || instance is null)
        {
            return null;
        }

        var port = instance.FindPort(source.PortName);
        return port is null ? null : ContractLayout.FromShape(port.Declaration.Shape);
    }

    // ---------------------------------------------------------------- 投递（数据直通）

    /// <summary>
    /// 发送一个数据单元：按绑定写入目标实例的输入队列（**Core 不经手数据**，1:N 由成员关系表达）。
    /// </summary>
    /// <returns>是否至少有一个目标接受了该单元。</returns>
    public bool Deliver(string sourceInstanceId, string sourcePortName, ReadOnlySpan<byte> payload)
    {
        var bindings = Bindings;
        var accepted = false;

        foreach (var binding in bindings.Bindings)
        {
            var source = binding.Network.Source;
            if (!string.Equals(source.InstanceId, sourceInstanceId, StringComparison.Ordinal)
                || !string.Equals(source.PortName, sourcePortName, StringComparison.Ordinal))
            {
                continue;
            }

            if (binding.IsSuspended || binding.Network.IsStalled || payload.Length < binding.Layout.SizeInBytes)
            {
                binding.Diagnostics.RecordDropped();
                continue;
            }

            foreach (var target in binding.Network.Targets)
            {
                if (!Carriers.TryGet(target.InstanceId, out var carrier) || carrier is null)
                {
                    binding.Diagnostics.RecordDropped();
                    continue;
                }

                // 多播：每个目标一份独立缓冲（所有权随入队移交；批量/零拷贝优化属 R 项）
                var buffer = Carriers.Buffers.RentCopy(binding.Layout, payload);

                if (carrier.TryEnqueue(target.PortName, buffer))
                {
                    binding.Diagnostics.RecordDelivered();
                    accepted = true;
                }
                else
                {
                    binding.Diagnostics.RecordDropped();
                }
            }
        }

        return accepted;
    }

    /// <summary>主动接收：从该实例的输入队列取一个待处理单元（含目标口名）。</summary>
    public bool TryReceive(string instanceId, out PayloadBuffer? buffer, out string? sourcePort)
    {
        if (Carriers.TryGet(instanceId, out var carrier) && carrier is not null
            && carrier.Queue.TryDequeue(out var unit))
        {
            buffer = unit.Buffer;
            sourcePort = unit.TargetPort;
            return true;
        }

        buffer = null;
        sourcePort = null;
        return false;
    }

    /// <summary>归还主动接收取得的缓冲。</summary>
    public void ReleaseBuffer(PayloadBuffer buffer) => Carriers.Buffers.Return(buffer);

    // ---------------------------------------------------------------- 订阅（UI 交接，Q4-4）

    /// <summary>
    /// 写入**订阅缓冲**（模块线程调用，经 <c>IModuleContext.Ports.Publish</c>）——供 UI 按刷新节拍读取。
    /// </summary>
    /// <remarks>
    /// 不阻塞、不分配（稳态）；未声明的数据源名 → 丢弃并**只提示一次**（不刷屏）。
    /// </remarks>
    public void Publish(string instanceId, string dataSourceName, ReadOnlySpan<byte> value)
    {
        if (!_subscriptions.TryGetValue(instanceId, out var subscriptions))
        {
            return;
        }

        var outcome = subscriptions.TryWrite(dataSourceName, value, out var failure);
        if (outcome is Views.SubscriptionWriteOutcome.Suppressed or Views.SubscriptionWriteOutcome.Written)
        {
            return;
        }

        Issues.Report($"module:{instanceId}", new IssueReport(
            IssueCategory.PresentationError,
            IssueSeverity.Warning,
            $"订阅写入被丢弃：{dataSourceName} — {failure}"));
    }

    /// <summary>取某实例的订阅缓冲集合（框架侧与查询面使用）。</summary>
    public bool TryGetSubscriptions(string instanceId, out Views.SubscriptionSet? subscriptions) =>
        _subscriptions.TryGetValue(instanceId, out subscriptions);

    /// <summary>全部订阅缓冲集合（查询面用）。</summary>
    public IReadOnlyCollection<Views.SubscriptionSet> Subscriptions => _subscriptions.Values;

    // ---------------------------------------------------------------- 查询

    /// <summary>取某实例的承载适配器（框架侧使用）。</summary>
    public bool TryGetAdapter(string instanceId, out ModuleCarrierAdapter? adapter) =>
        _adapters.TryGetValue(instanceId, out adapter);

    /// <summary>列出与某实例相关（作为源或宿）的全部绑定。</summary>
    public IReadOnlyList<RuntimeBinding> FindBindingsOf(string instanceId) =>
        [.. Bindings.Bindings.Where(b =>
            b.Network.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal)))];

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Carriers.Dispose();
    }

    /// <summary>变更应用计划（Prepare 产出、Commit 消费）。</summary>
    private sealed class ApplyPlan
    {
        public List<(ModuleInstance Instance, ModuleCarrierAdapter Adapter)> AddInstances { get; } = [];

        public List<string> RemoveInstances { get; } = [];

        public List<(string InstanceId, bool Enabled)> StateChanges { get; } = [];

        public List<Network> AddNetworks { get; } = [];

        public List<string> RemoveNetworks { get; } = [];

        public List<(string InstanceId, object Configuration)> ConfigChanges { get; } = [];

        public List<(string InstanceId, IReadOnlyList<object> Configurations)> MultiConfigChanges { get; } = [];

        public List<Api.ModuleLayoutView> LayoutChanges { get; } = [];

        public List<(string InstanceId, Api.PortGeometryView Geometry)> PortGeometryChanges { get; } = [];

        public List<(string InstanceId, string PortName, string? NetworkName)> PortNetworkChanges { get; } = [];

        /// <summary>本次提交要投递的命令（实例 → 命令名）。</summary>
        public List<(string InstanceId, string CommandName)> CommandDeliveries { get; } = [];

        /// <summary>本次提交要写入的输入（实例 → 输入源 → 文本）。</summary>
        public List<(string InstanceId, string Source, string Text)> InputDeliveries { get; } = [];
    }
}
