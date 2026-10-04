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
///   <item><b>口级自动建网开关</b>：<see cref="ParticipatesInAutoNetwork"/> 对应基线 §2.5 的"是否参与自动建网"；
///         全局 R0 开关关闭时该属性无效。</item>
/// </list>
/// </remarks>
public sealed record PortDeclaration
{
    /// <summary>创建口声明。</summary>
    /// <param name="name">口名（模块内唯一、稳定；建议小写短名）。</param>
    /// <param name="direction">方向（两态）。</param>
    /// <param name="shape">契约形状（受控原语组合，定长）。</param>
    /// <param name="semantics">传递语义标注（流/消息/事件）。</param>
    /// <param name="participatesInAutoNetwork">是否参与自动建网（R0–R6 快速建网）。</param>
    public PortDeclaration(
        string name,
        PortDirection direction,
        ContractShape shape,
        TransferSemantics semantics = TransferSemantics.Stream,
        bool participatesInAutoNetwork = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);

        Name = name;
        Direction = direction;
        Shape = shape;
        Semantics = semantics;
        ParticipatesInAutoNetwork = participatesInAutoNetwork;
    }

    /// <summary>口名（模块内唯一、稳定）。</summary>
    public string Name { get; }

    /// <summary>方向（发送 / 接收）。</summary>
    public PortDirection Direction { get; }

    /// <summary>契约形状（定长，受控原语组合）。</summary>
    public ContractShape Shape { get; }

    /// <summary>传递语义标注（跨语义不判兼容）。</summary>
    public TransferSemantics Semantics { get; }

    /// <summary>是否参与快速建网（R0–R6）。</summary>
    public bool ParticipatesInAutoNetwork { get; }

    /// <summary>
    /// 本口承载的**数据类型引用**（指向模块已声明的类型）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**可声明 ≠ 必须声明**：不声明 = 旧模块形态，兼容判定回退为**结构**比较（向后兼容）；</item>
    ///   <item>声明后兼容走**三层判据**（同标识同版本 / 同标识版本规则 / **不同标识 = 默认不兼容 + 显式确认可强连**）；</item>
    ///   <item>用 <c>init</c> 属性而非构造器参数：加可选构造参数会**改变方法签名**，已编译模块会抛
    ///     <c>MissingMethodException</c>（实测踩过）。</item>
    /// </list>
    /// 用法：<c>new PortDeclaration("in", PortDirection.Receive, shape) { Type = new("hyw/f32", 1) }</c>
    /// </remarks>
    public DataTypeRef? Type { get; init; }
}
