using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.Serial;

/// <summary>
/// **串口**：把一路串口接到工作区 —— 设备来的字节从 <c>rx</c> 出去，发往设备的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一个通道 = 一个会话</b>：一进一出两个口只是它的两个端点，不是两件事；</item>
///   <item><b>会话由框架持有并驱动</b>：模块只发打开/关闭请求，适配件在外部接入层；</item>
///   <item><b>契约沿用原始字节</b>：与文本发送器 / HEX 发送器 / 数据监视器**同一标识同一形状**才开箱即连；</item>
///   <item><b>结构约定</b>：声明 / 接线 / 状态计数由共用骨架 <see cref="ExternalAccessModule"/> 承担，
///     本类只提供接入名、口名、显示源、按钮文案与状态文案。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "serial",
    "1.0.0",
    DisplayName = "串口",
    Description = "把一路串口接到工作区：设备来的字节从 rx 出，发往设备的字节从 tx 进",
    Tags = ["serial", "io"],
    Capabilities = ModuleCapability.SerialPort,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190,
    ViewFactory = typeof(Views.SerialViewFactory))]
public sealed class SerialModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "serial";

    /// <summary>**发送口**（本模块是源）：设备 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 设备。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id（一个分块、一种类型、一进一出）。</summary>
    public const string RoleId = "serial";

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

    /// <summary>命令：打开串口。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：关闭串口。</summary>
    public const string CloseCommand = "close";

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        RxPort,
        TxPort,
        RoleId,
        "串口",
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        "打开",
        CloseCommand,
        "关闭");

    /// <inheritdoc />
    protected override IExternalAccessAdapter CreateAdapter() => new SerialAccessAdapter();

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "已打开"
            : $"已打开 {snapshot.Detail}",
        ExternalAccessState.Opening => string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "正在打开…"
            : $"正在打开 {snapshot.Detail}",
        ExternalAccessState.Closing => "正在关闭…",
        ExternalAccessState.Faulted => string.IsNullOrWhiteSpace(snapshot.LastError)
            ? "打开失败"
            : snapshot.LastError,
        _ => snapshot.Suspended ? "实例已停用" : "未打开",
    };
}
