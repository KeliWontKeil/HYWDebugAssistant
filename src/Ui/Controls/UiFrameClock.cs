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
