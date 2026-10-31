using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **吸附引擎（参照线模型 · 纯函数）的回归网**。
/// </summary>
/// <remarks>
/// <para>
/// 判据：一切吸附 = 把**模块外框的锚点**吸到**参照线**上（外框 4 边 + 中心线、端口锚点、外壳边）；
/// 级别由「线来源 + 交叉轴条件」派生（贴合 &gt; 端口锚点 &gt; 中心线 &gt; 边缘）；
/// **同坐标候选去重**；引擎不含跨帧状态（策略由调用方持有）。
/// </para>
/// </remarks>
public class SnapEngineTests
{
    /// <summary>把若干其他模块矩形展开成参照线表（模拟调用方：<c>LinesOf</c>）。</summary>
    private static List<SnapLine> Lines(params CanvasRect[] others) =>
        [.. others.SelectMany(SnapEngine.LinesOf)];

    // ---------------------------------------------------------------- 类别与优先级

    [Fact]
    public void 吸附_真接触时才算贴合()
    {
        var moving = new CanvasRect(673, 450, 330, 220);   // 竖直方向与邻居重叠

        var result = SnapEngine.Snap(moving, Lines(new CanvasRect(1000, 400, 320, 200)));

        result.X.ShouldBe(670);
        result.XKind.ShouldBe(SnapKind.Flush, "朝外那条边落在对方外框边上 且 交叉轴有重叠 → 贴合");
    }

    [Fact]
    public void 吸附迟滞_上一帧的线在释放距离内优先保持()
    {
        var lines = Lines(new CanvasRect(1000, 400, 320, 200));
        var first = SnapEngine.Snap(new CanvasRect(673, 450, 330, 220), lines);

        first.X.ShouldBe(670);
        first.XLine.ShouldBe(1000);

        // 右缘离目标 9px：超过常规贴合容差 8，但仍在释放距离内 ⇒ 保持上一帧的线。
        var held = SnapEngine.Snap(
            new CanvasRect(679, 450, 330, 220),
            lines,
            memory: new SnapMemory(first.XLine, first.YLine));

        held.X.ShouldBe(670, "迟滞期内不能因为越过普通容差就松手");
        held.SnappedX.ShouldBeTrue();

        // 再远到释放距离之外 ⇒ 允许松开。
        var released = SnapEngine.Snap(
            new CanvasRect(682, 450, 330, 220),
            lines,
            memory: new SnapMemory(first.XLine, first.YLine));

        released.SnappedX.ShouldBeFalse("超过释放距离必须松手");
        released.X.ShouldBe(682);
    }

    [Fact]
    public void 吸附_交叉轴无重叠时只能是边缘对齐()
    {
        var moving = new CanvasRect(673, 2000, 330, 220);   // 竖直方向相距 1400px 以上

        var result = SnapEngine.Snap(moving, Lines(new CanvasRect(1000, 400, 320, 200)));

        result.X.ShouldBe(670);
        result.XKind.ShouldBe(SnapKind.Edge, "无交叉轴重叠 → 只算边缘对齐，不得当成贴合");
    }

    [Fact]
    public void 吸附_模块中心线优先于边缘对齐()
    {
        var moving = new CanvasRect(998, 300, 330, 220);    // 中心线目标 1160−165 = 995（偏移 −3）

        var result = SnapEngine.Snap(moving, Lines(new CanvasRect(1000, 400, 320, 200)));

        result.X.ShouldBe(995, "中心线对齐的级别高于边缘对齐（左边缘对齐目标 1000 偏移更小）");
        result.XKind.ShouldBe(SnapKind.ModuleCenter);
    }

    [Fact]
    public void 吸附_端口锚点对齐优先于模块中心线()
    {
        CanvasRect other = new(1000, 400, 320, 200);          // 中心线 Y = 500
        var moving = new CanvasRect(1320, 389, 330, 220);      // 中心线 Y = 499（偏移 +1，在容差内）

        // 端口锚点：被拖模块口在竖直中心（偏移 0.5，即 Y=499）；对方口在 495（偏移 −4，也在容差内）
        var anchors = new List<PortAnchorPair> { new(AlignsX: false, OtherCoordinate: 495, other, MovingOffset: 0.5) };

        var result = SnapEngine.Snap(moving, Lines(other), SnapEngine.DefaultTolerance, anchors);

        result.Y.ShouldBe(385, "两个候选都在容差内时，端口锚点（级别更高）胜出");
        result.YKind.ShouldBe(SnapKind.PortCenter);
    }

    [Fact]
    public void 吸附_端口锚点在超出接近范围时不参与()
    {
        CanvasRect other = new(320, 2000, 320, 200);   // 横向离得很远
        var moving = new CanvasRect(1320, 395, 330, 220);

        var anchors = new List<PortAnchorPair> { new(AlignsX: false, OtherCoordinate: 508, other, MovingOffset: 0.5) };

        var result = SnapEngine.Snap(moving, Lines(other), SnapEngine.DefaultTolerance, anchors);

        result.SnappedY.ShouldBeFalse("端口锚点只在两模块接近时才有意义");
        result.Y.ShouldBe(395);
    }

