using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **吸附障碍按层判**：压在更低层上是合法的，压在目标层或更高层上才作废。
/// </summary>
/// <remarks>
/// 旧口径是"与任何模块重合就整帧不吸附"，密集布局里"目标位置背后有块东西"几乎是常态，
/// 于是正常情况也拼不上。现在只有"压住**目标层**"的那一条候选被剔除，其余候选照常参与。
/// </remarks>
public class SnapLayerObstacleTests
{
    /// <summary>参照模块（被贴的那个）的矩形。</summary>
    private static readonly CanvasRect Reference = new(0, 0, 200, 200);

    /// <summary>被拖模块：离参照右边缘 6px（贴合容差 8 内）。</summary>
    private static readonly CanvasRect Moving = new(206, 0, 200, 200);

    /// <summary>吸附后的位置：正好把目标位置完全盖住。</summary>
    private static readonly CanvasRect Target = new(200, 0, 200, 200);

    [Fact]
    public void 目标层被占时该候选应作废()
    {
        var result = SnapEngine.SnapGroup(
            [Moving],
            0,
            [.. SnapEngine.LinesOf(Reference, ownerLayer: 0)],
            tolerance: 4,
            flushTolerance: 8,
            obstacles: [new SnapObstacle(Target, Layer: 0)]);

        result.SnappedX.ShouldBeFalse("目标层已被占：拼上去会把整件顶到更高层");
        result.X.ShouldBe(Moving.X, "作废后停在指针原始位置，而不是整帧回退成别的坐标");
    }

    [Fact]
    public void 压在更低层的模块上应允许拼上()
    {
        // 参照件在层 1，挡路的那块在层 0：压住它完全合法（模块落在更高层，层就是为叠放引入的）。
        var result = SnapEngine.SnapGroup(
            [Moving],
            0,
            [.. SnapEngine.LinesOf(Reference, ownerLayer: 1)],
            tolerance: 4,
            flushTolerance: 8,
            obstacles: [new SnapObstacle(Target, Layer: 0)]);

        result.SnappedX.ShouldBeTrue();
        result.X.ShouldBe(200);
    }

    [Fact]
    public void 压在更高层的模块上仍应作废()
    {
        // 目标层 1、挡路者在层 2：拼上去会被它盖住（观感是"拖过去就消失"）。
        var result = SnapEngine.SnapGroup(
            [Moving],
            0,
            [.. SnapEngine.LinesOf(Reference, ownerLayer: 1)],
            tolerance: 4,
            flushTolerance: 8,
            obstacles: [new SnapObstacle(Target, Layer: 2)]);

        result.SnappedX.ShouldBeFalse();
    }

    [Fact]
    public void 几何等价时应优先取上层()
    {
        // 上下两个完全重合的组：同一坐标同时登记层 0 与层 1 两条贴合线。
        // 挡路块在层 0 ⇒ 只有"归层 1"的那条合法；同坐标去重若留了层 0，这条就整帧吸不上。
        var lines = SnapEngine
            .LinesOf(Reference, ownerLayer: 0)
            .Concat(SnapEngine.LinesOf(Reference, ownerLayer: 1))
            .ToList();

        var result = SnapEngine.SnapGroup(
            [Moving],
            0,
            lines,
            tolerance: 4,
            flushTolerance: 8,
            obstacles: [new SnapObstacle(Target, Layer: 0)]);

        result.SnappedX.ShouldBeTrue("同时满足两层时应归上层");
        result.X.ShouldBe(200);
    }

    [Fact]
    public void 外壳任何目标层都不许被压()
    {
        // 外壳不参与分层（它是底板），所以它的障碍是"永远挡"，与目标层号无关。
        var result = SnapEngine.SnapGroup(
            [Moving],
            0,
            [.. SnapEngine.LinesOf(Reference, ownerLayer: 5)],
            tolerance: 4,
            flushTolerance: 8,
            obstacles: [new SnapObstacle(Target, Layer: 0, AlwaysBlocks: true)]);

        result.SnappedX.ShouldBeFalse();
    }
}
