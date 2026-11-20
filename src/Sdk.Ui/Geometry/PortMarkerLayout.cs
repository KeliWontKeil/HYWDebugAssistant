using Avalonia;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Sdk.Ui.Geometry;

/// <summary>
/// **端口标记的沿边排布**——运行时标记层与预览外壳**共用这一份算法**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>沿边位置 = 边长度 × 归一化偏移 − 标记长度 / 2，再夹到 `[0, available − length]`；</item>
///   <item>跨轴贴窗口外沿：<see cref="EdgeInset"/> 为负 ⇒ 标记外沿与窗口外框重合
///     （相邻模块的标记带因此严丝合缝）；</item>
///   <item>**不做方位特判**：左右边 = 竖排，上下边 = 横排，只有这一个维度。</item>
/// </list>
/// </remarks>
public static class PortMarkerLayout
{
    /// <summary>
    /// 标记与窗口外沿的间距（px）——负值表示标记外沿与窗口外框完全重合。
    /// </summary>
    /// <remarks>
    /// 运行时面板位于描边**之内**，取负值让标记压到窗口外框上；否则相邻两窗之间会留出描边缝。
    /// </remarks>
    public const double EdgeInset = -1;

    /// <summary>
    /// **标记沿边占的那一段**（起点 + 可用长度）——标记层与按钮层共用。
    /// </summary>
    /// <param name="side">所在边。</param>
    /// <param name="offset">沿边归一化偏移（0–1；越界会夹紧）。</param>
    /// <param name="panel">面板尺寸。</param>
    /// <param name="marker">标记尺寸。</param>
    public static (double Start, double Available) AlongEdgeSpan(
        PortSide side,
        double offset,
        Size panel,
        Size marker)
    {
        var vertical = side is PortSide.Left or PortSide.Right;
        var available = vertical ? panel.Height : panel.Width;
        var length = vertical ? marker.Height : marker.Width;

        var start = (available * Math.Clamp(offset, 0, 1)) - (length / 2);
        start = Math.Clamp(start, 0, Math.Max(0, available - length));
        return (start, available);
    }

    /// <summary>
    /// **这条边放不放得下标记**：沿边长度必须容得下固定长度（上下边看宽、左右边看高）。
    /// </summary>
    /// <param name="side">边。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    public static bool Fits(PortSide side, double width, double height) =>
        (side is PortSide.Left or PortSide.Right ? height : width) >= PortMarkerMetrics.Length;

    /// <summary>
    /// **把声明边夹到放得下的边**：放得下就原样返回，放不下就**直接换一条**（单向，不记原边）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **标记尺寸固定**（缩长会丢标签信息），所以空间不够时改的是**落哪条边**。
    /// 返回的就是最终边，调用方**直接写回标记的边**（坐标式变更）——没有"声明边/有效边"两份状态，
    /// 也没有"空间回来再挪回去"的记忆；窗口再变宽时这条边已经放得下，自然保持不动。
    /// </para>
    /// <para>
    /// 不看锚点"固定"状态：固定只约束**拖动**，不约束**放不下时的避让**。
    /// 高度地板（160）保证左右边总能放下，因此"全都放不下"只会在人为绕过地板时出现 —— 那时保留原边。
    /// </para>
    /// </remarks>
    /// <param name="declared">声明边（`Auto` → 发送右 / 接收左）。</param>
    /// <param name="direction">方向（只用于解析 <c>Auto</c>）。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    public static PortEdge FitEdge(PortEdge declared, PortDirection direction, double width, double height)
    {
        var primary = declared == PortEdge.Auto
            ? (direction == PortDirection.Send ? PortEdge.Right : PortEdge.Left)
            : declared;

        if (Fits(SideOf(primary), width, height))
        {
            return primary;
        }

        var mirror = primary switch
        {
            PortEdge.Left => PortEdge.Right,
            PortEdge.Right => PortEdge.Left,
            PortEdge.Top => PortEdge.Bottom,
            _ => PortEdge.Top,
        };

        foreach (var edge in new[] { mirror, PortEdge.Left, PortEdge.Right, PortEdge.Top, PortEdge.Bottom })
        {
            if (Fits(SideOf(edge), width, height))
            {
                return edge;
            }
        }

        return primary;
    }

    /// <summary>
    /// **拖动落边**：最近边放不下标记 ⇒ 返回 <see langword="null"/>（保持当前边，不落过去）。
    /// </summary>
    /// <remarks>
    /// 落位与"换边"是同一份尺寸约束：允许落到放不下的边，松手后 <see cref="FitEdge"/> 又会把它挪走 ——
    /// 用户看到的就是"拖过去又弹回"。所以拖动阶段就拒绝这条边（指针移到可用边才能继续落位）。
    /// </remarks>
    /// <param name="nearest">指针最近的那条边。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    public static PortEdge? PickDragEdge(PortEdge nearest, double width, double height) =>
        Fits(SideOf(nearest), width, height) ? nearest : null;
    /// <summary>边 → 视图用的侧（`Auto` 只会在"声明缺失"的兜底路径出现，按左边渲染）。</summary>
    /// <param name="edge">边。</param>
    public static PortSide SideOf(PortEdge edge) => edge switch
    {
        PortEdge.Right => PortSide.Right,
        PortEdge.Top => PortSide.Top,
        PortEdge.Bottom => PortSide.Bottom,
        _ => PortSide.Left,
    };

    /// <summary>标记层的矩形：沿边按偏移分布，跨轴贴窗口外沿。</summary>
    /// <param name="side">所在边。</param>
    /// <param name="offset">沿边归一化偏移。</param>
    /// <param name="panel">面板尺寸（通常是窗口外框尺寸）。</param>
    /// <param name="marker">标记尺寸。</param>
    public static Rect MarkerRect(PortSide side, double offset, Size panel, Size marker)
    {
        var (start, _) = AlongEdgeSpan(side, offset, panel, marker);

        return side switch
        {
            PortSide.Left => new Rect(EdgeInset, start, marker.Width, marker.Height),
            PortSide.Right => new Rect(
                Math.Max(0, panel.Width - marker.Width - EdgeInset), start, marker.Width, marker.Height),
            PortSide.Top => new Rect(start, EdgeInset, marker.Width, marker.Height),
            _ => new Rect(
                start, Math.Max(0, panel.Height - marker.Height - EdgeInset), marker.Width, marker.Height),
        };
    }
}
