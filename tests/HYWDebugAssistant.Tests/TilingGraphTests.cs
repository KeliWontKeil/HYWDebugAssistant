using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **拼图图的唯一真源**：贴合谓词、外壳单侧规则、拼接件的重合守卫与确定性。
/// </summary>
public class TilingGraphTests
{
    [Fact]
    public void 贴合应以容差为界且排除重合()
    {
        var a = new CanvasRect(0, 0, 100, 100);

        TilingGraph.AreFlush(a, new CanvasRect(100, 0, 100, 100)).ShouldBeTrue("0 间隙");
        TilingGraph.AreFlush(a, new CanvasRect(108, 0, 100, 100)).ShouldBeTrue("8px 仍在容差内");
        TilingGraph.AreFlush(a, new CanvasRect(108.5, 0, 100, 100)).ShouldBeFalse("超出贴合容差");
        TilingGraph.AreFlush(a, new CanvasRect(100, 95, 100, 100)).ShouldBeFalse("交叉轴重叠不足（<8px）不足以算拼上");
        TilingGraph.AreFlush(a, new CanvasRect(50, 0, 100, 100)).ShouldBeFalse("正面积重合不是贴合");
        TilingGraph.AreFlush(a, new CanvasRect(0, 100, 100, 100)).ShouldBeTrue("上下相接同样是贴合");
    }

    [Fact]
    public void 外壳只认右侧单侧且严格1px()
    {
        var shell = new CanvasRect(-100, 0, 100, 400);   // 右边缘 = 0

        TilingGraph.IsShellGlued(new CanvasRect(0, 0, 100, 100), shell).ShouldBeTrue("左边缘贴外壳右边缘");
        TilingGraph.IsShellGlued(new CanvasRect(1, 0, 100, 100), shell).ShouldBeTrue("1px 取整误差仍算贴上");
        TilingGraph.IsShellGlued(new CanvasRect(2, 0, 100, 100), shell).ShouldBeFalse("没真贴上就不算（否则拖壳会带走路过的模块）");

        TilingGraph.IsShellGlued(new CanvasRect(-100, 0, 100, 100), shell).ShouldBeFalse("压在壳上不算贴壳");
        TilingGraph.IsShellGlued(new CanvasRect(-200, 0, 100, 100), shell).ShouldBeFalse("贴外壳左边缘不算（外壳只暴露右边缘）");
        TilingGraph.IsShellGlued(new CanvasRect(0, 399, 100, 100), shell).ShouldBeFalse("竖直重叠不足 1px");
    }

    [Fact]
    public void 压住件内某块的模块必须单独成件()
    {
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);   // 与 a 贴合
        var c = new CanvasRect(20, 0, 80, 100);     // 压住 a 的右半，同时贴在 b 左边

        var assemblies = Build([("a", a), ("b", b), ("c", c)], id => id switch
        {
            "a" => 0,
            "b" => 1,
            "c" => 2,
            _ => int.MinValue,
        });

        assemblies.Count.ShouldBe(2, "真刚体不能自己压自己：c 压着 a ⇒ 不能与 {a,b} 同件");
        assemblies.Single(x => x.Contains("c")).Members.Count.ShouldBe(1);
        assemblies.Single(x => x.Contains("a")).Members.Count.ShouldBe(2);
    }

    [Fact]
    public void 链式传递重合应按z升序保留底板那边()
    {
        // a—b 贴合、b—c 贴合，但 a 与 c 重合：三块不可能同时是一件（那会自压）。
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 300);
        var c = new CanvasRect(0, 50, 100, 100);

        var assemblies = Build([("a", a), ("b", b), ("c", c)], id => id switch
        {
            "a" => 0,
            "b" => 1,
            "c" => 2,
            _ => int.MinValue,
        });

        assemblies.Count.ShouldBe(2, "z 升序 ⇒ 底板那边的 {a,b} 先成立，c 被据（结果确定、可复现）");
        assemblies.Single(x => x.Contains("a")).Contains("b").ShouldBeTrue();
        assemblies.Single(x => x.Contains("c")).Members.Count.ShouldBe(1);
    }

    [Fact]
    public void 建件结果与输入顺序无关()
    {
        var nodes = new TilingNode[]
        {
            new("a", new CanvasRect(0, 0, 100, 100)),
            new("b", new CanvasRect(100, 0, 100, 100)),
            new("c", new CanvasRect(300, 0, 100, 100)),
        };

        var forward = Build([("a", nodes[0].Rect), ("b", nodes[1].Rect), ("c", nodes[2].Rect)], _ => 0);
        var reversed = Build([("c", nodes[2].Rect), ("b", nodes[1].Rect), ("a", nodes[0].Rect)], _ => 0);

        Signature(forward).ShouldBe(Signature(reversed));
    }

    [Fact]
    public void 贴壳标记只落在与外壳拼合的件上()
    {
        var shell = new CanvasRect(-100, 0, 100, 400);
        var assemblies = TilingGraph.Build(
            [
                new TilingNode("glued", new CanvasRect(0, 0, 100, 100)),
                new TilingNode("far", new CanvasRect(400, 0, 100, 100)),
            ],
            shell,
            _ => 0);

        assemblies.Count.ShouldBe(2);
        assemblies.Single(x => x.Contains("glued")).TouchesShell.ShouldBeTrue();
        assemblies.Single(x => x.Contains("far")).TouchesShell.ShouldBeFalse();
    }

    [Fact]
    public void 同时贴合两件时应归上层那一件()
    {
        // a 与 b 几何完全重合（互相压住 ⇒ 两件），d 贴在它们共同右边缘上、对两件的贴合判据同时成立。
        // 归谁直接决定 d 的层号：高层优先 ⇒ 归 b，d 与 b 同层、a 留在下面。
        var assemblies = TilingGraph.Build(
            [
                new TilingNode("a", new CanvasRect(0, 0, 100, 100)),
                new TilingNode("b", new CanvasRect(0, 0, 100, 100)),
                new TilingNode("d", new CanvasRect(100, 0, 100, 100)),
            ],
            shell: null,
            id => id switch
            {
                "a" => 0,
                "b" => 1,
                "d" => 2,
                _ => int.MinValue,
            });

        assemblies.Count.ShouldBe(2);
        assemblies.Single(x => x.Contains("d")).Contains("b").ShouldBeTrue("同时贴合两件时应归上面那一件");
        assemblies.Single(x => x.Contains("a")).Contains("d").ShouldBeFalse();
    }

    private static IReadOnlyList<TilingAssembly> Build(
        IReadOnlyList<(string Id, CanvasRect Rect)> nodes,
        Func<string, int> zOrder) =>
        TilingGraph.Build([.. nodes.Select(node => new TilingNode(node.Id, node.Rect))], shell: null, zOrder);

    private static string Signature(IReadOnlyList<TilingAssembly> assemblies) =>
        string.Join(
            " | ",
            assemblies.OrderBy(a => a.Members[0].Id, StringComparer.Ordinal)
                .Select(a => string.Join(",", a.Members.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal))));
}