    [Fact]
    public void 吸附_同坐标的重复候选只保留级别最高者()
    {
        // 单口模块的端口在正中（0.5）时，端口锚点与模块中心线**同坐标** —— 这是被用户点名的"重复登记"。
        CanvasRect other = new(1000, 400, 320, 200);           // 中心线 Y = 500
        var moving = new CanvasRect(1320, 392, 330, 220);       // 模块中心 Y = 502（偏移 −2，在容差内）

        // 对方口也在正中 → 端口锚点坐标 500，与中心线同坐标、同偏移
        var anchors = new List<PortAnchorPair> { new(AlignsX: false, OtherCoordinate: 500, other, MovingOffset: 0.5) };

        var result = SnapEngine.Snap(moving, Lines(other), SnapEngine.DefaultTolerance, anchors);

        result.Y.ShouldBe(390);
        result.YKind.ShouldBe(
            SnapKind.PortCenter,
            "同坐标只保留级别最高的一条（端口锚点 2 > 模块中心线 1）；"
            + "去重本身不改变位置（同坐标 ⇒ 同偏移），它消掉的是**重复登记**这类结构性隐患");
    }

    // ---------------------------------------------------------------- 容差分级

    [Fact]
    public void 吸附_贴合容差宽于对齐容差()
    {
        var moving = new CanvasRect(676, 450, 330, 220);   // 接触位置 670 → 偏移 −6
        var lines = Lines(new CanvasRect(1000, 400, 320, 200));

        var snapped = SnapEngine.Snap(moving, lines);
        snapped.X.ShouldBe(670, "贴合容差 8px：偏移 6px 仍然吸上");
        snapped.XKind.ShouldBe(SnapKind.Flush);

        var strict = SnapEngine.Snap(moving, lines, SnapEngine.DefaultTolerance, null, flushTolerance: 4);
        strict.SnappedX.ShouldBeFalse("把贴合容差也收成 4px 时，6px 的偏移不吸 —— 两档容差确实分开");
        strict.X.ShouldBe(676);
    }

    // ---------------------------------------------------------------- 外壳 = 只暴露一条边的对象

    [Fact]
    public void 吸附_外壳边沿线有重叠时贴到线上()
    {
        var lines = new List<SnapLine> { SnapEngine.LineOf(new SnapReferenceLine(true, 1320, 400, 600)) };
        var moving = new CanvasRect(1317, 450, 330, 220);

        var result = SnapEngine.Snap(moving, lines);

        result.X.ShouldBe(1320, "外壳只提供右边缘这一条参照线");
        result.XKind.ShouldBe(SnapKind.Flush);
    }

    [Fact]
    public void 吸附_外壳边沿线毫无重叠时只能算边缘对齐()
    {
        var lines = new List<SnapLine> { SnapEngine.LineOf(new SnapReferenceLine(true, 1320, 400, 600)) };
        var moving = new CanvasRect(1317, 1000, 330, 220);   // 落在外壳下方

        var result = SnapEngine.Snap(moving, lines);

        result.X.ShouldBe(1320);
        result.XKind.ShouldBe(SnapKind.Edge, "沿线的方向没有重叠 → 只能算边缘对齐");
    }

    [Fact]
    public void 吸附_外壳与模块外框进同一张线表()
    {
        // 模块外框（左 1000）与外壳边（1320）同时参与：谁在容差内就吸谁，没有"特殊通道"
        var lines = new List<SnapLine> { SnapEngine.LineOf(new SnapReferenceLine(true, 1320, 400, 600)) };
        lines.AddRange(SnapEngine.LinesOf(new CanvasRect(1000, 400, 320, 200)));

        var result = SnapEngine.Snap(new CanvasRect(1317, 450, 330, 220), lines);

        result.X.ShouldBe(1320, "外壳线（偏移 3px）比模块边（偏移 −317px）更近");
        result.XKind.ShouldBe(SnapKind.Flush);
    }

    // ---------------------------------------------------------------- 纯函数性 / 未命中

    [Fact]
    public void 吸附_相同输入必须得到相同结果()
    {
        var lines = Lines(new CanvasRect(1000, 400, 320, 200), new CanvasRect(1000, 402, 320, 200));
        var moving = new CanvasRect(995, 395, 330, 220);

        var first = SnapEngine.Snap(moving, lines);
        var second = SnapEngine.Snap(moving, lines);

        second.X.ShouldBe(first.X);
        second.Y.ShouldBe(first.Y);
        second.XKind.ShouldBe(first.XKind);
        second.YKind.ShouldBe(first.YKind, "引擎是纯函数 —— 策略与状态由调用方持有");
    }

    [Fact]
    public void 吸附_未命中时位置不变且无辅助线()
    {
        var moving = new CanvasRect(0, 0, 330, 220);

        var result = SnapEngine.Snap(moving, Lines(new CanvasRect(1000, 400, 320, 200)));

        result.SnappedX.ShouldBeFalse();
        result.SnappedY.ShouldBeFalse();
        result.X.ShouldBe(0);
        result.Y.ShouldBe(0);
        result.Guides.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- 贴合谓词（分组口径）

    [Fact]
    public void 拼接谓词_相邻且交叉轴有重叠才算拼上()
    {
        var a = new CanvasRect(0, 0, 240, 120);
        SnapEngine.AreJoined(a, new CanvasRect(240, 0, 240, 120)).ShouldBeTrue("左右相邻");
        SnapEngine.AreJoined(a, new CanvasRect(0, 120, 240, 120)).ShouldBeTrue("上下相邻");
        SnapEngine.AreJoined(a, new CanvasRect(244, 0, 240, 120)).ShouldBeTrue("4px 在容差内");
        SnapEngine.AreJoined(a, new CanvasRect(260, 0, 240, 120)).ShouldBeFalse("20px 超出容差");
        SnapEngine.AreJoined(a, new CanvasRect(240, 400, 240, 120)).ShouldBeFalse("边缘对齐但没有重叠 → 不算拼接");
    }
}
