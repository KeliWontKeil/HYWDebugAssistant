using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **缩放吸附**：移动边只认同层参照；贴合 &gt; 边对齐 &gt; 尺寸相等；
/// 约束先于吸附，不合法候选直接丢弃。
/// </summary>
public class ResizeSnapTests
{
    [Fact]
    public void 右边应贴合相邻模块左边()
    {
        var other = new CanvasRect(200, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 196, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Right.ShouldBe(200);
        result.Rect.Width.ShouldBe(200);
        result.XKind.ShouldBe(SnapKind.Flush);
        result.Guides.ShouldNotBeEmpty();
        SnapEngine.RectsOverlap(result.Rect, other).ShouldBeFalse();
    }

    [Fact]
    public void 左边应贴合相邻模块右边()
    {
        var other = new CanvasRect(0, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(104, 0, 100, 100),
            ResizeEdge.Left,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Left.ShouldBe(100);
        result.XKind.ShouldBe(SnapKind.Flush);
    }

    [Fact]
    public void 上下边应各自吸附()
    {
        var below = new CanvasRect(0, 200, 100, 100);
        var bottom = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 100, 196),
            ResizeEdge.Bottom,
            Lines(below),
            [],
            minWidth: 50,
            minHeight: 50);
        bottom.Rect.Bottom.ShouldBe(200);
        bottom.YKind.ShouldBe(SnapKind.Flush);

        var above = new CanvasRect(0, 0, 100, 100);
        var top = SnapEngine.SnapResize(
            new CanvasRect(0, 104, 100, 100),
            ResizeEdge.Top,
            Lines(above),
            [],
            minWidth: 50,
            minHeight: 50);
        top.Rect.Top.ShouldBe(100);
        top.YKind.ShouldBe(SnapKind.Flush);
    }

    [Fact]
    public void 同侧边只做边对齐而不是贴合()
    {
        // 对方在交叉轴上不重叠：即使吸到对方右边缘，也不能算“接触”。
        var other = new CanvasRect(100, 200, 100, 50);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 196, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Right.ShouldBe(200);
        result.XKind.ShouldBe(SnapKind.Edge);
        result.Guides.ShouldNotBeEmpty();
    }

    [Fact]
    public void 没有边界候选时应吸附到尺寸相等()
    {
        var reference = new CanvasRect(300, 0, 150, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 146, 100),
            ResizeEdge.Right,
            Lines(reference),
            [new ResizeSizeReference("ref", reference)],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Width.ShouldBe(150);
        result.XKind.ShouldBe(SnapKind.Size);
        result.XSizeReferenceId.ShouldBe("ref");
        result.Guides.ShouldBeEmpty("尺寸相等没有共享边，不画虚线");
    }

    [Fact]
    public void 边对齐应优先于更近的尺寸相等()
    {
        var edgeModule = new CanvasRect(148, 200, 50, 50);
        var sizeModule = new CanvasRect(300, 0, 150, 100);

        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 146, 100),
            ResizeEdge.Right,
            Lines(edgeModule, sizeModule),
            [new ResizeSizeReference("size", sizeModule)],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Right.ShouldBe(148);
        result.XKind.ShouldBe(SnapKind.Edge);
        result.XSizeReferenceId.ShouldBeNull();
    }

    [Fact]
    public void 贴合应优先于更近的边对齐()
    {
        var flushModule = new CanvasRect(200, 0, 100, 50);
        var edgeModule = new CanvasRect(197, 200, 50, 50);

        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 198, 100),
            ResizeEdge.Right,
            Lines(flushModule, edgeModule),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Right.ShouldBe(200);
        result.XKind.ShouldBe(SnapKind.Flush);
    }

    [Fact]
    public void 多个尺寸候选取最近且返回稳定参照()
    {
        var far = new CanvasRect(300, 0, 150, 100);
        var near = new CanvasRect(400, 0, 149, 100);

        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 146, 100),
            ResizeEdge.Right,
            Lines(far, near),
            [new ResizeSizeReference("far", far), new ResizeSizeReference("near", near)],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Width.ShouldBe(149);
        result.XKind.ShouldBe(SnapKind.Size);
        result.XSizeReferenceId.ShouldBe("near");
    }

    [Fact]
    public void 低于最小宽度的候选不应吸附()
    {
        var other = new CanvasRect(99, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 90, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 100,
            minHeight: 50);

        result.Rect.Width.ShouldBe(100);
        result.SnappedX.ShouldBeFalse();
        result.XKind.ShouldBeNull();
    }

    [Fact]
    public void 高于最大宽度的候选不应吸附()
    {
        var other = new CanvasRect(125, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 122, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50,
            maxWidth: 120);

        result.Rect.Width.ShouldBe(120);
        result.SnappedX.ShouldBeFalse();
    }

    [Fact]
    public void 候选造成正面积重合时应回落原始矩形()
    {
        var other = new CanvasRect(200, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 296, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50,
            obstacles: [new SnapObstacle(other, Layer: 0, AlwaysBlocks: true)]);

        result.Rect.Width.ShouldBe(296);
        result.SnappedX.ShouldBeFalse();
        result.Guides.ShouldBeEmpty();
    }

    [Fact]
    public void 中心线不参与缩放吸附()
    {
        var other = new CanvasRect(200, 0, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 246, 100),
            ResizeEdge.Right,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Width.ShouldBe(246, "右边缘靠近中心线，但缩放只认外框边");
        result.SnappedX.ShouldBeFalse();
    }

    [Fact]
    public void 角手柄两轴应独立吸附()
    {
        var other = new CanvasRect(200, 200, 100, 100);
        var result = SnapEngine.SnapResize(
            new CanvasRect(0, 0, 196, 196),
            ResizeEdge.Right | ResizeEdge.Bottom,
            Lines(other),
            [],
            minWidth: 50,
            minHeight: 50);

        result.Rect.Right.ShouldBe(200);
        result.Rect.Bottom.ShouldBe(200);
        result.SnappedX.ShouldBeTrue();
        result.SnappedY.ShouldBeTrue();
        SnapEngine.RectsOverlap(result.Rect, other).ShouldBeFalse();
    }

    private static IReadOnlyList<SnapLine> Lines(params CanvasRect[] rects) =>
        [.. rects.SelectMany(rect => SnapEngine.LinesOf(rect))];
}
