using System.Buffers;
using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Tuning;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// **每会话一条有界原始字节队列**：外部接入的数据单元在交给模块前先落这里。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>与口的 <see cref="InboundBuffer"/> 同一套纪律：绝不阻塞生产者，满时按策略丢弃并计数；</item>
///   <item>元素是**原始字节**（不含应用组帧）；组帧/拆帧在模块侧；</item>
///   <item>生产者 = 适配件的异步泵（经实例循环的 <c>TryRead</c>）；消费者 = 实例线程；</item>
///   <item>丢弃**按原因分账**：满队挤旧 / 满队拒新 / 超长 / 过期各有计数，
///     停止清尾单独记（它不是运行期丢弃，而是拆除时队列里剩下的单元）；</item>
///   <item>饱和压力由 <see cref="QueuePressure"/> 统一给出（与口队列同一个算法，不在这里另算一套）；</item>
///   <item>v1 用锁保证正确性；无锁/批量优化属后续优化项。</item>
/// </list>
/// </remarks>
public sealed class AccessInboundBuffer
{
    private readonly Queue<(byte[] Buffer, int Length, long EnqueuedTicks)> _queue;
    private readonly Lock _gate = new();
    private readonly QueuePressure _pressure;
    private readonly StatusHysteresis _status;
    private readonly int _capacity;
    private readonly DropPolicy _policy;
    private long _maxBacklogAgeTicks;
    private long _enqueued;
    private long _dropped;
    private long _droppedBytes;
    private long _evicted;
    private long _evictedBytes;
    private long _rejected;
    private long _rejectedBytes;
    private long _oversized;
    private long _oversizedBytes;
    private long _expired;
    private long _expiredBytes;
    private long _discarded;
    private long _discardedBytes;
    private long _lastDropAtTicks = -1;
    private int _lastDropReason = -1;


    /// <summary>创建有界队列。</summary>
    /// <param name="capacity">容量（单元数）。</param>
    /// <param name="policy">满时丢弃方式。</param>
    /// <param name="hintDepth">饱和提示的深度阈值（单元数）。</param>
    /// <param name="hintHold">持续多久算饱和（<see langword="null"/> ⇒ 缺省值）。</param>
    /// <param name="maxBacklogAge">最大积压年龄（<see langword="null"/> / 0 ⇒ 不限）。</param>
    public AccessInboundBuffer(
        int capacity,
        DropPolicy policy = DropPolicy.DropOldest,
        int hintDepth = QueuePressure.DefaultHintDepth,
        TimeSpan? hintHold = null,
        TimeSpan? maxBacklogAge = null,
        int statusEnterDepth = FlowDefaults.StatusBacklogEnterDepth,
        int statusExitDepth = FlowDefaults.StatusBacklogExitDepth,
        TimeSpan? statusHold = null,
        TimeSpan? statusDropHold = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _policy = policy;
        _queue = new Queue<(byte[] Buffer, int Length, long EnqueuedTicks)>(capacity);
        _maxBacklogAgeTicks = InboundBuffer.ToTicks(maxBacklogAge);

        // 提示阈值**夹到容量**：阈值大于容量时判据退化为"满"（否则队列永远到不了阈值 ⇒ 永不饱和）
        _pressure = new QueuePressure(
            Math.Clamp(hintDepth, 1, capacity),
            hintHold ?? QueuePressure.DefaultHintHold);
        _status = new StatusHysteresis(
            Math.Clamp(statusEnterDepth, 1, capacity),
            Math.Clamp(statusExitDepth, 0, capacity - 1),
            statusHold ?? TimeSpan.FromMilliseconds(FlowDefaults.StatusHoldMs),
            statusDropHold ?? TimeSpan.FromMilliseconds(FlowDefaults.StatusDropHoldMs));
    }

    /// <summary>容量（单元数）。</summary>
    public int Capacity => _capacity;

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

    /// <summary>
    /// 累计丢弃数（**合计**：满队挤旧 + 满队拒新 + 超长 + 过期 + 外部原因上报）。
    /// </summary>
    /// <remarks>
    /// 合计口径回答"这一侧一共没送到多少"；分原因的数见 <see cref="Evicted"/> / <see cref="Rejected"/> /
    /// <see cref="Oversized"/> / <see cref="Expired"/>。**停止清尾**（<see cref="Discarded"/>）**不并入**：
    /// 那是拆除时队列里剩下的单元，不是运行期丢包。
    /// </remarks>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>累计丢弃字节数（与 <see cref="Dropped"/> 同一口径：原始字节数）。</summary>
    public long DroppedBytes => Interlocked.Read(ref _droppedBytes);

