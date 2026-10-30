using System.Diagnostics;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.DataPlane.Queues;

/// <summary>队列满时的丢弃方式（**承载参数，全局设置**；不是每个口各写一份的声明）。</summary>
public enum DropPolicy
{
    /// <summary>丢最旧保最新（实时场景默认）。</summary>
    DropOldest = 0,

    /// <summary>丢最新（保留已有积压）。</summary>
    DropNewest = 1,
}

/// <summary>
/// 入站数据单元：缓冲 + **目标口名** + 可选**薄旁路头**。
/// </summary>
/// <remarks>
/// 队列是**每 (实例, 口) 一条**，元素仍携带目标口名——消费侧（投递循环/主动接收）
/// 需要它来把缓冲交给正确的接收处理，日志与诊断也据此归属。
/// <see cref="Header"/> **只在多源网络上非空**（单源网络零附加）；
/// 它是**旁路元数据**，不进 <see cref="PayloadBuffer"/> 的字节流。
/// </remarks>
/// <param name="Buffer">数据单元缓冲（所有权随入队移交给队列/消费方）。</param>
/// <param name="TargetPort">目标接收口名。</param>
/// <param name="Header">薄旁路头（**多源网络**才有；单源网络为 <see langword="null"/>）。</param>
public readonly record struct InboundUnit(PayloadBuffer Buffer, string TargetPort, DatagramHeader? Header = null)
{
    /// <summary>
    /// **入队时刻**（<see cref="Stopwatch"/> 计时刻度；0 = 未打点 ⇒ 视为刚入队，永不过期）。
    /// </summary>
    /// <remarks>
    /// 由队列在**入队时的锁内**统一打点。过期判据要的是"这个单元在队列里等了多久"，
    /// 让每个生产者各自打点等于把同一件事的事实分散到每个调用点（迟早有人漏打）。
    /// </remarks>
    public long EnqueuedTicks { get; init; }
}

/// <summary>
/// 有界输入队列：**绝不阻塞发送方**，满时按策略丢弃并计数；丢弃**按原因记账**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>**粒度 = 每 (实例, 口)**：容量按口计，跨口互不挤占（无跨口队头阻塞）；</item>
///   <item>生产者：任意线程（投递方）；消费者：该实例的专属线程（投递循环或模块活动体，二者互斥）；</item>
///   <item>满队时**丢哪一种**由 <see cref="DropPolicy"/> 决定（丢旧挤队头 / 丢新拒本次）；
///     两种都记进原因账本，读 <see cref="DroppedByReason"/> 可分辨；</item>
///   <item>丢弃的缓冲**由调用方归还池**（本类不持有池，避免锁内调用外部代码）；</item>
///   <item>饱和压力由 <see cref="QueuePressure"/> 统一给出（判据、迟滞都在那一处，
///     本类只在自己的持锁路径上喂它深度与丢弃）；</item>
///   <item>v1 用锁保证正确性；无锁/批量优化属后续优化项。</item>
/// </list>
/// </remarks>
public sealed class InboundBuffer
{
    private static readonly int ReasonCount = Enum.GetValues<DropReason>().Length;

    private readonly Queue<InboundUnit> _queue;
    private readonly Lock _gate = new();
    private readonly QueuePressure _pressure;
    private readonly long[] _droppedUnitsByReason = new long[ReasonCount];
    private readonly long[] _droppedBytesByReason = new long[ReasonCount];
    private long _enqueued;
    private long _dropped;
    private long _droppedBytes;
    private long _droppedUnitsTotal;
    private long _droppedBytesTotal;
    private long _lastDropTicks = -1;
    private int _lastDropReason = -1;

    /// <summary>创建有界队列。</summary>
    /// <param name="capacity">容量（单元数）。</param>
    /// <param name="policy">满时丢弃方式。</param>
    /// <param name="hintDepth">饱和提示的深度阈值（单元数）。</param>
    /// <param name="hintHold">持续多久算饱和（<see langword="null"/> ⇒ 缺省值）。</param>
    /// <remarks>
    /// **不受理"最大积压年龄"**：过期单元要连同缓冲一起丢掉，而本类**不持有池**
    /// （丢弃必须由能把缓冲归还的那一方做）。口队列的过期由承载的投递内层判定。
    /// </remarks>
    public InboundBuffer(
        int capacity,
        DropPolicy policy = DropPolicy.DropOldest,
        int hintDepth = QueuePressure.DefaultHintDepth,
        TimeSpan? hintHold = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        Policy = policy;
        _queue = new Queue<InboundUnit>(capacity);

        // 提示阈值**夹到容量**：阈值大于容量时判据退化为"满"（否则队列永远到不了阈值 ⇒ 永不饱和）
        _pressure = new QueuePressure(
            Math.Clamp(hintDepth, 1, capacity),
            hintHold ?? QueuePressure.DefaultHintHold);
    }

