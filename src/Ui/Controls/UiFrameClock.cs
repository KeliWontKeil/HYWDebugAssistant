using System.Diagnostics;
using Avalonia.Threading;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **UI 线程唯一帧时钟**：一个 <see cref="DispatcherTimer"/> 服务所有 UI 订阅与一次性延时。
/// </summary>
/// <remarks>
/// UI 动画/刷新/防抖都依赖 UI 线程；它们不能进模块的全局计时服务（那边是后台等待者）。
/// 但 UI 内部也不允许各起一个小定时器：统一走本时钟，空闲时停表。
/// </remarks>
public static class UiFrameClock
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private static readonly List<Action> Subscribers = [];
    private static readonly List<ScheduledItem> Scheduled = [];

    static UiFrameClock() => Timer.Tick += OnTick;

    /// <summary>当前节拍间隔（与用户的"界面刷新率"设置一致）。</summary>
    public static TimeSpan Interval => Timer.Interval;

    /// <summary>
    /// 把帧节拍对齐到界面的刷新率设置（用户可见的刷新行为只该由这一个口径决定）。
    /// </summary>
    /// <param name="herz">刷新率（Hz；夹到 1–240）。</param>
    /// <remarks>
    /// 帧时钟同时服务模块数据呈现、窗口几何与叠加层动画；它们都按"界面刷新率"这一件事发生，
    /// 因此只在这里改一处。计时器运行中改间隔会停一次再启动，让新节拍立刻生效。
    /// </remarks>
    public static void SetRate(int herz)
    {
        var rate = Math.Clamp(herz, 1, 240);
        var interval = TimeSpan.FromSeconds(1.0 / rate);

        if (Timer.Interval == interval)
        {
            return;
        }

        var running = Timer.IsEnabled;
        Timer.Interval = interval;

        if (running)
        {
            Timer.Stop();
            Timer.Start();
        }
    }

    /// <summary>订阅每个 UI 帧。</summary>
    /// <param name="action">订阅动作。</param>
    public static void Subscribe(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!Subscribers.Contains(action))
        {
            Subscribers.Add(action);
        }

        EnsureRunning();
    }

    /// <summary>退订。</summary>
    /// <param name="action">订阅动作。</param>
    public static void Unsubscribe(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = Subscribers.Remove(action);
        StopIfIdle();
    }

    /// <summary>在 UI 线程上安排一次延时动作。</summary>
    /// <param name="delay">延时。</param>
    /// <param name="action">动作。</param>
    /// <returns>可用于取消的句柄。</returns>
    public static IDisposable ScheduleOnce(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var item = new ScheduledItem(
            Stopwatch.GetTimestamp() + (long)(Math.Max(0, delay.TotalSeconds) * Stopwatch.Frequency),
            action);
        Scheduled.Add(item);
        EnsureRunning();
        return item;
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        foreach (var action in Subscribers.ToArray())
        {
            action();
        }

        var now = Stopwatch.GetTimestamp();
        for (var index = Scheduled.Count - 1; index >= 0; index--)
        {
            var item = Scheduled[index];
            if (item.IsCancelled)
            {
                Scheduled.RemoveAt(index);
                continue;
            }

            if (item.Deadline <= now)
            {
                Scheduled.RemoveAt(index);
                item.Action();
            }
        }

        StopIfIdle();
    }

    private static void EnsureRunning()
    {
        if (!Timer.IsEnabled)
        {
            Timer.Start();
        }
    }

    private static void StopIfIdle()
    {
        if (Subscribers.Count == 0 && Scheduled.Count == 0)
        {
            Timer.Stop();
        }
    }

    private sealed class ScheduledItem : IDisposable
    {
        public ScheduledItem(long deadline, Action action)
        {
            Deadline = deadline;
            Action = action;
        }

        public long Deadline { get; }

        public Action Action { get; }

        public bool IsCancelled { get; private set; }

        public void Dispose()
        {
            IsCancelled = true;
            _ = Scheduled.Remove(this);
            StopIfIdle();
        }
    }
}
