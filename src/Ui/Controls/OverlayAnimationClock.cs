using System.Diagnostics;
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
///   <item><b>相位与方向</b>：<see cref="DashOffset"/> 给出虚线偏移量；线的几何**恒按数据流方向**绘制
///     ⇒ 两侧同一个偏移量即表现为"发送侧向外流、接收侧向内流"。该函数是**纯函数**，可单独断言。</item>
/// </list>
/// </remarks>
public static class OverlayAnimationClock
{
    /// <summary>
    /// 时钟间隔（毫秒）：**16 ms ≈ 60 Hz**（时间相位下这只是"多久重绘一次"）。
    /// </summary>
    /// <remarks>
    /// 【用户第三十四轮实测反馈："虚线流动**一卡一卡**……像一根匀速旋转的指针，转到 270° 突然跳回 0°"】
    /// 两个问题分开看：① **帧率**（旧实现 20 Hz、每 tick 跳 1.2 px）⇒ 提到 60 Hz；
    /// ② **真正的"跳一下"是相位环绕点算错了**（见 <see cref="Period"/> 的说明）—— 平滑一段后**周期性跳**正是环绕不连续的典型症状。
    /// </remarks>
    public const int IntervalMs = 16;

    /// <summary>虚线**视觉流速**（DIP/秒）。</summary>
    public const double SpeedDipsPerSecond = 24;

    /// <summary>
    /// 虚线**图案的真实周期（DIP）** = （段长 + 间隙）× **线宽**。
    /// </summary>
    /// <remarks>
    /// <b>这是"平滑一段后突然跳一下"的根因</b>：`DashStyle` 的段长/间隙与 `Offset` 都以**笔宽的倍数**为单位
    /// （渲染实测：笔宽 2、段长 6 ⇒ 实际画出 12 DIP），因此**一个完整图案周期 = (段长+间隙) × 线宽**。
    /// 旧实现把周期当成 `段长 + 间隙`（= 10"图案单位"），却又按 DIP 推进、最后除以线宽换算成图案单位
    /// ⇒ 每轮只推进 **10 ÷ 2.5 = 4** 个图案单位就环绕回 0 ⇒ **每次环绕都倒跳 6 个图案单位（15 DIP）**，
    /// 于是"匀速转一会儿 → 突然跳回"。
    /// ⇒ 周期必须与**偏移的单位族**一致：以 DIP 计就是 `(段长+间隙) × 线宽`（本工程 = 25 DIP，约 1.04 秒一轮），
    /// 这样环绕前后**像素完全相同**（偏移恰好等于一个图案周期）。
    /// </remarks>
    public const double Period = (ConnectionOverlay.DashLength + ConnectionOverlay.DashGap) * ConnectionOverlay.LineThickness;

    private static readonly List<WeakReference<ConnectionOverlay>> Subscribers = [];

    private static DispatcherTimer? _timer;

    /// <summary>相位零点（时钟启动时刻）：相位 = 自此刻起的**真实时间** × 流速。</summary>
    private static long _origin;

    /// <summary>时钟当前是否在跑（无订阅者时为 <c>false</c>）。</summary>
    public static bool IsRunning => _timer is not null;

    /// <summary>当前订阅者数量（测试用）。</summary>
    public static int SubscriberCount => Prune();

    /// <summary>**当前相位**（0 → 周期，DIP）——由**真实时间**算出（不依赖 tick 计数）。</summary>
    public static double Phase => PhaseAt(ElapsedSeconds());

    /// <summary>虚线相位（0 → 周期）——**纯函数**，输入是秒数（可单独断言）。</summary>
    /// <param name="seconds">自相位零点起的秒数。</param>
    /// <returns>相位（DIP）。</returns>
    public static double PhaseAt(double seconds) => ((seconds * SpeedDipsPerSecond) % Period + Period) % Period;

    /// <summary>
    /// 虚线偏移量（**两侧共用一个值**）【ADR-0053 · 用户第三十四轮修正】——取**当前相位**。
    /// </summary>
    public static double DashOffset => DashOffsetAt(Phase);

    /// <summary>
    /// 把相位换算成 `DashStyle.Offset`（纯函数，可单独断言）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>约定</b>：<c>DashStyle.Offset</c> 增大 = 虚线图案整体朝几何**起点**方向移动（渲染实测确认）；</item>
    ///   <item><b>为什么两侧同号</b>：线的几何**恒按数据流方向**绘制（见 <c>ConnectionOverlay.LinkSegment</c>：
    ///     **发送** = 方块 → 窗外；**接收** = 窗外 → 方块）⇒ 两侧取同一个偏移量，看起来就自然"一个向外流、一个向内流"；
    ///     上一版按"两侧各自把方块中心当起点"取了**相反数**，于是接收侧看起来也在**向外**流（用户实测指出的方向问题）；</item>
    ///   <item>取**负相位** ⇒ 图案沿几何方向**从起点向终点**前进；</item>
    ///   <item><b>除以线宽</b>：`DashStyle.Offset` 的单位是**笔宽的倍数**（不是 DIP —— 渲染实测：笔宽 2、偏移 +2 ⇒ 位移 4 DIP）。</item>
    /// </list>
    /// </remarks>
    /// <param name="phaseDips">相位（DIP，0 → <see cref="Period"/>；<see cref="Period"/> 恰好等于一个图案周期）。</param>
    /// <returns>虚线偏移量（`DashStyle.Offset` 的单位 = 笔宽倍数）。</returns>
    public static double DashOffsetAt(double phaseDips) => -phaseDips / ConnectionOverlay.LineThickness;

    /// <summary>自相位零点起的秒数。</summary>
    private static double ElapsedSeconds() =>
        _origin == 0 ? 0 : (Stopwatch.GetTimestamp() - _origin) / (double)Stopwatch.Frequency;

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

        _origin = Stopwatch.GetTimestamp();   // 相位零点：从这里开始按**真实时间**算相位
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
        // 相位由**真实时间**给出（见 `Phase`）⇒ 定时器抖动/合并只影响"多久重绘一次"，
        // 不会让相位走慢或跳变（这正是旧实现"一卡一卡"的原因）。
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
