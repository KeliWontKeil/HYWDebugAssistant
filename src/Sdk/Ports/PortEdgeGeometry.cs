namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// **端口挂载边的几何小工具**（G5 · C5-7）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么放在 SDK</b>：这些判定被**多层**共用 —— UI 层（拖动时的"两个口是否面对面"）、
///     Core 层（建网判定）都要用；而 UI **不引用** Core 实现层（分层规则 AR）⇒ 只能落在双方都引用的 SDK；</item>
///   <item>纯几何/枚举运算，无副作用、无状态；<b>只增</b>（新类型 + 静态方法，不改既有签名）。</item>
/// </list>
/// </remarks>
public static class PortEdgeGeometry
{
    /// <summary>取**对面**那条边（左↔右、上↔下）——用于判定"两个口是否面对面"。</summary>
    /// <param name="edge">边。</param>
    /// <returns>对面的边（<see cref="PortEdge.Auto"/> 原样返回：未解析的边没有"对面"可言）。</returns>
    public static PortEdge Opposite(PortEdge edge) => edge switch
    {
        PortEdge.Left => PortEdge.Right,
        PortEdge.Right => PortEdge.Left,
        PortEdge.Top => PortEdge.Bottom,
        PortEdge.Bottom => PortEdge.Top,
        _ => PortEdge.Auto,
    };

    /// <summary>该边是否为**竖直边**（左/右）——决定"沿边偏移"沿哪个轴读。</summary>
    /// <param name="edge">边。</param>
    public static bool IsVertical(PortEdge edge) => edge is PortEdge.Left or PortEdge.Right;

    /// <summary>该边是否为**水平边**（上/下）。</summary>
    /// <param name="edge">边。</param>
    public static bool IsHorizontal(PortEdge edge) => edge is PortEdge.Top or PortEdge.Bottom;

    /// <summary>
    /// **数据流惯例**：<see cref="PortEdge.Auto"/> 落成发送在右、接收在左。
    /// </summary>
    /// <param name="direction">口方向。</param>
    /// <remarks>
    /// 这是"没特别要求时"的唯一惯例实现：Core 的声明解析、预览外壳、快照共用本方法，
    /// 不在各处再写一份三元表达式。
    /// </remarks>
    public static PortEdge DefaultFor(PortDirection direction) =>
        direction == PortDirection.Send ? PortEdge.Right : PortEdge.Left;
}
