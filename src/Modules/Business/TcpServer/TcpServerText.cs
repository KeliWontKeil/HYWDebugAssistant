namespace HYWDebugAssistant.Modules.TcpServer;

/// <summary>**TCP 服务端的文本表键**（表随模块包走）。</summary>
public static class TcpServerText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleListen = "view.roleListen";
    public const string Open = "view.open";
    public const string Close = "view.close";
    public const string ViewClearCounters = "view.clearCounters";
    public const string Rx = "view.rx";
    public const string Tx = "view.tx";
    public const string Drop = "view.drop";
    public const string Connections = "view.connections";
    public const string UnitConnections = "view.unitConnections";
    public const string StatusListening = "status.listening";
    public const string StatusListeningDetail = "status.listeningDetail";
    public const string StatusStarting = "status.starting";
    public const string StatusStartingDetail = "status.startingDetail";
    public const string StatusStopping = "status.stopping";
    public const string StatusFailedFrameworkInternal = "status.failed.frameworkInternal";
    public const string StatusFailedParametersNotRegistered = "status.failed.parametersNotRegistered";
    public const string StatusFailedAddressInvalid = "status.failed.addressInvalid";
    public const string StatusFailedPortOutOfRange = "status.failed.portOutOfRange";
    public const string StatusFailedMaxConnectionsOutOfRange = "status.failed.maxConnectionsOutOfRange";
    public const string StatusFailedPortInUse = "status.failed.portInUse";
    public const string StatusFailedListenFailed = "status.failed.listenFailed";
    public const string StatusFailedAcceptFailed = "status.failed.acceptFailed";
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
    public const string StatusConfigNotReady = "status.configNotReady";
    public const string StatusIdle = "status.idle";
}
