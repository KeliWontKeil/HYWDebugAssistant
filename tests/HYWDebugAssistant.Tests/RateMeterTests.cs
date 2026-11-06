using HYWDebugAssistant.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1：**速率计**——框架侧唯一的速率口径（UI 不得自行差分估算）。
/// </summary>
/// <remarks>用**显式时基**做判定，完全不依赖真实时间（确定性、无 flake）。</remarks>
public class RateMeterTests
{
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
    public void 同一次刷新里的重复读取_必须给出同一个数()
    {
        // 一次 UI 刷新会对**每个模块**各读一遍诊断（标记 / 角色信息 / 角色连线…）：
        // 若每次都重算「自上次观察以来」的增量，第一遍之后增量恒为 0 ⇒ 除第一个消费者外全都读到 0
        // （现场表现：角色的速率**恒显示占位符**，一直是 `---`）。
        var meter = new RateMeter();

        _ = meter.Observe(total: 0, timestamp: 0, frequency: 1000);
        var first = meter.Observe(total: 500, timestamp: 1000, frequency: 1000);   // 1 秒走了 500 ⇒ 500/s

        first.ShouldBe(500);

        // 同一毫秒内的后续读取（不同消费者）⇒ 必须与第一次相同，而不是 0
        meter.Observe(total: 500, timestamp: 1000, frequency: 1000).ShouldBe(500, "重复读取不得把速率打成 0");
        meter.Observe(total: 500, timestamp: 1050, frequency: 1000).ShouldBe(500, "间隔过短 ⇒ 仍返回上次速率");
        meter.Rate.ShouldBe(500);

        // 过了最小间隔后：按**累计区间**重算（基准仍是上一次真正重算的时刻）
        meter.Observe(total: 1000, timestamp: 2000, frequency: 1000).ShouldBe(500, "1 秒又走了 500 ⇒ 仍是 500/s");
    }
}
