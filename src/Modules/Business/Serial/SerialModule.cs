using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Serial;

/// <summary>
/// **串口**：把一路串口接到工作区 —— 设备来的字节从 <c>rx</c> 出去，发往设备的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>本版只有形态</b>：声明（类型 / 口 / 角色 / 界面契约）+ 配置。**没有 IO 逻辑** ——
///     外部接入的接线（谁开会话、谁等就绪、字节怎么变成数据单元）见执行计划的 P1/P3；</item>
///   <item><b>一个通道 = 一个会话</b>：一进一出两个口只是它的两个端点，不是两件事；</item>
///   <item><b>契约沿用原始字节</b>：与文本发送器 / HEX 发送器 / 数据监视器**同一标识同一形状**才开箱即连；</item>
///   <item><b>结构约定</b>：与既有模块同一套四段写法 —— ① 标识与常量 ② 声明块 ③ 钩子块 ④ 接线与业务。</item>
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
public sealed class SerialModule : IModule
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>**发送口**（本模块是源）：设备 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 设备。</summary>
    public const string TxPort = "tx";

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

    /// <summary>数据角色 Id（**一个分块、一种类型、一进一出**）。</summary>
    public const string RoleId = "serial";

    /// <summary>
    /// 数据类型标识 —— 与既有四个模块**逐字相同**（跨模块同标识 + 同形状才兼容 ⇒ 开箱即连）。
    /// </summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单个数据单元的字节区容量（= 一个完整 UDP 载荷量级）。</summary>
    public const int Capacity = 1472;

    /// <summary><c>RawBytes@v1</c> 的契约形状：<c>{ len: u16, data: bytes[1472] }</c>。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(Capacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // **读即登记**：读一次配置，框架就把它登记进该实例的配置快照 ——
        // 模块窗口的参数格（`configurationField`）据此取到字段的值与可选项，组合件据此保存它。
        // 本版没有任何别的逻辑，这一步就是全部"行为"。
        _ = context.Configuration.Read<SerialConfig>();

        // ① 数据类型（形状即契约）→ ② 口 → ③ 角色（顺序即既有模块的声明块顺序）
        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesVersion,
            RawBytesShape)
        {
            // `len` = 有效字节数 ⇒ 第 3 档速率才是真实有效速率（未声明时框架只能按契约尺寸算）
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(RxPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),

            // 设备来的字节从**下边**出去（与数据监视器的上边配对）；用户仍可拖动覆盖。
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclarePort(new PortDeclaration(TxPort, PortDirection.Receive, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),

            // 发往设备的字节从**上边**进来（与发送器的下边配对）。
            Edge = PortEdge.Top,
        });

        // **一进一出**：进 = 发往设备（tx），出 = 设备来的（rx）—— 两侧同型（都是 RawBytes）。
        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            TxPort,
            RxPort,
            "串口"));

        DeclareUi(context);
    }

    /// <summary>
    /// **界面契约**：显示源 / 命令。
    /// </summary>
    /// <remarks>
    /// 这些是**契约事实**（界面能读什么、能触发什么），与"画在哪、长什么样"分家 ——
    /// 后者由本模块自己的视图负责。声明是唯一来源：漏一条的后果是"界面永远读不到它"，
    /// 所以名字在这里只写一次，不再在任何数据文件里重复。
    /// </remarks>
    private static void DeclareUi(IModuleContext context)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareCommand(OpenCommand, "打开");
        context.Declaration.DeclareCommand(CloseCommand, "关闭");
    }

    // ---------------------------------------------------------------- ③ 钩子块

    // 本版不实现任何钩子：没有活动体（框架跑投递循环）、没有配置热生效、没有需要释放的资源。
    // P3 接上外部接入后，这里会出现"打开 / 关闭会话"的请求与状态上报 —— 那时候再写。

    // ---------------------------------------------------------------- ④ 接线与业务

    // 同上：接线与业务在 P3。
}
