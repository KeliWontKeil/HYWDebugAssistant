using System.Globalization;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **块轴（高度）唯一解析者**的语义判据。
/// </summary>
/// <remarks>
/// <para>
/// 这些是**薄判据**：钉住"地板怎么算""余量怎么分"这两条口径本身，
/// 不涉及任何控件、任何渲染。它们便宜、稳定，并且能挡住后人"调参数时把它悄悄改回去"。
/// </para>
/// <para>
/// 为什么要单独钉：从前高度被拆成五处算法（面板里的几个私有函数 + 读 <c>DesiredSize</c> 缓存），
/// 于是"同一个声明在不同上下文得到不同高"没有任何一处能回答。现在口径只有这一处，
/// 判据也就只有这一份。
/// </para>
/// </remarks>
public sealed class SurfaceBlockSolverTests
{
    [Fact]
    public void 声明像素_地板就是那个像素_且不吃余量()
    {
        var spec = SurfaceBlockSolver.SpecOf(Node(pixels: 32), measured: 999);

        spec.Mode.ShouldBe(AxisMode.Pixels);
        spec.Floor.ShouldBe(32, "声明写死的像素与可用空间无关");
        spec.Growable.ShouldBeFalse("像素档不吃余量");
    }

    [Fact]
    public void 按内容_地板是量到的高_且不吃余量()
    {
        var spec = SurfaceBlockSolver.SpecOf(Node(adaptive: true), measured: 15.2);

        spec.Mode.ShouldBe(AxisMode.Adaptive);
        spec.Floor.ShouldBe(15.2, 0.001);
        spec.Growable.ShouldBeFalse("按内容型不吃余量");
    }

    [Fact]
    public void 填满_地板只取下限_不吃内容高()
    {
        // **填满的地板与"内容有多高"无关**：它的语义是"吃掉容器余量"。
        // 把内容高当地板会让日志/输入框把整块表面撑到几千像素（兄弟被推出视口），
        // 而内容超出应当由它**自己内部的滚动**消化。
        var spec = SurfaceBlockSolver.SpecOf(Node(fill: true), measured: 58.4);

        spec.Mode.ShouldBe(AxisMode.Fill);
        spec.Floor.ShouldBe(0, 0.001, "没有声明下限 ⇒ 地板就是 0");
        spec.Growable.ShouldBeTrue("填满型吃余量");
    }

    [Fact]
    public void 声明下限_压过量到的高()
    {
        // 两条来源取最大者：声明下限（作者说了算）≥ 内容量到的高 —— 而 fill 档不取内容高。
        var spec = SurfaceBlockSolver.SpecOf(Node(fill: true, min: 90), measured: 30);

        spec.Floor.ShouldBe(90, 0.001);
    }

    [Fact]
    public void 上限_夹住声明下限()
    {
        var spec = SurfaceBlockSolver.SpecOf(Node(fill: true, min: 90, max: 40), measured: 100);

        spec.Floor.ShouldBe(40, 0.001, "上限必须能压低地板，否则'不许超过'这条声明失效");
    }

    [Fact]
    public void 分配_固定项拿自己的_余量等分给填满型()
    {
        // 一块 300 高的容器：两个 32 的按钮（固定）+ 一个填满的文本面（无声明下限），间距 2。
        var specs = new[]
        {
            SurfaceBlockSolver.SpecOf(Node(pixels: 32), 32),
            SurfaceBlockSolver.SpecOf(Node(pixels: 32), 32),
            SurfaceBlockSolver.SpecOf(Node(fill: true), 58.4),
        };

        var plan = SurfaceBlockSolver.Plan(specs, spacing: 2, containerHeight: 300);

        // 固定部分 = 32 + 32 + 2×2 = 68；余量 232 全给填满型（它的地板是 0）
        plan.Heights[0].ShouldBe(32, 0.001);
        plan.Heights[1].ShouldBe(32, 0.001);
        plan.Heights[2].ShouldBe(232, 0.001, "填满型吃掉扣掉固定项与间距之后的全部余量");
    }

    [Fact]
    public void 分配_两个填满型等分余量()
    {
        var specs = new[]
        {
            SurfaceBlockSolver.SpecOf(Node(fill: true), 20),
            SurfaceBlockSolver.SpecOf(Node(fill: true), 40),
        };

        var plan = SurfaceBlockSolver.Plan(specs, spacing: 2, containerHeight: 202);

        // 余量 = 202 − 2（间距）= 200，两个地板都是 0 ⇒ 等分 100 各一。
        plan.Heights[0].ShouldBe(100, 0.001);
        plan.Heights[1].ShouldBe(100, 0.001, "两个填满型的口径相同 ⇒ 实得也相同");
    }

    [Fact]
    public void 分配_放不下时各守地板()
    {
        // 容器只有 100，而固定项 32 + 填满型地板 200（声明下限）⇒ 放不下。
        var specs = new[]
        {
            SurfaceBlockSolver.SpecOf(Node(pixels: 32), 32),
            SurfaceBlockSolver.SpecOf(Node(fill: true, min: 200), 200),
        };

        var plan = SurfaceBlockSolver.Plan(specs, spacing: 2, containerHeight: 100);

        plan.Heights[0].ShouldBe(32, 0.001);
        plan.Heights[1].ShouldBe(200, 0.001, "放不下时守自己的地板，绝不塌成 0 —— 放不下是外层滚动的事");
    }

    [Fact]
    public void 分配_没有填满型时余量留在底部()
    {
        var specs = new[]
        {
            SurfaceBlockSolver.SpecOf(Node(pixels: 24), 24),
            SurfaceBlockSolver.SpecOf(Node(adaptive: true), 15.2),
        };

        var plan = SurfaceBlockSolver.Plan(specs, spacing: 2, containerHeight: 300);

        plan.Heights[0].ShouldBe(24, 0.001);
        plan.Heights[1].ShouldBe(15.2, 0.001, "没有填满型就谁也不许自作主张拉高，余量留底部");
    }

    [Fact]
    public void 分配_空容器不炸()
    {
        var plan = SurfaceBlockSolver.Plan([], spacing: 8, containerHeight: 100);

        plan.Heights.ShouldBeEmpty();
    }

    /// <summary>造一个只有几何相关的声明节点（其余字段给中性值 —— 本类只读高度与约束）。</summary>
    /// <param name="pixels">固定像素。</param>
    /// <param name="adaptive">按内容。</param>
    /// <param name="fill">填满。</param>
    /// <param name="min">声明下限。</param>
    /// <param name="max">声明上限。</param>
    /// <returns>节点。</returns>
    private static ViewNode Node(double? pixels = null, bool adaptive = false, bool fill = false, double? min = null, double? max = null) =>
        new()
        {
            Kind = "testBox",
            Title = string.Empty,
            Width = new AxisLayout { Fill = true },
            Height = new AxisLayout { Pixels = pixels, Adaptive = adaptive, Fill = fill, Min = min, Max = max },
            Layout = "flow",
            Overflow = "scroll",
            Columns = [],
            Rows = [],
            Children = [],
            Warnings = [],
            TextAlign = "default",
            Unit = string.Empty,
        };
}
