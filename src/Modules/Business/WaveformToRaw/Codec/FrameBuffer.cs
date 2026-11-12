namespace HYWDebugAssistant.Modules.WaveformToRaw.Codec;

/// <summary>
/// 定长容量、只进不出的线性字节缓冲：用于把跨单元到达的流拼成完整帧。
/// 溢出时丢弃最旧字节并计数，保证模块不会被无帧尾的输入拖垮。
/// </summary>
internal sealed class FrameBuffer
{
    private readonly byte[] _data;
    private int _start;
    private int _length;

    public FrameBuffer(int capacity)
    {
        Capacity = Math.Max(64, capacity);
        _data = new byte[Capacity];
    }

    /// <summary>容量上限（字节）。</summary>
    public int Capacity { get; }

    /// <summary>当前未消费字节数。</summary>
    public int Length => _length;

    /// <summary>因溢出被丢弃的字节数。</summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>未消费内容的只读视图；<see cref="Consume"/> 之后失效。</summary>
    public ReadOnlySpan<byte> Span => _data.AsSpan(_start, _length);

    /// <summary>追加一段字节。</summary>
    public void Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

        if (chunk.Length > Capacity)
        {
            var skip = chunk.Length - Capacity;
            DiscardedBytes += _length + skip;
            _start = 0;
            _length = 0;
            chunk = chunk[skip..];
        }

        if (_length + chunk.Length > Capacity)
        {
            var drop = _length + chunk.Length - Capacity;
            _start += drop;
            _length -= drop;
            DiscardedBytes += drop;
            if (_length == 0)
            {
                _start = 0;
            }
        }

        Compact();
        chunk.CopyTo(_data.AsSpan(_start + _length));
        _length += chunk.Length;
    }

    /// <summary>消费掉头部若干字节。</summary>
    public void Consume(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count >= _length)
        {
            _start = 0;
            _length = 0;
            return;
        }

        _start += count;
        _length -= count;
    }

    /// <summary>清空内容（保留容量）。</summary>
    public void Clear()
    {
        _start = 0;
        _length = 0;
    }

    private void Compact()
    {
        if (_start == 0)
        {
            return;
        }

        if (_length > 0)
        {
            Buffer.BlockCopy(_data, _start, _data, 0, _length);
        }

        _start = 0;
    }
}
