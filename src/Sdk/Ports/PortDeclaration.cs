using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// 口的**默认挂载边**（C5-7 · G5）。
/// </summary>
/// <remarks>
/// <see cref="Auto"/> = **框架沿用数据流惯例**（发送口在右边缘、接收口在左边缘）—— 这是**老模块的行为**，
/// 也是默认值。其余四值 = 模块**自己定义默认位置**。用户仍可在画布上把标记拖到任意边（拖动结果随组合件保存、优先于声明）。
/// </remarks>
public enum PortEdge
{
    /// <summary>按数据流惯例：**发送口在右、接收口在左**（默认；老模块行为不变）。</summary>
    Auto = 0,

    /// <summary>左边缘。</summary>
    Left = 1,

    /// <summary>右边缘。</summary>
    Right = 2,

    /// <summary>上边缘。</summary>
    Top = 3,

    /// <summary>下边缘。</summary>
    Bottom = 4,
}

/// <summary>
/// 口的**方向两态**（双向 = 两个口 + 两条单向网络）。
/// </summary>
public enum PortDirection
{
    /// <summary>发送口（网络的源）。</summary>
    Send = 0,

    /// <summary>接收口（网络的宿）。</summary>
    Receive = 1,
}

/// <summary>
/// 口（成员机制的首个实例）声明。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>口名唯一性</b>：模块内唯一且稳定（多同型用「名 + 序号」，如 <c>rx1</c>/<c>rx2</c>）；
///         由框架在加载/实例化期校验，重复即拒绝并报 Issue，**不静默改名**。</item>
///   <item><b>锚定</b>：连接锚定为 <c>实例ID.口名</c>（重锚依据，主文档 §9.3）。</item>
///   <item>**没有"口级自动建网开关"**：是否参与自动建网只由"有没有打标签"决定
///         （打标签 = 只参与命名网络，未打标签 = 可参与接触建网）；全局开关是应用级设置。
///         历史字段 `participatesInAutoNetwork` 声明了却从未被读取，已删除。</item>
/// </list>
/// </remarks>
public sealed record PortDeclaration
{
    /// <summary>创建口声明。</summary>
    /// <param name="name">口名（模块内唯一、稳定；建议小写短名）。</param>
    /// <param name="direction">方向（两态）。</param>
    /// <param name="shape">契约形状（受控原语组合，定长）。</param>
    /// <param name="semantics">传递语义标注（流/消息/事件）。</param>
    public PortDeclaration(
        string name,
        PortDirection direction,
        ContractShape shape,
        TransferSemantics semantics = TransferSemantics.Stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);

        Name = name;
        Direction = direction;
        Shape = shape;
        Semantics = semantics;
    }

    /// <summary>口名（模块内唯一、稳定）。</summary>
    public string Name { get; }

    /// <summary>方向（发送 / 接收）。</summary>
    public PortDirection Direction { get; }

    /// <summary>契约形状（定长，受控原语组合）。</summary>
    public ContractShape Shape { get; }

    /// <summary>传递语义标注（跨语义不判兼容）。</summary>
    public TransferSemantics Semantics { get; }

    /// <summary>
    /// 本口承载的**数据类型标识**（指向模块已声明的类型）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**可声明 ≠ 必须声明**：不声明 = 旧模块形态，兼容判定回退为**结构**比较；</item>
    ///   <item>声明后按 **标识 + 形状完全一致** 判定；**不同标识 = 不兼容**（没有"强制连接"的放行路径）；</item>
    ///   <item>用 <c>init</c> 属性而非构造器参数：加可选构造参数会**改变方法签名**，已编译模块会抛
    ///     <c>MissingMethodException</c>（实测踩过）。</item>
    /// </list>
    /// 用法：<c>new PortDeclaration("in", PortDirection.Receive, shape) { TypeId = "RawBytes" }</c>
    /// </remarks>
    public string? TypeId { get; init; }

    /// <summary>
    /// 本口默认挂在**哪条边**（空 = 沿用数据流惯例：发送在右、接收在左）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用户可以把标记拖到任意边，**拖动结果优先于本声明**（随组合件保存）；这里只是"第一次出现时在哪"。
    /// </para>
    /// <para>
    /// 它是**模块的契约事实**（这个口在现实中长在哪侧），因此写在代码里、与口名/方向/形状同处，
    /// 而不是放进任何外部数据文件。
    /// </para>
    /// </remarks>
    public PortEdge Edge { get; init; } = PortEdge.Auto;

    /// <summary>本口在所属边上的默认位置（0–1；默认居中）。</summary>
    public double AnchorOffset { get; init; } = 0.5;

    /// <summary>
    /// 本口的标记**默认是否可拖动**（<see langword="false"/> = 框架固定项）。
    /// </summary>
    /// <remarks>
    /// 用户显式解锁 / 锁定的结果优先于本项。默认固定：绝大多数口的位置由模块的形态决定，
    /// 允许拖动是例外而非常态。
    /// </remarks>
    public bool AnchorDraggable { get; init; }
}
