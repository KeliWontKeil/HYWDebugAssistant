using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Context;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Adapters;

/// <summary>
/// 数据面适配器：把（<see cref="ModuleInstance"/> + <see cref="ModuleContext"/>）适配为
/// <see cref="ICarrierTarget"/>——**由 L4 提供**，因为 L3 不得依赖 L2（AR-3）。
/// </summary>
/// <remarks>
/// 这是"宿主与数据面之间唯一的接缝"：数据面只认本接口，不认识模块实例类型。
/// </remarks>
public sealed class ModuleCarrierAdapter : ICarrierTarget
{
    private readonly ModuleInstance _instance;
    private readonly ModuleContext _context;
    private readonly IIssueSink _issues;
    private readonly string _objectPath;
    private readonly IReadOnlyList<string> _receivePorts;
    private readonly StepDueTimer? _dueTimer;

    /// <summary>创建适配器。</summary>
    /// <param name="instance">模块实例。</param>
    /// <param name="context">该实例的模块上下文（Hosting 的最小实现）。</param>
    /// <param name="issues">框架侧上报口（通常是 Issue 中心）。</param>
    /// <param name="dueTimer">相对到期调度（可选；无 step 能力的模块不需要）。</param>
    internal ModuleCarrierAdapter(
        ModuleInstance instance,
        ModuleContext context,
        IIssueSink issues,
        StepDueTimer? dueTimer = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issues);

        _instance = instance;
        _context = context;
        _issues = issues;
        _objectPath = $"module:{instance.InstanceId}";
        _dueTimer = dueTimer;
        _receivePorts = [.. instance.Ports
            .Where(p => p.Direction == PortDirection.Receive)
            .Select(p => p.Name)];
    }

    /// <summary>所适配的模块上下文（供 L4 内部使用，如配置读写）。</summary>
    public ModuleContext Context => _context;

    /// <inheritdoc />
    public string InstanceId => _instance.InstanceId;

    /// <inheritdoc />
    public IReadOnlyList<string> ReceivePortNames => _receivePorts;

    /// <inheritdoc />
    public bool TryGetReceiveHandler(string portName, out ReceiveHandler handler) =>
        _context.ReceiveHandlers.TryGetValue(portName, out handler!);

    /// <inheritdoc />
    public bool HasStepModel => _instance.ActivitySource is not null;

    /// <inheritdoc />
    public ReceiveMode ReceiveMode => _instance.ActivitySource?.Receive ?? ReceiveMode.Push;

    /// <inheritdoc />
    public bool HasPendingInbox => _context.Inbox.HasPending;

    /// <inheritdoc />
    public void OnStep(StepReason reason)
    {
        var source = _instance.ActivitySource;

        if (source is null)
        {
            return;
        }

        source.OnStep(_context, reason);
        _dueTimer?.Arm(source.NextDelay);
    }

    /// <summary>暂停到期调度（实例禁用时；到期回调不应再唤醒承载者）。</summary>
    internal void PauseDue() => _dueTimer?.Pause();

    /// <summary>恢复到期调度（实例重新启用时，按最近一次声明的延迟从现在重排）。</summary>
    internal void ResumeDue() => _dueTimer?.Resume();

    /// <summary>移除实例时取消到期调度。</summary>
    internal void DisposeDue() => _dueTimer?.Dispose();

    /// <inheritdoc />
    public bool HasAuxiliaryWork => _instance.Context.Access is RuntimeExternalAccess;

    /// <inheritdoc />
    public TimeSpan? NextWakeupDue =>
        _instance.Context.Access is RuntimeExternalAccess access
            ? access.NextWakeup
            : null;

    /// <inheritdoc />
    public bool PumpAuxiliary()
    {
        // 外部接入：应用开关/重启/停用，把就绪数据取进队列并按需投递给回调。
        return _instance.Context.Access is RuntimeExternalAccess access && access.Pump();
    }

    /// <inheritdoc />
    public void ReportFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _issues.Report(_objectPath, new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Error,
            CoreText.Get(
                "core.runtime.activityFailure",
                exception.GetType().Name,
                exception.Message)));
    }
}
