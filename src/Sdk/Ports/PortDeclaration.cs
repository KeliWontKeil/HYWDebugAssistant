using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// 口的**方向两态**——ADR-0022（双向 = 两个口 + 两条单向网络）。
/// </summary>
public enum PortDirection
{
    /// <summary>发送口（网络的源）。</summary>
    Send = 0,

    /// <summary>接收口（网络的宿）。</summary>
    Receive = 1,
}

/// <summary>
/// 口（成员机制的首个实例）声明——ADR-0028 §3.2 / 主文档 §5.4 PQ-IF-02。
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
}
