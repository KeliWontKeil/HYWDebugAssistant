using Avalonia.Threading;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **叠加信息层的共享动画时钟**【ADR-0045 · 第十五轮】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么共享</b>：叠加层的"流动虚线"要随时间推进，但**不能**每个模块窗口各起一个定时器
///     （N 个窗口 = N 条时间线，还可能因相位不同看起来各不相同）。这里只维护**一个** 20 Hz 时钟，
///     所有**可见**的 <see cref="ConnectionOverlay"/> 共用同一个 tick ⇒ 全屏虚线**同相流动**；</item>
///   <item><b>什么时候不跑</b>：叠加层本身是**默认关闭**的调试层（ADR-0045 D1）。关闭时对应的
///     <see cref="ConnectionOverlay"/> 会把自己退订 ⇒ **没有订阅者就不跑表**（不白烧 CPU）；
///     订阅者存在但窗口被隐藏（`IsEffectivelyVisible == false`）时，tick 里**跳过重绘**；</item>
///   <item><b>相位与方向</b>：<see cref="DashOffsets"/> 给出"发送 / 接收"两种方向的虚线偏移量（互为相反数），
///     使虚线**沿数据流方向**流动（发送 = 由模块中心向外、接收 = 由外向内）。该函数是**纯函数**，可单独断言。</item>
/// </list>
/// </remarks>
public static class OverlayAnimationClock
{
    /// <summary>时钟间隔（毫秒）：20 Hz —— 虚线流动足够顺滑，又不至于让每帧重绘成为负担。</summary>
    public const int IntervalMs = 50;

    /// <summary>虚线每 tick 前进的距离（DIP）：20 Hz × 1.2 ≈ 24 DIP/秒。</summary>
    public const double SpeedPerTick = 1.2;

    /// <summary>虚线周期（= 段长 + 间隙），与 <see cref="ConnectionOverlay.DashLength"/> / <see cref="ConnectionOverlay.DashGap"/> 一致。</summary>
    public const double Period = ConnectionOverlay.DashLength + ConnectionOverlay.DashGap;

    private static readonly List<WeakReference<ConnectionOverlay>> Subscribers = [];

    private static DispatcherTimer? _timer;

    /// <summary>已推进的 tick 数（**测试用**：叠加层关闭时它不应增长）。</summary>
    public static int TickCount { get; private set; }

    /// <summary>时钟当前是否在跑（无订阅者时为 <c>false</c>）。</summary>
    public static bool IsRunning => _timer is not null;

    /// <summary>当前订阅者数量（测试用）。</summary>
    public static int SubscriberCount => Prune();

    /// <summary>虚线相位（0 → 周期）。</summary>
    /// <param name="tick">已推进的 tick 数。</param>
    /// <returns>相位（DIP）。</returns>
    public static double DashPhase(int tick) => ((tick * SpeedPerTick) % Period + Period) % Period;

    /// <summary>
    /// 两个方向的虚线偏移量（**互为相反数**）。
    /// </summary>
    /// <remarks>
    /// `DashStyle.Offset` 增大 = 虚线图案整体朝几何**起点**方向移动。本层的几何起点 = **模块中心的类型方块**，
    /// 因此：**接收**口（数据由外向内）用 **正**偏移；**发送**口（由内向外）用 **负**偏移。
    /// </remarks>
    /// <param name="tick">已推进的 tick 数。</param>
    /// <returns>发送 / 接收两种方向的偏移量。</returns>
    public static (double Send, double Receive) DashOffsets(int tick)
    {
        var phase = DashPhase(tick);
        return (-phase, phase);
    }

    /// <summary>订阅（由 <see cref="ConnectionOverlay"/> 在"可见 + 已挂到视觉树"时调用）。</summary>
    /// <param name="overlay">叠加层控件。</param>
    public static void Register(ConnectionOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        if (Subscribers.Any(r => r.TryGetTarget(out var target) && ReferenceEquals(target, overlay)))
        {
            return;
        }

        Subscribers.Add(new WeakReference<ConnectionOverlay>(overlay));
        Start();
    }

    /// <summary>退订（叠加层关闭或离开视觉树时调用；退到空则**停表**）。</summary>
    /// <param name="overlay">叠加层控件。</param>
    public static void Unregister(ConnectionOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        _ = Subscribers.RemoveAll(r => !r.TryGetTarget(out var target) || ReferenceEquals(target, overlay));

        if (Prune() == 0)
        {
            Stop();
        }
    }

    private static void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalMs) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        TickCount++;

        foreach (var reference in Subscribers.ToArray())
        {
            if (reference.TryGetTarget(out var overlay))
            {
                overlay.OnAnimationTick();
            }
        }
    }

    /// <summary>清掉已被回收的弱引用，返回剩余数量。</summary>
    private static int Prune()
    {
        _ = Subscribers.RemoveAll(r => !r.TryGetTarget(out _));
        return Subscribers.Count;
    }
}
