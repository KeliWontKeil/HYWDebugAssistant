using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.Udp;

/// <summary>
/// **UDP**：绑定一个本地端口收发数据报 —— 收到的数据报从 <c>rx</c> 出去，
/// 要发出去的从 <c>tx</c> 进来（发给参数里的默认目标）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>绑定 = 一个会话</b>：UDP 无连接 ⇒ 会话里没有"连接状态"，只有"绑定没绑定"；</item>
///   <item>一次接收 = 一个数据报；超过单元容量的部分被截断并计数；</item>
///   <item><b>不自动重试</b>：绑定失败/解绑后用户再次〔绑定〕。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "udp",
    "1.0.0",
    DisplayName = "UDP",
    Description = "绑定本地端口收发数据报：收到的从 rx 出，要发的从 tx 进",
    Tags = ["udp", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190,
    ViewFactory = typeof(Views.UdpViewFactory))]
public sealed class UdpModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "udp";

    /// <summary>**发送口**（本模块是源）：收到的数据报。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：要发出去的数据报。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id。</summary>
    public const string RoleId = "udp";

    /// <summary>显示源：绑定状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：绑定。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：解绑。</summary>
    public const string CloseCommand = "close";

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        RxPort,
        TxPort,
        RoleId,
        "数据报",
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        "绑定",
        CloseCommand,
        "解绑");

    /// <inheritdoc />
    protected override IExternalAccessAdapter CreateAdapter() => new UdpAccessAdapter();

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "已绑定"
            : $"已绑定 {snapshot.Detail}",
        ExternalAccessState.Opening => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "正在绑定…"
            : $"正在绑定 {snapshot.Detail}",
        ExternalAccessState.Closing => "正在解绑…",
        ExternalAccessState.Faulted => string.IsNullOrWhiteSpace(snapshot.LastError)
            ? "绑定失败"
            : snapshot.LastError,
        _ => snapshot.Suspended ? "实例已停用" : "未绑定",
    };
}
