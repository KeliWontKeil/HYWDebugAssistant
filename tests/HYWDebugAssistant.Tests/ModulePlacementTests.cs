using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>新模块落位：空位优先、排满后在工作区内堆叠、工作区按外壳所在屏选择。</summary>
public class ModulePlacementTests
{
    [Fact]
    public void 空位沿外壳右侧向下排()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 800);

        var result = ModulePlacement.Next(shell, work, [], 300, 200);

        result.Stacked.ShouldBeFalse();
        result.X.ShouldBe(shell.Right + ModulePlacement.Gap);
        result.Y.ShouldBe(shell.Top);
    }

    [Fact]
    public void 排满时在工作区内堆叠()
    {
        var shell = new CanvasRect(0, 0, 100, 300);
        var work = new CanvasRect(0, 0, 500, 300);
        var occupied = new[] { new CanvasRect(shell.Right + ModulePlacement.Gap, 0, 200, 200) };

        var result = ModulePlacement.Next(shell, work, occupied, 200, 200);

        result.Stacked.ShouldBeTrue();
        result.X.ShouldBeGreaterThanOrEqualTo(work.Left);
        (result.X + 200).ShouldBeLessThanOrEqualTo(work.Right);
        (result.Y + 200).ShouldBeLessThanOrEqualTo(work.Bottom);
    }

    [Fact]
    public void 外壳右侧排满时仍应找到其它空位()
    {
        var shell = new CanvasRect(700, 0, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 600);
        var occupied = new[] { new CanvasRect(712, 0, 200, 200) };

        var result = ModulePlacement.Next(shell, work, occupied, 200, 200);

        result.Stacked.ShouldBeFalse("屏幕别处仍有完整空位，不应直接堆叠");
        result.Y.ShouldBeGreaterThan(0, "外壳右侧排满后应继续利用下方的空位");
    }

    [Fact]
    public void 同尺寸模块连续堆叠时不应完全重合()
    {
        var shell = new CanvasRect(0, 0, 100, 300);
        var work = new CanvasRect(0, 0, 320, 300);
        var occupied = new[] { new CanvasRect(112, 0, 200, 200) };

        var first = ModulePlacement.Next(shell, work, occupied, 200, 200);
        first.Stacked.ShouldBeTrue();

        var second = ModulePlacement.Next(
            shell,
            work,
            [.. occupied, new CanvasRect(first.X, first.Y, 200, 200)],
            200,
            200);

        second.Stacked.ShouldBeTrue();
        (Math.Abs(second.X - first.X) > 0.5 || Math.Abs(second.Y - first.Y) > 0.5)
            .ShouldBeTrue("第二个堆叠模块必须与第一个错开，否则用户点不到它");
    }

    [Fact]
    public void 工作区选与外壳相交最多的一块()
    {
        var shell = new CanvasRect(600, 0, 200, 200);
        var first = new CanvasRect(0, 0, 500, 1000);
        var second = new CanvasRect(500, 0, 1000, 1000);

        ModulePlacement.ChooseWorkArea(shell, [first, second]).GetValueOrDefault().ShouldBe(second);
    }
}
