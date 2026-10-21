using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Udp;

/// <summary>
/// **UDP**：绑定一个本地端口收发数据报 —— 收到的数据报从 <c>rx</c> 出去，
/// 要发出去的从 <c>tx</c> 进来（发给参数里的默认目标）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>本版只有形态</b>：声明（类型 / 口 / 角色 / 界面契约）+ 配置；**没有 IO 逻辑**；</item>
///   <item><b>绑定 = 一个会话</b>：UDP 无连接 ⇒ 会话里没有"连接状态"，只有"绑定没绑定"；</item>
///   <item><b>契约沿用原始字节</b>：与既有模块同一标识同一形状才开箱即连。</item>
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
public sealed class UdpModule : IModule
{
    // ---------------------------------------------------------------- ① 标识与常量

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

    /// <summary>数据类型标识 —— 与既有模块逐字相同。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单个数据单元的字节区容量。</summary>
    public const int Capacity = 1472;

    /// <summary><c>RawBytes@v1</c> 的契约形状。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(Capacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = context.Configuration.Read<UdpConfig>();

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesVersion,
            RawBytesShape)
        {
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(RxPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclarePort(new PortDeclaration(TxPort, PortDirection.Receive, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            TxPort,
            RxPort,
            "通道"));

        DeclareUi(context);
    }

    /// <summary>界面契约（显示源 / 命令）。</summary>
    private static void DeclareUi(IModuleContext context)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareCommand(OpenCommand, "绑定");
        context.Declaration.DeclareCommand(CloseCommand, "解绑");
    }

    // ---------------------------------------------------------------- ③ 钩子块 / ④ 接线与业务

    // 本版不实现任何钩子与业务：绑定、收数据报、发数据报都在 P4（外部接入接线）。
}
