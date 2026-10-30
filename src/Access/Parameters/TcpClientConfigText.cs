namespace HYWDebugAssistant.Access.Parameters;

/// <summary>**TCP 客户端参数文本键**（文本住在声明该接入的模块表里；键常量留在配置类型旁）。</summary>
public static class TcpClientConfigText
{
    public const string Address = "config.tcpClient.address";
    public const string AddressDescription = "config.tcpClient.address.description";
    public const string Port = "config.tcpClient.port";
    public const string Timeout = "config.tcpClient.timeout";
    public const string NoDelay = "config.tcpClient.noDelay";
    public const string NoDelayDescription = "config.tcpClient.noDelay.description";
    public const string GroupConnection = "config.group.connection";
    public const string GroupOptions = "config.group.options";
}
