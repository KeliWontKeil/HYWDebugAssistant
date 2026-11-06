using HYWDebugAssistant.Hosting.Timing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>模块定时器：到期的相位推进与生命周期。</summary>
public class ModuleTimerTests
{
    /// <summary>相位推进：未到不动；正好 / 落后半拍推进到一个间隔；落后多拍跳整拍（不补发）。</summary>
    [Theory]
    [InlineData(30d, 20d, 30d)]
    [InlineData(20d, 20d, 30d)]
    [InlineData(20d, 25d, 30d)]
    [InlineData(20d, 45d, 50d)]
    public void 相位推进_未到不动_到期推进到下一同相位(double dueMs, double nowMs, double expectedMs)
    {
        var due = TimeSpan.FromMilliseconds(dueMs);
        var now = TimeSpan.FromMilliseconds(nowMs);
        var interval = TimeSpan.FromMilliseconds(10);

        ModuleTimer.SkipToFuture(due, interval, now).ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void 定时器_启动前不触发_启动后按间隔触发_停止后不再触发()
    {
        using var set = new ModuleTimerSet();
        using var ticks = new ManualResetEventSlim();
        using var timer = set.Create();

        timer.Interval = TimeSpan.FromMilliseconds(20);
        timer.Tick += (_, _) => ticks.Set();

        set.Attach(
            action =>
            {
                action();
                return true;
            },
            _ => { });
        set.Resume();

        ticks.Wait(TimeSpan.FromMilliseconds(200)).ShouldBeFalse("未 Start 不应触发");

        timer.Start();
        ticks.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("Start 后应在一个间隔后触发");

        timer.Stop();
        ticks.Reset();
        ticks.Wait(TimeSpan.FromMilliseconds(100)).ShouldBeFalse("Stop 后不应再触发");
    }
}
