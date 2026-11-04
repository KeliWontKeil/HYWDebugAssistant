using System.Diagnostics;
using HYWDebugAssistant.Hosting.Timing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>全局计时服务：到期回调触发，取消后不触发。</summary>
public class GlobalTimerServiceTests
{
    [Fact]
    public void 计时回调_应在目标之后触发()
    {
        using var fired = new ManualResetEventSlim();
        var start = Stopwatch.GetTimestamp();

        using var schedule = FrameworkTimer.Shared.Schedule(
            FrameworkTimer.Shared.Now + TimeSpan.FromMilliseconds(20),
            () => fired.Set());

        fired.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        Stopwatch.GetElapsedTime(start).ShouldBeGreaterThan(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void 取消计时期_不应触发回调()
    {
        using var fired = new ManualResetEventSlim();

        var schedule = FrameworkTimer.Shared.Schedule(
            FrameworkTimer.Shared.Now + TimeSpan.FromSeconds(5),
            () => fired.Set());

        schedule.Dispose();

        fired.Wait(TimeSpan.FromMilliseconds(100)).ShouldBeFalse();
    }
}
