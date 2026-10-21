using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Access.Parameters;

/// <summary>
/// **TCP 客户端参数**（随组合件保存的实例参数）。
/// </summary>
/// <remarks>
/// <b>地址是文本、端口是数字</b>：目标地址可能是主机名，也可能是一个具体 IP —— "可用值"这件事
/// 在客户端侧没有来源（不像本机地址能枚举），所以它是文本框；端口有明确定义域
/// （1–65535），走数字档，配错了由框架的校验先拦一道。
/// </remarks>
public sealed class TcpClientConfig
{
    /// <summary>目标地址（主机名或 IP）。</summary>
    [ConfigurationField("目标地址", Group = "连接", Description = "主机名或 IP 地址")]
    public string RemoteHost { get; set; } = "127.0.0.1";

    /// <summary>目标端口。</summary>
    [ConfigurationField("目标端口", Group = "连接", Minimum = 1, Maximum = 65535)]
    public int RemotePort { get; set; } = 8080;

    /// <summary>连接超时（秒）。</summary>
    [ConfigurationField("超时(秒)", Group = "连接", Minimum = 1, Maximum = 60)]
    public int ConnectTimeout { get; set; } = 5;

    /// <summary>是否禁用 Nagle 算法。</summary>
    [ConfigurationField("禁用 Nagle", Group = "选项", Description = "勾上 = 小包即时发；不勾 = 允许内核攒包")]
    public bool NoDelay { get; set; } = true;
}
