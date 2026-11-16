using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **外壳参照线只接受模块低侧边**：竖线即左边缘，与“模块左边缘贴外壳右边缘”的成组判据同侧。
/// </summary>
/// <remarks>
/// 若右边缘也能吸上去，模块会以右边缘对齐外壳右边缘 —— 视觉上压在壳上，
/// 但 `AreNodesJoined` 只认左边缘 ⇒ 看起来吸上了却不成组。
/// </remarks>
public class ShellSnapTests
{
    [Fact]
    public void 外壳线只吸附模块左边缘()
    {
        var line = SnapEngine.LineOf(new SnapReferenceLine(IsVertical: true, Position: 100, Start: 100, End: 300));

        // 模块整体在线的左侧、右边缘正好贴线：这是“压在壳上”的候选，必须不吸。
        var overlapping = SnapEngine.Snap(
            new CanvasRect(0, 100, 100, 100),
            [line],
            tolerance: 4,
            flushTolerance: 8);

        overlapping.SnappedX.ShouldBeFalse("外壳线不能吸附模块右边缘，否则会重叠且不成组");

        // 模块左边缘接近线：这才是贴壳的锚点。
        var attaching = SnapEngine.Snap(
            new CanvasRect(96, 100, 100, 100),
            [line],
            tolerance: 4,
            flushTolerance: 8);

        attaching.SnappedX.ShouldBeTrue();
        attaching.X.ShouldBe(100);
    }

    [Fact]
    public void 外壳对齐线允许模块高侧边()
    {
        var line = SnapEngine.LineOf(new SnapReferenceLine(
            IsVertical: true,
            Position: 100,
            Start: 0,
            End: 300,
            Role: ShellLineRole.Align));

        // 模块右边缘贴线：对齐线允许高侧边参与（但不产生贴合/成组）。
        var rightSide = SnapEngine.Snap(
            new CanvasRect(0, 100, 100, 100),
            [line],
            tolerance: 4,
            flushTolerance: 8);

        rightSide.SnappedX.ShouldBeTrue();
        rightSide.X.ShouldBe(0);
        rightSide.XKind.ShouldNotBe(SnapKind.Flush);
    }
}
