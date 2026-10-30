using System.Buffers;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **适配件的读暂存**：网络/串口泵把收到的字节先放这里，框架按就绪信号来取。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>有界、丢旧</b>：慢消费时不让内存无限增长；丢弃由适配件通过
///     <see cref="Sdk.Access.IExternalAccessReady.ReportDropped"/> 记账；</item>
///   <item><b>目标不足 ≠ 丢弃</b>：一次交付装不下时**分片交付**（拷满目标、余量留在队头），
///     绝不把整块丢掉——这是"接收不静默丢"的底层保证；</item>
///   <item>与框架的每会话队列不是同一层：这里只解决"适配件异步泵 → 框架拉取"之间的暂存；</item>
///   <item>生产者一个、消费者一个（框架实例线程），但收发泵可能并发，故用锁。</item>
/// </list>
/// </remarks>
internal sealed class AdapterBuffer
{
    private readonly List<Entry> _queue;
    private readonly Lock _gate = new();
    private readonly int _capacity;

    /// <summary>队头下标（前面的槽位在压缩前不再使用）。</summary>
    private int _head;

    public AdapterBuffer(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _queue = new List<Entry>(capacity);
    }

    /// <summary>当前暂存的单元数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count - _head;
            }
        }
    }

    /// <summary>入队（满时丢最旧，保证新数据进来）。</summary>
    /// <param name="payload">原始字节（空 ⇒ 拒绝）。</param>
    /// <param name="droppedBytes">被挤掉的最旧单元**剩余**长度（没有丢弃 = 0）。</param>
    /// <returns>是否入队。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload, out long droppedBytes)
    {
        droppedBytes = 0;

        if (payload.Length == 0)
        {
            return false;
        }

        var copy = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(copy);

        byte[]? evicted = null;

        lock (_gate)
        {
            if (_queue.Count - _head >= _capacity)
            {
                var head = _queue[_head];
                _queue[_head] = default;
                _head++;
                evicted = head.Buffer;
                droppedBytes = head.Length;
            }

            _queue.Add(new Entry(copy, 0, payload.Length));
            CompactLocked();
        }

        if (evicted is not null)
        {
            ArrayPool<byte>.Shared.Return(evicted);
        }

        return true;
    }

    /// <summary>
    /// 出队并拷到目标缓冲。**目标装不下整个队头时分片交付**（本次拷满目标，余量留在队头），
    /// 因此本方法不会因为"块比目标大"而丢数据。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <param name="length">本次交付的字节数。</param>
    /// <returns>是否交付了数据（队空或目标为空 ⇒ <see langword="false"/>）。</returns>
    public bool TryDequeue(Span<byte> destination, out int length)
    {
        length = 0;

        if (destination.Length == 0)
        {
            return false;
        }

        byte[]? consumed = null;

        lock (_gate)
        {
            if (_queue.Count == _head)
            {
                return false;
            }

            var head = _queue[_head];
            var take = Math.Min(head.Length, destination.Length);

            // **锁内拷贝**：生产者挤队时会把数组归还池，锁外拷贝会读到被复用的内存。
            head.Buffer.AsSpan(head.Offset, take).CopyTo(destination);
            length = take;

            if (take == head.Length)
            {
                _queue[_head] = default;
                _head++;
                consumed = head.Buffer;
            }
            else
            {
                _queue[_head] = new Entry(head.Buffer, head.Offset + take, head.Length - take);
            }

            CompactLocked();
        }

        if (consumed is not null)
        {
            ArrayPool<byte>.Shared.Return(consumed);
        }

        return true;
    }

    /// <summary>清空（关闭/重开时调用）。</summary>
    public void Clear()
    {
        List<byte[]> drained;

        lock (_gate)
        {
            drained = new List<byte[]>(_queue.Count - _head);

            for (var index = _head; index < _queue.Count; index++)
            {
                drained.Add(_queue[index].Buffer);
            }

            _queue.Clear();
            _head = 0;
        }

        foreach (var payload in drained)
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>把已经消费掉的头部槽位清掉，避免列表无限增长（摊销 O(1)）。</summary>
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

    /// <summary>一个暂存单元：数组 + 偏移 + 剩余长度（分片交付时偏移前移）。</summary>
    private readonly struct Entry
    {
        public Entry(byte[] buffer, int offset, int length)
        {
            Buffer = buffer;
            Offset = offset;
            Length = length;
        }

        public byte[] Buffer { get; }

        public int Offset { get; }

        public int Length { get; }
    }
}
