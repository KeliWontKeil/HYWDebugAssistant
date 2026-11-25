using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **抬窗范围 = 拼接件**（不是接地整体）：点一个贴壳件只抬它那一件，外壳与另外的件留在原位。
/// </summary>
/// <remarks>
/// 位移要整块接地整体一起走（否则贴壳边被撕开），但叠放顺序没有这个约束 ——
/// 把接地整体搬到浮动带末尾会把外壳一起越过，表现为"点 B 却把另一块顶上来、主窗口被压下去"。
/// </remarks>
public class RaiseScopeTests
{
    /// <summary>外壳：右边缘在 x = 0，竖直覆盖 0–600。</summary>
    private static readonly HYWDebugAssistant.Ui.Geometry.CanvasRect Shell = new(-300, 0, 300, 600);

    [Fact]
    public void 抬窗范围应只有拼接件()
    {
        // a、b 都贴在外壳右边缘，但彼此相隔 100px（相距 > 贴合容差）⇒ 两件。
        // c 与 b 贴合 ⇒ 与 b 同一件。
        using var harness = new TilingHarness(
            [
                ("a", 0, 0, 200, 200),
                ("b", 0, 300, 200, 200),
                ("c", 200, 300, 200, 200),
            ],
            shell: Shell,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;

        var piece = windows.AssemblyMembersOf("b");

        piece.ShouldContain("b");
        piece.ShouldContain("c", "贴合件要一起抬，件内成员在同一个平面上");
        piece.ShouldNotContain("a", "a 是另一件：抬 b 不该把 a 也拉到上面");
        piece.ShouldNotContain(HYWDebugAssistant.Ui.ViewModels.ModuleWindowsViewModel.ShellNodeId);
        piece.Count.ShouldBe(2);

        windows.AssemblyMembersOf("a").ShouldBe(["a"], "单模块件等价于只抬自己");
    }

    [Fact]
    public void 选中范围仍是接地整体()
    {
        // 选中口径这次不动：点任一贴壳件 ⇒ 整个接地整体（全部贴壳件）都选中。
        using var harness = new TilingHarness(
            [
                ("a", 0, 0, 200, 200),
                ("b", 0, 300, 200, 200),
                ("c", 200, 300, 200, 200),
            ],
            shell: Shell,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        var selected = harness.Windows.GroupMembersOf("b");

        selected.ShouldContain("a");
        selected.ShouldContain("b");
        selected.ShouldContain("c");
    }
}
