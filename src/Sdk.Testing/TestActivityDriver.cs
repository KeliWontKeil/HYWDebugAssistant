using System.Diagnostics;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// **测试用 step 驱动**：在测试线程上按 <see cref="IActivitySource.NextDelay"/> 驱动 <c>OnStep</c>。
/// </summary>
/// <remarks>
/// 框架不使用它（框架拥有实例循环与计时服务）；它只让"直接驱动模块"的行为测试不必自己写循环。
/// 这里允许 `Thread.Sleep`：测试线程，不是模块实现。
/// </remarks>
public static class TestActivityDriver
{
    /// <summary>驱动到取消令牌被触发。</summary>
    /// <param name="source">活动源。</param>
    /// <param name="context">模块上下文。</param>
    /// <param name="cancellation">测试侧停止令牌。</param>
    public static void Run(
        IActivitySource source,
        IModuleContext context,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        var clock = Stopwatch.StartNew();

        source.OnStep(context, StepReason.Start);

        var lastDelay = source.NextDelay;
        var dueAt = DueAt(clock, lastDelay);

        while (!cancellation.IsCancellationRequested)
        {
            var now = clock.Elapsed;

            if (dueAt is { } deadline && now >= deadline)
            {
                source.OnStep(context, StepReason.Due);
                lastDelay = source.NextDelay;
                dueAt = DueAt(clock, lastDelay);
                continue;
            }

            // 到期前仍让测试有机会送命令/输入；普通轮询步骤不重置已排好的到期。
            source.OnStep(context, StepReason.Command | StepReason.Input);

            var delay = source.NextDelay;

            if (delay != lastDelay)
            {
                lastDelay = delay;
                dueAt = DueAt(clock, delay);
            }

            var sleep = TimeSpan.FromMilliseconds(20);

            if (dueAt is { } next)
            {
                var remaining = next - clock.Elapsed;

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

    private static TimeSpan? DueAt(Stopwatch clock, TimeSpan? delay) =>
        delay is { } value ? clock.Elapsed + value : null;
}
