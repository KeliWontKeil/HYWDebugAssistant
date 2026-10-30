namespace HYWDebugAssistant.Access.Parameters;

/// <summary>**TCP 服务端参数文本键**（文本住在声明该接入的模块表里；键常量留在配置类型旁）。</summary>
public static class TcpServerConfigText
{
    public const string Address = "config.tcpServer.address";
    public const string AddressDescription = "config.tcpServer.address.description";
    public const string Port = "config.tcpServer.port";
    public const string MaxConnections = "config.tcpServer.maxConnections";
    public const string NoDelay = "config.tcpServer.noDelay";
    public const string NoDelayDescription = "config.tcpServer.noDelay.description";
    public const string GroupListen = "config.group.listen";
    public const string GroupQuota = "config.group.quota";
    public const string GroupOptions = "config.group.options";
}
