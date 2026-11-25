using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **外壳是跨层锚点，而"贴壳"把跨带的件连成一个接地整体**：层各自派生，拖动范围共用一份。
/// </summary>
/// <remarks>
/// 贴壳件分处不同带时，拖任一贴壳件都必须带走整个贴壳整体（含外壳）——
/// 外壳一走，全部贴壳件都得跟，否则贴壳边被撕开（表现为"模块之间像弹簧一样互相追赶"）。
/// </remarks>
public class ModuleLayerIntegrationTests
{
    [Fact]
    public void 贴壳件属于同一接地整体且外壳移动跨层()
    {
        using var harness = Build();
        var windows = harness.Windows;

        var a = harness.Module("a");
        var b = harness.Module("b");
        var c = harness.Module("c");

        // c 在 b 下方，b 压在 c 上 ⇒ b 升到 1 层；a/c 并排不重合 ⇒ 0 层。
        a.Layer.ShouldBe(0);
        c.Layer.ShouldBe(0);
        b.Layer.ShouldBe(1);

        // 三块都贴壳 ⇒ 同一个接地整体：从任一块出发都拿到全部（跨带）。
        var fromA = windows.GroupMembersOf("a");
        fromA.ShouldContain("a");
        fromA.ShouldContain("c");
        fromA.ShouldContain("b", "贴壳件彼此连通，层号不同不影响它属于同一个接地整体");

        var fromB = windows.GroupMembersOf("b");
        fromB.ShouldContain("a");
        fromB.ShouldContain("c");

        // 改变叠放顺序：b 移到 c 下方后，重合关系的上下层互换（层仍按件派生）。
        windows.ZOrderProvider = id => id switch
        {
            "a" => 0,
            "b" => 1,
            "c" => 2,
            _ => int.MinValue,
        };
        windows.RefreshLayers();

        b.Layer.ShouldBe(0, "b 现在在 c 下方");
        c.Layer.ShouldBe(1, "c 现在压在 b 上");

        // 外壳移动是跨层的：所有贴壳模块（a/b/c）都跟着走。
        var beforeA = a.PixelRect.Left;
        var beforeB = b.PixelRect.Left;
        var beforeC = c.PixelRect.Left;
        var shell = windows.ShellPhysicalRect.ShouldNotBeNull();

        windows.TranslateShellGroup(10, 0, shell);

        a.PixelRect.Left.ShouldBe(beforeA + 10);
        b.PixelRect.Left.ShouldBe(beforeB + 10);
        c.PixelRect.Left.ShouldBe(beforeC + 10);
    }

    [Fact]
    public void 拖任一贴壳件应带走整个贴壳整体()
    {
        using var harness = Build();
        var windows = harness.Windows;

        var a = harness.Module("a");
        var b = harness.Module("b");
        var c = harness.Module("c");

        var startA = a.PixelRect.Left;
        var startB = b.PixelRect.Left;
        var startC = c.PixelRect.Left;

        windows.BeginDrag("a", pointerX: 50, pointerY: 50, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 60, pointerY: 50);

        a.PixelRect.Left.ShouldBe(startA + 10);
        c.PixelRect.Left.ShouldBe(startC + 10, "贴壳件与外壳是一个视觉整体");
        b.PixelRect.Left.ShouldBe(
            startB + 10,
            "b 在 1 层也要跟：外壳一走它就必须走，否则贴壳边被撕开（表现为弹簧）");

        // 拖完之后贴壳关系没有被破坏：外壳右边缘仍与三块左边缘重合。
        var shell = windows.ShellPhysicalRect.ShouldNotBeNull();
        TilingGraph.IsShellGlued(a.PhysicalRect, shell).ShouldBeTrue();
        TilingGraph.IsShellGlued(b.PhysicalRect, shell).ShouldBeTrue();
        TilingGraph.IsShellGlued(c.PhysicalRect, shell).ShouldBeTrue();

        windows.EndDrag();
    }

    private static TilingHarness Build() => new(
        [
            ("a", 0, 0, 100, 100),
            ("b", 0, 200, 100, 100),
            ("c", 0, 250, 100, 100),
        ],
        // 外壳右边缘 = 0；三个模块都贴壳；a 与 c 不重合，b 压住 c。
        shell: new CanvasRect(-100, 0, 100, 400),
        zOrder: id => id switch
        {
            "a" => 0,
            "c" => 1,
            "b" => 2,
            _ => int.MinValue,
        });
}
