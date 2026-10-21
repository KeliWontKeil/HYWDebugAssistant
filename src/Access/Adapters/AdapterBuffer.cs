using System.Buffers;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **适配件的读暂存**：网络/串口泵把收到的字节先放这里，框架按就绪信号来取。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>有界、丢旧：慢消费时不让内存无限增长；丢弃由适配件通过 <see cref="Sdk.Access.IExternalAccessReady.ReportDropped"/> 记账；</item>
///   <item>与框架的每会话队列不是同一层：这里只解决"适配件异步泵 → 框架拉取"之间的暂存；</item>
///   <item>生产者一个、消费者一个（框架实例线程），但收发泵可能并发，故用锁。</item>
/// </list>
/// </remarks>
internal sealed class AdapterBuffer
{
    private readonly Queue<(byte[] Buffer, int Length)> _queue;
    private readonly Lock _gate = new();
    private readonly int _capacity;

    public AdapterBuffer(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _queue = new Queue<(byte[] Buffer, int Length)>(capacity);
    }

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

    /// <summary>入队（满时丢最旧，保证新数据进来）。</summary>
    /// <param name="payload">原始字节（空 ⇒ 拒绝）。</param>
    /// <param name="droppedBytes">被挤掉的最旧单元长度（没有丢弃 = 0）。</param>
    /// <returns>是否入队。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload, out long droppedBytes)
    {
        droppedBytes = 0;

        if (payload.Length == 0)
        {
            return false;
        }

        var length = payload.Length;
        var copy = ArrayPool<byte>.Shared.Rent(length);
        payload.CopyTo(copy);

        (byte[] Buffer, int Length)? dropped = null;

        lock (_gate)
        {
            if (_queue.Count >= _capacity)
            {
                dropped = _queue.Dequeue();
            }

            _queue.Enqueue((copy, length));
        }

        if (dropped is { } evicted)
        {
            droppedBytes = evicted.Length;
            ArrayPool<byte>.Shared.Return(evicted.Buffer);
        }

        return true;
    }

    /// <summary>出队并拷到目标缓冲（目标不足 ⇒ 丢弃该单元并返回 <see langword="false"/>）。</summary>
    public bool TryDequeue(Span<byte> destination, out int length)
    {
        length = 0;
        (byte[] Buffer, int Length) payload;
        var oversized = false;

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

    /// <summary>清空（关闭/重开时调用）。</summary>
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
}
