using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>新模块落位：中央放置区内逐层按列铺开、同层避让、层间坐标偏移、工作区按外壳所在屏选择。</summary>
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
    public void 第二层网格相对第一层应有坐标偏移()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1200, 900);
        var region = ModulePlacement.PlacementRegion(shell, work, 100, 100).ShouldNotBeNull();

        var first = ModulePlacement.NextLayered(shell, work, [], 100, 100);
        first.Sheet.ShouldBe(0);
        var obstacles = new List<PlacementObstacle>
        {
            new(new CanvasRect(first.X, first.Y, 100, 100), first.Sheet),
        };

        ModulePlacementResult? firstOfSecond = null;

        for (var index = 0; index < 80; index++)
        {
            var result = ModulePlacement.NextLayered(shell, work, obstacles, 100, 100);
            result.Stacked.ShouldBeFalse();

            if (result.Sheet == 1)
            {
                firstOfSecond = result;
                break;
            }

            obstacles.Add(new PlacementObstacle(new CanvasRect(result.X, result.Y, 100, 100), result.Sheet));
        }

        firstOfSecond.ShouldNotBeNull("第一层排满后应进入第二层");
        (Math.Abs(firstOfSecond.Value.X - first.X) > 0.5
            || Math.Abs(firstOfSecond.Value.Y - first.Y) > 0.5)
            .ShouldBeTrue("第二层网格相对第一层必须有坐标偏移");
        Inside(region, firstOfSecond.Value.X, firstOfSecond.Value.Y, 100, 100).ShouldBeTrue();
    }

    [Fact]
    public void 第一层排满后应进入第二层而不是单点堆叠()
    {
        var shell = new CanvasRect(0, 0, 100, 300);
        var work = new CanvasRect(0, 0, 500, 300);
        var occupied = new[] { new PlacementObstacle(new CanvasRect(shell.Right + ModulePlacement.Gap, 0, 200, 200), Sheet: 0) };

        var result = ModulePlacement.NextLayered(shell, work, occupied, 200, 200);

        result.Stacked.ShouldBeFalse();
        result.Sheet.ShouldBe(1);
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
    public void 同尺寸模块连续进入新层时不应完全重合()
    {
        var shell = new CanvasRect(0, 0, 100, 300);
        var work = new CanvasRect(0, 0, 320, 300);
        var occupied = new[] { new PlacementObstacle(new CanvasRect(112, 0, 200, 200), Sheet: 0) };

        var first = ModulePlacement.NextLayered(shell, work, occupied, 200, 200);
        first.Stacked.ShouldBeFalse();
        first.Sheet.ShouldBe(1);

        var second = ModulePlacement.NextLayered(
            shell,
            work,
            [.. occupied, new PlacementObstacle(new CanvasRect(first.X, first.Y, 200, 200), first.Sheet)],
            200,
            200);

        second.Stacked.ShouldBeFalse();
        (Math.Abs(second.X - first.X) > 0.5 || Math.Abs(second.Y - first.Y) > 0.5)
            .ShouldBeTrue("第二层内部也必须继续排开，不能与第一个位置重合");
    }

    [Fact]
    public void 工作区选与外壳相交最多的一块()
    {
        var shell = new CanvasRect(600, 0, 200, 200);
        var first = new CanvasRect(0, 0, 500, 1000);
        var second = new CanvasRect(500, 0, 1000, 1000);

        ModulePlacement.ChooseWorkArea(shell, [first, second]).GetValueOrDefault().ShouldBe(second);
    }

    [Fact]
    public void 放置区以工作区中心为范围且贴外壳右侧()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 800);

        var region = ModulePlacement.PlacementRegion(shell, work, 300, 200).ShouldNotBeNull();

        region.Left.ShouldBe(shell.Right + ModulePlacement.Gap);
        region.CenterY.ShouldBe(work.CenterY);
        region.Right.ShouldBeLessThanOrEqualTo(
            work.Right - (Math.Min(work.Width, work.Height) * ModulePlacement.PlacementMarginRatio));
    }

    [Fact]
    public void 无外壳时放置区居中()
    {
        var work = new CanvasRect(0, 0, 1000, 800);

        var region = ModulePlacement.PlacementRegion(null, work, 300, 200).ShouldNotBeNull();

        region.CenterX.ShouldBe(work.CenterX);
        region.CenterY.ShouldBe(work.CenterY);
    }

    [Fact]
    public void 首次落位在放置区左上角()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 800);

        var region = ModulePlacement.PlacementRegion(shell, work, 300, 200).ShouldNotBeNull();
        var result = ModulePlacement.Next(shell, work, [], 300, 200);

        result.Stacked.ShouldBeFalse();
        result.X.ShouldBe(region.Left);
        result.Y.ShouldBe(region.Top);
    }

    [Fact]
    public void 同一列内应自上而下继续铺开()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 800);
        var first = ModulePlacement.Next(shell, work, [], 300, 200);
        var second = ModulePlacement.Next(
            shell,
            work,
            [new CanvasRect(first.X, first.Y, 300, 200)],
            300,
            200);

        second.Stacked.ShouldBeFalse();
        second.X.ShouldBe(first.X);
        second.Y.ShouldBeGreaterThan(first.Y);
    }

    [Fact]
    public void 放置区未排满时不应堆叠()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1200, 900);
        var region = ModulePlacement.PlacementRegion(shell, work, 100, 100).ShouldNotBeNull();
        var occupied = new List<CanvasRect>();

        for (var index = 0; index < 20; index++)
        {
            var result = ModulePlacement.Next(shell, work, occupied, 100, 100);
            result.Stacked.ShouldBeFalse($"安全区仍有空位时第 {index} 个模块不应堆叠");
            Inside(region, result.X, result.Y, 100, 100).ShouldBeTrue();
            occupied.Add(new CanvasRect(result.X, result.Y, 100, 100));
        }
    }

    [Fact]
    public void 每层排满后进入下一层且同层不重叠()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1200, 900);
        var region = ModulePlacement.PlacementRegion(shell, work, 100, 100).ShouldNotBeNull();
        var obstacles = new List<PlacementObstacle>();
        var bySheet = new Dictionary<int, List<CanvasRect>>();
        var maxSheet = 0;

        for (var index = 0; index < 120; index++)
        {
            var result = ModulePlacement.NextLayered(shell, work, obstacles, 100, 100);
            result.Stacked.ShouldBeFalse($"第 {index} 个模块仍应能在某一层排开");
            Inside(region, result.X, result.Y, 100, 100).ShouldBeTrue();

            var rect = new CanvasRect(result.X, result.Y, 100, 100);
            if (bySheet.TryGetValue(result.Sheet, out var sheetRects))
            {
                foreach (var other in sheetRects)
                {
                    ModulePlacement.Overlaps(rect, [other], gap: 0)
                        .ShouldBeFalse("同一放置层内不得重叠");
                }

                sheetRects.Add(rect);
            }
            else
            {
                bySheet[result.Sheet] = [rect];
            }

            maxSheet = Math.Max(maxSheet, result.Sheet);
            obstacles.Add(new PlacementObstacle(rect, result.Sheet));
        }

        maxSheet.ShouldBeGreaterThanOrEqualTo(2, "第一、二层排满后应继续进入第三层");
    }

    [Fact]
    public void 已有模块在角落不影响中心落位()
    {
        var shell = new CanvasRect(100, 100, 200, 200);
        var work = new CanvasRect(0, 0, 1000, 800);
        var region = ModulePlacement.PlacementRegion(shell, work, 100, 100).ShouldNotBeNull();
        var occupied = new[] { new CanvasRect(0, 0, 50, 50) };

        var result = ModulePlacement.Next(shell, work, occupied, 100, 100);

        result.Stacked.ShouldBeFalse();
        Inside(region, result.X, result.Y, 100, 100).ShouldBeTrue();
    }

    [Fact]
    public void 外壳靠右时放置区向左调整但仍留在屏内()
    {
        var shell = new CanvasRect(850, 0, 100, 200);
        var work = new CanvasRect(0, 0, 1000, 600);
        var region = ModulePlacement.PlacementRegion(shell, work, 200, 200).ShouldNotBeNull();

        region.Right.ShouldBeLessThanOrEqualTo(work.Right - (Math.Min(work.Width, work.Height) * ModulePlacement.PlacementMarginRatio));
        region.Left.ShouldBeGreaterThanOrEqualTo(work.Left);

        var result = ModulePlacement.Next(shell, work, [], 200, 200);
        result.Stacked.ShouldBeFalse();
        Inside(region, result.X, result.Y, 200, 200).ShouldBeTrue();
    }

    [Fact]
    public void 大模块应扩展到容纳它的放置区()
    {
        var shell = new CanvasRect(100, 100, 100, 100);
        var work = new CanvasRect(0, 0, 1100, 900);
        var region = ModulePlacement.PlacementRegion(shell, work, 850, 700).ShouldNotBeNull();

        region.Width.ShouldBeGreaterThanOrEqualTo(850);
        region.Height.ShouldBeGreaterThanOrEqualTo(700);
        region.Right.ShouldBeLessThanOrEqualTo(work.Right);
        region.Bottom.ShouldBeLessThanOrEqualTo(work.Bottom);

        var result = ModulePlacement.Next(shell, work, [], 850, 700);
        result.Stacked.ShouldBeFalse();
        Inside(region, result.X, result.Y, 850, 700).ShouldBeTrue();
    }

    [Fact]
    public void 无工作区时放置区为空()
    {
        ModulePlacement.PlacementRegion(null, null, 100, 100).ShouldBeNull();
    }

    private static bool Inside(CanvasRect region, double x, double y, double width, double height) =>
        x >= region.Left - 0.01
        && y >= region.Top - 0.01
        && x + width <= region.Right + 0.01
        && y + height <= region.Bottom + 0.01;
}
