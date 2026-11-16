using System.Text;

namespace HYWDebugAssistant.Modules.WaveformToRaw.Codec;

/// <summary>
/// 自定义文本帧编解码：<c>[帧头][空格][数据一][空格]…[空格][帧尾]</c>，例 <c>"ab 1.2 4.35 3.6 cd"</c>。
/// </summary>
internal static class TextFrameCodec
{
    /// <summary>单帧字符上界（FireWater 复用同一缓冲；文本帧按字节直接写）。</summary>
    public const int MaxChars = 2048;

    /// <summary>把一帧写入目标**字节**区间，返回写入字节数；返回负数表示目标过小。</summary>
    /// <remarks>
    /// 帧头/帧尾是**字节**，数值按 ASCII 写入：<c>[帧头字节][空格][值 空格 值…][空格][帧尾字节]</c>。
    /// 这样帧头可以包含任意不可输入的字节（如 <c>00 FF</c>）。
    /// </remarks>
    public static int Write(
        Span<byte> destination,
        ReadOnlySpan<float> values,
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> footer,
        int decimals)
    {
        Span<char> number = stackalloc char[32];
        var position = 0;

        if (!header.IsEmpty)
        {
            if (position + header.Length > destination.Length)
            {
                return -1;
            }

            header.CopyTo(destination);
            position += header.Length;
            destination[position++] = (byte)' ';
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                destination[position++] = (byte)' ';
            }

            if (!ValueFormat.TryFormat(values[i], decimals, number, out var written)
                || position + written > destination.Length)
            {
                return -1;
            }

            for (var index = 0; index < written; index++)
            {
                destination[position++] = (byte)number[index];
            }
        }

        if (!footer.IsEmpty)
        {
            if (position + 1 + footer.Length > destination.Length)
            {
                return -1;
            }

            destination[position++] = (byte)' ';
            footer.CopyTo(destination[position..]);
            position += footer.Length;
        }

        return position;
    }
}

/// <summary>文本帧的流式解码器：攒包、找帧头、找帧尾、按空白切分数字。</summary>
internal sealed class TextFrameDecoder
{
    private readonly FrameBuffer _buffer;
    private readonly byte[] _header;
    private readonly byte[] _footer;
    private long _errors;
    private long _discarded;

    /// <summary>创建解码器。<paramref name="header"/> 与 <paramref name="footer"/> 不得为空。</summary>
    public TextFrameDecoder(string header, string footer, int capacityBytes = 8192)
    {
        ArgumentException.ThrowIfNullOrEmpty(header);
        ArgumentException.ThrowIfNullOrEmpty(footer);

        _header = Encoding.UTF8.GetBytes(header);
        _footer = Encoding.UTF8.GetBytes(footer);
        _buffer = new FrameBuffer(capacityBytes);
    }

    /// <summary>累计坏帧数。</summary>
    public long Errors => _errors;

    /// <summary>累计丢弃字节数（含找不到帧头与溢出）。</summary>
    public long DiscardedBytes => _discarded + _buffer.DiscardedBytes;

    /// <summary>追加输入。</summary>
    public void Append(ReadOnlySpan<byte> data) => _buffer.Append(data);

    /// <summary>清空内部缓冲。</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>尝试取出一帧；数据不完整时返回 <c>false</c>。</summary>
    public bool TryReadFrame(Span<float> values, out int count)
    {
        count = 0;
        while (true)
        {
            var span = _buffer.Span;
            if (span.IsEmpty)
            {
                return false;
            }

            if (!span.StartsWith(_header))
            {
                var index = span.IndexOf(_header);
                if (index < 0)
                {
                    // 保留可能是半个帧头的尾部，其余丢弃。
                    var keep = Math.Min(span.Length, _header.Length - 1);
                    var drop = span.Length - keep;
                    if (drop > 0)
                    {
                        _buffer.Consume(drop);
                        _discarded += drop;
                    }

                    return false;
                }

                _buffer.Consume(index);
                _discarded += index;
                span = _buffer.Span;
            }

            var bodyStart = _header.Length;
            var footerIndex = span[bodyStart..].IndexOf(_footer);
            if (footerIndex < 0)
            {
                return false;
            }

            footerIndex += bodyStart;
            var parsed = TryParseValues(span[bodyStart..footerIndex], values, out var parsedCount);
            _buffer.Consume(footerIndex + _footer.Length);
            if (!parsed || parsedCount == 0)
            {
                _errors++;
                continue;
            }

            count = parsedCount;
            return true;
        }
    }

    private static bool TryParseValues(ReadOnlySpan<byte> body, Span<float> values, out int count)
    {
        count = 0;
        var i = 0;
        while (i < body.Length)
        {
            while (i < body.Length && ValueFormat.IsSpace(body[i]))
            {
                i++;
            }

            if (i >= body.Length)
            {
                break;
            }

            var start = i;
            while (i < body.Length && !ValueFormat.IsSpace(body[i]))
            {
                i++;
            }

            if (count >= values.Length || !ValueFormat.TryParse(body[start..i], out var value))
            {
                count = 0;
                return false;
            }

            values[count++] = value;
        }

        return count > 0;
    }
}
