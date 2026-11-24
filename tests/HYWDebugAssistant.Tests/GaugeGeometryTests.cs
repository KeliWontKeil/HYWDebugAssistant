using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **仪表盘几何的不变量**：圆心固定在控件中心，刻度环整体落在控件内，并按 <c>Padding</c> 留白。
/// </summary>
/// <remarks>
/// 用户看到过两个问题：外面的圆被裁、圆心随标题下移。这里钉“圆心 = 控件中心”“半径只由短边决定”
/// 以及“四个方向都在边界内”，不钉具体字体的排版。
/// </remarks>
public class GaugeGeometryTests
{
    /// <summary>常见控件尺寸（宽 × 高 × 留白）下，环外沿都在边界内且留白。</summary>
    [Theory]
    [InlineData(300, 300, 6)]
    [InlineData(250, 270, 6)]
    [InlineData(271, 235, 6)]
    [InlineData(558, 250, 6)]
    [InlineData(214, 550, 6)]
    [InlineData(160, 300, 6)]
    [InlineData(400, 224, 6)]
    [InlineData(250, 270, 0)]
    [InlineData(300, 300, 12)]
    public void 圆盘几何_外沿在控件内且留白不小于Padding(double width, double height, double padding)
    {
        var metrics = GaugeGeometry.Compute(width, height, padding, 240, 2);

        metrics.Radius.ShouldBeGreaterThan(0);

        var sin = Math.Sin(metrics.HalfAngle);
        var cos = Math.Cos(metrics.HalfAngle);
        var horizontalFactor = metrics.HalfAngle >= Math.PI / 2 ? 1.0 : sin;
        var halfThickness = metrics.Thickness / 2;
        var left = metrics.CenterX - (metrics.Radius * horizontalFactor) - halfThickness;
        var right = metrics.CenterX + (metrics.Radius * horizontalFactor) + halfThickness;
        var top = metrics.CenterY - metrics.Radius - halfThickness;
        var bottom = metrics.CenterY - (metrics.Radius * cos) + halfThickness;
        var pad = padding - 0.5;

        left.ShouldBeGreaterThanOrEqualTo(pad);
        right.ShouldBeLessThanOrEqualTo(width - pad);
        top.ShouldBeGreaterThanOrEqualTo(pad);
        bottom.ShouldBeLessThanOrEqualTo(height - pad);
    }

    /// <summary>圆心恒为控件中心；标题/字体不参与几何。</summary>
    [Theory]
    [InlineData(300, 300)]
    [InlineData(250, 270)]
    [InlineData(558, 250)]
    [InlineData(160, 300)]
    public void 圆心固定在控件中心(double width, double height)
    {
        var metrics = GaugeGeometry.Compute(width, height, 4, 240, 2);

        metrics.CenterX.ShouldBe(width / 2, 0.001);
        metrics.CenterY.ShouldBe(height / 2, 0.001);
    }

    [Fact]
    public void 半径只由短边决定()
    {
        var wide = GaugeGeometry.Compute(400, 200, 0, 240, 2);
        var tall = GaugeGeometry.Compute(200, 400, 0, 240, 2);

        wide.Radius.ShouldBe(tall.Radius, 0.001);
    }

    /// <summary>尺寸小到放不下（或留白吃掉全部空间）时不产生几何，调用方据此跳过绘制。</summary>
    [Theory]
    [InlineData(20, 5, 0)]
    [InlineData(40, 300, 30)]
    public void 放不下时不产生圆心与半径(double width, double height, double padding)
    {
        var metrics = GaugeGeometry.Compute(width, height, padding, 240, 2);
        metrics.Radius.ShouldBe(0);
    }
}
