using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Timing;

/// <summary>
/// **模块自持定时器**：一个由模块实例化、由框架调度、Tick 在实例线程触发的周期定时器。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁拥有</b>：模块通过 <see cref="IModuleContext.Timers"/> 创建并持有实例；
///     框架只提供调度能力，不替模块决定"要几个定时器、什么周期"。</item>
///   <item><b>线程</b>：<see cref="Tick"/> 只在该实例的专属线程触发，与 <c>OnStep</c>、生命周期钩子、
///     接收回调同线程，**不并发**；模块不得在 Tick 中阻塞或自建线程。</item>
///   <item><b>生命周期</b>：实例禁用时框架暂停全部定时器；恢复后从现在重新计一个间隔；
///     实例移除时框架统一释放。模块显式 <see cref="Stop"/>/<see cref="IDisposable.Dispose"/> 是幂等且允许的。</item>
///   <item><b>失败</b>：Tick 异常由框架捕获、归到该实例并停掉该定时器，不会每拍刷屏。</item>
/// </list>
/// </remarks>
public interface IModuleTimer : IDisposable
{
    /// <summary>触发间隔（必须大于 <see cref="TimeSpan.Zero"/>）；运行中修改从修改时刻重新计。</summary>
    TimeSpan Interval { get; set; }

    /// <summary>是否处于启用状态（暂停/停止后仍保留模块之前设置的启用意图）。</summary>
    bool IsEnabled { get; }

    /// <summary>到期事件；只在实例线程触发。</summary>
    event EventHandler? Tick;

    /// <summary>启动（已启动时幂等）。第一个 Tick 在一个 <see cref="Interval"/> 之后。</summary>
    void Start();

    /// <summary>停止（已停止时幂等）。</summary>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Start/Stop is the familiar WinForms-style timer API; keeping it is clearer than renaming.")]
    void Stop();
}

/// <summary>
/// **模块定时器工厂**：每个实例一份，创建出来的定时器归属该实例。
/// </summary>
public interface IModuleTimers
{
    /// <summary>创建一个未启动的定时器（默认间隔 100 ms）。</summary>
    IModuleTimer Create();
}

/// <summary>
/// 未接线上下文使用的**空实现**：可以创建，但永不触发 Tick。
/// </summary>
/// <remarks>设计期、没有真实实例线程的测试替身与旧调用点不需要写空判断。</remarks>
public sealed class EmptyModuleTimers : IModuleTimers
{
    /// <summary>共享实例。</summary>
    public static EmptyModuleTimers Instance { get; } = new();

    private EmptyModuleTimers()
    {
    }

    /// <inheritdoc />
    public IModuleTimer Create() => new EmptyModuleTimer();

    private sealed class EmptyModuleTimer : IModuleTimer
    {
        private TimeSpan _interval = TimeSpan.FromMilliseconds(100);

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
            }
        }

        public bool IsEnabled => false;

#pragma warning disable CS0067 // 空实现永不触发
        public event EventHandler? Tick;
#pragma warning restore CS0067

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
