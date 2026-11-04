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

    /// <summary>创建适配器。</summary>
    /// <param name="instance">模块实例。</param>
    /// <param name="context">该实例的模块上下文（Hosting 的最小实现）。</param>
    /// <param name="issues">框架侧上报口（通常是 Issue 中心）。</param>
    public ModuleCarrierAdapter(ModuleInstance instance, ModuleContext context, IIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issues);

        _instance = instance;
        _context = context;
        _issues = issues;
        _objectPath = $"module:{instance.InstanceId}";
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
    public bool HasActivity => _instance.ActivitySource is not null;

    /// <inheritdoc />
    public bool HasAuxiliaryWork =>
        !HasActivity
        && (_instance.Context.Access is RuntimeExternalAccess || Sink is not null);

    /// <inheritdoc />
    public TimeSpan? NextWakeupDue =>
        !HasActivity && _instance.Context.Access is RuntimeExternalAccess access
            ? access.NextWakeup
            : null;

    /// <inheritdoc />
    public bool PumpAuxiliary()
    {
        var didWork = false;

        // 外部接入：应用开关/重启/停用，把就绪数据取进队列并按需投递给回调。
        if (_instance.Context.Access is RuntimeExternalAccess access)
        {
            didWork |= access.Pump();
        }

        // 收件箱：没有活动体的模块由实例循环推给它（命令 / 输入）。
        if (Sink is { } sink)
        {
            while (_context.Inbox.TryDequeueCommand(out var command))
            {
                sink.OnCommand(command);
                didWork = true;
            }

            while (_context.Inbox.TryDequeueInput(out var source, out var text))
            {
                sink.OnInput(source, text);
                didWork = true;
            }
        }

        return didWork;
    }

    /// <inheritdoc />
    public void NotifyWorkCompleted() => Sink?.OnWork();

    /// <inheritdoc />
    public void RunActivity() => _instance.ActivitySource?.RunActivity(_context);

    /// <summary>模块的交互回调面（没有实现时为 <see langword="null"/>）。</summary>
    private IModuleInteractionSink? Sink => _instance.Module as IModuleInteractionSink;

    /// <inheritdoc />
    public IStopSignal StopSignal => _context.StopSignal;

    /// <inheritdoc />
    public void RequestStop() => _context.RequestStop();

    /// <inheritdoc />
    public void ReportActivityFailure(Exception exception)
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
