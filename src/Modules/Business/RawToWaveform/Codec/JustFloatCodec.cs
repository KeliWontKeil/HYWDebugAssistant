using System.Buffers.Binary;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>
/// VOFA+ JustFloat 编解码：小端 f32 数组 + 帧尾 <c>00 00 80 7F</c>（+Inf 的小端表示）。
/// </summary>
internal static class JustFloatCodec
{
    /// <summary>帧尾四字节。</summary>
    public static ReadOnlySpan<byte> Tail => [0x00, 0x00, 0x80, 0x7F];

    /// <summary>编码一帧所需的字节数。</summary>
    public static int GetEncodedLength(int channelCount) => (channelCount * sizeof(float)) + Tail.Length;

    /// <summary>编码一帧到目标缓冲，返回写入字节数。</summary>
    public static int Encode(ReadOnlySpan<float> values, Span<byte> destination)
    {
        var needed = GetEncodedLength(values.Length);
        if (destination.Length < needed)
        {
            throw new ArgumentException(null, nameof(destination));
        }

        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(i * sizeof(float), sizeof(float)), values[i]);
        }

        Tail.CopyTo(destination[((values.Length * sizeof(float)) + 0)..]);
        return needed;
    }
}

/// <summary>JustFloat 的流式解码器：按帧尾切帧，长度非 4 倍数时逐字节重新同步。</summary>
internal sealed class JustFloatDecoder
{
    private readonly FrameBuffer _buffer;
    private long _discarded;

    /// <summary>创建解码器。</summary>
    public JustFloatDecoder(int capacityBytes = 8192) => _buffer = new FrameBuffer(capacityBytes);

    /// <summary>累计丢弃字节数。</summary>
    public long DiscardedBytes => _discarded + _buffer.DiscardedBytes;

    /// <summary>追加输入。</summary>
    public void Append(ReadOnlySpan<byte> data) => _buffer.Append(data);

    /// <summary>清空内部缓冲。</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>尝试取出一帧；没有完整帧时返回 <c>false</c>。</summary>
    public bool TryReadFrame(Span<float> values, out int count)
    {
        count = 0;
        while (true)
        {
            var span = _buffer.Span;
            if (span.Length < JustFloatCodec.Tail.Length)
            {
                return false;
            }

            var index = span.IndexOf(JustFloatCodec.Tail);
            if (index < 0)
            {
                // 帧尾还没到：不能丢，否则跨单元到达的帧会被截断；容量由 FrameBuffer 兜底。
                return false;
            }

            if (index == 0)
            {
                _buffer.Consume(JustFloatCodec.Tail.Length);
                continue;
            }

            if (index % sizeof(float) != 0)
            {
                var skip = index % sizeof(float);
                _buffer.Consume(skip);
                _discarded += skip;
                continue;
            }

            var channelCount = index / sizeof(float);
            if (channelCount > values.Length)
            {
                _buffer.Consume(index + JustFloatCodec.Tail.Length);
                continue;
            }

            for (var i = 0; i < channelCount; i++)
            {
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(i * sizeof(float), sizeof(float)));
            }

            _buffer.Consume(index + JustFloatCodec.Tail.Length);
            count = channelCount;
            return true;
        }
    }
}
