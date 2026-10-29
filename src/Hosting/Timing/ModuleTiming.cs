using HYWDebugAssistant.Sdk.Timing;

namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>把一个模块的停止令牌与全局计时服务绑定起来。</summary>
internal sealed class ModuleTiming : IModuleTiming
{
    private readonly GlobalTimerService _service;
    private readonly CancellationToken _cancellation;

    public ModuleTiming(GlobalTimerService service, CancellationToken cancellation)
    {
        _service = service;
        _cancellation = cancellation;
    }

    /// <inheritdoc />
    public TimeSpan Now => _service.Now;

    /// <inheritdoc />
    public bool WaitUntil(TimeSpan deadline) => _service.WaitUntil(deadline, _cancellation);

    /// <inheritdoc />
    public bool WaitFor(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            Thread.Yield();
            return _cancellation.IsCancellationRequested;
        }

        return WaitUntil(Now + delay);
    }
}
