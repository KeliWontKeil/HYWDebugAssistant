using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Queues;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>状态显示只做小滞回：原始队列/丢弃事实不在这里改变。</summary>
public sealed class StatusHysteresisTests
{
    private static long Ticks(int milliseconds) =>
        (long)(TimeSpan.FromMilliseconds(milliseconds).TotalSeconds * Stopwatch.Frequency);

    [Fact]
    public void 单个在途单元不应立即显示积压()
    {
        var status = new StatusHysteresis(
            enterDepth: 2,
            exitDepth: 1,
            hold: TimeSpan.FromMilliseconds(200),
            dropHold: TimeSpan.FromMilliseconds(300));

        status.ObserveDepth(1, 0);

        status.IsBacklogActive(0).ShouldBeFalse();
    }

    [Fact]
    public void 达到进入深度并持续保持时间后才显示积压()
    {
        var status = new StatusHysteresis(
            enterDepth: 2,
            exitDepth: 1,
            hold: TimeSpan.FromMilliseconds(200),
            dropHold: TimeSpan.FromMilliseconds(300));

        status.ObserveDepth(2, 0);
        status.IsBacklogActive(Ticks(199)).ShouldBeFalse();
        status.IsBacklogActive(Ticks(200)).ShouldBeTrue();
    }

    [Fact]
    public void 回落到退出深度并持续保持时间后才取消积压()
    {
        var status = new StatusHysteresis(
            enterDepth: 2,
            exitDepth: 1,
            hold: TimeSpan.FromMilliseconds(200),
            dropHold: TimeSpan.FromMilliseconds(300));

        status.ObserveDepth(2, 0);
        status.IsBacklogActive(Ticks(200)).ShouldBeTrue();
        status.ObserveDepth(1, Ticks(200));

        status.IsBacklogActive(Ticks(399)).ShouldBeTrue();
        status.IsBacklogActive(Ticks(400)).ShouldBeFalse();
    }

    [Fact]
    public void 丢弃事实消失后应保持到保持时间结束()
    {
        var status = new StatusHysteresis(
            enterDepth: 2,
            exitDepth: 1,
            hold: TimeSpan.FromMilliseconds(200),
            dropHold: TimeSpan.FromMilliseconds(300));

        status.ObserveDrop(true, 0);
        status.IsDropActive(0).ShouldBeTrue();

        status.ObserveDrop(false, Ticks(100));
        status.IsDropActive(Ticks(399)).ShouldBeTrue();
        status.IsDropActive(Ticks(400)).ShouldBeFalse();
    }

    [Fact]
    public void 再次丢弃应重新开始保持计时()
    {
        var status = new StatusHysteresis(
            enterDepth: 2,
            exitDepth: 1,
            hold: TimeSpan.FromMilliseconds(200),
            dropHold: TimeSpan.FromMilliseconds(300));

        status.ObserveDrop(true, 0);
        status.ObserveDrop(false, Ticks(100));
        status.ObserveDrop(true, Ticks(200));
        status.ObserveDrop(false, Ticks(300));

        status.IsDropActive(Ticks(599)).ShouldBeTrue();
        status.IsDropActive(Ticks(600)).ShouldBeFalse();
    }
}
