using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.Serial;

/// <summary>
/// **串口**：把一路串口接到工作区 —— 设备来的字节从 <c>rx</c> 出去，发往设备的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一个通道 = 一个会话</b>：一进一出两个口只是它的两个端点，不是两件事；</item>
///   <item><b>会话由框架持有并驱动</b>：模块只发打开/关闭请求，适配器与参数类型在框架侧；</item>
///   <item><b>契约沿用原始字节</b>：与文本发送器 / HEX 发送器 / 数据监视器**同一标识同一形状**才开箱即连；</item>
///   <item><b>结构约定</b>：声明 / 接线 / 状态计数由共用骨架 <see cref="ExternalAccessModule"/> 承担，
///     本类只提供标准标识、接入名、口名、显示源、按钮文案与状态文案。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "serial",
    DisplayNameKey = SerialText.Title,
    DescriptionKey = SerialText.Description,
    Tags = ["serial", "io"],
    Capabilities = ModuleCapability.SerialPort,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    ViewFactory = typeof(Views.SerialViewFactory))]
public sealed class SerialModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "serial";

    /// <summary>**发送口**（本模块是源）：设备 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 设备。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id（一个分块、一种类型、一进一出）。</summary>
    public const string RoleId = "serial";

    /// <summary>显示源：连接状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：打开串口。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：关闭串口。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空本接入的计数（队列数据、连接数与故障态都不动）。</summary>
    public const string ClearCountersCommand = "clearCounters";

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(SerialModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(SerialModule).Assembly, key, args);

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        ExternalAccessStandardIds.Serial,
        RxPort,
        TxPort,
        RoleId,
        Tr(SerialText.RoleSerial),
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        Tr(SerialText.Open),
        CloseCommand,
        Tr(SerialText.Close),
        ClearCountersCommand,
        Tr(SerialText.ViewClearCounters));

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot)
    {
        var endpoint = EndpointText(snapshot.Endpoint);

        return snapshot.State switch
        {
            ExternalAccessState.Open => endpoint.Length == 0
                ? Tr(SerialText.StatusOpened)
                : Tr(SerialText.StatusOpenedDetail, endpoint),
            ExternalAccessState.Opening => endpoint.Length == 0
                ? Tr(SerialText.StatusOpening)
                : Tr(SerialText.StatusOpeningDetail, endpoint),
            ExternalAccessState.Closing => Tr(SerialText.StatusClosing),
            ExternalAccessState.Faulted => FailureText(snapshot),
            _ => snapshot.Suspended
                ? Tr(SerialText.StatusSuspended)
                : snapshot.RejectedSinceOpen > 0
                    ? Tr(SerialText.StatusRejected, snapshot.RejectedSinceOpen)
                    : Tr(SerialText.StatusIdle),
        };
    }

    /// <summary>按失败种类从本模块表取句子（未覆盖的种类走未知回退）。</summary>
    private static string FailureText(ExternalAccessSnapshot snapshot)
    {
        return snapshot.Failure?.Kind switch
        {
            ExternalAccessFailureKind.FrameworkInternal => Tr(SerialText.StatusFailedFrameworkInternal),
            ExternalAccessFailureKind.ParametersNotRegistered => Tr(SerialText.StatusFailedParametersNotRegistered),
            ExternalAccessFailureKind.PortNotSelected => Tr(SerialText.StatusFailedPortNotSelected),
            ExternalAccessFailureKind.DeviceLost => Tr(SerialText.StatusFailedDeviceLost),
            ExternalAccessFailureKind.OpenFailed => Tr(SerialText.StatusFailedOpenFailed),
            ExternalAccessFailureKind.ReadFailed => Tr(SerialText.StatusFailedReadFailed),
            ExternalAccessFailureKind.WriteFailed => Tr(SerialText.StatusFailedWriteFailed),
            _ => Tr(SerialText.StatusFailedUnknown),
        };
    }

    /// <inheritdoc />
    protected override string? DescribePressure(
        PressureKind kind,
        in PortFlowState sendFlow,
        ExternalAccessSnapshot snapshot) => kind switch
        {
            PressureKind.ReceiveLost => Tr(SerialText.StatusReceiveLost, snapshot.ReceiveDroppedSinceOpen),
            PressureKind.SendLost => Tr(SerialText.StatusSendLost),
            PressureKind.SendBacklogged => Tr(SerialText.StatusTxBacklog, snapshot.SendBacklogDepth),
            PressureKind.ReceiveSaturated => Tr(SerialText.StatusRxSaturated, snapshot.ReceiveQueueDepth),
            PressureKind.SendSaturated => Tr(
                SerialText.StatusTxSaturated,
                sendFlow.Depth,
                sendFlow.DroppedWhileSaturatedUnits),
            _ => null,
        };
}