    /// <summary>把"最大积压年龄"折成 <see cref="Stopwatch"/> 计时刻度（0 / 负值 ⇒ 0 = 不限）。</summary>
    /// <param name="age">最大积压年龄。</param>
    internal static long ToTicks(TimeSpan? age) =>
        age is { } value && value > TimeSpan.Zero
            ? (long)(value.TotalSeconds * Stopwatch.Frequency)
            : 0;

    /// <summary>容量（单元数）。</summary>
    public int Capacity { get; }

    /// <summary>满时丢弃方式。</summary>
    public DropPolicy Policy { get; }

    /// <summary>当前深度。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>累计入队数（含被后续丢弃的）。</summary>
    public long Enqueued => Interlocked.Read(ref _enqueued);

    /// <summary>累计丢弃数（**满队丢弃**：挤旧 + 拒新；停止清尾与模块内部丢弃不计入这里）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>累计丢弃字节数（**满队丢弃**：= 契约尺寸 × 被挤/被拒的单元数）。</summary>
    public long DroppedBytes => Interlocked.Read(ref _droppedBytes);

    /// <summary>最近一次丢弃的原因（从未丢弃为 <see langword="null"/>）。</summary>
    public DropReason? LastDropReason
    {
        get
        {
            var reason = Volatile.Read(ref _lastDropReason);
            return reason < 0 ? null : (DropReason)reason;
        }
    }

    /// <summary>某原因的累计丢弃单元数。</summary>
    /// <param name="reason">丢弃原因。</param>
    /// <returns>该原因的累计单元数。</returns>
    public long DroppedByReason(DropReason reason) => Interlocked.Read(ref _droppedUnitsByReason[(int)reason]);

    /// <summary>某原因的累计丢弃字节数（= 契约尺寸 × 单元数）。</summary>
    /// <param name="reason">丢弃原因。</param>
    /// <returns>该原因的累计字节数。</returns>
    public long DroppedBytesByReason(DropReason reason) => Interlocked.Read(ref _droppedBytesByReason[(int)reason]);

    /// <summary>入队；满时按策略丢弃。丢弃的缓冲经 <paramref name="dropped"/> 返回，**由调用方归还池**。</summary>
    /// <returns>本次入队是否成功（丢新策略下满队时返回 <see langword="false"/>）。</returns>
    public bool TryEnqueue(InboundUnit unit, out PayloadBuffer? dropped)
    {
        dropped = null;

        lock (_gate)
        {
            // 入队时刻在锁内统一打点：过期判据读的是"在队列里等了多久"
            unit = unit with { EnqueuedTicks = Stopwatch.GetTimestamp() };

            if (_queue.Count < Capacity)
            {
                _queue.Enqueue(unit);
                Interlocked.Increment(ref _enqueued);
                _pressure.OnEnqueued(_queue.Count);
                return true;
            }

            if (Policy == DropPolicy.DropNewest)
            {
                dropped = unit.Buffer;      // 丢弃新来的
                RecordDropLocked(DropReason.QueueFullRejected, 1, unit.Buffer.Length, countAsFullDrop: true);
                _pressure.OnDropped();
                return false;
            }

            var evicted = _queue.Dequeue().Buffer;   // 丢最旧
            dropped = evicted;
            _queue.Enqueue(unit);
            Interlocked.Increment(ref _enqueued);
            RecordDropLocked(DropReason.QueueFullEvicted, 1, evicted.Length, countAsFullDrop: true);
            _pressure.OnEnqueued(_queue.Count);
            _pressure.OnDropped();
            return true;
        }
    }

