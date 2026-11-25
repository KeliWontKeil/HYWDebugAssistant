using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **带隔离的残余职责**：层不再决定"能不能拼"，但仍决定"跟谁对齐/参照尺寸"。
/// </summary>
/// <remarks>
/// 拼接 = 贴合 + 互不遮挡（跨带）；对齐/端口/尺寸参照 = 同带。
/// 把"同层"也当拼接门禁，跨带贴合就会被一律拒掉（组被抬层后另一端再也拼不上）。
/// </remarks>
public class LayerIsolationTests
{
    [Fact]
    public void 跨带贴合仍应成组()
    {
        // u 垫在 a 左半下面 ⇒ a 在 1 层；b 与 a 相距 4px（在贴合容差内）且不重合。
        using var harness = new TilingHarness(
            [
                ("u", 0, 0, 150, 150),
                ("a", 0, 0, 200, 200),
                ("b", 204, 0, 200, 200),
            ],
            zOrder: id => id switch
            {
                "u" => 0,
                "a" => 1,
                "b" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;

        harness.Module("a").Layer.ShouldBe(1, "a 压在 u 上 ⇒ 1 层");
        harness.Module("b").Layer.ShouldBe(1, "b 与 a 是同一件 ⇒ 必须同层");

        windows.GroupMembersOf("a").ShouldContain("b", "贴合且互不遮挡 ⇒ 成组，层号差多少都一样");
        windows.GroupMembersOf("b").ShouldContain("a");
    }

    [Fact]
    public void 尺寸参照仍只认同带()
    {
        // u（宽 180）在底部；g（宽 200）压住 u ⇒ 1 层；h（宽 220）单独在 0 层。
        using var harness = new TilingHarness(
            [
                ("u", 0, 0, 180, 200),
                ("g", 0, 0, 200, 200),
                ("h", 400, 0, 220, 200),
            ],
            zOrder: id => id switch
            {
                "u" => 0,
                "g" => 1,
                "h" => 2,
                _ => int.MinValue,
            });

        var windows = harness.Windows;
        var h = harness.Module("h");

        windows.BeginResize("h", ResizeHandle.Right, pointerX: 620, pointerY: 50).ShouldBeTrue();

        // 同带的 u（宽 180）是参照：拖到 181 会被吸到 180。
        windows.ResizeTo(pointerX: 581, pointerY: 50);
        h.Width.ShouldBe(180, "同带模块的宽度是尺寸参照");

        // 另一带的 g（宽 200）不是参照：拖到 201 不会被吸走。
        windows.ResizeTo(pointerX: 601, pointerY: 50);
        h.Width.ShouldBe(201, "跨带模块不提供尺寸参照（带隔离仍在）");

        windows.EndResize();
    }
}
