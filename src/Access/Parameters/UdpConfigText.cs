namespace HYWDebugAssistant.Access.Parameters;

/// <summary>**UDP 参数文本键**（文本住在声明该接入的模块表里；键常量留在配置类型旁）。</summary>
public static class UdpConfigText
{
    public const string LocalAddress = "config.udp.localAddress";
    public const string LocalAddressDescription = "config.udp.localAddress.description";
    public const string LocalPort = "config.udp.localPort";
    public const string TargetAddress = "config.udp.targetAddress";
    public const string TargetAddressDescription = "config.udp.targetAddress.description";
    public const string TargetPort = "config.udp.targetPort";
    public const string Broadcast = "config.udp.broadcast";
    public const string BroadcastDescription = "config.udp.broadcast.description";
    public const string GroupLocal = "config.group.local";
    public const string GroupTarget = "config.group.target";
    public const string GroupOptions = "config.group.options";
}
