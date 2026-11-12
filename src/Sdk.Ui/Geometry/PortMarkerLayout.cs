using Avalonia;

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
