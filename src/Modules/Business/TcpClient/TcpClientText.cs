namespace HYWDebugAssistant.Modules.TcpClient;

/// <summary>**TCP 客户端的文本表键**（表随模块包走）。</summary>
public static class TcpClientText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleConnect = "view.roleConnect";
    public const string Open = "view.open";
    public const string Close = "view.close";
    public const string ViewClearCounters = "view.clearCounters";
    public const string Rx = "view.rx";
    public const string Tx = "view.tx";
    public const string Drop = "view.drop";
    public const string StatusConnected = "status.connected";
    public const string StatusConnectedDetail = "status.connectedDetail";
    public const string StatusConnecting = "status.connecting";
    public const string StatusConnectingDetail = "status.connectingDetail";
    public const string StatusDisconnecting = "status.disconnecting";
    public const string StatusFailedFrameworkInternal = "status.failed.frameworkInternal";
    public const string StatusFailedParametersNotRegistered = "status.failed.parametersNotRegistered";
    public const string StatusFailedAddressEmpty = "status.failed.addressEmpty";
    public const string StatusFailedWildcardAddress = "status.failed.wildcardAddressNotAllowed";
    public const string StatusFailedPortOutOfRange = "status.failed.portOutOfRange";
    public const string StatusFailedConnectTimeout = "status.failed.connectTimeout";
    public const string StatusFailedConnectionRefused = "status.failed.connectionRefused";
    public const string StatusFailedPeerClosed = "status.failed.peerClosed";
    public const string StatusFailedOpenFailed = "status.failed.openFailed";
    public const string StatusFailedReadFailed = "status.failed.readFailed";
    public const string StatusFailedWriteFailed = "status.failed.writeFailed";
    public const string StatusFailedUnknown = "status.failed.unknown";
    public const string StatusRejected = "status.rejected";
    public const string StatusRxSaturated = "status.rxSaturated";
    public const string StatusTxSaturated = "status.txSaturated";
    public const string StatusReceiveLost = "status.receiveLost";
    public const string StatusSendLost = "status.sendLost";
    public const string StatusTxBacklog = "status.txBacklog";
    public const string StatusSuspended = "status.suspended";
    public const string StatusIdle = "status.idle";
}
