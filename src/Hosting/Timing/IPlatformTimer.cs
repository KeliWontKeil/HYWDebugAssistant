namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// 平台计时后端：只回答“等到某个单调截止时刻，或被 wake 唤醒”。
/// </summary>
/// <remarks>
/// 只允许框架计时层实现平台差异；模块层不接触任何平台 API。
/// </remarks>
internal interface IPlatformTimer : IDisposable
{
    /// <summary>
    /// 等待到 <paramref name="deadlineTicks"/>（<see cref="System.Diagnostics.Stopwatch"/> 时基）。
    /// </summary>
    /// <param name="deadlineTicks">绝对截止时刻。</param>
    /// <param name="wake">被新等待者/停止请求唤醒的事件。</param>
    /// <returns>因 <paramref name="wake"/> 提前返回为 <see langword="true"/>；到期为 <see langword="false"/>。</returns>
    bool WaitUntil(long deadlineTicks, WaitHandle wake);
}
