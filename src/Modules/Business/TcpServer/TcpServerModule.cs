using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.TcpServer;

/// <summary>
/// **TCP 服务端**：监听一个端口，把每条进来的连接接成一路数据 —— 各连接来的字节从 <c>rx</c> 出去，
/// 发往各连接的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>本版只有形态</b>：声明（类型 / 口 / 角色 / 界面契约）+ 配置；**没有 IO 逻辑**；</item>
///   <item><b>监听 = 一个会话，连接 = 它的多路</b>：对数据面而言仍是一进一出两个口 ——
///     "哪条连接"是会话内部的事，不该让下游按连接数分叉；</item>
///   <item><b>契约沿用原始字节</b>：与既有模块同一标识同一形状才开箱即连。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "tcp-server",
    "1.0.0",
    DisplayName = "TCP 服务端",
    Description = "监听本地端口：各连接来的字节从 rx 出，发往各连接的字节从 tx 进",
    Tags = ["tcp", "server", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190,
    ViewFactory = typeof(Views.TcpServerViewFactory))]
public sealed class TcpServerModule : IModule
{
    // ---------------------------------------------------------------- ① 标识与常量

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

        _ = context.Configuration.Read<TcpServerConfig>();

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
            "监听"));

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
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ConnCountSource, ViewDataSourceKind.Value, ContractPrimitive.U32));

        context.Declaration.DeclareCommand(OpenCommand, "启动");
        context.Declaration.DeclareCommand(CloseCommand, "停止");
    }

    // ---------------------------------------------------------------- ③ 钩子块 / ④ 接线与业务

    // 本版不实现任何钩子与业务：监听、接受连接、收发字节都在 P4（外部接入接线）。
}
