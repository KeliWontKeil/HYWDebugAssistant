using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Core.Binding;

/// <summary>
/// 绑定的诊断计数（可观测性 L4）：投递数 / 丢弃数 / 最后数据时间。
/// </summary>
/// <remarks>这些数据只在框架侧用于诊断呈现，**不进数据面**（主文档 §5.4.2）。</remarks>
public sealed class BindingDiagnostics
{
    private long _delivered;
    private long _dropped;
    private long _lastDataTicks = -1;
    private long _lastSequence;

    /// <summary>累计投递数（成功写入目标队列的次数）。</summary>
    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>累计丢弃数（目标队列满或口停流）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>最后投递序号。</summary>
    public long LastSequence => Interlocked.Read(ref _lastSequence);

    /// <summary>最后数据时间（无数据时为 <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDataTime
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDataTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>记录一次成功投递。</summary>
    public void RecordDelivered()
    {
        Interlocked.Increment(ref _delivered);
        Interlocked.Increment(ref _lastSequence);
        Interlocked.Exchange(ref _lastDataTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>记录一次丢弃。</summary>
    public void RecordDropped() => Interlocked.Increment(ref _dropped);
}

/// <summary>
/// 绑定：**网络在运行时的投影**（主文档 §6.1：Composition ≠ Runtime Binding）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>数据直通</b>：绑定把单元直接写入目标实例的输入队列，Core **不经手数据**（§7.2/P2/E1）；</item>
///   <item><b>1:N 多播</b>：目标列表逐个写入，无转发对象；</item>
///   <item><b>契约</b>：以源口的契约为准（目标契约已在建网时校验为"接收 ⊇ 发送"）；</item>
///   <item><b>生命周期</b>：绑定随网络存在；网络删除即绑定销毁（`Disabled` 时保留但停流，见数据面队列）。</item>
/// </list>
/// </remarks>
public sealed class Binding
{
    /// <summary>创建绑定。</summary>
    /// <param name="network">对应的网络声明。</param>
    /// <param name="layout">源口的契约布局（数据单元的定长形状）。</param>
    public Binding(Network network, ContractLayout layout)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(layout);

        Network = network;
        Layout = layout;
    }

    /// <summary>对应的网络声明。</summary>
    public Network Network { get; }

    /// <summary>数据单元的契约布局。</summary>
    public ContractLayout Layout { get; }

    /// <summary>诊断计数。</summary>
    public BindingDiagnostics Diagnostics { get; } = new();

    /// <summary>停止投递（上游 Disabled 或停流时；绑定保留）。</summary>
    public bool IsSuspended { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Network} [{Layout.SizeInBytes}B]";
}
