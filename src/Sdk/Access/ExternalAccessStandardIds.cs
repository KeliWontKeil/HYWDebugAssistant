namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **框架内置的外部标准标识**：模块声明接入时使用，框架据此解析适配器与参数类型。
/// </summary>
/// <remarks>
/// 标识是**跨模块的公共词汇**：同一个标准在框架里只有一个 descriptor（显示名 / 参数类型 / 适配器工厂），
/// 模块只声明"用哪个标准"，不携带实现件。标识字符串一旦发布不得改名。
/// </remarks>
public static class ExternalAccessStandardIds
{
    /// <summary>串口（<c>System.IO.Ports</c>）。</summary>
    public const string Serial = "serial";

    /// <summary>TCP 客户端（一条出站连接）。</summary>
    public const string TcpClient = "tcp-client";

    /// <summary>TCP 服务端（监听 + 多连接）。</summary>
    public const string TcpServer = "tcp-server";

    /// <summary>UDP（本地绑定 + 默认收件人）。</summary>
    public const string Udp = "udp";
}
