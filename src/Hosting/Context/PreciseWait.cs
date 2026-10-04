using System.Diagnostics;

namespace HYWDebugAssistant.Hosting.Context;

/// <summary>
/// **定时等待模式**：等待类操作（模块活动体每轮、投递循环空闲）的精度取舍。
/// </summary>
/// <remarks>
/// 上位机通常有较强的时序需求（定时发送/接收这类有明确时序过程的操作），
/// **不能接受粗粒度的解析** ⇒ 提供「等待 + 自旋补偿混合」的精确模式，同时保留粗粒度模式备选。
/// </remarks>
public enum ActivityTimingMode
{
    /// <summary>
    /// **粗粒度**：只做一次阻塞等待（`Thread.Sleep` / `WaitHandle`）。
    /// </summary>
    /// <remarks>CPU 最省，但受 **Windows 系统计时器粒度**限制（实测常见 **1–15 ms**）⇒ 1 ms 的周期拿不到 1 ms 的精度。</remarks>
    Coarse = 0,

    /// <summary>
    /// **精确（默认）**：**等待 + 自旋补偿混合** —— 先阻塞睡到"目标时刻 − 自旋窗口"，剩下的窗口内用
    /// `Thread.SpinWait` 自旋补偿 ⇒ 亚毫秒级落点。
    /// </summary>
    /// <remarks>代价 = 每个周期末尾 <see cref="PreciseWait.SpinWindow"/>（默认 **2 ms**）的自旋 CPU 占用；
    /// 对上位机这种「低负载但强时序」的场景可接受。</remarks>
    Precise = 1,
}

/// <summary>
/// **等待 + 自旋补偿**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要</b>：模块活动体用 `IStopSignal.Wait(1 ms)` 做周期，而 `Thread.Sleep(1)` 在 Windows 上
///     实际会睡 **1–15 ms**（系统计时器粒度）⇒ 1 kHz 的周期名不副实、定时发送/接收的时序被拉粗；</item>
///   <item><b>做法</b>：把一次等待拆成"**阻塞睡到 deadline − 自旋窗口**"+"**短自旋到 deadline**"两段，
///     每轮都检查停止/取消条件 ⇒ 精度进到亚毫秒，且仍然可被及时打断；</item>
///   <item><b>可切换</b>：<see cref="Mode"/> 默认 <see cref="ActivityTimingMode.Precise"/>；
///     置 <see cref="ActivityTimingMode.Coarse"/> 即回到"只阻塞等待"（省 CPU，用于不关心时序的场合/对照实验）。</item>
/// </list>
/// </remarks>
public static class PreciseWait
{
    /// <summary>自旋窗口（毫秒）：等待的最后这段时间改用自旋补偿。</summary>
    public const double SpinWindowMilliseconds = 2.0;

    /// <summary>每轮自旋的 CPU 空转次数（`Thread.SpinWait` 的参数；不产生调度）。</summary>
    private const int SpinIterations = 400;

    /// <summary>
    /// **等待模式**（默认精确）。**全局开关**：本类是"框架侧唯一"的等待实现，改这里即全局生效
    /// （放进设置页属可选后续项，见 `roadmap.md`）。
    /// </summary>
    public static ActivityTimingMode Mode { get; set; } = ActivityTimingMode.Precise;

    /// <summary>
    /// 等待到 <paramref name="timeout"/> 之后，或直到 <paramref name="cancelled"/> 返回 <see langword="true"/>。
    /// </summary>
    /// <param name="timeout">等待时长（≤0 ⇒ 只让出一次时间片）。</param>
    /// <param name="cancelled">取消判据（通常 = "已请求停止"）。</param>
    /// <returns>是否因取消而提前返回。</returns>
    public static bool Wait(TimeSpan timeout, Func<bool> cancelled)
    {
        ArgumentNullException.ThrowIfNull(cancelled);

        if (cancelled())
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            _ = Thread.Yield();
            return cancelled();
        }

        if (Mode == ActivityTimingMode.Coarse)
        {
            Thread.Sleep(timeout);
            return cancelled();
        }

        var frequency = (double)Stopwatch.Frequency;
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * frequency);
        var spinTicks = (long)(SpinWindowMilliseconds / 1000.0 * frequency);

        while (true)
        {
            if (cancelled())
            {
                return true;
            }

            var remaining = deadline - Stopwatch.GetTimestamp();

            if (remaining <= 0)
            {
                return false;
            }

            if (remaining <= spinTicks)
            {
                // **自旋补偿**：最后这 ≤2 ms 不睡觉，直接空转（每轮仍检查取消）
                Thread.SpinWait(SpinIterations);
                continue;
            }

            // 睡到"目标时刻 − 自旋窗口"；至少 1 ms，避免 0 睡变成忙等
            var sleepMs = (int)Math.Max(1, (remaining - spinTicks) * 1000.0 / frequency);
            Thread.Sleep(sleepMs);
        }
    }
}