    /// <summary>满队**挤掉最旧**的累计单元数。</summary>
    public long Evicted => Interlocked.Read(ref _evicted);

    /// <summary>满队**挤掉最旧**的累计字节数。</summary>
    public long EvictedBytes => Interlocked.Read(ref _evictedBytes);

    /// <summary>满队**拒绝新到**的累计单元数。</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>满队**拒绝新到**的累计字节数。</summary>
    public long RejectedBytes => Interlocked.Read(ref _rejectedBytes);

    /// <summary>**超长**（目标缓冲装不下）的累计单元数（**绝不截断交付**）。</summary>
    public long Oversized => Interlocked.Read(ref _oversized);

    /// <summary>**超长**的累计字节数。</summary>
    public long OversizedBytes => Interlocked.Read(ref _oversizedBytes);

    /// <summary>**过期**（停留过久被清出）的累计单元数。</summary>
    public long Expired => Interlocked.Read(ref _expired);

    /// <summary>**过期**的累计字节数。</summary>
    public long ExpiredBytes => Interlocked.Read(ref _expiredBytes);

    /// <summary>**停止 / 清空时清尾**的累计单元数（不属于运行期丢弃）。</summary>
    public long Discarded => Interlocked.Read(ref _discarded);

    /// <summary>**停止 / 清空时清尾**的累计字节数。</summary>
    public long DiscardedBytes => Interlocked.Read(ref _discardedBytes);

    /// <summary>观察到的最大深度（单元数）。</summary>
    public int PeakDepth
    {
        get
        {
            lock (_gate)
            {
                return _pressure.PeakDepth;
            }
        }
    }

