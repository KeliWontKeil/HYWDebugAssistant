using System;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **流式适配件读暂存**（串口 / TCP）：按**字节流**暂存，按目标缓冲大小切片交付。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么不是"一事件一单元"</b>：串口一次 <c>DataReceived</c> 的字节数由驱动决定，
///     长文本连写/回环常被拆成几十个很小的块。若一块一单元，一次 1472 B 的回显会变成上百个单元，
///     既挤爆下游队列，也让"一个单元"失去契约意义；按字节流暂存后，一次交付最多
///     <c>destination.Length</c> 字节（会话侧就是契约单元）。</item>
///   <item><b>有界、丢旧字节</b>：容量按**字节**计；满时丢最旧的字节并交回丢弃字节数，
///     由适配件经 <see cref="Sdk.Access.IExternalAccessReady.ReportDropped"/> 记账。</item>
///   <item>生产者（收发泵）与消费者（实例线程）各一个，用锁保护。</item>
/// </list>
/// </remarks>
internal sealed class StreamAdapterBuffer
{
    private readonly byte[] _ring;
    private readonly Lock _gate = new();
    private int _head;
    private int _count;

    /// <param name="capacityBytes">容量（字节；默认 = 64 KB，约 5.6 s 的 115200 线路）。</param>
    public StreamAdapterBuffer(int capacityBytes = 64 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityBytes, 1);
        _ring = new byte[capacityBytes];
    }

    /// <summary>当前可读字节数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>追加字节（满时丢最旧字节，保证新数据进来）。</summary>
    /// <param name="payload">原始字节（空 ⇒ 拒绝）。</param>
    /// <param name="droppedBytes">因容量不足被丢掉的字节数（没有丢弃 = 0）。</param>
    /// <returns>是否接受。</returns>
    public bool TryEnqueue(ReadOnlySpan<byte> payload, out long droppedBytes)
    {
        droppedBytes = 0;

        if (payload.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            // 单次就超过整个容量：只保留最后 capacityBytes 字节。
            if (payload.Length >= _ring.Length)
            {
                droppedBytes = _count + payload.Length - _ring.Length;
                payload = payload[(payload.Length - _ring.Length)..];
                payload.CopyTo(_ring);
                _head = 0;
                _count = _ring.Length;
                return true;
            }

            var overflow = _count + payload.Length - _ring.Length;

            if (overflow > 0)
            {
                droppedBytes = overflow;
                _head = (_head + overflow) % _ring.Length;
                _count -= overflow;
            }

            var tail = (_head + _count) % _ring.Length;
            var first = Math.Min(payload.Length, _ring.Length - tail);
            payload[..first].CopyTo(_ring.AsSpan(tail));

            if (payload.Length > first)
            {
                payload[first..].CopyTo(_ring);
            }

            _count += payload.Length;
            return true;
        }
    }

    /// <summary>出队并拷到目标缓冲（最多 <paramref name="destination"/>.Length 字节）。</summary>
    /// <param name="destination">目标缓冲。</param>
    /// <param name="length">实际交付的字节数。</param>
    /// <returns>是否交付了数据（无可读字节或目标为空 ⇒ <see langword="false"/>）。</returns>
    public bool TryDequeue(Span<byte> destination, out int length)
    {
        length = 0;

        if (destination.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_count == 0)
            {
                return false;
            }

            var take = Math.Min(_count, destination.Length);
            var first = Math.Min(take, _ring.Length - _head);
            _ring.AsSpan(_head, first).CopyTo(destination);

            if (take > first)
            {
                _ring.AsSpan(0, take - first).CopyTo(destination[first..]);
            }

            _head = (_head + take) % _ring.Length;
            _count -= take;
            length = take;
            return true;
        }
    }

    /// <summary>清空（关闭/重开时调用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _head = 0;
            _count = 0;
        }
    }
}
