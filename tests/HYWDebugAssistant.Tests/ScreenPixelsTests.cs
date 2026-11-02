using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **窗口落位的取整口径**：位置与尺寸必须由同一个整数物理矩形推出，
/// 否则 125%/150% 下相邻窗口的公共边会差 1px。
/// </summary>
public class ScreenPixelsTests
{
    private static readonly double[] Scalings = [1.0, 1.25, 1.5, 1.75, 2.0];

    /// <summary>相邻矩形：A 的右边缘必须严丝合缝等于 B 的左边缘（所有缩放）。</summary>
    [Fact]
    public void 相邻矩形的公共边应落在同一物理像素()
    {
        var a = new CanvasRect(0, 0, 359, 240);
        var b = new CanvasRect(359, 0, 359, 240);

        foreach (var scaling in Scalings)
        {
            var left = ScreenPixels.PixelRectOf(a, scaling);
            var right = ScreenPixels.PixelRectOf(b, scaling);

            right.Left.ShouldBe(
                left.Right,
                $"缩放 {scaling}：B.Left 必须等于 A.Right（否则公共边差 1px）");
            right.Top.ShouldBe(left.Top);
            right.Bottom.ShouldBe(left.Bottom);
        }
    }

    /// <summary>负坐标（左副屏）：x=-359 在 1.25 下 Left=-449，且与 x=0 的矩形 Left=0 相接。</summary>
    [Fact]
    public void 负坐标应与原点矩形相接()
    {
        var left = ScreenPixels.PixelRectOf(new CanvasRect(-359, 0, 359, 240), 1.25);
        var origin = ScreenPixels.PixelRectOf(new CanvasRect(0, 0, 359, 240), 1.25);

        left.Left.ShouldBe(-449);
        left.Right.ShouldBe(0);
        origin.Left.ShouldBe(left.Right, "负坐标矩形的右边缘必须等于原点矩形的左边缘");
    }

    /// <summary>退化输入：缩放 0 / NaN 按 1.0；矩形非法不抛。</summary>
    [Fact]
    public void 退化输入不应抛()
    {
        var rect = new CanvasRect(10, 20, 100, 50);

        ScreenPixels.PixelRectOf(rect, 0).ShouldBe(new PixelEdges(10, 20, 110, 70));
        ScreenPixels.PixelRectOf(rect, double.NaN).ShouldBe(new PixelEdges(10, 20, 110, 70));

        var invalid = new CanvasRect(double.NaN, double.NaN, double.NaN, double.NaN);
        var edges = ScreenPixels.PixelRectOf(invalid, 1.25);
        edges.Left.ShouldBe(0);
        edges.Right.ShouldBe(0);
    }
}
