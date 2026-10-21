using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.TcpClient;

/// <summary>
/// **TCP 客户端**：把一条 TCP 连接接到工作区 —— 对端来的字节从 <c>rx</c> 出去，发往对端的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>本版只有形态</b>：声明（类型 / 口 / 角色 / 界面契约）+ 配置；**没有 IO 逻辑**；</item>
///   <item><b>一条连接 = 一个会话</b>：一进一出两个口是它的两个端点；断线由框架按连接策略处理；</item>
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
public sealed class TcpClientModule : IModule
{
    // ---------------------------------------------------------------- ① 标识与常量

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

    /// <summary>数据类型标识 —— 与既有模块**逐字相同**。</summary>
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

        // 读即登记：参数出现在模块窗口的配置表单里，并随组合件保存。
        _ = context.Configuration.Read<TcpClientConfig>();

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
            "连接"));

        DeclareUi(context);
    }

    /// <summary>界面契约（显示源 / 命令）：与"画在哪"分家，名字只在这里写一次。</summary>
    private static void DeclareUi(IModuleContext context)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareCommand(OpenCommand, "连接");
        context.Declaration.DeclareCommand(CloseCommand, "断开");
    }

    // ---------------------------------------------------------------- ③ 钩子块 / ④ 接线与业务

    // 本版不实现任何钩子与业务：开会话、发字节、断线处理都在 P4（外部接入接线）。
}
