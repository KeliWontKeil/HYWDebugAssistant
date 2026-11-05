using System.Diagnostics;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// **测试用活动驱动**：在测试线程上推进测试时钟、触发模块定时器，并周期调用 <c>OnStep</c>。
/// </summary>
/// <remarks>
/// 框架不使用它（框架拥有实例线程、计时服务与定时器派发）；它只让"直接驱动模块"的行为测试
/// 不必自己写循环。这里允许 <c>WaitOne</c>：测试线程，不是模块实现。
/// 模块的周期现在来自 <see cref="IModuleContext.Timers"/>，不再是 <c>NextDelay</c>。
/// </remarks>
public static class TestActivityDriver
{
    /// <summary>驱动到取消令牌被触发。</summary>
    /// <param name="source">活动源。</param>
    /// <param name="context">测试上下文。</param>
    /// <param name="cancellation">测试侧停止令牌。</param>
    public static void Run(
        IActivitySource source,
        TestModuleContext context,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        source.OnStep(context, StepReason.Start);

        var clock = Stopwatch.StartNew();

        while (!cancellation.IsCancellationRequested)
        {
            context.Timers.AdvanceTo(clock.Elapsed);

            // 到期前仍让测试有机会送命令/输入；普通轮询步骤不重置已排好的定时器。
            source.OnStep(context, StepReason.Command | StepReason.Input);

            var sleep = TimeSpan.FromMilliseconds(20);

            if (context.Timers.NextDue is { } due)
            {
                var remaining = due - clock.Elapsed;

                if (remaining < sleep)
                {
                    sleep = remaining;
                }
            }

            if (sleep <= TimeSpan.Zero)
            {
                continue;
            }

            _ = cancellation.WaitHandle.WaitOne(sleep);
        }
    }
}
