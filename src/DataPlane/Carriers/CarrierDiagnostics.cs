namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 承载诊断计数（可观测性 L4）：投递数 / 丢弃数 / 队列深度 / 最后数据时间。
/// </summary>
/// <remarks>
/// 这些数据**只在框架侧**用于诊断呈现（主文档 §5.4.2：框架不把元信息写入数据面）。
/// </remarks>
public sealed class CarrierDiagnostics
{
    private long _enqueued;
    private long _delivered;
    private long _dropped;
    private long _lastDataTicks = -1;
    private long _lastSequence;

    /// <summary>累计入队数。</summary>
    public long Enqueued => Interlocked.Read(ref _enqueued);

    /// <summary>累计投递（交付给模块）数。</summary>
    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>累计丢弃数（队列满）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>最后投递单元的序号（自增；便于测试断言顺序）。</summary>
    public long LastSequence => Interlocked.Read(ref _lastSequence);

    /// <summary>最后收到数据的时间（无数据时为 <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDataTime
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDataTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>记录一次入队。</summary>
    public void RecordEnqueued() => Interlocked.Increment(ref _enqueued);

    /// <summary>记录一次丢弃。</summary>
    public void RecordDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>记录一次投递。</summary>
    public void RecordDelivered()
    {
        Interlocked.Increment(ref _delivered);
        Interlocked.Increment(ref _lastSequence);
        Interlocked.Exchange(ref _lastDataTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
