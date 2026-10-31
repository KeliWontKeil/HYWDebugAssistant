using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Theming;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>工作区优先、显式改比例才重算的外壳几何规则。</summary>
public class ShellGeometryPlannerTests
{
    [Fact]
    public void 启动_有工作区时精确恢复尺寸且不受比例设置影响()
    {
        var geometry = ShellGeometryPlanner.Startup(
            "9:19.5",
            fixedSize: true,
            new WorkspacePlacementView(100, 100, 1000, 700));

        geometry.Width.ShouldBe(1000);
        geometry.Height.ShouldBe(700);
        geometry.CanResize.ShouldBeFalse("固定尺寸只控制可缩放性，不改变工作区尺寸");
    }

    [Fact]
    public void 启动_无工作区时按比例测量()
    {
        var (defaultWidth, defaultHeight) = ShellLayout.Measure("9:19.5");
        ShellGeometryPlanner.Startup("9:19.5", true, null)
            .ShouldBe(new ShellGeometry(defaultWidth, defaultHeight, false));

        var (wideWidth, wideHeight) = ShellLayout.Measure("9:16");
        ShellGeometryPlanner.Startup("9:16", false, null)
            .ShouldBe(new ShellGeometry(wideWidth, wideHeight, true));
        ShellLayout.Aspects.ShouldNotContain("16:9");
    }

    [Fact]
    public void 设置应用_只切固定不改变尺寸()
    {
        var geometry = ShellGeometryPlanner.AfterSettings(
            "9:19.5",
            oldFixedSize: true,
            "9:19.5",
            newFixedSize: false,
            currentWidth: 1000,
            currentHeight: 700);

        geometry.Width.ShouldBe(1000);
        geometry.Height.ShouldBe(700);
        geometry.CanResize.ShouldBeTrue();
    }

    [Fact]
    public void 设置应用_显式改比例才重新测量()
    {
        var geometry = ShellGeometryPlanner.AfterSettings(
            "9:19.5",
            oldFixedSize: false,
            "9:16",
            newFixedSize: false,
            currentWidth: 1000,
            currentHeight: 700);

        var (wideWidth, wideHeight) = ShellLayout.Measure("9:16");
        geometry.Width.ShouldBe(wideWidth, "显式改比例 ⇒ 按新比例重新测量");
        geometry.Height.ShouldBe(wideHeight);
        geometry.CanResize.ShouldBeTrue();
    }
}
