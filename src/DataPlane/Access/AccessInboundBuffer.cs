using System.Buffers;
using HYWDebugAssistant.DataPlane.Queues;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// **每会话一条有界原始字节队列**：外部接入的数据单元在交给模块前先落这里。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>与口的 <see cref="InboundBuffer"/> 同一套纪律：绝不阻塞生产者，满时按策略丢弃并计数；</item>
///   <item>元素是**原始字节**（不含应用组帧）；组帧/拆帧在模块侧；</item>
///   <item>生产者 = 适配件的异步泵（经实例循环的 <c>TryRead</c>）；消费者 = 实例线程；</item>
///   <item>v1 用锁保证正确性；无锁/批量优化属后续优化项。</item>
/// </list>
/// </remarks>
public sealed class AccessInboundBuffer
{
    private readonly Queue<(byte[] Buffer, int Length)> _queue;
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly DropPolicy _policy;
    private long _enqueued;
    private long _dropped;
    private long _droppedBytes;

    /// <summary>创建有界队列。</summary>
    /// <param name="capacity">容量（单元数）。</param>
    /// <param name="policy">满时丢弃方式。</param>
    public AccessInboundBuffer(int capacity, DropPolicy policy = DropPolicy.DropOldest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _policy = policy;
        _queue = new Queue<(byte[] Buffer, int Length)>(capacity);
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

    /// <summary>累计丢弃数。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>累计丢弃字节数。</summary>
    public long DroppedBytes => Interlocked.Read(ref _droppedBytes);

    /// <summary>入队；满时按策略丢弃（丢旧 / 丢新）。</summary>
    /// <param name="payload">原始字节（空单元不接受：零长度无法与"无数据"区分）。</param>
    /// <returns>本次是否入队成功。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            RecordDropped(1, 0);
            return false;
        }

        var length = payload.Length;
        var copy = ArrayPool<byte>.Shared.Rent(length);
        payload.CopyTo(copy);

        (byte[] Buffer, int Length)? dropped = null;
        bool accepted;

        lock (_gate)
        {
            if (_queue.Count < _capacity)
            {
                _queue.Enqueue((copy, length));
                accepted = true;
            }
            else if (_policy == DropPolicy.DropNewest)
            {
                dropped = (copy, length);
                accepted = false;
            }
            else
            {
                dropped = _queue.Dequeue();
                _queue.Enqueue((copy, length));
                accepted = true;
            }

            if (accepted)
            {
                Interlocked.Increment(ref _enqueued);
            }

            if (dropped is { } evicted)
            {
                Interlocked.Increment(ref _dropped);
                Interlocked.Add(ref _droppedBytes, evicted.Length);
            }
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
    /// <remarks>目标缓冲小于单元长度时，该单元被丢弃并计数，返回 <see langword="false"/>（不截断）。</remarks>
    /// <param name="destination">目标缓冲。</param>
    /// <param name="length">实际字节数（成功时）。</param>
    public bool TryDequeue(Span<byte> destination, out int length)
    {
        length = 0;
        (byte[] Buffer, int Length) payload;
        bool oversized = false;

        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return false;
            }

            payload = _queue.Peek();
            if (payload.Length > destination.Length)
            {
                _ = _queue.Dequeue();
                oversized = true;
                Interlocked.Increment(ref _dropped);
                Interlocked.Add(ref _droppedBytes, payload.Length);
            }
            else
            {
                _ = _queue.Dequeue();
            }
        }

        if (oversized)
        {
            ArrayPool<byte>.Shared.Return(payload.Buffer);
            return false;
        }

        payload.Buffer.AsSpan(0, payload.Length).CopyTo(destination);
        length = payload.Length;
        ArrayPool<byte>.Shared.Return(payload.Buffer);
        return true;
    }

    /// <summary>清空队列并归还全部数组（关闭/释放时调用）。</summary>
    public void Clear()
    {
        List<byte[]> drained;

        lock (_gate)
        {
            drained = new List<byte[]>(_queue.Count);
            while (_queue.Count > 0)
            {
                drained.Add(_queue.Dequeue().Buffer);
            }
        }

        foreach (var payload in drained)
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>记一次**外部原因**的丢弃（超长、写失败等，不是队列满）。</summary>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数。</param>
    public void RecordDropped(long units, long bytes)
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
}