    /// <summary>出队（非阻塞）。</summary>
    public bool TryDequeue(out InboundUnit unit)
    {
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                unit = _queue.Dequeue();
                _pressure.OnDequeued(_queue.Count);
                return true;
            }
        }

        unit = default;
        return false;
    }

    /// <summary>清空队列，把其中的缓冲交给 <paramref name="onBuffer"/>（锁外调用）。</summary>
    /// <remarks>普通清空（拆除路径）**不记原因账本**；要按"停止清尾"记账请用 <see cref="Purge"/>。</remarks>
    public void Drain(Action<PayloadBuffer> onBuffer)
    {
        ArgumentNullException.ThrowIfNull(onBuffer);

        List<PayloadBuffer> buffers;
        lock (_gate)
        {
            buffers = new List<PayloadBuffer>(_queue.Count);
            while (_queue.Count > 0)
            {
                buffers.Add(_queue.Dequeue().Buffer);
            }

            _pressure.OnDequeued(0);
        }

        foreach (var buffer in buffers)
        {
            onBuffer(buffer);
        }
    }

    /// <summary>
    /// 清空队列并把每个单元计入 <see cref="DropReason.DiscardedOnStop"/>（单元 + 字节），
    /// 缓冲交给 <paramref name="onBuffer"/>（**锁外调用**）。
    /// </summary>
    /// <param name="onBuffer">接收缓冲的回调（通常归还池）。</param>
    public void Purge(Action<PayloadBuffer> onBuffer)
    {
        ArgumentNullException.ThrowIfNull(onBuffer);

        List<PayloadBuffer> buffers;
        lock (_gate)
        {
            buffers = new List<PayloadBuffer>(_queue.Count);
            while (_queue.Count > 0)
            {
                var unit = _queue.Dequeue();
                RecordDropLocked(DropReason.DiscardedOnStop, 1, unit.Buffer.Length, countAsFullDrop: false);
                buffers.Add(unit.Buffer);
            }

            // 队列空了就没有压力：不把"已经堵了多久"留给下一个使用者
            _pressure.OnDequeued(0);
        }

        foreach (var buffer in buffers)
        {
            onBuffer(buffer);
        }
    }

    /// <summary>
    /// 读本队列的流动状态（深度 / 峰值 / 丢弃账本 / 饱和）。
    /// </summary>
    /// <param name="state">流动状态。</param>
    /// <returns>本队列总能给出状态（返回 <see langword="false"/> 的情形是"这个口没有队列"，由调用方判定）。</returns>
    public bool TryGetFlowState(out PortFlowState state)
    {
        var now = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            var saturated = _pressure.IsSaturated(now);
            var lastDropTicks = Interlocked.Read(ref _lastDropTicks);

            state = new PortFlowState(
                _queue.Count,
                Capacity,
                _pressure.PeakDepth,
                Interlocked.Read(ref _enqueued),
                Interlocked.Read(ref _droppedUnitsTotal),
                Interlocked.Read(ref _droppedBytesTotal),
                _pressure.DroppedWhileSaturated,
                saturated,
                saturated ? _pressure.SaturatedFor(now) : TimeSpan.Zero,
                LastDropReason,
                lastDropTicks < 0 ? null : Stopwatch.GetElapsedTime(lastDropTicks, now));
        }

        return true;
    }

    /// <summary>记一次**模块内部**丢弃（只接受 <see cref="DropReason.ModuleInternal"/> 的口径）。</summary>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数（= 契约尺寸 × 单元数）。</param>
    public void RecordModuleLoss(long units, long bytes) =>
        RecordDrop(DropReason.ModuleInternal, units, bytes);

    /// <summary>记一次**过期**丢弃（单元在队列里停留过久，被清出）。</summary>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数（= 契约尺寸 × 单元数）。</param>
    public void RecordExpired(long units, long bytes) =>
        RecordDrop(DropReason.Expired, units, bytes);

    /// <summary>改饱和压力策略（改阈值即重开压力段；队列内容不动）。</summary>
    /// <param name="hintDepth">深度提示阈值（单元数）。</param>
    /// <param name="hintHold">持续多久算饱和。</param>
    /// <remarks>阈值**夹到容量**：大于容量时饱和判据退化为"满"，与构造时同一口径。</remarks>
    public void UpdateFlowPolicy(int hintDepth, TimeSpan hintHold)
    {
        lock (_gate)
        {
            _pressure.UpdateHint(Math.Clamp(hintDepth, 1, Capacity), hintHold);
        }
    }

    private void RecordDrop(DropReason reason, long units, long bytes)
    {
        if (units <= 0 && bytes <= 0)
        {
            return;
        }

        lock (_gate)
        {
            RecordDropLocked(reason, units, bytes, countAsFullDrop: false);
        }
    }

    /// <summary>
    /// 记一笔丢弃（**调用方必须持有 <see cref="_gate"/>**：原因账本与"最近一次丢弃"必须同拍落地，
    /// 否则读到的原因与时刻会来自两次不同的丢弃）。
    /// </summary>
    /// <param name="reason">丢弃原因。</param>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数。</param>
    /// <param name="countAsFullDrop">是否并入"满队丢弃"合计（<see cref="Dropped"/> / <see cref="DroppedBytes"/>）。</param>
    private void RecordDropLocked(DropReason reason, long units, long bytes, bool countAsFullDrop)
    {
        if (units > 0)
        {
            Interlocked.Add(ref _droppedUnitsByReason[(int)reason], units);
            Interlocked.Add(ref _droppedUnitsTotal, units);
        }

        if (bytes > 0)
        {
            Interlocked.Add(ref _droppedBytesByReason[(int)reason], bytes);
            Interlocked.Add(ref _droppedBytesTotal, bytes);
        }

        if (countAsFullDrop)
        {
            if (units > 0)
            {
                Interlocked.Add(ref _dropped, units);
            }

            if (bytes > 0)
            {
                Interlocked.Add(ref _droppedBytes, bytes);
            }
        }

        Volatile.Write(ref _lastDropReason, (int)reason);
        Interlocked.Exchange(ref _lastDropTicks, Stopwatch.GetTimestamp());
    }
}
