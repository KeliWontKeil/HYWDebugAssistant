namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>仪表盘几何：圆心、半径、环厚与半张角。</summary>
/// <param name="CenterX">圆心 X（控件本地坐标）。</param>
/// <param name="CenterY">圆心 Y。</param>
/// <param name="Radius">刻度环中径。</param>
/// <param name="Thickness">刻度环线宽。</param>
/// <param name="HalfAngle">弧的半张角（弧度）。</param>
internal readonly record struct GaugeMetrics(
    double CenterX,
    double CenterY,
    double Radius,
    double Thickness,
    double HalfAngle);

/// <summary>
/// **仪表盘几何的唯一算法**：圆心固定在控件中心，刻度环按短边内接，四周按 <c>Padding</c> 留白。
/// </summary>
/// <remarks>
/// <para>
/// 三个必须同时成立的事实：
/// </para>
/// <list type="bullet">
///   <item><b>圆心 = 控件中心</b>：标签、读数、单位都相对圆心排；圆心若随标题下移，
///     上下边框与布局都会变得不可预测。</item>
///   <item>环线自身有厚度：半径按 <c>min(宽, 高)/2 − padding − 厚度/2</c> 限，环外沿才落在控件内。</item>
///   <item><c>padding</c> 是**唯一的留白来源**（0 = 环外沿贴控件边）；标题/读数/单位在环内绘制，
///     不参与尺寸计算，因此改文字不会改变表盘大小。</item>
/// </list>
/// <para>
/// 半径与厚度互相依赖，迭代收敛后按最终厚度回算半径，保证外沿精确落在边界内。
/// </para>
/// </remarks>
internal static class GaugeGeometry
{
    /// <summary>默认留白（DIP）：<c>GaugeDial.Padding</c> 的默认值。</summary>
    public const double DefaultPadding = 6;

    /// <summary>按控件尺寸与留白算出一个完整落在控件内、圆心居中的刻度环。</summary>
    /// <param name="width">控件宽（DIP）。</param>
    /// <param name="height">控件高（DIP）。</param>
    /// <param name="padding">四周留白（DIP；0 = 环外沿贴控件边）。</param>
    /// <param name="sweepAngle">弧的总张角（度）。</param>
    /// <param name="minimumThickness">环厚下限（DIP）。</param>
    public static GaugeMetrics Compute(
        double width,
        double height,
        double padding,
        double sweepAngle,
        double minimumThickness)
    {
        if (!(width > 0) || !(height > 0) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return default;
        }

        var pad = double.IsFinite(padding) && padding > 0 ? padding : 0;
        var sweep = double.IsFinite(sweepAngle) ? Math.Clamp(sweepAngle, 40, 330) : 240;
        var half = sweep * Math.PI / 360;
        if (Math.Sin(half) < 0.05)
        {
            return default;
        }

        var available = Math.Min(width, height) - (2 * pad);
        if (available <= 16)
        {
            return default;
        }

        var thickness = Math.Max(2, double.IsFinite(minimumThickness) ? minimumThickness : 2);
        var radius = (available - thickness) / 2;

        // 半径 ↔ 环厚两轮收敛；最后按最终厚度回算半径，外沿精确贴住内接圆边界。
        for (var iteration = 0; iteration < 2; iteration++)
        {
            radius = (available - Math.Max(2, radius * 0.045)) / 2;
        }

        thickness = Math.Max(2, radius * 0.045);
        radius = (available - thickness) / 2;
        if (!(radius > 0))
        {
            return default;
        }

        return new GaugeMetrics(width / 2, height / 2, radius, thickness, half);
    }
}
