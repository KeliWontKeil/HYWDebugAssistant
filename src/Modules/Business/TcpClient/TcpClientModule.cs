using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.TcpClient;

/// <summary>
/// **TCP 客户端**：把一条 TCP 连接接到工作区 —— 对端来的字节从 <c>rx</c> 出去，发往对端的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一条连接 = 一个会话</b>：一进一出两个口是它的两个端点；</item>
///   <item><b>不自动重试</b>：连接失败或对端关闭 ⇒ 状态变红并给原因；用户再次点〔连接〕才会重开；</item>
///   <item><b>契约沿用原始字节</b>：与既有模块**同一标识同一形状**才开箱即连。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "tcp-client",
    DisplayNameKey = TcpClientText.Title,
    DescriptionKey = TcpClientText.Description,
    Tags = ["tcp", "client", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 166,
    ViewFactory = typeof(Views.TcpClientViewFactory))]
public sealed class TcpClientModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "tcp-client";

    /// <summary>**发送口**（本模块是源）：对端 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 对端。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id（一个分块、一种类型、一进一出）。</summary>
    public const string RoleId = "tcp-client";

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

    /// <summary>命令：连接。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：断开。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空本接入的计数（队列数据、连接数与故障态都不动）。</summary>
    public const string ClearCountersCommand = "clearCounters";

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(TcpClientModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TcpClientModule).Assembly, key, args);

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        ExternalAccessStandardIds.TcpClient,
        RxPort,
        TxPort,
        RoleId,
        Tr(TcpClientText.RoleConnect),
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        Tr(TcpClientText.Open),
        CloseCommand,
        Tr(TcpClientText.Close),
        ClearCountersCommand,
        Tr(TcpClientText.ViewClearCounters));

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot)
    {
        var endpoint = EndpointText(snapshot.Endpoint);

        return snapshot.State switch
        {
            ExternalAccessState.Open => endpoint.Length == 0
                ? Tr(TcpClientText.StatusConnected)
                : Tr(TcpClientText.StatusConnectedDetail, endpoint),
            ExternalAccessState.Opening => endpoint.Length == 0
                ? Tr(TcpClientText.StatusConnecting)
                : Tr(TcpClientText.StatusConnectingDetail, endpoint),
            ExternalAccessState.Closing => Tr(TcpClientText.StatusDisconnecting),
            ExternalAccessState.Faulted => FailureText(snapshot),
            _ => snapshot.Suspended
                ? Tr(TcpClientText.StatusSuspended)
                : snapshot.RejectedSinceOpen > 0
                    ? Tr(TcpClientText.StatusRejected, snapshot.RejectedSinceOpen)
                    : Tr(TcpClientText.StatusIdle),
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
            ExternalAccessFailureKind.FrameworkInternal => Tr(TcpClientText.StatusFailedFrameworkInternal),
            ExternalAccessFailureKind.ParametersNotRegistered => Tr(TcpClientText.StatusFailedParametersNotRegistered),
            ExternalAccessFailureKind.AddressEmpty => Tr(TcpClientText.StatusFailedAddressEmpty),
            ExternalAccessFailureKind.WildcardAddressNotAllowed => Tr(TcpClientText.StatusFailedWildcardAddress, value),
            ExternalAccessFailureKind.PortOutOfRange => Tr(TcpClientText.StatusFailedPortOutOfRange, value),
            ExternalAccessFailureKind.ConnectTimeout => Tr(TcpClientText.StatusFailedConnectTimeout, endpoint),
            ExternalAccessFailureKind.ConnectionRefused => Tr(TcpClientText.StatusFailedConnectionRefused, endpoint),
            ExternalAccessFailureKind.PeerClosed => Tr(TcpClientText.StatusFailedPeerClosed, endpoint),
            ExternalAccessFailureKind.OpenFailed => Tr(TcpClientText.StatusFailedOpenFailed),
            ExternalAccessFailureKind.ReadFailed => Tr(TcpClientText.StatusFailedReadFailed),
            ExternalAccessFailureKind.WriteFailed => Tr(TcpClientText.StatusFailedWriteFailed),
            _ => Tr(TcpClientText.StatusFailedUnknown),
        };
    }

    /// <inheritdoc />
    protected override string? DescribePressure(
        PressureKind kind,
        in PortFlowState sendFlow,
        ExternalAccessSnapshot snapshot) => kind switch
        {
            PressureKind.ReceiveLost => Tr(TcpClientText.StatusReceiveLost, snapshot.ReceiveDroppedSinceOpen),
            PressureKind.SendLost => Tr(TcpClientText.StatusSendLost),
            PressureKind.SendBacklogged => Tr(TcpClientText.StatusTxBacklog, snapshot.SendBacklogDepth),
            PressureKind.ReceiveSaturated => Tr(TcpClientText.StatusRxSaturated, snapshot.ReceiveQueueDepth),
            PressureKind.SendSaturated => Tr(
                TcpClientText.StatusTxSaturated,
                sendFlow.Depth,
                sendFlow.DroppedWhileSaturatedUnits),
            _ => null,
        };
}




