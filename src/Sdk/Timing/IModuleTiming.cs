namespace HYWDebugAssistant.Sdk.Timing;

/// <summary>
/// **模块全局计时面**：所有模块的等待/周期节拍都通过它，模块不得自建定时器或线程等待。
/// </summary>
/// <remarks>
/// <para>
/// 时间基准是单调时钟（等价于 <c>Stopwatch</c>），不是墙钟；<see cref="Now"/> 只用于计算间隔。
/// </para>
/// <para>
/// 框架实现是进程级唯一的 <c>GlobalTimerService</c>：一个调度线程 + 一个高精度平台计时器；
/// 模块线程只等待自己的信号，不在模块里创建 OS 定时器。
/// </para>
/// </remarks>
public interface IModuleTiming
{
    /// <summary>当前单调时间。</summary>
    TimeSpan Now { get; }

    /// <summary>等待到绝对截止时刻（单调时间）。</summary>
    /// <param name="deadline">截止时刻（由 <see cref="Now"/> 推导）。</param>
    /// <returns>已请求停止/取消时为 <see langword="true"/>。</returns>
    bool WaitUntil(TimeSpan deadline);

    /// <summary>等待一段相对时间。</summary>
    /// <param name="delay">等待时长；≤0 表示只让出一次时间片。</param>
    /// <returns>已请求停止/取消时为 <see langword="true"/>。</returns>
    bool WaitFor(TimeSpan delay);
}
