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
}
