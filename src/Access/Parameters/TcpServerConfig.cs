using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Access.Parameters;

/// <summary>
/// **TCP 服务端参数**（随组合件保存的实例参数）。
/// </summary>
/// <remarks>
/// <b>本地地址是动态下拉</b>（见 <see cref="GetChoices"/>）：列表 = <c>0.0.0.0</c>（听全部网卡）+ 本机各
/// IPv4 地址 —— "该填哪个地址"是这里的常见坑，能选就不该让人手打。
/// </remarks>
public sealed class TcpServerConfig : IConfigurationChoices
{
    /// <summary>监听地址（<c>0.0.0.0</c> = 全部网卡）。</summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>本地监听地址。</summary>
    [ConfigurationField("本地地址", Group = "监听", Description = "0.0.0.0 = 监听全部网卡")]
    public string LocalAddress { get; set; } = AnyAddress;

    /// <summary>本地监听端口。</summary>
    [ConfigurationField("本地端口", Group = "监听", Minimum = 1, Maximum = 65535)]
    public int LocalPort { get; set; } = 8080;

    /// <summary>最大同时连接数。</summary>
    /// <remarks>
    /// 单独一组（"限额"）：这样它能与"当前连接数"**排在同一行**上（组 = 一块表单，
    /// 一块表单只画它那一组 ⇒ 位置与宽度由声明面用坐标说了算）。
    /// </remarks>
    [ConfigurationField("最大连接", Group = "限额", Minimum = 1, Maximum = 256)]
    public int MaxConnections { get; set; } = 16;

    /// <summary>是否禁用 Nagle 算法（对本服务端接受的每条连接生效）。</summary>
    [ConfigurationField("禁用 Nagle", Group = "选项", Description = "勾上 = 小包即时发；不勾 = 允许内核攒包")]
    public bool NoDelay { get; set; } = true;

    /// <inheritdoc />
    /// <remarks>
    /// 只对<b>本地地址</b>给动态选项；查询廉价、不抛（取不到就当作"现在一个都没有"）。
    /// </remarks>
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
