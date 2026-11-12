using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **屏幕几何量化口径**：整数物理像素是唯一量化点，DIP 由像素派生。
/// </summary>
/// <remarks>
/// 断言的是**关系/不变量**（整数像素位移不改变像素矩形关系），不是某个窗口的当前宽高；
/// 设计者改布局不需要改这里。
/// </remarks>
public class ScreenPixelsTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void 整数像素与DIP应可逆往返(double scaling)
    {
        foreach (var pixel in new[] { -100, 0, 1, 101, 1000, 4096 })
        {
            var dip = ScreenPixels.DipOfPixel(pixel, scaling);
            ScreenPixels.ToPixel(dip * scaling).ShouldBe(pixel);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void 整组共用整数像素位移时应保持像素矩形关系(double scaling)
    {
        var start = ScreenPixels.PixelRectOf(new CanvasRect(101.0 / scaling, 77.0 / scaling, 200, 120), scaling);
        var moved = ScreenPixels.PixelRectOf(
            new CanvasRect((101 + 7) / scaling, (77 - 3) / scaling, 200, 120),
            scaling);

        moved.Left.ShouldBe(start.Left + 7);
        moved.Right.ShouldBe(start.Right + 7);
        moved.Top.ShouldBe(start.Top - 3);
        moved.Bottom.ShouldBe(start.Bottom - 3);
        (moved.Right - moved.Left).ShouldBe(start.Right - start.Left);
        (moved.Bottom - moved.Top).ShouldBe(start.Bottom - start.Top);
    }
}
