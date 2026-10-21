using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Access.Parameters;

/// <summary>
/// **UDP 参数**（随组合件保存的实例参数）。
/// </summary>
/// <remarks>
/// <para>
/// UDP **没有连接**，所以参数分两组、语义各自独立：<b>本地</b>（绑定到哪个地址与端口 —— 决定收得到什么）
/// 与<b>目标</b>（默认把数据发去哪 —— 留空就只收不发）。
/// </para>
/// <para>
/// <b>本地地址是动态下拉</b>（见 <see cref="GetChoices"/>）；<b>目标地址是文本框</b> ——
/// 对端地址没有"可用值"这类来源，能选的只有本机自己。
/// </para>
/// </remarks>
public sealed class UdpConfig : IConfigurationChoices
{
    /// <summary>绑定全部网卡。</summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>本地绑定地址。</summary>
    [ConfigurationField("本地地址", Group = "本地", Description = "0.0.0.0 = 绑定全部网卡")]
    public string LocalAddress { get; set; } = AnyAddress;

    /// <summary>本地绑定端口。</summary>
    [ConfigurationField("本地端口", Group = "本地", Minimum = 1, Maximum = 65535)]
    public int LocalPort { get; set; } = 9000;

    /// <summary>默认目标地址（留空 = 只收不发）。</summary>
    [ConfigurationField("目标地址", Group = "目标", Description = "留空 = 只收不发；主机名或 IP")]
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>默认目标端口（0 = 未指定）。</summary>
    [ConfigurationField("目标端口", Group = "目标", Minimum = 0, Maximum = 65535)]
    public int RemotePort { get; set; }

    /// <summary>是否允许广播（目标地址是广播地址时必须打开）。</summary>
    [ConfigurationField("允许广播", Group = "选项", Description = "往 255.255.255.255 或网段广播地址发时必须打开")]
    public bool Broadcast { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string>? GetChoices(string fieldName)
    {
        if (!string.Equals(fieldName, nameof(LocalAddress), StringComparison.Ordinal))
        {
            return null;
        }

        var list = new List<string> { AnyAddress };

        try
        {
            foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var text = address.ToString();
                if (!list.Contains(text, StringComparer.Ordinal))
                {
                    list.Add(text);
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // 环境取不到就报"现在一个都没有"：仍然是动态下拉（还有 0.0.0.0 这一项）。
        }

        return list;
    }
}
