using System.Diagnostics;
using HYWDebugAssistant.Hosting.Timing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>全局计时服务：到期与取消都能唤醒等待者。</summary>
public class GlobalTimerServiceTests
{
    [Fact]
    public void 等待到绝对截止时刻_应在目标之后返回()
    {
        var service = GlobalTimerService.Shared;
        var start = Stopwatch.GetTimestamp();

        var cancelled = service.WaitUntil(
            service.Now + TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        var elapsed = Stopwatch.GetElapsedTime(start);
        cancelled.ShouldBeFalse();
        elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(10));
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void 取消令牌_应提前唤醒长等待()
    {
        var service = GlobalTimerService.Shared;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var start = Stopwatch.GetTimestamp();

        var cancelled = service.WaitUntil(
            service.Now + TimeSpan.FromSeconds(5),
            cancellation.Token);

        var elapsed = Stopwatch.GetElapsedTime(start);
        cancelled.ShouldBeTrue();
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ModuleTiming_每模块停止令牌应能打断等待()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var timing = new ModuleTiming(GlobalTimerService.Shared, cancellation.Token);

        var cancelled = timing.WaitFor(TimeSpan.FromSeconds(5));

        cancelled.ShouldBeTrue();
    }

    [Fact]
    public void 一毫秒等待_不应退化成十毫秒级()
    {
        var service = GlobalTimerService.Shared;
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5;   // ~200 ms
        var count = 0;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            _ = service.WaitUntil(service.Now + TimeSpan.FromMilliseconds(1), CancellationToken.None);
            count++;
        }

        count.ShouldBeGreaterThan(
            50,
            "1 ms 周期在 200 ms 窗口内至少应有 50 次唤醒；10–15 ms 级定时器只有约 13 次");
    }
}
