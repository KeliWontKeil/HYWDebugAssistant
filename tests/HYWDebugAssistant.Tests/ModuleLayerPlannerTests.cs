using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **层 = 叠放顺序 + 重合关系派生的“叠放带”**，不是全局 z-order 序号。
/// </summary>
/// <remarks>
/// 同层实体必须两两不重合；一个实体压住更低实体时，层号 = 下方重叠实体的最大层号 + 1。
/// 不重叠的模块即使 z-order 不同也保持同层，否则模块间将永远无法拼接。
/// **层先于组**：贴合不能反向把不同层模块并成同层。
/// </remarks>
public class ModuleLayerPlannerTests
{
    [Fact]
    public void 层应由叠放顺序与重合关系派生()
    {
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);   // 与 a 贴合 = 同一拼接组
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
    public void 贴合但不同层的模块不得被合并()
    {
        // c 在 a 下方并与 a 重合 ⇒ a 升到 1 层；b 与 a 贴合，但不与 c 重合 ⇒ b 仍在 0 层。
        var c = new CanvasRect(0, 0, 100, 100);
        var a = new CanvasRect(0, 0, 100, 100);
        var b = new CanvasRect(100, 0, 100, 100);

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

        layers["a"].ShouldBe(1);
        layers["b"].ShouldBe(0);
        SnapEngine.AreJoined(a, b).ShouldBeTrue("几何上贴边，但层不同就不能成组");
    }
}
