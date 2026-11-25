using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **拖动必须是指针位置的纯函数**：同一落点重复进出不得漂移（吸附候选在手势开始时冻结）。
/// </summary>
/// <remarks>
/// 旧实现每帧按新层重建候选：靠近/离开某个带时对齐候选整体换一批，位置会在阈值附近两态跳。
/// </remarks>
public class DragPurityTests
{
    [Fact]
    public void 同一指针位置应得到同一落点()
    {
        using var harness = new TilingHarness(
            [
                ("a", 0, 0, 100, 100),
                ("b", 100, 0, 100, 100),
                ("far", 0, 400, 100, 100),
            ],
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "far" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;
        var a = harness.Module("a");
        var b = harness.Module("b");

        windows.BeginDrag("a", pointerX: 50, pointerY: 50, independent: false).ShouldBeTrue();

        windows.DragTo(pointerX: 70, pointerY: 50);
        var first = a.PixelRect.Left;

        windows.DragTo(pointerX: 70, pointerY: 50);
        a.PixelRect.Left.ShouldBe(first, "同一指针位置重复到达不得漂移");

        // 阈值附近往复：进去、出来、再进去，最终必须回到同一落点（不许积累偏移）。
        windows.DragTo(pointerX: 52, pointerY: 50);
        windows.DragTo(pointerX: 70, pointerY: 50);
        a.PixelRect.Left.ShouldBe(first, "往复不得发散");

        b.PixelRect.Left.ShouldBe(a.PixelRect.Left + 100, "件内相对位置在整个拖动中不变");

        windows.EndDrag();
    }
}
