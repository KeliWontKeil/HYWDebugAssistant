namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// **框架计时服务**：单调时钟 + 绝对到期回调；仅供框架内部使用，不对模块暴露。
/// </summary>
/// <remarks>
/// 这是进程级唯一计时实现的公开门面；回调在计时线程执行，必须只做"投一个工作信号"，
/// 不得执行模块代码或阻塞。
/// </remarks>
public sealed class FrameworkTimer
{
    /// <summary>进程共享实例。</summary>
    public static FrameworkTimer Shared { get; } = new();

    private readonly GlobalTimerService _service = GlobalTimerService.Shared;

    private FrameworkTimer()
    {
    }

    /// <summary>单调当前时间（与框架计时服务同一基准）。</summary>
    public TimeSpan Now => _service.Now;

    /// <summary>在绝对时刻调用一次回调；返回取消句柄。</summary>
    /// <param name="due">绝对到期时刻。</param>
    /// <param name="callback">到期回调（计时线程，短小、不抛）。</param>
    /// <returns>取消句柄。</returns>
    public IDisposable Schedule(TimeSpan due, Action callback) => _service.Schedule(due, callback);
}
