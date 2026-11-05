using HYWDebugAssistant.Sdk.Timing;

namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// 一个模块实例的**定时器集合**：模块只通过 <see cref="IModuleTimers"/> 创建定时器；
/// 框架负责把 Tick 投回实例线程、在禁用时暂停、在移除时统一释放。
/// </summary>
/// <remarks>
/// <para>
/// 本类**不执行模块代码**：所有到期回调只调用 <c>Post</c>，真正的 <see cref="IModuleTimer.Tick"/>
/// 在 <see cref="DataPlane.Carriers.ModuleCarrier"/> 的 posted-work 队列里、由实例线程执行。
/// </para>
/// <para>
/// 生命周期：实例起步时处于暂停态；<see cref="Attach"/> 只接上投递入口，
/// 由运行时在生命周期钩子完成后调用 <see cref="Resume"/>。禁用/移除分别走
/// <see cref="Suspend"/> 与 <see cref="Dispose"/>。
/// </para>
/// </remarks>
public sealed class ModuleTimerSet : IModuleTimers, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<ModuleTimer> _timers = [];
    private Func<Action, bool>? _post;
    private Action<Exception>? _report;
    private bool _suspended = true;
    private bool _disposed;

    /// <inheritdoc />
    public IModuleTimer Create()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var timer = new ModuleTimer(this);
            _timers.Add(timer);
            return timer;
        }
    }

    /// <summary>
    /// 接上实例线程的投递入口与失败上报口（框架内部；模块作者不使用）。
    /// </summary>
    /// <param name="post">把动作排到实例线程的非阻塞入口；返回是否已排入。</param>
    /// <param name="report">Tick 异常的上报口。</param>
    public void Attach(Func<Action, bool> post, Action<Exception> report)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            _post = post;
            _report = report;
        }
    }

    /// <summary>暂停全部定时器（禁用实例时）；保留每个定时器的启用意图。</summary>
    public void Suspend()
    {
        ModuleTimer[] timers;

        lock (_gate)
        {
            if (_suspended || _disposed)
            {
                return;
            }

            _suspended = true;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.Suspend();
        }
    }

    /// <summary>恢复全部定时器（重新启用实例时）；从恢复时刻重新计一个间隔。</summary>
    public void Resume()
    {
        ModuleTimer[] timers;

        lock (_gate)
        {
            if (!_suspended || _disposed)
            {
                return;
            }

            _suspended = false;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.Resume();
        }
    }

    /// <summary>把一次 Tick 派发排到实例线程（计时线程调用，**不阻塞**）。</summary>
    internal bool Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Func<Action, bool>? post;

        lock (_gate)
        {
            if (_suspended || _disposed)
            {
                return false;
            }

            post = _post;
        }

        return post?.Invoke(action) ?? false;
    }

    /// <summary>上报一次 Tick 异常（实例线程调用）。</summary>
    internal void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Action<Exception>? report;

        lock (_gate)
        {
            report = _report;
        }

        report?.Invoke(exception);
    }

    /// <summary>定时器自行释放时从集合移除。</summary>
    internal void Remove(ModuleTimer timer)
    {
        lock (_gate)
        {
            _ = _timers.Remove(timer);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ModuleTimer[] timers;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timers = [.. _timers];
            _timers.Clear();
            _post = null;
            _report = null;
        }

        foreach (var timer in timers)
        {
            timer.Dispose();
        }
    }
}

/// <summary>
/// 一个真实的模块定时器：**只负责周期**，不执行模块代码。
/// </summary>
/// <remarks>
/// 排期使用框架计时服务；到期回调只把 Tick 投回实例线程并安排下一拍。
/// 下一拍保留相位：上一到期 + 间隔，落后超过整拍时按整拍跳到未来，避免每拍把
/// “这一次晚到的时间”累加进周期，也避免补发风暴。
/// </remarks>
internal sealed class ModuleTimer : IModuleTimer
{
    private readonly ModuleTimerSet _set;
    private readonly Lock _gate = new();

    private TimeSpan _interval = TimeSpan.FromMilliseconds(100);
    private bool _enabled;
    private bool _suspended;
    private bool _disposed;
    private bool _tickPending;
    private long _generation;
    private long _dispatchToken;
    private IDisposable? _pending;

