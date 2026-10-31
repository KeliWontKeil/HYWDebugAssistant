using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.Udp;

/// <summary>
/// **UDP**：绑定一个本地端口收发数据报 —— 收到的数据报从 <c>rx</c> 出去，
/// 要发出去的从 <c>tx</c> 进来（发给参数里的默认目标）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>绑定 = 一个会话</b>：UDP 无连接 ⇒ 会话里没有"连接状态"，只有"绑定没绑定"；</item>
///   <item>一次接收 = 一个数据报；超过单元容量的部分被截断并计数；</item>
///   <item><b>不自动重试</b>：绑定失败/解绑后用户再次〔绑定〕。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "udp",
    "1.0.0",
    DisplayNameKey = UdpText.Title,
    DescriptionKey = UdpText.Description,
    Tags = ["udp", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 170,
    ViewFactory = typeof(Views.UdpViewFactory))]
public sealed class UdpModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "udp";

    /// <summary>**发送口**（本模块是源）：收到的数据报。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：要发出去的数据报。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id。</summary>
    public const string RoleId = "udp";

    /// <summary>显示源：绑定状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：绑定。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：解绑。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空本接入的计数（队列数据、连接数与故障态都不动）。</summary>
    public const string ClearCountersCommand = "clearCounters";

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(UdpModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(UdpModule).Assembly, key, args);

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        ExternalAccessStandardIds.Udp,
        RxPort,
        TxPort,
        RoleId,
        Tr(UdpText.RoleDatagram),
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        Tr(UdpText.Open),
        CloseCommand,
        Tr(UdpText.Close),
        ClearCountersCommand,
        Tr(UdpText.ViewClearCounters));

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot)
    {
        var endpoint = EndpointText(snapshot.Endpoint);

        return snapshot.State switch
        {
            ExternalAccessState.Open => endpoint.Length == 0
                ? Tr(UdpText.StatusBound)
                : Tr(UdpText.StatusBoundDetail, endpoint),
            ExternalAccessState.Opening => endpoint.Length == 0
                ? Tr(UdpText.StatusBinding)
                : Tr(UdpText.StatusBindingDetail, endpoint),
            ExternalAccessState.Closing => Tr(UdpText.StatusUnbinding),
            ExternalAccessState.Faulted => FailureText(snapshot),
            _ => snapshot.Suspended
                ? Tr(UdpText.StatusSuspended)
                : snapshot.RejectedSinceOpen > 0
                    ? Tr(UdpText.StatusRejected, snapshot.RejectedSinceOpen)
                    : Tr(UdpText.StatusIdle),
        };
    }

    /// <summary>按失败种类从本模块表取句子（未覆盖的种类走未知回退）。</summary>
    private static string FailureText(ExternalAccessSnapshot snapshot)
    {
        var failure = snapshot.Failure;
        var endpoint = EndpointText(snapshot.Endpoint);
        var value = failure?.Value ?? string.Empty;

        return failure?.Kind switch
        {
            ExternalAccessFailureKind.FrameworkInternal => Tr(UdpText.StatusFailedFrameworkInternal),
            ExternalAccessFailureKind.ParametersNotRegistered => Tr(UdpText.StatusFailedParametersNotRegistered),
            ExternalAccessFailureKind.AddressInvalid => Tr(UdpText.StatusFailedAddressInvalid, value),
            ExternalAccessFailureKind.WildcardAddressNotAllowed => Tr(UdpText.StatusFailedWildcardAddress, value),
            ExternalAccessFailureKind.PortOutOfRange => Tr(UdpText.StatusFailedPortOutOfRange, value),
            ExternalAccessFailureKind.PortInUse => Tr(UdpText.StatusFailedPortInUse, endpoint),
            ExternalAccessFailureKind.BindFailed => Tr(UdpText.StatusFailedBindFailed),
            ExternalAccessFailureKind.OpenFailed => Tr(UdpText.StatusFailedOpenFailed),
            ExternalAccessFailureKind.ReadFailed => Tr(UdpText.StatusFailedReadFailed),
            ExternalAccessFailureKind.WriteFailed => Tr(UdpText.StatusFailedWriteFailed),
            _ => Tr(UdpText.StatusFailedUnknown),
        };
    }

    /// <inheritdoc />
    protected override string? DescribePressure(
        PressureKind kind,
        in PortFlowState sendFlow,
        ExternalAccessSnapshot snapshot) => kind switch
        {
            PressureKind.ReceiveLost => Tr(UdpText.StatusReceiveLost, snapshot.ReceiveDroppedSinceOpen),
            PressureKind.SendLost => Tr(UdpText.StatusSendLost),
            PressureKind.SendBacklogged => Tr(UdpText.StatusTxBacklog, snapshot.SendBacklogDepth),
            PressureKind.ReceiveSaturated => Tr(UdpText.StatusRxSaturated, snapshot.ReceiveQueueDepth),
            PressureKind.SendSaturated => Tr(
                UdpText.StatusTxSaturated,
                sendFlow.Depth,
                sendFlow.DroppedWhileSaturatedUnits),
            _ => null,
        };
}




