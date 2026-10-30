using System.Buffers;
using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Queues;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// **会话发送积压**：适配件写是阻塞的，缓冲由会话持有、由专用写泵线程消费。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>按**字节**有界（单元数另有 8192 上限），满时丢最旧（`SendBacklogEvicted`）；
///     `maxBacklogAge` 大于 0 时，排队超龄的单元在出队时按 `Expired` 丢弃。</item>
///   <item>饱和判据复用 <see cref="QueuePressure"/>（同一套阈值/迟滞），只作为提示事实，不丢数据。</item>
///   <item>入队拷贝到池租数组；写泵取走租约、写完归还——会话线程只做 O(字节) 拷贝与入队。</item>
/// </list>
/// </remarks>
internal sealed class SendBacklog
{
    /// <summary>单元数硬上限（字节上限之外的防呆；正常单元 ≤ 1472 B）。</summary>
    private const int MaxUnits = 8192;

    private readonly List<Entry> _queue = new();
    private readonly object _gate = new();
    private readonly int _capacityBytes;
    private readonly QueuePressure _pressure;

    private int _head;
    private int _bytes;
    private int _peakBytes;
    private TimeSpan _maxAge;
    private long _evictedUnits;
    private long _evictedBytes;
    private long _expiredUnits;
    private long _expiredBytes;
    private long _discardedUnits;
    private long _discardedBytes;

    /// <summary>创建发送积压。</summary>
    /// <param name="capacityBytes">容量（字节；小于 1 按 1 处理）。</param>
    /// <param name="maxAge">最大滞留（<see cref="TimeSpan.Zero"/> = 不限）。</param>
    /// <param name="hintDepth">饱和提示深度（单元数）。</param>
    /// <param name="hintHold">饱和提示保持时长。</param>
    public SendBacklog(int capacityBytes, TimeSpan maxAge, int hintDepth, TimeSpan hintHold)
    {
        _capacityBytes = Math.Max(1, capacityBytes);
        _maxAge = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge;
        _pressure = new QueuePressure(hintDepth, hintHold);
    }

    /// <summary>容量（字节）。</summary>
    public int CapacityBytes => _capacityBytes;