    /// <summary>当前是否处于饱和（深度到达阈值并持续超过保持时长）。</summary>
    public bool Saturated
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                return _pressure.IsSaturated(now);
            }
        }
    }

    /// <summary>已持续饱和的时长（未饱和为 <see cref="TimeSpan.Zero"/>）。</summary>
    public TimeSpan SaturatedFor
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                return _pressure.IsSaturated(now) ? _pressure.SaturatedFor(now) : TimeSpan.Zero;
            }
        }
    }

    /// <summary>最近一次丢弃的原因（从未丢弃为 <see langword="null"/>）。</summary>
    public DropReason? LastDropReason
    {
        get
        {
            var reason = Volatile.Read(ref _lastDropReason);
            return reason < 0 ? null : (DropReason)reason;
        }
    }

    /// <summary>最近一次丢弃发生的时刻（从未丢弃为 <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDropAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDropAtTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>入队；满时按策略丢弃（丢旧 / 丢新）。</summary>
    /// <param name="payload">原始字节（空单元不接受：零长度无法与"无数据"区分）。</param>
    /// <returns>本次是否入队成功。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            // 零长度无法与"无数据"区分：直接拒收。
            // **不记账**：这不是一次"丢弃"（没有单元、没有字节），记进去只会让丢包率凭空多一个分母。
            return false;
        }

        var length = payload.Length;
        var copy = ArrayPool<byte>.Shared.Rent(length);
        payload.CopyTo(copy);

        (byte[] Buffer, int Length, long EnqueuedTicks)? dropped = null;
        bool accepted;

        lock (_gate)
        {
            // 入队时刻在锁内统一打点：过期判据读的是"在队列里等了多久"
            var enqueuedTicks = Stopwatch.GetTimestamp();

            if (_queue.Count < _capacity)
            {
                _queue.Enqueue((copy, length, enqueuedTicks));
                accepted = true;
            }
            else if (_policy == DropPolicy.DropNewest)
            {
                dropped = (copy, length, enqueuedTicks);
                accepted = false;
            }
            else
            {
                dropped = _queue.Dequeue();
                _queue.Enqueue((copy, length, enqueuedTicks));
                accepted = true;
            }

            if (accepted)
            {
                Interlocked.Increment(ref _enqueued);
                _pressure.OnEnqueued(_queue.Count);
            }

            if (dropped is { } evict)
            {
                _status.ObserveDrop(true, enqueuedTicks);
                if (accepted)
                {
                    RecordDropLocked(DropReason.SessionQueueFullEvicted, 1, evict.Length, ref _evicted, ref _evictedBytes);
                }
                else
                {
                    RecordDropLocked(DropReason.SessionQueueFullRejected, 1, evict.Length, ref _rejected, ref _rejectedBytes);
                }

                _pressure.OnDropped();
            }

            _status.ObserveDepth(_queue.Count, enqueuedTicks);
        }

        if (dropped is { } returned)
        {
            ArrayPool<byte>.Shared.Return(returned.Buffer);
        }

        return accepted;
    }

    /// <summary>
    /// 出队一个单元并拷到目标缓冲（非阻塞）。
    /// </summary>
    /// <remarks>
    /// <para>目标缓冲小于单元长度时，该单元被丢弃并计数，返回 <see langword="false"/>（不截断）。</para>
    /// <para>
    /// **过期的队头先清出去**（可连续多个，全部计 <see cref="DropReason.Expired"/>）：迟到的实时数据
    /// 交给消费方只会让界面显示一个已经过期的值。全部过期 ⇒ 返回 <see langword="false"/>。
    /// </para>
    /// </remarks>
    /// <param name="destination">目标缓冲。</param>
    /// <param name="length">实际字节数（成功时）。</param>
    public bool TryDequeue(Span<byte> destination, out int length)
    {
        length = 0;
        (byte[] Buffer, int Length)? payload = null;
        List<byte[]>? expired = null;
        var oversized = false;

        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var maxAge = _maxBacklogAgeTicks;

            // 队头过期就清出去（可能连续多个）：迟到的实时数据交给消费方只会显示一个过期的值
            while (_queue.Count > 0)
            {
                var head = _queue.Peek();

                if (maxAge <= 0 || now - head.EnqueuedTicks <= maxAge)
                {
                    break;
                }

                _ = _queue.Dequeue();
                RecordDropLocked(DropReason.Expired, 1, head.Length, ref _expired, ref _expiredBytes);
                (expired ??= []).Add(head.Buffer);
            }

            if (_queue.Count > 0)
            {
                var head = _queue.Dequeue();
                payload = (head.Buffer, head.Length);

                if (head.Length > destination.Length)
                {
                    oversized = true;
                    RecordDropLocked(DropReason.OversizedUnit, 1, head.Length, ref _oversized, ref _oversizedBytes);
                }
            }

            _pressure.OnDequeued(_queue.Count);
            _status.ObserveDepth(_queue.Count, now);
            _status.ObserveDrop(false, now);
        }

        // 丢弃的数组一律**锁外**归还池：ArrayPool.Return 可能触发清理，不该在锁里做
        if (expired is not null)
        {
            foreach (var buffer in expired)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        if (payload is not { } unit)
        {
            return false;
        }

        if (oversized)
        {
            ArrayPool<byte>.Shared.Return(unit.Buffer);
            return false;
        }

        unit.Buffer.AsSpan(0, unit.Length).CopyTo(destination);
        length = unit.Length;
        ArrayPool<byte>.Shared.Return(unit.Buffer);
        return true;
    }

    /// <summary>
    /// 清空队列并归还全部数组（关闭/释放时调用）；队列里的单元按
    /// <see cref="DropReason.DiscardedOnStop"/> 记一次清尾丢弃。
    /// </summary>
    public void Clear()
    {
        List<byte[]> drained;

        lock (_gate)
        {
            drained = new List<byte[]>(_queue.Count);
            while (_queue.Count > 0)
            {
                var payload = _queue.Dequeue();
                drained.Add(payload.Buffer);
                Interlocked.Increment(ref _discarded);
                Interlocked.Add(ref _discardedBytes, payload.Length);
                Volatile.Write(ref _lastDropReason, (int)DropReason.DiscardedOnStop);
                Interlocked.Exchange(ref _lastDropAtTicks, DateTimeOffset.UtcNow.UtcTicks);
            }

            // 队列空了就没有压力：不把"已经堵了多久"留给下一次打开
            _pressure.OnDequeued(0);
            _status.ObserveDepth(0, Stopwatch.GetTimestamp());
            _status.ObserveDrop(false, Stopwatch.GetTimestamp());
        }

        foreach (var payload in drained)
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>清零丢弃计数（会话的清空计数路径）。</summary>
    /// <remarks>
    /// 队列里的数据与累计入队数不动：清计数不是清数据。
    /// <b>"最近一次丢弃"也要一起消失</b>——否则清完之后读数还说"刚丢过"。
    /// </remarks>
    public void ResetCounters()
    {
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _droppedBytes, 0);
        Interlocked.Exchange(ref _evicted, 0);
        Interlocked.Exchange(ref _evictedBytes, 0);
        Interlocked.Exchange(ref _rejected, 0);
        Interlocked.Exchange(ref _rejectedBytes, 0);
        Interlocked.Exchange(ref _oversized, 0);
        Interlocked.Exchange(ref _oversizedBytes, 0);
        Interlocked.Exchange(ref _expired, 0);
        Interlocked.Exchange(ref _expiredBytes, 0);
        Interlocked.Exchange(ref _discarded, 0);
        Interlocked.Exchange(ref _discardedBytes, 0);
        Volatile.Write(ref _lastDropReason, -1);
        Interlocked.Exchange(ref _lastDropAtTicks, -1);
    }

    /// <summary>记一次**过期**丢弃（单元在队列里停留过久，被清出）。</summary>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数。</param>
    public void RecordExpired(long units, long bytes)
    {
        if (units <= 0 && bytes <= 0)
        {
            return;
        }

        lock (_gate)
        {
            RecordDropLocked(DropReason.Expired, units, bytes, ref _expired, ref _expiredBytes);
        }
    }

    /// <summary>改饱和压力与积压上限策略（改阈值即重开压力段；队列内容不动）。</summary>
    /// <param name="hintDepth">深度提示阈值（单元数）。</param>
    /// <param name="hintHold">持续多久算饱和。</param>
    /// <param name="maxBacklogAge">最大积压年龄（0 ⇒ 不限）。</param>
    /// <remarks>阈值**夹到容量**：大于容量时饱和判据退化为"满"，与构造时同一口径。</remarks>
    public void UpdateFlowPolicy(int hintDepth, TimeSpan hintHold, TimeSpan maxBacklogAge)
    {
        lock (_gate)
        {
            _pressure.UpdateHint(Math.Clamp(hintDepth, 1, _capacity), hintHold);
            _maxBacklogAgeTicks = InboundBuffer.ToTicks(maxBacklogAge);
        }
    }

    /// <summary>
    /// 记一笔带原因的丢弃（**调用方必须持有 <see cref="_gate"/>**：原因账本、分原因计数与
    /// "最近一次丢弃"必须同拍落地，否则读到的原因与时刻会来自两次不同的丢弃）。
    /// </summary>
    /// <param name="reason">丢弃原因。</param>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数。</param>
    /// <param name="reasonUnits">该原因的单元计数。</param>
    /// <param name="reasonBytes">该原因的字节计数。</param>
    private void RecordDropLocked(
        DropReason reason,
        long units,
        long bytes,
        ref long reasonUnits,
        ref long reasonBytes)
    {
        if (units > 0)
        {
            Interlocked.Add(ref _dropped, units);
            Interlocked.Add(ref reasonUnits, units);
        }

        if (bytes > 0)
        {
            Interlocked.Add(ref _droppedBytes, bytes);
            Interlocked.Add(ref reasonBytes, bytes);
        }

        Volatile.Write(ref _lastDropReason, (int)reason);
        Interlocked.Exchange(ref _lastDropAtTicks, DateTimeOffset.UtcNow.UtcTicks);

        if (reason != DropReason.DiscardedOnStop && _queue.Count > 0)
        {
            _status.ObserveDrop(true, Stopwatch.GetTimestamp());
        }
    }
    /// <summary>记一笔**队列外**的接收丢弃（适配件溢出/超长单元），用于状态行保持。</summary>
    public void NoteExternalDrop()
    {
        lock (_gate)
        {
            _status.ObserveDrop(true, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>状态行是否应显示接收积压（带滞回）。</summary>
    public bool BacklogStatusActive
    {
        get
        {
            lock (_gate)
            {
                return _status.IsBacklogActive(Stopwatch.GetTimestamp());
            }
        }
    }

    /// <summary>状态行是否应显示接收丢弃（带恢复保持）。</summary>
    public bool DropStatusActive
    {
        get
        {
            lock (_gate)
            {
                return _status.IsDropActive(Stopwatch.GetTimestamp());
            }
        }
    }

    /// <summary>更新状态行滞回策略。</summary>
    public void UpdateStatusPolicy(int enterDepth, int exitDepth, TimeSpan hold, TimeSpan dropHold)
    {
        lock (_gate)
        {
            _status.UpdatePolicy(
                Math.Clamp(enterDepth, 1, _capacity),
                Math.Clamp(exitDepth, 0, _capacity - 1),
                hold,
                dropHold);
        }
    }
}