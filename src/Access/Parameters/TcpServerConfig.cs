using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Access.Parameters;

/// <summary>
/// **TCP 服务端参数**（随组合件保存的实例参数）。
/// </summary>
/// <remarks>
/// <b>本地地址是动态下拉</b>（见 <see cref="GetChoices"/>）：列表 = <c>0.0.0.0</c>（听全部网卡）+
/// <c>127.0.0.1</c>（本机回环）+ 本机各 IPv4 地址 —— "该填哪个地址"是这里的常见坑，能选就不该让人手打。
/// </remarks>
public sealed class TcpServerConfig : IConfigurationChoices
{
    /// <summary>监听地址（<c>0.0.0.0</c> = 全部网卡）。</summary>
    public const string AnyAddress = "0.0.0.0";

    /// <summary>本地监听地址。</summary>
    [ConfigurationField(
        TitleKey = TcpServerConfigText.Address,
        GroupKey = TcpServerConfigText.GroupListen,
        DescriptionKey = TcpServerConfigText.AddressDescription)]
    public string LocalAddress { get; set; } = "127.0.0.1";

    /// <summary>本地监听端口。</summary>
    [ConfigurationField(TitleKey = TcpServerConfigText.Port, GroupKey = TcpServerConfigText.GroupListen, Minimum = 0, Maximum = 65535)]
    public int LocalPort { get; set; } = 60721;

    /// <summary>最大同时连接数。</summary>
    /// <remarks>
    /// 单独一组（"限额"）：这样它能与"当前连接数"**排在同一行**上（组 = 一块表单，
    /// 一块表单只画它那一组 ⇒ 位置与宽度由声明面用坐标说了算）。
    /// </remarks>
    [ConfigurationField(TitleKey = TcpServerConfigText.MaxConnections, GroupKey = TcpServerConfigText.GroupQuota, Minimum = 1, Maximum = 256)]
    public int MaxConnections { get; set; } = 16;

    /// <summary>是否禁用 Nagle 算法（对本服务端接受的每条连接生效）。</summary>
    [ConfigurationField(
        TitleKey = TcpServerConfigText.NoDelay,
        GroupKey = TcpServerConfigText.GroupOptions,
        DescriptionKey = TcpServerConfigText.NoDelayDescription)]
    public bool NoDelay { get; set; } = true;

    /// <inheritdoc />
    /// <remarks>
    /// 只对<b>本地地址</b>给动态选项；列表来自 <see cref="LocalAddressCatalog"/>，
    /// 严格同步本机当前可用地址（最多 500 ms 过期）。当前地址消失时由下拉控件按值通道清空。
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
