using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **仪表盘几何的不变量**：刻度环整体落在控件内并留出边距。
/// </summary>
/// <remarks>
/// 用户看到过"外面的圆左右被裁"：根因有两个，且这条用例最初只盖住了第一个 ——
/// 限宽时既没扣环线厚度，也把水平最远点当成弧端点（张角 ≥ 180° 时弧其实经过圆的左右极点）。
/// 这里钉"任意宽高比下、四个方向都在边界内且留白"，不钉具体半径。
/// </remarks>
public class GaugeGeometryTests
{
    /// <summary>常见控件尺寸（宽 × 高 × 标题占用高）下，环外沿都在边界内且留白。</summary>
    [Theory]
    [InlineData(300, 300, 0)]
    [InlineData(250, 270, 19)]
    [InlineData(271, 235, 0)]
    [InlineData(558, 250, 0)]
    [InlineData(214, 550, 0)]
    [InlineData(160, 300, 0)]
    [InlineData(400, 224, 0)]
    public void 圆盘几何_外沿在控件内且留有边距(double width, double height, double titleHeight)
    {
        var metrics = GaugeGeometry.Compute(width, height, 2, titleHeight, 240, 2);

        metrics.Radius.ShouldBeGreaterThan(0);

        var sin = Math.Sin(metrics.HalfAngle);
        var cos = Math.Cos(metrics.HalfAngle);
        var horizontalFactor = metrics.HalfAngle >= Math.PI / 2 ? 1.0 : sin;
        var halfThickness = metrics.Thickness / 2;
        var left = metrics.CenterX - (metrics.Radius * horizontalFactor) - halfThickness;
        var right = metrics.CenterX + (metrics.Radius * horizontalFactor) + halfThickness;
        var top = metrics.CenterY - metrics.Radius - halfThickness;
        var bottom = metrics.CenterY - (metrics.Radius * cos) + halfThickness;
        var pad = GaugeGeometry.EdgePadding - 0.5;

        left.ShouldBeGreaterThanOrEqualTo(pad);
        right.ShouldBeLessThanOrEqualTo(width - pad);
        top.ShouldBeGreaterThanOrEqualTo(pad);
        bottom.ShouldBeLessThanOrEqualTo(height - pad);
    }

    /// <summary>弧经过圆的左右极点时，限宽用的是半径本身（不是端点跨度）。</summary>
    [Theory]
    [InlineData(300, 300)]
    [InlineData(250, 270)]
    [InlineData(160, 300)]
    public void 宽大于高时圆盘不被左右裁(double width, double height)
    {
        var metrics = GaugeGeometry.Compute(width, height, 2, 0, 240, 2);

        var extent = metrics.CenterX + metrics.Radius + (metrics.Thickness / 2);
        extent.ShouldBeLessThanOrEqualTo(width - GaugeGeometry.EdgePadding + 0.5);
        (metrics.CenterX - metrics.Radius - (metrics.Thickness / 2))
            .ShouldBeGreaterThanOrEqualTo(GaugeGeometry.EdgePadding - 0.5);
    }

    /// <summary>放不下时不产生几何，调用方据此跳过绘制。</summary>
    [Fact]
    public void 放不下时不产生圆心与半径()
    {
        var metrics = GaugeGeometry.Compute(20, 20, 2, 0, 240, 2);
        metrics.Radius.ShouldBe(0);
    }
}
