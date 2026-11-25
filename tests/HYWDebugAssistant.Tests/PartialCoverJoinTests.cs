using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **部分叠放**：拼接件的一段压在别的模块上、另一段悬空时，浅层模块仍必须能拼到悬空段上。
/// </summary>
/// <remarks>
/// 拿"同层"当拼接门禁就会漏掉这条：整件因左半被抬层，右半虽然视觉上在表面却永远拼不上（层不匹配）。
/// 判据是"贴合 + 互不遮挡"，层号差多少都能拼——但**压住件内某块**的模块仍然拼不上（遮挡即障碍）。
/// </remarks>
public class PartialCoverJoinTests
{
    [Fact]
    public void 悬空段应能被浅层模块拼上()
    {
        // u 在底部（只垫住 g1 的左半）；g1 压住 u、g2 与 g1 贴合 ⇒ {g1,g2} 是一件、被抬到 1 层；
        // x 在 0 层、离 g2 右边缘 20px。
        using var harness = new TilingHarness(
            [
                ("u", 0, 0, 150, 150),
                ("g1", 0, 0, 200, 200),
                ("g2", 200, 0, 200, 200),
                ("x", 420, 0, 200, 200),
            ],
            zOrder: id => id switch
            {
                "u" => 0,
                "g1" => 1,
                "g2" => 2,
                "x" => 3,
                _ => int.MinValue,
            });

        var windows = harness.Windows;
        var g2 = harness.Module("g2");
        var x = harness.Module("x");

        g2.Layer.ShouldBe(1, "件的左半压住 u ⇒ 整件在 1 层");
        x.Layer.ShouldBe(0);

        var before = windows.GroupMembersOf("g2");
        before.ShouldContain("g1");
        before.ShouldNotContain("x", "还差 20px：此刻不该算拼上");

        // 拖 x 靠近 g2 的右边缘：跨带也必须能吸到 0 间隙（旧实现连参考线都没有）。
        windows.BeginDrag("x", pointerX: 600, pointerY: 50, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 582, pointerY: 50);

        x.PixelRect.Left.ShouldBe(400, "跨带贴合必须吸到 0 间隙（组的一段悬空在浅带上）");

        // 层是派生值：真实运行时由窗口管理器的几何变化事件刷新，测试里显式刷一次。
        windows.RefreshLayers();
        x.Layer.ShouldBe(1, "拼上之后 x 与整件同层（一件刚体只能在一个平面上）");

        var after = windows.GroupMembersOf("x");
        after.ShouldContain("g1");
        after.ShouldContain("g2");

        windows.EndDrag();
    }

    [Fact]
    public void 压住件内某块的模块不得被拼上()
    {
        // 遮挡型几何：c 压住 a 的右半、同时贴在 b 的左边缘；{a,b} 是一件。
        using var harness = new TilingHarness(
            [
                ("a", 0, 0, 200, 200),
                ("b", 200, 0, 200, 200),
                ("c", 40, 0, 160, 200),
            ],
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;

        harness.Module("a").Layer.ShouldBe(0);
        harness.Module("b").Layer.ShouldBe(0);
        harness.Module("c").Layer.ShouldBe(1, "压住 a 的模块必须升层");

        var fromC = windows.GroupMembersOf("c");
        fromC.Count.ShouldBe(1, "压住件内某块 ⇒ 贴得再近也不能拼（真刚体不能自压）");
        fromC.ShouldContain("c");

        var fromA = windows.GroupMembersOf("a");
        fromA.ShouldContain("b");
        fromA.ShouldNotContain("c");
    }

    [Fact]
    public void 压在更低层模块上应仍能拼上()
    {
        // u 垫在下面；g 压住 u ⇒ 层 1；w 在层 0、与 g 相隔 40px（不同件），
        // x 贴到 g 右边缘后的位置正好把 w 盖住 —— 压住更低层是合法的，必须吸上。
        using var harness = new TilingHarness(
            [
                ("u", 0, 0, 200, 200),
                ("g", 100, 0, 200, 200),
                ("w", 340, 0, 200, 200),
                ("x", 700, 0, 200, 200),
            ],
            zOrder: id => id switch
            {
                "u" => 0,
                "g" => 1,
                "w" => 2,
                "x" => 3,
                _ => int.MinValue,
            });

        var windows = harness.Windows;
        var x = harness.Module("x");

        harness.Module("g").Layer.ShouldBe(1, "g 压住 u ⇒ 升层");
        harness.Module("w").Layer.ShouldBe(0);
        x.Layer.ShouldBe(0);

        // 拖到离 g 右边缘 6px：贴合容差内。
        windows.BeginDrag("x", pointerX: 750, pointerY: 100, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 356, pointerY: 100);

        x.PixelRect.Left.ShouldBe(300, "压住层 0 的 w 不该阻止吸到层 1 的 g 上");

        windows.RefreshLayers();
        x.Layer.ShouldBe(1, "拼上之后与 g 同层");

        var group = windows.GroupMembersOf("x");
        group.ShouldContain("g");
        group.ShouldNotContain("w", "压住它就不算拼上");

        windows.EndDrag();
    }
}
