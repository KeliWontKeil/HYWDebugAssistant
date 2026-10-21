using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.TcpClient;

/// <summary>
/// **TCP 客户端**：把一条 TCP 连接接到工作区 —— 对端来的字节从 <c>rx</c> 出去，发往对端的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一条连接 = 一个会话</b>：一进一出两个口是它的两个端点；</item>
///   <item><b>不自动重试</b>：连接失败或对端关闭 ⇒ 状态变红并给原因；用户再次点〔连接〕才会重开；</item>
///   <item><b>契约沿用原始字节</b>：与既有模块**同一标识同一形状**才开箱即连。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "tcp-client",
    "1.0.0",
    DisplayName = "TCP 客户端",
    Description = "连到远端：对端来的字节从 rx 出，发往对端的字节从 tx 进",
    Tags = ["tcp", "client", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190,
    ViewFactory = typeof(Views.TcpClientViewFactory))]
public sealed class TcpClientModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "tcp-client";

    /// <summary>**发送口**（本模块是源）：对端 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 对端。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id（一个分块、一种类型、一进一出）。</summary>
    public const string RoleId = "tcp-client";

    /// <summary>显示源：连接状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：连接。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：断开。</summary>
    public const string CloseCommand = "close";

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        RxPort,
        TxPort,
        RoleId,
        "连接",
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        "连接",
        CloseCommand,
        "断开");

    /// <inheritdoc />
    protected override IExternalAccessAdapter CreateAdapter() => new TcpClientAccessAdapter();

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "已连接"
            : $"已连接 {snapshot.Detail}",
        ExternalAccessState.Opening => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "正在连接…"
            : $"正在连接 {snapshot.Detail}",
        ExternalAccessState.Closing => "正在断开…",
        ExternalAccessState.Faulted => string.IsNullOrWhiteSpace(snapshot.LastError)
            ? "连接失败"
            : snapshot.LastError,
        _ => snapshot.Suspended ? "实例已停用" : "未连接",
    };
}
