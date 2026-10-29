using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.TcpServer;

/// <summary>
/// **TCP 服务端**：监听一个端口，把每条进来的连接接成一路数据 —— 各连接来的字节从 <c>rx</c> 出去，
/// 发往各连接的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>监听 = 一个会话，连接 = 它的多路</b>：对数据面而言仍是一进一出两个口；</item>
///   <item><b>单条客户端断开只清该连接</b>，不把监听打成故障；监听本身失败才变红；</item>
///   <item><b>不自动重试</b>：停止/失败后用户再次〔启动〕。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "tcp-server",
    "1.0.0",
    DisplayNameKey = TcpServerText.Title,
    DescriptionKey = TcpServerText.Description,
    Tags = ["tcp", "server", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190,
    ViewFactory = typeof(Views.TcpServerViewFactory))]
public sealed class TcpServerModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "tcp-server";

    /// <summary>**发送口**（本模块是源）：客户端 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 客户端。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id。</summary>
    public const string RoleId = "tcp-server";

    /// <summary>显示源：监听状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>显示源：当前连接数。</summary>
    public const string ConnCountSource = "connCount";

    /// <summary>命令：启动监听。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：停止监听。</summary>
    public const string CloseCommand = "close";

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(TcpServerModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TcpServerModule).Assembly, key, args);

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        ExternalAccessStandardIds.TcpServer,
        RxPort,
        TxPort,
        RoleId,
        Tr(TcpServerText.RoleListen),
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        Tr(TcpServerText.Open),
        CloseCommand,
        Tr(TcpServerText.Close),
        ConnCountSource);

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? Tr(TcpServerText.StatusListening, snapshot.ActiveConnections)
            : Tr(TcpServerText.StatusListeningDetail, snapshot.Detail, snapshot.ActiveConnections),
        ExternalAccessState.Opening => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? Tr(TcpServerText.StatusStarting)
            : Tr(TcpServerText.StatusStartingDetail, snapshot.Detail),
        ExternalAccessState.Closing => Tr(TcpServerText.StatusStopping),
        ExternalAccessState.Faulted => string.IsNullOrWhiteSpace(snapshot.LastError)
            ? Tr(TcpServerText.StatusFailed)
            : snapshot.LastError,
        _ => snapshot.Suspended ? Tr(TcpServerText.StatusSuspended) : Tr(TcpServerText.StatusIdle),
    };
}
