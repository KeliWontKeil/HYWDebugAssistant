using HYWDebugAssistant.Hosting.Timing;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Core.Adapters;

/// <summary>
/// 把一个 step 模块的**相对延迟**挂到框架计时服务上；到期只向承载者投一个工作信号。
/// </summary>
/// <remarks>
/// <para>
/// 延迟未变且已有排期 ⇒ 不重排；上一到期刚触发且延迟未变 ⇒ 从上一到期推进（避免每拍处理耗时累积成漂移）；
/// 落后超过一拍 ⇒ 从现在重排（跳过漏拍，不补发突发）。
/// </para>
/// <para>
/// 取消/重排都通过 generation 校验：旧回调即使已在计时线程排队，也不会在重排后再投一次。
/// 本类不执行模块代码。
/// </para>
/// </remarks>
internal sealed class StepDueTimer : IDisposable
{
    private readonly Action<StepReason> _signal;
    private readonly Lock _gate = new();
    private TimeSpan? _lastDelay;
    private TimeSpan? _lastDue;
    private IDisposable? _pending;
    private long _generation;
    private bool _paused;
    private bool _disposed;

    public StepDueTimer(Action<StepReason> signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signal = signal;
    }

    /// <summary>按相对延迟排期；相同延迟且已有排期时不重复排队。</summary>
    public void Arm(TimeSpan? delay)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_paused)
            {
                _lastDelay = delay;
                DisarmLocked();
                _lastDue = null;
                return;
            }

            var same = delay == _lastDelay;

            if (same && _pending is not null)
            {
                return;
            }

            ScheduleLocked(delay, fromNow: !same);
        }
    }

    /// <summary>暂停排期；保留最近一次延迟，恢复时从现在重排。</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _paused = true;
            DisarmLocked();
            _lastDue = null;
        }
    }

    /// <summary>恢复排期（按最近一次延迟从现在重排）。</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _paused = false;
            ScheduleLocked(_lastDelay, fromNow: true);
        }
    }

    /// <summary>取消当前到期。</summary>
    public void Disarm()
    {
        lock (_gate)
        {
            DisarmLocked();
            _lastDue = null;
        }
    }

    private void ScheduleLocked(TimeSpan? delay, bool fromNow)
    {
        DisarmLocked();
        _lastDelay = delay;

        if (delay is not { } value)
        {
            _lastDue = null;
            return;
        }

        if (value <= TimeSpan.Zero)
        {
            // 立即到期：直接在调用线程投工作信号，不经过计时线程/线程池。
            _lastDue = FrameworkTimer.Shared.Now;
            _pending = null;
            _signal(StepReason.Due);
            return;
        }

        var now = FrameworkTimer.Shared.Now;
        var due = !fromNow && _lastDue is { } last ? last + value : now + value;

        if (due <= now)
        {
            due = now + value;
        }

        var generation = ++_generation;
        _lastDue = due;
        _pending = FrameworkTimer.Shared.Schedule(due, () =>
        {
            lock (_gate)
            {
                if (_generation != generation || _disposed)
                {
                    return;
                }

                _pending = null;
                _lastDue = due;
            }

            _signal(StepReason.Due);
        });
    }

    private void DisarmLocked()
    {
        _generation++;
        _pending?.Dispose();
        _pending = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisarmLocked();
            _lastDelay = null;
            _lastDue = null;
        }
    }
}
