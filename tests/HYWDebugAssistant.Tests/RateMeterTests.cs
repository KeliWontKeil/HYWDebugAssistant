using HYWDebugAssistant.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1（ADR-0046 D8）：**速率计**——框架侧唯一的速率口径（UI 不得自行差分估算）。
/// </summary>
/// <remarks>用**显式时基**做判定，完全不依赖真实时间（确定性、无 flake）。</remarks>
public class RateMeterTests
{
    [Fact]
    public void 首次观察_只建立基准且不猜历史速率()
    {
        var meter = new RateMeter();

        meter.Observe(total: 1000, timestamp: 500, frequency: 100).ShouldBe(0, "首个观察没有「上一时刻」，不得猜历史速率");
        meter.Rate.ShouldBe(0);
    }

    [Fact]
    public void 稳定增量_应给出每秒速率()
    {
        var meter = new RateMeter();

        _ = meter.Observe(total: 0, timestamp: 0, frequency: 100);
        var rate = meter.Observe(total: 300, timestamp: 300, frequency: 100);   // 3 秒（时基 100/s）走了 300

        rate.ShouldBe(100, "300 个计数 / 3 秒 = 100/s");
        meter.Rate.ShouldBe(100);
    }

    [Fact]
    public void 时间未推进_应保持上次速率而不是除零()
    {
        var meter = new RateMeter();

        _ = meter.Observe(total: 0, timestamp: 0, frequency: 100);
        _ = meter.Observe(total: 100, timestamp: 100, frequency: 100);

        meter.Observe(total: 150, timestamp: 100, frequency: 100).ShouldBe(100, "同一时刻再次观察不得产生无穷速率");
    }

    [Fact]
    public void 计数回退_应归零而不是负速率()
    {
        var meter = new RateMeter();

        _ = meter.Observe(total: 0, timestamp: 0, frequency: 100);
        _ = meter.Observe(total: 500, timestamp: 100, frequency: 100);

        // 绑定重建/计数器替换 ⇒ 计数可能回退
        meter.Observe(total: 10, timestamp: 200, frequency: 100).ShouldBe(0, "计数回退视为 0，不出现负速率");
    }

    [Fact]
    public void 重置后_应重新建立基准()
    {
        var meter = new RateMeter();

        _ = meter.Observe(total: 0, timestamp: 0, frequency: 100);
        _ = meter.Observe(total: 100, timestamp: 100, frequency: 100);
        meter.Rate.ShouldBe(100);

        meter.Reset();
        meter.Rate.ShouldBe(0);
        meter.Observe(total: 90_000, timestamp: 500, frequency: 100).ShouldBe(0, "重置后首个观察只建基准");
    }
}
