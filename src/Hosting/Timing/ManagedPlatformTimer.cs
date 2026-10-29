using System.Diagnostics;

namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// 托管回退计时后端：远离截止时刻时阻塞等待，接近截止时刻时短自旋。
/// </summary>
/// <remarks>
/// 平台没有高精度计时原语时使用；1 ms 连续等待会落进自旋窗口，可能占用一个核。
/// 这是明确的降级路径，不是默认路径。
/// </remarks>
internal sealed class ManagedPlatformTimer : IPlatformTimer
{
    private const double SpinWindowMilliseconds = 2.0;

    /// <inheritdoc />
    public bool WaitUntil(long deadlineTicks, WaitHandle wake)
    {
        while (true)
        {
            var remaining = deadlineTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return false;
            }

            var remainingMs = remaining * 1000.0 / Stopwatch.Frequency;

            if (remainingMs > SpinWindowMilliseconds)
            {
                var waitMs = (int)Math.Min(4, Math.Max(1, remainingMs - SpinWindowMilliseconds));
                if (wake.WaitOne(waitMs))
                {
                    return true;
                }

                continue;
            }

            if (wake.WaitOne(0))
            {
                return true;
            }

            Thread.SpinWait(400);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