    /// <summary>当前积压单元数。</summary>
    public int Depth
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count - _head;
            }
        }
    }

    /// <summary>当前积压字节数。</summary>
    public int Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>历史峰值字节数。</summary>
    public int PeakBytes
    {
        get
        {
            lock (_gate)
            {
                return _peakBytes;
            }
        }
    }

    /// <summary>被挤旧的单元数。</summary>
    public long EvictedUnits
    {
        get
        {
            lock (_gate)
            {
                return _evictedUnits;
            }
        }
    }

    /// <summary>被挤旧的字节数。</summary>
    public long EvictedBytes
    {
        get
        {
            lock (_gate)
            {
                return _evictedBytes;
            }
        }
    }

    /// <summary>过期丢弃的单元数。</summary>
    public long ExpiredUnits
    {
        get
        {
            lock (_gate)
            {
                return _expiredUnits;
            }
        }
    }

    /// <summary>过期丢弃的字节数。</summary>
    public long ExpiredBytes
    {
        get
        {
            lock (_gate)
            {
                return _expiredBytes;
            }
        }
    }

    /// <summary>关闭/故障清空时被丢弃的单元数。</summary>
    public long DiscardedUnits
    {
        get
        {
            lock (_gate)
            {
                return _discardedUnits;
            }
        }
    }

    /// <summary>关闭/故障清空时被丢弃的字节数。</summary>
    public long DiscardedBytes
    {
        get
        {
            lock (_gate)
            {
                return _discardedBytes;
            }
        }
    }

    /// <summary>是否饱和（入队深度达到阈值并持续足够久）。</summary>
    public bool Saturated
    {
        get
        {
            lock (_gate)
            {
                return _pressure.IsSaturated(Stopwatch.GetTimestamp());
            }
        }
    }

    /// <summary>已饱和多久（未饱和为 <see cref="TimeSpan.Zero"/>）。</summary>
    public TimeSpan SaturatedFor
    {
        get
        {
            lock (_gate)
            {
                return _pressure.SaturatedFor(Stopwatch.GetTimestamp());
            }
        }
    }

    /// <summary>入队（拷贝；满时挤旧；超容量单元按丢弃计数）。</summary>
    /// <param name="payload">有效字节。</param>
    /// <param name="evictedBytes">本次被挤旧（含超容量丢弃）的字节数。</param>
    /// <returns>是否接受（空载荷 ⇒ <see langword="false"/>）。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload, out long evictedBytes)
    {
        evictedBytes = 0;

        if (payload.Length == 0)
        {
            return false;
        }

        var copy = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(copy);

        lock (_gate)
        {
            // 单单元就超过总容量：不能装，按丢弃记账（调用方据 evictedBytes 记"发送积压丢失"）。
            if (payload.Length > _capacityBytes)
            {
                ArrayPool<byte>.Shared.Return(copy);
                _evictedUnits++;
                _evictedBytes += payload.Length;
                _pressure.OnDropped();
                evictedBytes = payload.Length;
                return true;
            }

            while ((_bytes + payload.Length > _capacityBytes || _queue.Count - _head >= MaxUnits)
                   && _queue.Count > _head)
            {
                var head = _queue[_head];
                _queue[_head] = default;
                _head++;
                _bytes -= head.Length;
                _evictedUnits++;
                _evictedBytes += head.Length;
                evictedBytes += head.Length;
                _pressure.OnDropped();
                ArrayPool<byte>.Shared.Return(head.Buffer);
            }

            _queue.Add(new Entry(copy, payload.Length, Stopwatch.GetTimestamp()));
            _bytes += payload.Length;

            if (_bytes > _peakBytes)
            {
                _peakBytes = _bytes;
            }

            _pressure.OnEnqueued(_queue.Count - _head);
            CompactLocked();
            Monitor.PulseAll(_gate);
        }

        return true;
    }

    /// <summary>取一个单元（可等待）；超龄单元在取时丢弃并继续。</summary>
    /// <param name="buffer">租约数组（用后必须 <see cref="Release"/>）。</param>
    /// <param name="length">有效字节数。</param>
    /// <param name="waitMs">无数据时最多等待多久（毫秒）。</param>
    /// <param name="expiredBytes">本次取的过程中因超龄丢弃的字节数。</param>
    /// <returns>是否取到单元。</returns>
    public bool TryTake(out byte[] buffer, out int length, int waitMs, out long expiredBytes)
    {
        buffer = [];
        length = 0;
        expiredBytes = 0;

        lock (_gate)
        {
            while (true)
            {
                if (_queue.Count > _head)
                {
                    var head = _queue[_head];
                    _queue[_head] = default;
                    _head++;
                    _bytes -= head.Length;
                    _pressure.OnDequeued(_queue.Count - _head);

                    if (_maxAge > TimeSpan.Zero && Stopwatch.GetElapsedTime(head.EnqueuedTicks) > _maxAge)
                    {
                        _expiredUnits++;
                        _expiredBytes += head.Length;
                        expiredBytes += head.Length;
                        ArrayPool<byte>.Shared.Return(head.Buffer);
                        CompactLocked();
                        continue;
                    }

                    buffer = head.Buffer;
                    length = head.Length;
                    CompactLocked();
                    return true;
                }

                if (waitMs <= 0)
                {
                    return false;
                }

                _ = Monitor.Wait(_gate, waitMs);
                return false;
            }
        }
    }

    /// <summary>归还租约数组。</summary>
    /// <param name="buffer">取出的数组。</param>
    internal static void Release(byte[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>唤醒等待中的写泵（入队、停止、策略变更后调用）。</summary>
    public void Wake()
    {
        lock (_gate)
        {
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>清空（关闭/故障）：所有在队单元按丢弃计数。</summary>
    public void Purge()
    {
        List<byte[]> drained;

        lock (_gate)
        {
            drained = new List<byte[]>(_queue.Count - _head);

            for (var index = _head; index < _queue.Count; index++)
            {
                drained.Add(_queue[index].Buffer);
                _bytes -= _queue[index].Length;
                _discardedUnits++;
                _discardedBytes += _queue[index].Length;
            }

            _queue.Clear();
            _head = 0;
            _bytes = 0;
            _pressure.OnDequeued(0);
            Monitor.PulseAll(_gate);
        }

        foreach (var buffer in drained)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>清计数（不动在队数据）。</summary>
    public void ClearCounters()
    {
        lock (_gate)
        {
            _evictedUnits = 0;
            _evictedBytes = 0;
            _expiredUnits = 0;
            _expiredBytes = 0;
            _discardedUnits = 0;
            _discardedBytes = 0;
            _peakBytes = _bytes;
        }
    }

    /// <summary>改策略（即时）：滞留上限与饱和判据；容量不变。</summary>
    /// <param name="maxAge">最大滞留（0 = 不限）。</param>
    /// <param name="hintDepth">饱和提示深度。</param>
    /// <param name="hintHold">饱和提示保持时长。</param>
    public void UpdatePolicy(TimeSpan maxAge, int hintDepth, TimeSpan hintHold)
    {
        lock (_gate)
        {
            _maxAge = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge;
            _pressure.UpdateHint(hintDepth, hintHold);
        }
    }

    private void CompactLocked()
    {
        if (_head == 0)
        {
            return;
        }

        if (_head == _queue.Count)
        {
            _queue.Clear();
            _head = 0;
            return;
        }

        if (_head >= 64 && _head * 2 >= _queue.Count)
        {
            _queue.RemoveRange(0, _head);
            _head = 0;
        }
    }

    private readonly struct Entry
    {
        public Entry(byte[] buffer, int length, long enqueuedTicks)
        {
            Buffer = buffer;
            Length = length;
            EnqueuedTicks = enqueuedTicks;
        }

        public byte[] Buffer { get; }

        public int Length { get; }

        public long EnqueuedTicks { get; }
    }
}
