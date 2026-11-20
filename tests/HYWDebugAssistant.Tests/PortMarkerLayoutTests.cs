using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **端口标记的落边规则**：尺寸固定，放不下的边**直接换成放得下的边**（单向，不记原边）。
/// </summary>
public class PortMarkerLayoutTests
{
    /// <summary>声明边放得下 ⇒ 原样返回（坐标不变）。</summary>
    [Theory]
    [InlineData(PortEdge.Top, 400.0, 180.0, PortEdge.Top)]
    [InlineData(PortEdge.Bottom, 400.0, 180.0, PortEdge.Bottom)]
    [InlineData(PortEdge.Left, 200.0, 320.0, PortEdge.Left)]
    [InlineData(PortEdge.Right, 200.0, 320.0, PortEdge.Right)]
    public void 放得下时保留声明边(PortEdge declared, double width, double height, PortEdge expected)
    {
        PortMarkerLayout.FitEdge(declared, PortDirection.Receive, width, height).ShouldBe(expected);
    }

    /// <summary>窄窗（宽 &lt; 150）放不下上下边的标记 ⇒ 直接换成左右边。</summary>
    [Fact]
    public void 窄窗把上下边的标记换到左右边()
    {
        PortMarkerLayout.FitEdge(PortEdge.Top, PortDirection.Receive, 130, 420)
            .ShouldBe(PortEdge.Left);
        PortMarkerLayout.FitEdge(PortEdge.Bottom, PortDirection.Receive, 130, 420)
            .ShouldBe(PortEdge.Left);
    }

    /// <summary>矮窗（高 &lt; 150）放不下左右边的标记 ⇒ 直接换成上下边。</summary>
    [Fact]
    public void 矮窗把左右边的标记换到上下边()
    {
        PortMarkerLayout.FitEdge(PortEdge.Left, PortDirection.Send, 340, 120)
            .ShouldBe(PortEdge.Top);
        PortMarkerLayout.FitEdge(PortEdge.Right, PortDirection.Send, 340, 120)
            .ShouldBe(PortEdge.Top);
    }

    /// <summary>`Auto` 先按方向解析（发送右 / 接收左），放不下再换。</summary>
    [Theory]
    [InlineData(PortDirection.Send, 320.0, 220.0, PortEdge.Right)]
    [InlineData(PortDirection.Receive, 320.0, 220.0, PortEdge.Left)]
    [InlineData(PortDirection.Send, 130.0, 420.0, PortEdge.Right)]
    public void 自动边按方向解析(PortDirection direction, double width, double height, PortEdge expected)
    {
        PortMarkerLayout.FitEdge(PortEdge.Auto, direction, width, height).ShouldBe(expected);
    }

    /// <summary>换过去之后就是它自己（不再来回变）：同一条边再算一次仍是它。</summary>
    [Fact]
    public void 换边后保持稳定()
    {
        var fitted = PortMarkerLayout.FitEdge(PortEdge.Top, PortDirection.Receive, 130, 420);
        PortMarkerLayout.FitEdge(fitted, PortDirection.Receive, 130, 420).ShouldBe(fitted);
    }

    /// <summary>两侧都放不下（人为绕过地板）⇒ 保留声明边。</summary>
    [Fact]
    public void 全都放不下时退回声明边()
    {
        PortMarkerLayout.FitEdge(PortEdge.Top, PortDirection.Receive, 100, 100)
            .ShouldBe(PortEdge.Top);
    }

    /// <summary>拖动：这条边放不下 ⇒ 不落位（保持当前边，不会"拖过去又弹回"）。</summary>
    [Fact]
    public void 拖动不放下的边不落位()
    {
        PortMarkerLayout.PickDragEdge(PortEdge.Top, 130, 420).ShouldBeNull();
        PortMarkerLayout.PickDragEdge(PortEdge.Left, 340, 120).ShouldBeNull();
    }

    /// <summary>拖动：能放下的边照常落位。</summary>
    [Fact]
    public void 拖动能放下的边正常落位()
    {
        PortMarkerLayout.PickDragEdge(PortEdge.Left, 130, 420).ShouldBe((PortEdge?)PortEdge.Left);
        PortMarkerLayout.PickDragEdge(PortEdge.Top, 400, 180).ShouldBe((PortEdge?)PortEdge.Top);
    }

    /// <summary>标记尺寸是固定的：竖排 20×150、横排 150×20（不随窗口缩短）。</summary>
    [Fact]
    public void 标记尺寸固定()
    {
        PortMarkerMetrics.Width.ShouldBe(20);
        PortMarkerMetrics.Length.ShouldBe(150);
    }
}
