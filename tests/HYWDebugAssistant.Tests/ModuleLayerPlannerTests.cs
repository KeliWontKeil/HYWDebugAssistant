using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **层 = 拼接件 + 叠放顺序 + 重合关系派生的“叠放带”**，不是全局 z-order 序号。
/// </summary>
/// <remarks>
/// 拼接件内恒同层（一件刚体只能在一个平面上）；件间：压住更低件的件 = 下方重叠件的最大层号 + 1。
/// 不重合的实体即使 z-order 不同也保持同层，否则模块间将永远无法拼接。
/// **层不参与"能不能拼"**：跨带但互不遮挡的两块可以拼，见 <see cref="TilingGraph"/>。
/// </remarks>
public class ModuleLayerPlannerTests
{
    [Fact]
    public void 层应由叠放顺序与重合关系派生()
    {
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);   // 与 a 贴合 = 同一拼接件
        var c = new CanvasRect(50, 0, 100, 100);    // 压住 a/b，z-order 更高
        var d = new CanvasRect(300, 0, 100, 100);   // 与 a/b 不重合，即便 z-order 更高也同层

        var layers = ModuleLayerPlanner.Plan(
            [("a", a), ("b", b), ("c", c), ("d", d)],
            shell: null,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                "d" => 3,
                _ => int.MinValue,
            });

        layers["a"].ShouldBe(0);
        layers["b"].ShouldBe(0);
        layers["c"].ShouldBe(1, "压在 a/b 上的实体必须升层");
        layers["d"].ShouldBe(0, "不重合的实体不能因为 z-order 更高而失去同层");

        var shell = new CanvasRect(-100, 0, 100, 100);
        var onShell = new CanvasRect(-50, 20, 60, 60);
        var besideShell = new CanvasRect(0, 20, 60, 60);

        var shellLayers = ModuleLayerPlanner.Plan(
            [("top", onShell), ("side", besideShell)],
            shell,
            zOrder: id => id == "top" ? 1 : 0);

        shellLayers["top"].ShouldBe(1, "压在外壳上的模块与外壳分层");
        shellLayers["side"].ShouldBe(0, "贴在外壳右侧、不重合的模块仍在 0 层");
    }

    [Fact]
    public void 贴合即同层_层差异不得拆开拼接件()
    {
        // c 压住 a 的左半（不是贴边，所以它与 {a,b} 无关）；a 被抬到 1 层；b 与 a 贴边。
        // 逐模块定层就会给出 a=1、b=0 把组拆散——层必须以件为单位。
        var c = new CanvasRect(100, 0, 50, 100);
        var a = new CanvasRect(100, 0, 100, 100);
        var b = new CanvasRect(200, 0, 100, 100);

        var layers = ModuleLayerPlanner.Plan(
            [("c", c), ("a", a), ("b", b)],
            shell: null,
            zOrder: id => id switch
            {
                "c" => 0,
                "a" => 1,
                "b" => 2,
                _ => int.MinValue,
            });

        layers["c"].ShouldBe(0);
        layers["a"].ShouldBe(1);
        layers["b"].ShouldBe(1, "同一拼接件的成员必须同层：刚体只能在一个平面上");
        TilingGraph.AreFlush(a, b).ShouldBeTrue();
    }

    [Fact]
    public void 叠放键应取件内最大z()
    {
        // 件 {a, b}：a 在最底、b 在最顶。c 只压住 a（且**不与 b 贴合**，否则就是"归哪一件"的问题了），
        // z 序介于两者之间。叠放键取 max ⇒ 整件排在 c 之上（a 也升到 1 层）；取 min 会给出相反结果。
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);
        var c = new CanvasRect(20, 0, 60, 100);

        var layers = ModuleLayerPlanner.Plan(
            [("a", a), ("b", b), ("c", c)],
            shell: null,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 5,
                "c" => 3,
                _ => int.MinValue,
            });

        layers["c"].ShouldBe(0);
        layers["a"].ShouldBe(1, "件的叠放键 = 成员最大 z ⇒ 整件在 c 之上");
        layers["b"].ShouldBe(1);
    }

    [Fact]
    public void 压住件内某块的模块不得与件同层()
    {
        // 遮挡型几何：b 贴在 a 右边（会拼成一件），c 压住 a 的右半、又贴在 b 的左边。
        // c 永远进不了这一件 ⇒ c 必须单独成层、且在件之上；不能靠"贴边"把三块并成一片。
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);
        var c = new CanvasRect(20, 0, 80, 100);

        var layers = ModuleLayerPlanner.Plan(
            [("a", a), ("b", b), ("c", c)],
            shell: null,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        layers["a"].ShouldBe(0);
        layers["b"].ShouldBe(0);
        layers["c"].ShouldBe(1, "压住 b 的模块必须升层");

        var assemblies = TilingGraph.Build(
            [new TilingNode("a", a), new TilingNode("b", b), new TilingNode("c", c)],
            shell: null,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        assemblies.Count.ShouldBe(2, "真正刚体不能自己压自己：c 与 {a,b} 必须是两件");
    }

    [Fact]
    public void 无贴合时应逐位退化为逐模块分层()
    {
        // 三块互不贴合、两两重叠 ⇒ 每块一件，层号就是纯叠放带 0/1/2。
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(10, 10, 100, 100);
        var c = new CanvasRect(20, 20, 100, 100);

        var layers = ModuleLayerPlanner.Plan(
            [("a", a), ("b", b), ("c", c)],
            shell: null,
            zOrder: id => id switch
            {
                "a" => 0,
                "b" => 1,
                "c" => 2,
                _ => int.MinValue,
            });

        layers["a"].ShouldBe(0);
        layers["b"].ShouldBe(1);
        layers["c"].ShouldBe(2);
    }
}
