using HYWDebugAssistant.Sdk.Timing;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// 测试用定时器集合：**不占线程、不依赖真实时间**，由测试驱动推进。
/// </summary>
/// <remarks>
/// 模块行为测试用 <see cref="AdvanceTo"/> 模拟"时间到了"；生产实现走框架计时服务，
/// 二者对模块暴露的 <see cref="IModuleTimer"/> 语义相同（Start/Stop/Interval/Tick）。
/// </remarks>
public sealed class TestModuleTimers : IModuleTimers
{
    private readonly List<TestModuleTimer> _timers = [];

    /// <summary>当前虚拟时刻。</summary>
    public TimeSpan Now { get; private set; }

    /// <summary>全部定时器（快照）。</summary>
    public IReadOnlyList<TestModuleTimer> Timers => _timers;

    /// <summary>最近一个启用定时器的到期时刻（没有启用项 ⇒ null）。</summary>
    public TimeSpan? NextDue
    {
        get
        {
            TimeSpan? next = null;

            foreach (var timer in _timers)
            {
                if (timer.IsEnabled && (next is null || timer.DueAt < next))
                {
                    next = timer.DueAt;
                }
            }

            return next;
        }
    }

    /// <inheritdoc />
    public IModuleTimer Create()
    {
        var timer = new TestModuleTimer(this);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>把虚拟时间推进到 <paramref name="now"/>，触发所有到期 Tick。</summary>
    /// <param name="now">目标时刻（不得小于当前时刻）。</param>
    public void AdvanceTo(TimeSpan now)
    {
        if (now < Now)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Test clock cannot move backwards");
        }

        Now = now;

        foreach (var timer in _timers.ToArray())
        {
            timer.AdvanceTo(now);
        }
    }

    /// <summary>删除一个已释放的定时器。</summary>
    internal void Remove(TestModuleTimer timer) => _ = _timers.Remove(timer);
}

/// <summary>测试用定时器：由 <see cref="TestModuleTimers.AdvanceTo"/> 触发 Tick。</summary>
public sealed class TestModuleTimer : IModuleTimer
{
    private readonly TestModuleTimers _owner;
    private TimeSpan _interval = TimeSpan.FromMilliseconds(100);
    private bool _enabled;
    private bool _disposed;

    internal TestModuleTimer(TestModuleTimers owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <inheritdoc />
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Timer interval must be greater than zero");
            }

            _interval = value;

            if (_enabled)
            {
                DueAt = _owner.Now + value;
            }
        }
    }

    /// <inheritdoc />
    public bool IsEnabled => _enabled;

    /// <summary>下一次到期（仅测试读取）。</summary>
    public TimeSpan DueAt { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Tick;

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_enabled)
        {
            return;
        }

        _enabled = true;
        DueAt = _owner.Now + _interval;
    }

    /// <inheritdoc />
    public void Stop()
    {
        _enabled = false;
    }

    /// <summary>立即触发一次 Tick（测试用；不改变下一次到期）。</summary>
    public void FireNow()
    {
        if (_enabled && !_disposed)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>推进到指定时刻，逐个触发到期。</summary>
    internal void AdvanceTo(TimeSpan now)
    {
        var guard = 0;

        while (_enabled && !_disposed && DueAt <= now)
        {
            // 防止测试时钟一次跳太远时把死循环带进测试；正常用例不会接近这个上限。
            if (++guard > 10_000)
            {
                throw new InvalidOperationException("Too many timer ticks in one test-clock advance");
            }

            DueAt += _interval;
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _enabled = false;
        Tick = null;
        _owner.Remove(this);
    }
}
