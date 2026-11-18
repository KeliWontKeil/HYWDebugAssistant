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
/// **仪表盘几何的唯一算法**：保证刻度环整体落在控件内，并留出边距。
/// </summary>
/// <remarks>
/// <para>
/// 两个必须同时成立的事实：
/// </para>
/// <list type="bullet">
///   <item><b>水平最远点不是弧端点</b>：张角 ≥ 180° 时弧经过圆的左右极点，最远点是
///     <c>半径本身</c>；只按"端点水平跨度（r·sin）"限宽，环会从左右被裁。</item>
///   <item>环线自身有厚度，限宽/限高都要扣掉 <c>厚度/2</c> 再留边距。</item>
/// </list>
/// <para>
/// 半径与厚度互相依赖，迭代两轮收敛；放得下的富余高度上下均分，表盘在控件里视觉居中。
/// </para>
/// </remarks>
internal static class GaugeGeometry
{
    /// <summary>圆环外沿与控件边之间的最小留白（DIP）。</summary>
    public const double EdgePadding = 4;

    /// <summary>按可用尺寸算出一个完整落在控件内的刻度环。</summary>
    /// <param name="width">控件宽（DIP）。</param>
    /// <param name="height">控件高（DIP）。</param>
    /// <param name="inset">控件内缩（DIP）。</param>
    /// <param name="titleHeight">标题占用的高度（DIP，可为 0）。</param>
    /// <param name="sweepAngle">弧的总张角（度）。</param>
    /// <param name="minimumThickness">环厚下限（DIP）。</param>
    public static GaugeMetrics Compute(
        double width,
        double height,
        double inset,
        double titleHeight,
        double sweepAngle,
        double minimumThickness)
    {
        if (!(width > 0) || !(height > 0) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return default;
        }

        var sweep = double.IsFinite(sweepAngle) ? Math.Clamp(sweepAngle, 40, 330) : 240;
        var half = sweep * Math.PI / 360;
        var sin = Math.Sin(half);
        var cos = Math.Cos(half);
        if (sin < 0.05)
        {
            return default;
        }

        var pad = EdgePadding;
        var contentTop = inset + (double.IsFinite(titleHeight) && titleHeight > 0 ? titleHeight : 0) + pad;
        var availableHeight = height - inset - pad - contentTop;
        if (availableHeight <= 8)
        {
            return default;
        }

        // 半张角 ≥ 90° 时，弧的水平最远点是圆的左右极点（半径本身），不是端点。
        var horizontalFactor = half >= Math.PI / 2 ? 1.0 : sin;

        var thickness = Math.Max(2, double.IsFinite(minimumThickness) ? minimumThickness : 2);
        var radius = 0.0;

        for (var iteration = 0; iteration < 2; iteration++)
        {
            var horizontalLimit = ((width / 2) - inset - pad - (thickness / 2)) / horizontalFactor;
            var verticalLimit = (availableHeight - thickness) / (1 - cos);
            radius = Math.Min(horizontalLimit, verticalLimit);
            if (!(radius > 0))
            {
                return default;
            }

            thickness = Math.Max(2, radius * 0.045);
        }

        var ringHeight = (radius * (1 - cos)) + thickness;
        var slack = availableHeight - ringHeight;
        var top = contentTop + (slack > 0 ? slack / 2 : 0);

        return new GaugeMetrics(
            width / 2,
            top + radius + (thickness / 2),
            radius,
            thickness,
            half);
    }
}
