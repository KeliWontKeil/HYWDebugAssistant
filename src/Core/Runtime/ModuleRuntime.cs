using HYWDebugAssistant.Core.Adapters;
using HYWDebugAssistant.Core.Binding;
using RuntimeBinding = HYWDebugAssistant.Core.Binding.Binding;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Workspace;
using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Hosting.Context;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Lifecycle;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using System.Diagnostics;

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
    private readonly Dictionary<string, RuntimeExternalAccess> _accesses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Views.SubscriptionSet> _subscriptions = new(StringComparer.Ordinal);
    private readonly IExternalAccessCatalog? _catalog;
    private readonly ModuleLifecycleController _lifecycle;

    private BindingSet _bindings = BindingSet.Empty;
    private bool _disposed;

    /// <summary>创建运行时。</summary>
    /// <param name="issues">Issue 中心（缺省新建）。</param>
    /// <param name="carrierOptions">承载参数（队列容量/丢弃策略/停止超时）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：创建实例时按实例绑一份**只读**读取面交给模块
    /// （`IModuleContext.ViewState`），移除实例时清理该实例的视图状态。`null` ⇒ 不注入（模块读到"全部未知"）。
    /// </param>
    /// <param name="catalog">
    /// **外部标准目录**：按模块声明的标准标识解析适配器与参数类型；`null` ⇒ 外部接入不接线，
    /// 模块收到 Error 后以未接线状态继续（老调用点零改动）。
    /// </param>
    public ModuleRuntime(
        IssueCenter? issues = null,
        CarrierOptions? carrierOptions = null,
        Api.IViewStateAccess? viewState = null,
        IExternalAccessCatalog? catalog = null)
    {
        Issues = issues ?? new IssueCenter();
        Carriers = new CarrierHost(options: carrierOptions);
        _catalog = catalog;
        ExternalAccessHost = new ExternalAccessHost(carrierOptions, catalog);
        ExternalAccessHost.SignalResolver = instanceId =>
            Carriers.TryGet(instanceId, out var carrier) && carrier is not null
                ? carrier.SignalWork
                : null;
        Content = new WorkspaceContent();
        _lifecycle = new ModuleLifecycleController(Issues);
        ViewState = viewState;
    }

    /// <summary>
    /// **视图状态存取面**（UI 与 L4 共用；`null` = 未接线 ⇒ 模块读到"全部未知"、UI 回落本地缓冲）。
    /// </summary>
    public Api.IViewStateAccess? ViewState { get; }

    /// <summary>
    /// **文件选择请求面的解析器**（按实例给一份；`null` = 未接线 ⇒ 模块的"弹选择器"请求被忽略）。
    /// </summary>
    /// <remarks>
    /// <b>为什么是"解析器"而不是一个面</b>：系统选择器需要一个宿主窗口（`TopLevel.StorageProvider`），
    /// 而"哪个窗口属于哪个实例"只有 **UI 侧**知道（窗口集控制器持有实例 → 窗口的映射）。
    /// L4 因此不持有窗口，只按实例问一句"这一份请求面是谁" —— 与
    /// <see cref="ViewState"/> 的按实例绑定同一个形状（那边由存储面 `DisplayFor(instanceId)` 给出）。
    /// <b>由 App 在启动时挂上</b>（同 <c>RuntimeServices.BuiltInModuleDirectory</c> 的做法）。
    /// </remarks>
    public Func<string, Sdk.Context.IFileAccess?>? FileAccessFor { get; set; }

    /// <summary>Issue 中心（框架判定与模块报告的统一收集点）。</summary>
    public IssueCenter Issues { get; }

    /// <summary>数据面宿主（承载者与缓冲池）。</summary>
    public CarrierHost Carriers { get; }

    /// <summary>外部接入宿主（会话、队列、状态；与承载者共用 <see cref="CarrierOptions"/>）。</summary>
    public ExternalAccessHost ExternalAccessHost { get; }

    /// <summary>当前工作现场内容（实例 + 网络）。</summary>
    public WorkspaceContent Content { get; }

    /// <summary>
    /// **对齐自动建网**开关（由应用级设置驱动，即时生效）：
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
                        failures.Add(CoreText.Get("core.runtime.instanceIdExists", add.InstanceId));
                        break;
                    }

                    if (!typeof(IModule).IsAssignableFrom(add.Definition.EntryType))
                    {
                        failures.Add(CoreText.Get("core.runtime.moduleEntryNotModule", add.InstanceId));
                        break;
                    }

                    // 实例化在此完成（对象创建 + OnRegistered 声明）；**不启动线程、不改动现场**
                    // 视图状态**按实例绑定**交给模块（只读）：模块在活动体里拉取快照。
                    var created = ModuleInstanceFactory.Create(
                        add.InstanceId,
                        add.Definition,
                        Issues,
                        ViewState?.ReaderFor(add.InstanceId),
                        (ViewState as Services.ViewStateStore)?.DisplayFor(add.InstanceId),
                        FileAccessFor?.Invoke(add.InstanceId),
                        catalog: _catalog);
                    plan.AddInstances.Add((created, new ModuleCarrierAdapter(created, created.Context, Issues)));
                    break;

                case RemoveModuleOperation remove:
                    if (!Content.TryGetInstance(remove.InstanceId, out _) && !Content.TryGetPlaceholder(remove.InstanceId, out _))
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", remove.InstanceId));
                    }
                    else
                    {
                        plan.RemoveInstances.Add(remove.InstanceId);
                    }

                    break;

                case SetModuleEnabledOperation toggle:
                    if (!Content.TryGetInstance(toggle.InstanceId, out var toggleTarget) || toggleTarget is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", toggle.InstanceId));
                    }
                    else if (toggle.Enabled && toggleTarget.State != ModuleInstanceState.Disabled)
                    {
                        failures.Add(CoreText.Get("core.runtime.notDisabled", toggle.InstanceId));
                    }
                    else if (!toggle.Enabled && toggleTarget.State != ModuleInstanceState.Active)
                    {
                        failures.Add(CoreText.Get("core.runtime.notActive", toggle.InstanceId));
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
                        failures.Add(CoreText.Get("core.runtime.disconnectNeedsName"));
                    }
                    else if (!Content.Networks.Any(n => string.Equals(n.Name, disconnect.NetworkName, StringComparison.Ordinal)))
                    {
                        failures.Add(CoreText.Get("core.runtime.networkMissing", disconnect.NetworkName));
                    }
                    else
                    {
                        plan.RemoveNetworks.Add(disconnect.NetworkName);
                    }

                    break;

                case UpdateConfigurationOperation update:
                    if (!Content.TryGetInstance(update.InstanceId, out _))
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", update.InstanceId));
                    }
                    else
                    {
                        plan.ConfigChanges.Add((update.InstanceId, update.Configuration));
                    }

                    break;

                case UpdateConfigurationsOperation updateMany:
                    if (!Content.TryGetInstance(updateMany.InstanceId, out _))
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", updateMany.InstanceId));
                    }
                    else if (updateMany.Configurations.Count == 0)
                    {
                        failures.Add(CoreText.Get("core.runtime.configSetEmpty", updateMany.InstanceId));
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
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", layout.Layout.InstanceId));
                    }
                    else
                    {
                        plan.LayoutChanges.Add(layout.Layout);
                    }

                    break;

                case UpdatePortNetworkOperation portNetwork:
                    if (string.IsNullOrWhiteSpace(portNetwork.PortName))
                    {
                        failures.Add(CoreText.Get("core.runtime.portLabelMissingName"));
                    }
                    else if (!Content.TryGetInstance(portNetwork.InstanceId, out var labeledInstance) || labeledInstance is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", portNetwork.InstanceId));
                    }
                    else if (labeledInstance.FindPort(portNetwork.PortName) is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.portMissing", portNetwork.InstanceId, portNetwork.PortName));
                    }
                    else if (portNetwork.NetworkName is { Length: > 0 } label
                        && !NetworkNaming.TryValidateName(label, out var nameError))
                    {
                        failures.Add(DescribeNetworkNameError(nameError, label));
                    }
                    else
                    {
                        plan.PortNetworkChanges.Add((portNetwork.InstanceId, portNetwork.PortName, portNetwork.NetworkName));
                    }

                    break;

                case SendCommandOperation command:
                    if (!Content.TryGetInstance(command.InstanceId, out var commandInstance) || commandInstance is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", command.InstanceId));
                    }
                    else if (!DeclaresCommand(commandInstance, command.CommandName))
                    {
                        // 未声明的命令**不静默**：直接拒绝，避免"点了没反应"
                        failures.Add(CoreText.Get("core.runtime.commandUndeclared", command.InstanceId, command.CommandName));
                    }
                    else
                    {
                        plan.CommandDeliveries.Add((command.InstanceId, command.CommandName));
                    }

                    break;

                case SubmitInputOperation input:
                    if (!Content.TryGetInstance(input.InstanceId, out var inputInstance) || inputInstance is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", input.InstanceId));
                    }
                    else if (!DeclaresInput(inputInstance, input.Source))
                    {
                        // **判定看的是模块声明的输入源集合**，不是"界面上有没有控件绑它"：
                        // 一条输入可以没有任何界面实体（例如系统对话框选完文件后回填的路径），
                        // 只认界面会让那种源被整个拒掉，表现是"选完了却什么都没发生"。
                        failures.Add(CoreText.Get("core.runtime.inputUndeclared", input.InstanceId, input.Source));
                    }
                    else
                    {
                        plan.InputDeliveries.Add((input.InstanceId, input.Source, input.Text));
                    }

                    break;

                case ResetModuleConfigurationOperation reset:
                    if (!Content.TryGetInstance(reset.InstanceId, out var resetInstance) || resetInstance is null)
                    {
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", reset.InstanceId));
                    }
                    else
                    {
                        plan.ConfigurationResets.Add(reset.InstanceId);
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
                        failures.Add(CoreText.Get("core.runtime.instanceMissing", geometry.InstanceId));
                    }
                    else
                    {
                        plan.PortGeometryChanges.Add((geometry.InstanceId, geometry.Geometry));
                    }

                    break;

                default:
                    failures.Add(CoreText.Get("core.runtime.unsupportedOperation", operation.GetType().Name));
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
            errors.Add(CoreText.Get("core.runtime.sourceInstanceMissing", connect.Source.InstanceId));
            return errors;
        }

        var sourcePort = sourceInstance.FindPort(connect.Source.PortName);
        if (sourcePort is null)
        {
            errors.Add(CoreText.Get("core.runtime.sourcePortMissing", connect.Source));
            return errors;
        }

        if (sourcePort.Direction != PortDirection.Send)
        {
            errors.Add(CoreText.Get("core.runtime.sourceNotSend", connect.Source, sourcePort.Direction));
        }

        var targets = new List<NetworkMember>();
        foreach (var target in connect.Targets)
        {
            if (!Content.TryGetInstance(target.InstanceId, out var targetInstance) || targetInstance is null)
            {
                errors.Add(CoreText.Get("core.runtime.targetInstanceMissing", target.InstanceId));
                continue;
            }

            var targetPort = targetInstance.FindPort(target.PortName);
            if (targetPort is null)
            {
                errors.Add(CoreText.Get("core.runtime.targetPortMissing", target));
                continue;
            }

            if (targetPort.Direction != PortDirection.Receive)
            {
                errors.Add(CoreText.Get("core.runtime.targetNotReceive", target, targetPort.Direction));
                continue;
            }

            // 兼容判定：**三层判据**（类型标识/版本 + 结构回退）；不自动转换
            var compatibility = CheckCompatibility(
                connect.Source.InstanceId, sourcePort,
                target.InstanceId, targetPort,
                connect.AllowUnverifiedTypeMatch);

            if (!compatibility.IsCompatible)
            {
                errors.Add(CoreText.Get("core.runtime.contractIncompatible", connect.Source, target, compatibility.Reason));
                continue;
            }

            targets.Add(target);
        }

        if (connect.Name is { Length: > 0 } name)
        {
            if (!NetworkNaming.TryValidateName(name, out var nameError))
            {
                errors.Add(DescribeNetworkNameError(nameError, name));
            }
            else if (Content.IsNameTaken(name))
            {
                errors.Add(CoreText.Get("core.runtime.networkNameTaken", name));
            }
        }

        if (errors.Count == 0)
        {
            network = new Network(
                connect.Name ?? string.Empty,
                [connect.Source],
                targets,
                allowUnverifiedTypeMatch: connect.AllowUnverifiedTypeMatch);
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
            _subscriptions[instance.InstanceId] = new Views.SubscriptionSet(
                instance.InstanceId, instance.DisplaySources);
            instance.Context.AttachDataPlane(new RuntimePortAccess(this, instance.InstanceId, instance.Context));

            // 外部接入必须在承载启动前挂上：实例循环的第一拍就会用到它。
            var runtimeAccess = AttachExternalAccess(instance);
            if (runtimeAccess is not null)
            {
                _accesses[instance.InstanceId] = runtimeAccess;
            }

            if (!Carriers.TryActivate(adapter, out _, out var failure))
            {
                Issues.Report($"module:{instance.InstanceId}", new IssueReport(
                    IssueCategory.RuntimeError,
                    IssueSeverity.Error,
                    CoreText.Get("core.runtime.carrierStartFailed", failure)));
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
                SignalCarrier(instanceId);

                var title = CommandTitle(commandInstance, commandName) ?? commandName;
                notices.Add(CoreText.Get("core.runtime.commandDelivered", title, commandInstance.Definition.DisplayName));
            }
        }

        foreach (var (instanceId, source, text) in plan.InputDeliveries)
        {
            if (Content.TryGetInstance(instanceId, out var inputInstance) && inputInstance is not null)
            {
                inputInstance.Context.EnqueueInput(source, text);
                SignalCarrier(instanceId);
                notices.Add(CoreText.Get("core.runtime.inputWritten", source, text, inputInstance.Definition.DisplayName));
            }
        }

        // 配置还原（模块窗口的「还原」按钮）：丢掉已保存的配置 → 模块重读即回到默认值
        foreach (var instanceId in plan.ConfigurationResets)
        {
            if (Content.TryGetInstance(instanceId, out var resetInstance) && resetInstance is not null)
            {
                _ = resetInstance.Context.ResetConfiguration();
                NotifyConfigurationChanged(instanceId);
                notices.Add(CoreText.Get("core.runtime.configReset", resetInstance.Definition.DisplayName));
            }
        }

        foreach (var networkName in plan.RemoveNetworks)
        {
            RemoveNetworkCore(networkName);
        }

        foreach (var instanceId in plan.RemoveInstances)
        {
            RemoveInstanceCore(instanceId);

            // 视图状态/模块壁纸随实例移除清理（关闭模块即丢；保存过的副本在组合件/工作区里）
            ViewState?.RemoveInstance(instanceId);
            RemoveWallpaper(instanceId);
        }

        // **每次变更都重算派生网络**：布局（拖动/缩放）· 增删启停 · 标签变更 · 加载
        // 统一在"现场定局之后"重算一次，不做增量 → 不存在隐藏状态；派生网络 = 标签网络 + 对齐网络。
        ReconcileNetworks(notices, AlignAutoNetwork);
    }

    // **界面契约由模块在代码里声明**（显示源 / 输入源 / 命令）：运行期判定只读这一份，
    // 不再从任何界面数据文件里翻找 —— 那会让"同一件事有两个来源"，而两个来源迟早会打架。
    private static bool DeclaresInput(Hosting.Instances.ModuleInstance instance, string source) =>
        instance.Inputs.Contains(source, StringComparer.Ordinal);

    /// <summary>把网络名校验枚举翻成文本表里的文案（拥有者 = Core 程序集）。</summary>
    private static string DescribeNetworkNameError(NetworkNameError error, string name) => error switch
    {
        NetworkNameError.Empty => TextCatalog.Get(typeof(NetworkNaming).Assembly, NetworkNameText.Empty, name),
        NetworkNameError.TooLong => TextCatalog.Get(
            typeof(NetworkNaming).Assembly,
            NetworkNameText.TooLong,
            name,
            NetworkNaming.MaxNameLength),
        NetworkNameError.ContainsWhitespace => TextCatalog.Get(
            typeof(NetworkNaming).Assembly,
            NetworkNameText.ContainsWhitespace,
            name),
        _ => string.Empty,
    };

    private static bool DeclaresCommand(Hosting.Instances.ModuleInstance instance, string commandName) =>
        instance.Commands.Any(command => string.Equals(command.Name, commandName, StringComparison.Ordinal));

    private static string? CommandTitle(Hosting.Instances.ModuleInstance instance, string commandName) =>
        instance.Commands
            .FirstOrDefault(command => string.Equals(command.Name, commandName, StringComparison.Ordinal))
            .Title;

    // ---------------------------------------------------------------- 增量重建与自动建网

    /// <summary>加入网络并**原子替换绑定快照**（模块实例与状态不受影响——增量重建单位 = 绑定）。</summary>
    /// <remarks>
    /// **逐源建绑定**：多源网络为每个源各建一条绑定，绑定携带**它自己的源**、
    /// **该源可用的宿子集**（逐 (源,宿) 对兼容过滤）与**该源的契约布局/诊断计数**。
    /// **部分源不可用（口缺失/契约无法解析/无兼容宿）⇒ 其余源照常投递**，只对该源报 Issue。
    /// </remarks>
    private void AddNetworkCore(Network network)
    {
        Content.AddNetwork(network);
        RebuildBindingsCore(network);
    }

    /// <summary>
    /// **逐源重建绑定**（网络已在 Content 中；成员重锚时也走这里）。
    /// </summary>
    /// <remarks>
    /// 多源网络为每个源各建一条绑定，绑定携带它自己的源、**该源可用的宿子集**（逐 (源,宿) 对兼容过滤）
    /// 与**该源的契约布局/诊断计数**；**部分源不可用 ⇒ 其余源照常投递**，只对该源报 Issue。
    /// </remarks>
    private void RebuildBindingsCore(Network network)
    {
        ArgumentNullException.ThrowIfNull(network);

        if (network.IsStalled)
        {
            // 停流 = **全部源未锚定**（加载产生的降级态）：**保留声明、不建绑定**，其余部分照常运行（§9.3）。
            // 停流的事实、提示与 Issue 由**锚定路径**（`AnchorNetwork` + `ReportNetworkAnchoring`）负责，
            // 这里静默返回——否则同一事实会被报两遍。
            return;
        }

        // ① **先把新绑定全算出来**（纯计算，不碰 `_bindings`）；Issue 先攒着，换完快照再报
        var additions = new List<RuntimeBinding>(network.Sources.Count);
        var problems = new List<IssueReport>();

        foreach (var source in network.Sources)
        {
            var port = FindPort(source.InstanceId, source.PortName);
            if (port is null || port.Declaration.Direction != PortDirection.Send)
            {
                // **未锚定的源**：声明保留、不建绑定；问题已在重锚/加载时**逐源**报过，
                // 此处静默跳过（避免同一事实刷两条 Issue）
                continue;
            }

            var layout = ResolveLayout(source);
            if (layout is null)
            {
                problems.Add(new IssueReport(
                    IssueCategory.InterfaceError, IssueSeverity.Error, CoreText.Get("core.runtime.contractSourceUnbound", source)));
                continue;
            }

            // **允许同一实例的源 → 宿**（模块内自回环）：结构与显式连接都不再排除自连；
            // 真正约束仍是方向 + 契约兼容（逐对过滤）。
            var targets = network.Targets
                .Where(target => IsPairCompatible(network, source, target))
                .ToList();
            if (targets.Count == 0)
            {
                problems.Add(new IssueReport(
                    IssueCategory.InterfaceError, IssueSeverity.Warning, CoreText.Get("core.runtime.sourceNoCompatibleTarget", source)));
                continue;
            }

            additions.Add(new RuntimeBinding(network, source, targets, layout, ResolveMetering(source)));
        }

        if (additions.Count == 0)
        {
            problems.Add(new IssueReport(
                IssueCategory.InterfaceError, IssueSeverity.Warning, CoreText.Get("core.runtime.noBindings")));
        }

        // ② **一次 CAS** 用"该网络的新绑定集"整体替换它的旧绑定集。
        // 【缺陷修复 · **偶发静默丢单元**】旧写法是**两步**：先把该网络的绑定**全部清掉**（一次 CAS），
        // 再**逐源加回**（每源一次 CAS）⇒ 两步之间存在一个"**该网络没有任何绑定的空窗**"。
        // 模块线程恰好在这个窗口里 `TrySend` ⇒ `Deliver` 遍历快照找不到绑定 ⇒ `TrySend` 返回 false ⇒
        // 单元**被静默丢弃**：既不计入"传输丢弃"（没有绑定可记），也没有 Issue ⇒ 现场只剩"发了但没到"。
        // 而派生网络**每次变更提交**末尾都会重算（`ReconcileNetworks`）⇒ 这个窗口被反复打开
        // （现场特征：`TrySend` 被拒 + 绑定诊断全 0 + 无 Issue）。
        // 现在"新集合"算完才发布，且**只发一次** ⇒ 投递方要么看到旧快照、要么看到新快照，**不存在空窗**。
        BindingSet current, updated;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.Without(network);

            foreach (var binding in additions)
            {
                updated = updated.With(binding);
            }
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));

        foreach (var problem in problems)
        {
            Issues.Report($"network:{network}", problem);
        }
    }

    /// <summary>逐 **(源, 宿)** 对判定契约兼容（方向 + 契约；只排除不兼容的那一对）。</summary>
    private bool IsPairCompatible(Network network, NetworkMember source, NetworkMember target)
    {
        var from = FindPort(source.InstanceId, source.PortName);
        var to = FindPort(target.InstanceId, target.PortName);

        return from is not null
            && to is not null
            && from.Declaration.Direction == PortDirection.Send
            && to.Declaration.Direction == PortDirection.Receive
            && CheckCompatibility(
                source.InstanceId, from, target.InstanceId, to, network.AllowUnverifiedTypeMatch).IsCompatible;
    }

    /// <summary>
    /// 一个口的**类型身份**：已声明类型 ⇒ `标识@v版本`；未声明/引用不可解析 ⇒ **结构描述**。
    /// </summary>
    /// <remarks>
    /// **只用于呈现**（端口标记的类型色条 / 色板映射）：不参与任何判定。
    /// 之所以不复用呈现文本：`f32` 无法区分"声明了 `hyw/f32@1`"与"声明了别的标识但形状相同"，
    /// 而二者**不是同一种量** ⇒ 用文本会把两种类型混为一色。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="port">口实例。</param>
    internal string DescribePortTypeKey(string instanceId, PortInstance port)
    {
        ArgumentNullException.ThrowIfNull(port);

        if (ResolveTypeReference(instanceId, port) is { } reference)
        {
            return $"{reference.TypeId}@v{reference.Version}";
        }

        return port.Declaration.Shape.Describe();
    }

    /// <summary>
    /// 枚举**逐对兼容**的端口连接（源口 → 宿口）——**与绑定构建同一判定**。
    /// </summary>
    /// <remarks>
    /// 用"网络声明 + 逐对兼容"而非"已建立的绑定"：这样**停流网络**的成员关系也能呈现
    /// （停流是合法状态，用户仍需看到"这个口连给谁"）；判定规则与 <see cref="RebuildBindingsCore"/> 一致
    /// （同一 <see cref="CheckCompatibility"/> + 同一方向检查），故两者不会漂移。
    /// </remarks>
    internal List<(NetworkMember Source, NetworkMember Target, string NetworkKey)> DescribePortLinks()
    {
        var links = new List<(NetworkMember Source, NetworkMember Target, string NetworkKey)>();

        foreach (var network in Content.Networks)
        {
            var networkKey = DescribeKey(network);

            foreach (var source in network.Sources)
            {
                var from = FindPort(source.InstanceId, source.PortName);
                if (from is null || from.Declaration.Direction != PortDirection.Send)
                {
                    continue;
                }

                foreach (var target in network.Targets)
                {
                    if (!IsPairCompatible(network, source, target))
                    {
                        continue;
                    }

                    links.Add((source, target, networkKey));
                }
            }
        }

        return links;
    }

    /// <summary>
    /// **契约兼容判定的唯一入口**：解析两侧的**数据类型引用**（本模块已声明的类型），
    /// 交给 <see cref="ContractCompatibility.Check(ContractShape, TransferSemantics, DataTypeRef?, ContractShape, TransferSemantics, DataTypeRef?, bool)"/>
    /// 走**类型身份 + 形状完全一致**的判据；任一方未声明类型 ⇒ 只比形状。
    /// </summary>
    /// <param name="sourceInstanceId">源的实例 ID（用于解析该实例声明的类型）。</param>
    /// <param name="source">源口。</param>
    /// <param name="targetInstanceId">宿的实例 ID。</param>
    /// <param name="target">宿口。</param>
    /// <param name="allowUnverified">用户是否已显式确认允许"不同标识"强连。</param>
    private CompatibilityResult CheckCompatibility(
        string sourceInstanceId,
        PortInstance source,
        string targetInstanceId,
        PortInstance target,
        bool allowUnverified = false) =>
        ContractCompatibility.Check(
            source.Declaration.Shape,
            source.Declaration.Semantics,
            ResolveTypeReference(sourceInstanceId, source),
            target.Declaration.Shape,
            target.Declaration.Semantics,
            ResolveTypeReference(targetInstanceId, target),
            allowUnverified);

    /// <summary>
    /// 解析口上的数据类型引用：**引用必须能在本实例声明的类型里找到**，否则视为"未声明"（回退结构判定）。
    /// </summary>
    /// <remarks>
    /// 为什么必须"本模块声明"：框架要靠声明拿到**尺寸/解码器/计量块**才能替模块做机械判断；
    /// 只写一个标识而本模块不声明该类型 = 框架不知道它的形状 ⇒ 只能回退结构比较（并已由实例化工位报 Issue）。
    /// </remarks>
    private DataTypeRef? ResolveTypeReference(string instanceId, PortInstance port)
    {
        if (port.Declaration.Type is not { } reference)
        {
            return null;
        }

        if (Content.TryGetInstance(instanceId, out var instance)
            && instance is not null
            && instance.FindDataType(reference) is not null)
        {
            return reference;
        }

        return null;
    }

    /// <summary>
    /// 解析源口的**计量块**（来自该实例声明的类型）：未声明类型 ⇒ 返回缺省（不猜）。
    /// </summary>
    /// <param name="source">源成员。</param>
    private MeteringBlock ResolveMetering(NetworkMember source)
    {
        if (FindPort(source.InstanceId, source.PortName) is not { } port
            || ResolveTypeReference(source.InstanceId, port) is not { } reference
            || !Content.TryGetInstance(source.InstanceId, out var instance)
            || instance is null)
        {
            return MeteringBlock.Default;
        }

        return instance.FindDataType(reference)?.Metering ?? MeteringBlock.Default;
    }

    private PortInstance? FindPort(string instanceId, string portName) =>
        Content.TryGetInstance(instanceId, out var instance) && instance is not null
            ? instance.FindPort(portName)
            : null;

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

    /// <summary>
    /// 移除一条网络（**按对象引用**定位绑定）——匿名网络（对齐直连）没有唯一名，按名移除会误伤其他匿名网络。
    /// </summary>
    /// <param name="network">要移除的网络。</param>
    private void RemoveNetworkCore(Network network)
    {
        ArgumentNullException.ThrowIfNull(network);

        _ = Content.RemoveNetwork(network);

        BindingSet current, updated;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.Without(network);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));
    }

    /// <summary>
    /// 为实例创建并挂上外部接入存取面（没有声明时返回 <see langword="null"/>）。
    /// </summary>
    /// <remarks>
    /// 必须在承载启动**之前**调用：实例循环的第一拍就可能用到它。参数对象按声明类型从当前配置快照取，
    /// 因此配置改完之后的重开会自动拿到新值。
    /// </remarks>
    private RuntimeExternalAccess? AttachExternalAccess(Hosting.Instances.ModuleInstance instance)
    {
        if (instance.ExternalAccesses.Count == 0)
        {
            return null;
        }

        var access = new RuntimeExternalAccess(ExternalAccessHost, instance.InstanceId, Issues);

        object? ParametersOf(ExternalAccessDeclaration declaration)
        {
            var typeName = ResolveAccessParametersType(declaration.StandardId)?.FullName;
            return typeName is not null
                && instance.Context.ConfigurationSnapshot.TryGetValue(typeName, out var parameters)
                    ? parameters
                    : null;
        }

        if (!ExternalAccessHost.TryActivate(
                instance.InstanceId,
                instance.ExternalAccesses,
                ParametersOf,
                out var failure))
        {
            Issues.Report($"module:{instance.InstanceId}", new IssueReport(
                IssueCategory.RuntimeError,
                IssueSeverity.Error,
                CoreText.Get("core.runtime.externalAccessInitFailed", failure)));
            return null;
        }

        instance.Context.AttachAccess(access);
        return access;
    }

    /// <summary>
    /// 按标准标识从 catalog 解析参数类型（无 catalog / 未知标准 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <param name="standardId">外部标准标识。</param>
    private Type? ResolveAccessParametersType(string standardId)
    {
        if (_catalog is null || string.IsNullOrWhiteSpace(standardId))
        {
            return null;
        }

        return _catalog.TryResolve(standardId, out var descriptor) ? descriptor.ParametersType : null;
    }

    private void SignalCarrier(string instanceId)
    {
        if (Carriers.TryGet(instanceId, out var carrier) && carrier is not null)
        {
            carrier.SignalWork();
        }
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

        // Disabled = 保留实例/拓扑/配置、**暂停数据流**（绑定保留，只停流；会话关闭但保留意图）
        SetSuspended(instanceId, suspended: !enabled);

        if (!enabled && Carriers.TryGet(instanceId, out var suspendedCarrier) && suspendedCarrier is not null)
        {
            // 暂停也清尾：停流之后队列里剩下的单元没有消费方，留着只会让"已入队"变成假账
            suspendedCarrier.PurgeQueues();
        }

        if (_accesses.TryGetValue(instanceId, out var access))
        {
            access.SetSuspended(!enabled);
        }
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
                IssueCategory.ConfigurationError, IssueSeverity.Error, CoreText.Get("core.runtime.configWriteFailed", ex.Message)));
            return;
        }

        if (notify)
        {
            _lifecycle.NotifyConfigurationChanged(instance);
            NotifyAccessConfigurationChanged(instanceId);
        }
    }

    /// <summary>配置变更通知（批量写入后统一触发一次）。</summary>
    private void NotifyConfigurationChanged(string instanceId)
    {
        if (Content.TryGetInstance(instanceId, out var instance) && instance is not null)
        {
            _lifecycle.NotifyConfigurationChanged(instance);
            NotifyAccessConfigurationChanged(instanceId);
        }
    }

    private void NotifyAccessConfigurationChanged(string instanceId)
    {
        if (_accesses.TryGetValue(instanceId, out var access))
        {
            access.NotifyConfigurationChanged();
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
                CoreText.Get("core.runtime.moduleStopTimeout")));
        }

        // 承载已停：宿主兜底关闭会话（模块循环通常已在停止信号里关过一次）。
        if (ExternalAccessHost.TryDeactivate(instanceId, out var accessFailures))
        {
            foreach (var failure in accessFailures)
            {
                Issues.Report($"module:{instanceId}", new IssueReport(
                    ExternalAccessIssue.CategoryOf(failure),
                    ExternalAccessIssue.Severity,
                    CoreText.Get("core.runtime.externalAccessFailure", failure.AccessName, failure.Message)));
            }
        }

        _accesses.Remove(instanceId);
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
            // 绑定的源 = **绑定自己携带的源**（多源网络下每个源一条绑定）
            var source = binding.Source;
            if (!string.Equals(source.InstanceId, sourceInstanceId, StringComparison.Ordinal)
                || !string.Equals(source.PortName, sourcePortName, StringComparison.Ordinal))
            {
                continue;
            }

            if (binding.IsSuspended || binding.Network.IsStalled)
            {
                // **停流未投递**是合法状态（不是故障）⇒ 单独归类，不并入"传输丢弃"
                binding.Diagnostics.RecordDropped(DropReason.Stalled);
                continue;
            }

            if (payload.Length < binding.Layout.SizeInBytes)
            {
                binding.Diagnostics.RecordDropped(DropReason.InvalidPayload);
                continue;
            }

            // **发布计数 = 每个数据报一次**（按源；不受多播放大），在宿循环之前记。
            // 同时累计**有效字节**（读类型声明的有效长度字段；未声明 ⇒ 静态尺寸）
            // —— 第 3 档速率（有效载荷字节速率）的分子就是它，与发布计数**同一处**累加
            // （分开写就会出现"分子分母不是同一批数据报"）。
            binding.Diagnostics.RecordPublished(binding.PayloadBytesOfUnit(payload));

            // **薄旁路头只在多源网络**：单源网络**零附加**（不构造、不入队、不读）。
            // 序号 = 按源递增（= 刚记下的发布计数，1 起）；接收时刻 = 框架投递该数据报的时刻（单调计数）。
            // 头是**旁路元数据**：不进 payload 字节流（字节守恒不受影响）。
            var header = binding.Network.Sources.Count > 1
                ? new DatagramHeader(
                    source.InstanceId,
                    source.PortName,
                    binding.Diagnostics.Published,
                    Stopwatch.GetTimestamp())
                : (DatagramHeader?)null;

            foreach (var target in binding.Targets)
            {
                if (!Carriers.TryGet(target.InstanceId, out var carrier) || carrier is null)
                {
                    binding.Diagnostics.RecordDropped(DropReason.NoCarrier);
                    continue;
                }

                // 多播：每个目标一份独立缓冲（所有权随入队移交；批量/零拷贝优化属 R 项）
                var buffer = Carriers.Buffers.RentCopy(binding.Layout, payload);

                switch (carrier.TryEnqueue(target.PortName, buffer, header))
                {
                    case CarrierEnqueueResult.Accepted:
                        binding.Diagnostics.RecordDelivered();
                        accepted = true;
                        break;

                    // 已入队但**挤掉了一个旧单元**（丢旧策略）：本次投递成立，同时如实记一次"队列满挤旧"
                    case CarrierEnqueueResult.QueueFullEvicted:
                        binding.Diagnostics.RecordDelivered();
                        binding.Diagnostics.RecordDropped(DropReason.QueueFullEvicted);
                        accepted = true;
                        break;

                    // 目标队列满（**丢新**策略：本次被拒，best-effort 丢弃）
                    case CarrierEnqueueResult.QueueFull:
                        binding.Diagnostics.RecordDropped(DropReason.QueueFullRejected);
                        break;

                    // 承载**存在过、现在已经停了**（实例正在拆除）⇒ 与"从来没有承载"分开：
                    // 两者的处置方向与排障对象都不相同，混成一个数就查不出是哪一种
                    case CarrierEnqueueResult.Stopped:
                        binding.Diagnostics.RecordDropped(DropReason.CarrierStopped);
                        break;

                    // 口不存在 ⇒ **无承载**（与"满队"、"承载已停"都分开计数）
                    case CarrierEnqueueResult.NoCarrier:
                        binding.Diagnostics.RecordDropped(DropReason.NoCarrier);
                        break;
                }
            }
        }

        return accepted;
    }

    /// <summary>
    /// 主动接收：从该实例**任一非空输入口**取一个待处理单元（含目标口名；**轮转公平**）。
    /// </summary>
    public bool TryReceive(string instanceId, out PayloadBuffer? buffer, out string? sourcePort)
    {
        if (Carriers.TryGet(instanceId, out var carrier) && carrier is not null
            && carrier.TryDequeueAny(out var unit))
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

    /// <summary>
    /// 读某实例某个接收口的队列流动状态（深度 / 峰值 / 丢弃账本 / 饱和）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">接收口名。</param>
    /// <param name="state">流动状态（无承载 / 无该口时为 <see cref="PortFlowState.None"/>）。</param>
    /// <returns>该实例的该口有队列时为 <see langword="true"/>。</returns>
    public bool TryGetPortFlow(string instanceId, string portName, out PortFlowState state)
    {
        state = PortFlowState.None;

        return !string.IsNullOrWhiteSpace(instanceId)
            && !string.IsNullOrWhiteSpace(portName)
            && Carriers.TryGet(instanceId, out var carrier)
            && carrier is not null
            && carrier.TryGetFlowState(portName, out state);
    }

    /// <summary>
    /// 记一次**模块内部**丢弃到某实例某个口的原因账本。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">接收口名。</param>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数（= 契约尺寸 × 单元数）。</param>
    /// <returns>该实例的该口存在并已记账时为 <see langword="true"/>；否则丢弃这次上报（**不抛**）。</returns>
    public bool ReportModuleLoss(string instanceId, string portName, long units, long bytes)
    {
        if (string.IsNullOrWhiteSpace(instanceId)
            || string.IsNullOrWhiteSpace(portName)
            || !Carriers.TryGet(instanceId, out var carrier)
            || carrier is null
            || !carrier.TryGetQueue(portName, out var queue)
            || queue is null)
        {
            return false;
        }

        carrier.ReportModuleLoss(portName, units, bytes);
        return true;
    }

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
            CoreText.Get("core.runtime.subscriptionDiscarded", dataSourceName, failure)));
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
        ExternalAccessHost.Dispose();
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

        /// <summary>本次提交要**还原配置**的实例（回到模块默认值）。</summary>
        public List<string> ConfigurationResets { get; } = [];
    }
}
