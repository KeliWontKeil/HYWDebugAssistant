using HYWDebugAssistant.Hosting.Timing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>模块定时器：到期的相位推进与生命周期。</summary>
public class ModuleTimerTests
{
    [Fact]
    public void 相位推进_未来到期不动()
    {
        var due = TimeSpan.FromMilliseconds(30);
        var now = TimeSpan.FromMilliseconds(20);
        var interval = TimeSpan.FromMilliseconds(10);

        ModuleTimer.SkipToFuture(due, interval, now).ShouldBe(due);
    }

    [Fact]
    public void 相位推进_正好到期应推进一个间隔()
    {
        var due = TimeSpan.FromMilliseconds(20);
        var now = due;
        var interval = TimeSpan.FromMilliseconds(10);

        ModuleTimer.SkipToFuture(due, interval, now)
            .ShouldBe(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void 相位推进_落后半拍应推进到下一同相位()
    {
        var due = TimeSpan.FromMilliseconds(20);
        var now = TimeSpan.FromMilliseconds(25);
        var interval = TimeSpan.FromMilliseconds(10);

        ModuleTimer.SkipToFuture(due, interval, now)
            .ShouldBe(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void 相位推进_落后多拍应跳整拍而不补发()
    {
        var due = TimeSpan.FromMilliseconds(20);
        var now = TimeSpan.FromMilliseconds(45);
        var interval = TimeSpan.FromMilliseconds(10);

        ModuleTimer.SkipToFuture(due, interval, now)
            .ShouldBe(TimeSpan.FromMilliseconds(50));
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
