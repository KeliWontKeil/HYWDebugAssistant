using System.Diagnostics;

namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// **进程级唯一计时服务**：一个调度线程 + 一个平台计时后端，所有模块的等待都挂在这里。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>模块线程只等待自己的 <see cref="Waiter"/> 信号，不在模块里创建 Timer / Sleep 调度；</item>
///   <item>调度线程维护最早 deadline；平台后端负责精确唤醒；</item>
///   <item>多个等待者共享一个平台计时器，1 ms 与低频等待不会再各建一套机制。</item>
/// </list>
/// </remarks>
internal sealed class GlobalTimerService : IDisposable
{
    private static readonly Lazy<GlobalTimerService> SharedLazy =
        new(static () => new GlobalTimerService(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly object _gate = new();
    private readonly PriorityQueue<Waiter, long> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly IPlatformTimer _platform;
    private readonly Thread _thread;
    private readonly long _start = Stopwatch.GetTimestamp();
    private bool _disposed;

    private GlobalTimerService()
    {
        IPlatformTimer fallback = new ManagedPlatformTimer();
        _platform = OperatingSystem.IsWindows()
            ? new WindowsPlatformTimer(fallback)
            : fallback;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HYW.GlobalTimer",
        };
        _thread.Start();
    }

    /// <summary>进程共享实例。</summary>
    public static GlobalTimerService Shared => SharedLazy.Value;

    /// <summary>单调当前时间。</summary>
    public TimeSpan Now =>
        TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency);

    /// <summary>在单调截止时刻调用一次回调；返回取消句柄。</summary>
    /// <param name="deadline">截止时刻。</param>
    /// <param name="callback">到期回调（在计时线程调用，必须短小且不抛）。</param>
    /// <returns>取消句柄。</returns>
    public IDisposable Schedule(TimeSpan deadline, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var deadlineTicks = _start + (long)(deadline.TotalSeconds * Stopwatch.Frequency);
        var waiter = new Waiter(deadlineTicks, callback);

        if (deadlineTicks <= Stopwatch.GetTimestamp())
        {
            _ = ThreadPool.QueueUserWorkItem(static state =>
            {
                var (target, action) = ((Waiter, Action))state!;

                if (!target.IsCancelled)
                {
                    Invoke(action);
                }
            }, (waiter, callback));

            return waiter;
        }

        lock (_gate)
        {
            _queue.Enqueue(waiter, waiter.Deadline);
        }

        _wake.Set();
        return waiter;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CompleteAll();
        }

        _wake.Set();
        _ = _thread.Join(TimeSpan.FromSeconds(1));
        _wake.Dispose();
        _platform.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            long deadline;
            var empty = false;

            lock (_gate)
            {
                if (_disposed)
                {
                    CompleteAll();
                    return;
                }

                while (_queue.TryPeek(out var waiter, out var due) && waiter.IsCancelled)
                {
                    _ = _queue.Dequeue();
                }

                if (_queue.Count == 0)
                {
                    empty = true;
                    deadline = 0;
                }
                else
                {
                    deadline = _queue.Peek().Deadline;
                }
            }

            if (empty)
            {
                _ = _wake.WaitOne();
                continue;
            }

            if (deadline <= Stopwatch.GetTimestamp())
            {
                CompleteDue();
                continue;
            }

            var woken = _platform.WaitUntil(deadline, _wake);
            if (!woken)
            {
                CompleteDue();
            }
        }
    }

    private readonly List<Action> _dueCallbacks = [];

    private void CompleteDue()
    {
        lock (_gate)
        {
            _dueCallbacks.Clear();

            var now = Stopwatch.GetTimestamp();
            while (_queue.TryPeek(out var waiter, out var due) && due <= now)
            {
                _ = _queue.Dequeue();
                if (waiter.IsCancelled)
                {
                    continue;
                }

                if (waiter.Callback is { } callback)
                {
                    _dueCallbacks.Add(callback);
                }
            }
        }

        for (var index = 0; index < _dueCallbacks.Count; index++)
        {
            Invoke(_dueCallbacks[index]);
        }

        _dueCallbacks.Clear();
    }

    private static void Invoke(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            // 计时线程不承载模块代码；回调只许投信号。异常必须可见，不能吞掉后让计时线程静默失步。
            System.Diagnostics.Trace.TraceError("timer callback failed: {0}", exception);
        }
    }

    private void CompleteAll()
    {
        while (_queue.TryDequeue(out var waiter, out _))
        {
            waiter.Cancel();
        }
    }

    private sealed class Waiter : IDisposable
    {
        private int _cancelled;

        public Waiter(long deadline, Action? callback = null)
        {
            Deadline = deadline;
            Callback = callback;
        }

        public long Deadline { get; }

        public Action? Callback { get; }

        public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

        public void Cancel() => _ = Interlocked.Exchange(ref _cancelled, 1);

        public void Dispose() => Cancel();
    }
}
