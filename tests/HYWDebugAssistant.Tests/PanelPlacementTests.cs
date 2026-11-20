using Avalonia;
using HYWDebugAssistant.Sdk.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>浮层面板落位：贴着锚点，放不下换边，最终夹进工作区。</summary>
public class PanelPlacementTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1080);
    private static readonly PixelSize Panel = new(300, 360);

    [Fact]
    public void 右侧放得下时贴右侧()
    {
        var anchor = new PixelRect(100, 100, 20, 20);

        PanelPlacement.Compute(anchor, Panel, Work).ShouldBe(new PixelPoint(124, 100));
    }

    [Fact]
    public void 右侧放不下时换左侧()
    {
        var anchor = new PixelRect(1700, 100, 20, 20);

        PanelPlacement.Compute(anchor, Panel, Work).ShouldBe(new PixelPoint(1396, 100));
    }

    [Fact]
    public void 左右都放不下时放下方()
    {
        var work = new PixelRect(0, 0, 500, 1080);
        var anchor = new PixelRect(240, 100, 20, 20);

        var point = PanelPlacement.Compute(anchor, Panel, work);

        point.Y.ShouldBe(124);
        point.X.ShouldBeInRange(work.X, work.Right - Panel.Width);
    }

    [Fact]
    public void 下方也放不下时放上方()
    {
        var work = new PixelRect(0, 0, 500, 600);
        var anchor = new PixelRect(240, 500, 20, 20);

        var point = PanelPlacement.Compute(anchor, Panel, work);

        point.Y.ShouldBe(136);
        point.X.ShouldBeInRange(work.X, work.Right - Panel.Width);
    }

    [Fact]
    public void 四面都放不下时夹进工作区()
    {
        var work = new PixelRect(10, 20, 400, 300);
        var anchor = new PixelRect(150, 150, 20, 20);

        var point = PanelPlacement.Compute(anchor, Panel, work);

        point.X.ShouldBe(work.Right - Panel.Width);
        point.Y.ShouldBe(work.Y);
    }
}