    public ModuleTimer(ModuleTimerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _set = set;
    }

    /// <inheritdoc />
    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }

        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), HostingText.Get("hosting.timer.intervalPositive"));
            }

            lock (_gate)
            {
                if (_disposed || _interval == value)
                {
                    return;
                }

                _interval = value;
                InvalidateDispatchLocked();

                if (_enabled && !_suspended)
                {
                    ScheduleLocked();
                }
            }
        }
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Tick;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_enabled)
            {
                return;
            }

            _enabled = true;
            InvalidateDispatchLocked();

            if (!_suspended)
            {
                ScheduleLocked();
            }
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = false;
            InvalidateDispatchLocked();
            CancelLocked();
        }
    }

    /// <summary>实例禁用时暂停排期；保留启用意图。</summary>
    internal void Suspend()
    {
        lock (_gate)
        {
            if (_suspended || _disposed)
            {
                return;
            }

            _suspended = true;
            InvalidateDispatchLocked();
            CancelLocked();
        }
    }

    /// <summary>实例恢复时重新排期；从恢复时刻重新计一个间隔。</summary>
    internal void Resume()
    {
        lock (_gate)
        {
            if (!_suspended || _disposed)
            {
                return;
            }

            _suspended = false;
            InvalidateDispatchLocked();

            if (_enabled)
            {
                ScheduleLocked();
            }
        }
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
            _enabled = false;
            InvalidateDispatchLocked();
            CancelLocked();
            Tick = null;
        }

        _set.Remove(this);
    }

    /// <summary>
    /// 纯函数：把已经过去的到期推进到未来，保留相位。
    /// </summary>
    /// <param name="due">原到期时刻。</param>
    /// <param name="interval">间隔（必须为正）。</param>
    /// <param name="now">当前时刻。</param>
    /// <returns>未来最近的一个同相位到期。</returns>
    internal static TimeSpan SkipToFuture(TimeSpan due, TimeSpan interval, TimeSpan now)
    {
        if (interval <= TimeSpan.Zero || due > now)
        {
            return due;
        }

        var behind = now.Ticks - due.Ticks;
        var steps = (behind / interval.Ticks) + 1;
        return due + TimeSpan.FromTicks(steps * interval.Ticks);
    }

    private void ScheduleLocked()
    {
        CancelLocked();

        var due = FrameworkTimer.Shared.Now + _interval;
        var generation = ++_generation;
        _pending = FrameworkTimer.Shared.Schedule(
            due,
            () => OnDue(generation, due));
    }

    private void OnDue(long generation, TimeSpan due)
    {
        Action? dispatch = null;
        long dispatchToken = 0;

        lock (_gate)
        {
            if (generation != _generation || _disposed || !_enabled || _suspended)
            {
                return;
            }

            _pending = null;

            if (!_tickPending)
            {
                _tickPending = true;
                dispatchToken = _dispatchToken;
                dispatch = () => Dispatch(dispatchToken);
            }

            // 下一拍从“上一到期 + 间隔”推进；落后时按整拍跳过，不补发。
            var next = SkipToFuture(
                due + _interval,
                _interval,
                FrameworkTimer.Shared.Now);
            var nextGeneration = ++_generation;
            _pending = FrameworkTimer.Shared.Schedule(
                next,
                () => OnDue(nextGeneration, next));
        }

        if (dispatch is not null && !_set.Post(dispatch))
        {
            // 实例线程此刻暂停/已停止：排不进去就把 pending 清掉，恢复后下一拍还能再派发。
            lock (_gate)
            {
                if (_dispatchToken == dispatchToken)
                {
                    _tickPending = false;
                }
            }
        }
    }

    private void Dispatch(long token)
    {
        lock (_gate)
        {
            if (_disposed || token != _dispatchToken || !_tickPending)
            {
                return;
            }

            _tickPending = false;

            if (!_enabled || _suspended)
            {
                return;
            }
        }

        try
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            try
            {
                _set.Report(exception);
            }
            finally
            {
                Stop();
            }
        }
    }

    private void InvalidateDispatchLocked()
    {
        _dispatchToken++;
        _tickPending = false;
    }

    private void CancelLocked()
    {
        _generation++;
        _pending?.Dispose();
        _pending = null;
    }
}
