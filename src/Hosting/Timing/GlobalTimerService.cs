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

    /// <summary>
    /// 当前线程的**可复用等待信号**。
    /// </summary>
    /// <remarks>
    /// 每次等待都新建 <see cref="ManualResetEventSlim"/> 会在"真的阻塞"时创建一个内核事件；
    /// 1 ms 周期的模块每秒上千次，句柄与终结器压力会持续累积。等待是同步的（同一线程一次只有一个），
    /// 因此按线程复用一个信号即可；它只承载"该醒了"，偶发的迟到 Set 只会让下一轮提前醒一次（无害）。
    /// </remarks>
    [ThreadStatic]
    private static ManualResetEventSlim? t_signal;

    /// <summary>等待到单调截止时刻。</summary>
    /// <param name="deadline">截止时刻。</param>
    /// <param name="cancellation">停止取消令牌。</param>
    /// <returns>已取消为 <see langword="true"/>。</returns>
    public bool WaitUntil(TimeSpan deadline, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            return true;
        }

        var deadlineTicks = _start + (long)(deadline.TotalSeconds * Stopwatch.Frequency);
        if (deadlineTicks <= Stopwatch.GetTimestamp())
        {
            return false;
        }

        var signal = t_signal ??= new ManualResetEventSlim(initialState: false);
        signal.Reset();

        var waiter = new Waiter(deadlineTicks, signal);
        using var registration = cancellation.Register(waiter.Cancel);
        lock (_gate)
        {
            _queue.Enqueue(waiter, waiter.Deadline);
        }

        _wake.Set();
        signal.Wait(CancellationToken.None);

        return waiter.IsCancelled || cancellation.IsCancellationRequested;
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

    private void CompleteDue()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            while (_queue.TryPeek(out var waiter, out var due) && due <= now)
            {
                _ = _queue.Dequeue();
                if (!waiter.IsCancelled)
                {
                    waiter.Signal.Set();
                }
            }
        }
    }

    private void CompleteAll()
    {
        while (_queue.TryDequeue(out var waiter, out _))
        {
            waiter.Cancel();
        }
    }

    private sealed class Waiter
    {
        private int _cancelled;

        public Waiter(long deadline, ManualResetEventSlim signal)
        {
            Deadline = deadline;
            Signal = signal;
        }

        public long Deadline { get; }

        public ManualResetEventSlim Signal { get; }

        public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

        public void Cancel()
        {
            _ = Interlocked.Exchange(ref _cancelled, 1);
            Signal.Set();
        }
    }
}
