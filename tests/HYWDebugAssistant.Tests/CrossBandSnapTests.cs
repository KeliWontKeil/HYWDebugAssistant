using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **吸附只看几何，不看层**：跨层的模块一样能对齐，压在主窗口上的模块一样能吸上邻居。
/// </summary>
/// <remarks>
/// <para>
/// 层只回答一件事："落位**新增**的遮挡看不看得见"。它既不是"能不能拼"的门禁，也不是"能不能对齐"的门禁——
/// 一旦让层去剪裁候选，同一件事就有了两套判据，贴合与对齐迟早给出不同答案（表现为"能吸上却对不齐"）。
/// </para>
/// <para>
/// 判据问的是**增量**：手势开始时就已经重合的实体（压在主窗口上、或两块叠在一起）不参与遮挡判定。
/// 否则它会让每一条候选都被判掉，用户看到的是"完全吸不上、连辅助线都没有"。
/// </para>
/// </remarks>
public class CrossBandSnapTests
{
    [Fact]
    public void 压在主窗口上的模块仍应能吸上邻居()
    {
        // 外壳右边缘在 x = 0；on 与 peer 都跨在壳上（横向压住）⇒ 都在 1 层，彼此相差 100px。
        using var harness = new TilingHarness(
            [
                ("on", -150, 100, 200, 200),
                ("peer", -150, 400, 200, 100),
            ],
            shell: new CanvasRect(-300, 0, 300, 600),
            zOrder: id => id == "on" ? 0 : 1);

        var windows = harness.Windows;
        var on = harness.Module("on");
        var peer = harness.Module("peer");

        on.Layer.ShouldBe(1, "压在外壳上 ⇒ 与外壳分层");
        peer.Layer.ShouldBe(1);

        // 下拖到离 peer 上边缘 5px（贴合容差内）。移动过程中 on 始终压着外壳。
        windows.BeginDrag("on", pointerX: 50, pointerY: 200, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 50, pointerY: 295);

        on.PixelRect.Bottom.ShouldBe(
            peer.PixelRect.Top,
            "起点就压着外壳不该让候选全部作废：遮挡没变多，就该照常吸上");

        windows.EndDrag();
    }

    [Fact]
    public void 跨层模块之间应能对齐边缘()
    {
        // u 在底部；g 压住 u ⇒ 1 层；x 单独在 0 层。g 与 x 差一层。
        using var harness = new TilingHarness(
            [
                ("u", 0, 0, 200, 200),
                ("g", 0, 50, 200, 200),
                ("x", 400, 0, 200, 200),
            ],
            zOrder: id => id switch
            {
                "u" => 0,
                "g" => 1,
                "x" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;
        var g = harness.Module("g");
        var x = harness.Module("x");

        g.Layer.ShouldBe(1, "g 压住 u ⇒ 升层");
        x.Layer.ShouldBe(0, "x 与谁都无关 ⇒ 留在底板层");

        // 下拖到离 g 上边缘 3px（对齐容差内）：跨层也必须能吸齐。
        windows.BeginDrag("x", pointerX: 500, pointerY: 100, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 500, pointerY: 147);

        x.PixelRect.Top.ShouldBe(g.PixelRect.Top, "层号不同的两块也必须能边对齐");
        x.Guides.ShouldNotBeEmpty("吸上了就该有对齐标线");

        windows.EndDrag();
    }
}
