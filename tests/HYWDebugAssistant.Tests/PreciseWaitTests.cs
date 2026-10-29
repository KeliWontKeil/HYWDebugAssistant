using HYWDebugAssistant.Hosting.Context;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 等待器的**自旋预算**判据：短等待绝不能整段自旋。
/// </summary>
/// <remarks>
/// 这是纯函数测试，不做时序/CPU 断言；真实 CPU 占用由人工打开 TextSender + DataMonitor 复测。
/// </remarks>
public class PreciseWaitTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void 短等待不得分配自旋预算(double timeoutMilliseconds)
    {
        var budget = PreciseWait.SpinBudgetFor(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        budget.ShouldBe(TimeSpan.Zero, "1–4 ms 的活动周期不能靠自旋等待；否则每个模块占满一个 CPU 核");
    }

    [Theory]
    [InlineData(10.0, 1.0)]
    [InlineData(20.0, 1.0)]
    [InlineData(100.0, 1.0)]
    public void 长等待的自旋预算应有上限(double timeoutMilliseconds, double maxSpinMilliseconds)
    {
        var budget = PreciseWait.SpinBudgetFor(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        budget.ShouldBeGreaterThan(TimeSpan.Zero);
        budget.ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(maxSpinMilliseconds));
    }
}
