using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 性能监视器的纯计算判据：CPU 归一化与短内存文本。
/// 不断言真实进程/系统数值（随机器与调试器变化），也不把侧栏布局抄成契约。
/// </summary>
public class PerformanceMonitorTests
{
    [Theory]
    [InlineData(0.5, 1.0, 2, 25.0)]
    [InlineData(0.1, 1.0, 1, 10.0)]
    [InlineData(1.0, 1.0, 1, 100.0)]
    [InlineData(2.0, 1.0, 1, 100.0)]
    public void CPU百分比按核数归一化并夹到0到100(
        double cpuSeconds,
        double elapsedSeconds,
        int processorCount,
        double expected)
    {
        var actual = PerformanceMonitorViewModel.CalculateCpuPercent(
            TimeSpan.FromSeconds(cpuSeconds),
            TimeSpan.FromSeconds(elapsedSeconds),
            processorCount);

        actual.ShouldBe(expected, 0.001);
    }

    [Fact]
    public void CPU百分比遇到零耗时或负差值返回零()
    {
        PerformanceMonitorViewModel.CalculateCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4).ShouldBe(0);
        PerformanceMonitorViewModel.CalculateCpuPercent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), 4).ShouldBe(0);
    }

    [Fact]
    public void 内存短文本在M与G边界可读()
    {
        PerformanceMonitorViewModel.FormatMemory(0).ShouldBe("0M");
        PerformanceMonitorViewModel.FormatMemory(48L * 1024 * 1024).ShouldBe("48M");
        PerformanceMonitorViewModel.FormatMemory(1536L * 1024 * 1024).ShouldBe("1.5G");
    }
}
