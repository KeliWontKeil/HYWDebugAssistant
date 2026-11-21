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
/// <b>本地地址是动态下拉</b>（见 <see cref="GetChoices"/>，含 <c>0.0.0.0</c> 与 <c>127.0.0.1</c>）；
/// <b>目标地址是文本框</b> —— 对端地址没有"可用值"这类来源，能选的只有本机自己。
/// </para>
/// </remarks>
public sealed class UdpConfig : IConfigurationChoices
{
    /// <summary>绑定全部网卡。</summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>本地绑定地址。</summary>
    [ConfigurationField(
        TitleKey = UdpConfigText.LocalAddress,
        GroupKey = UdpConfigText.GroupLocal,
        DescriptionKey = UdpConfigText.LocalAddressDescription)]
    public string LocalAddress { get; set; } = "127.0.0.1";

    /// <summary>本地绑定端口。</summary>
    [ConfigurationField(TitleKey = UdpConfigText.LocalPort, GroupKey = UdpConfigText.GroupLocal, Minimum = 0, Maximum = 65535)]
    public int LocalPort { get; set; } = 60721;

    /// <summary>默认目标地址（留空 = 只收不发）。</summary>
    [ConfigurationField(
        TitleKey = UdpConfigText.TargetAddress,
        GroupKey = UdpConfigText.GroupTarget,
        DescriptionKey = UdpConfigText.TargetAddressDescription)]
    public string RemoteAddress { get; set; } = "127.0.0.1";

    /// <summary>默认目标端口（0 = 未指定）。</summary>
    [ConfigurationField(TitleKey = UdpConfigText.TargetPort, GroupKey = UdpConfigText.GroupTarget, Minimum = 0, Maximum = 65535)]
    public int RemotePort { get; set; } = 60721;

    /// <summary>是否允许广播（目标地址是广播地址时必须打开）。</summary>
    [ConfigurationField(
        TitleKey = UdpConfigText.Broadcast,
        GroupKey = UdpConfigText.GroupOptions,
        DescriptionKey = UdpConfigText.BroadcastDescription)]
    public bool Broadcast { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// 只对<b>本地地址</b>给动态选项；列表来自 <see cref="LocalAddressCatalog"/>，
    /// 严格同步本机当前可用地址（最多 500 ms 过期）。当前地址消失时由下拉控件按值通道清空。
    /// 目标地址是文本框，没有“可用值”来源。
    /// </remarks>
    public IReadOnlyList<string>? GetChoices(string fieldName) =>
        string.Equals(fieldName, nameof(LocalAddress), StringComparison.Ordinal)
            ? LocalAddressCatalog.Addresses
            : null;

    /// <inheritdoc />
    public long GetChoicesRevision() => LocalAddressCatalog.Revision;

    /// <inheritdoc />
    public bool KeepCurrentChoiceWhenUnavailable => false;
}
