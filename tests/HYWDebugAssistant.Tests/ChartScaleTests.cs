using HYWDebugAssistant.Sdk.Ui.Controls.Charting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>滚轮缩放的纯数学：围绕锚点缩放，非法输入原样返回。</summary>
public class ChartScaleTests
{
    [Fact]
    public void 围绕锚点缩放时锚点值保持不动()
    {
        var (min, max) = ChartScale.ZoomAt(0, 100, 50, 2);

        min.ShouldBe(25);
        max.ShouldBe(75);
        (min + max).ShouldBe(100);
    }

    [Fact]
    public void 非法输入返回原范围()
    {
        ChartScale.ZoomAt(0, 100, 50, 0).ShouldBe((0d, 100d));
        ChartScale.ZoomAt(100, 100, 50, 2).ShouldBe((100d, 100d));
    }

    [Fact]
    public void 图表数学_刻度映射与夹紧()
    {
        ChartScale.NiceStep(100, 10).ShouldBe(10);
        ChartScale.NiceStep(10, 10).ShouldBe(1);
        ChartScale.Map(5, 0, 10, 0, 100).ShouldBe(50);
        ChartScale.Unmap(50, 0, 10, 0, 100).ShouldBe(5);
        ChartScale.Clamp(12, 0, 10).ShouldBe(10);
        ChartScale.Clamp(-2, 0, 10).ShouldBe(0);
    }

    [Fact]
    public void 图表数学_拖动应平移范围并绕中心缩放()
    {
        var pan = ChartScale.Pan(10, 20, 5);
        pan.Min.ShouldBe(15);
        pan.Max.ShouldBe(25);

        var zoomIn = ChartScale.Zoom(-1, 1, 100, 500);
        ((zoomIn.Min + zoomIn.Max) / 2).ShouldBe(0, 1e-12);
        (zoomIn.Max - zoomIn.Min).ShouldBeLessThan(2);

        var zoomOut = ChartScale.Zoom(-1, 1, -100, 500);
        ((zoomOut.Min + zoomOut.Max) / 2).ShouldBe(0, 1e-12);
        (zoomOut.Max - zoomOut.Min).ShouldBeGreaterThan(2);

        var unchanged = ChartScale.Zoom(10, 20, 0, 500);
        unchanged.Min.ShouldBe(10);
        unchanged.Max.ShouldBe(20);
    }
}
