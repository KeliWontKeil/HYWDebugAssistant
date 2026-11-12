using System.Text;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>
/// 自定义文本帧编解码：<c>[帧头][空格][数据一][空格]…[空格][帧尾]</c>，例 <c>"ab 1.2 4.35 3.6 cd"</c>。
/// </summary>
internal static class TextFrameCodec
{
    /// <summary>单帧字符上界：64 个数值的最长表示 + 帧头帧尾。</summary>
    public const int MaxChars = 2048;

    /// <summary>把一帧写入目标字符区间，返回写入字符数；返回负数表示目标过小。</summary>
    public static int Write(
        Span<char> destination,
        ReadOnlySpan<float> values,
        string header,
        string footer,
        int decimals)
    {
        var position = 0;
        if (!string.IsNullOrEmpty(header))
        {
            header.AsSpan().CopyTo(destination);
            position += header.Length;
            destination[position++] = ' ';
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                destination[position++] = ' ';
            }

            if (!ValueFormat.TryFormat(values[i], decimals, destination[position..], out var written))
            {
                return -1;
            }

            position += written;
        }

        if (!string.IsNullOrEmpty(footer))
        {
            destination[position++] = ' ';
            footer.AsSpan().CopyTo(destination[position..]);
            position += footer.Length;
        }

        return position;
    }

    /// <summary>编码一帧为字符串（测试与文档示例用；热路径用 <see cref="Write"/>）。</summary>
    public static string Encode(ReadOnlySpan<float> values, string header, string footer, int decimals)
    {
        Span<char> buffer = stackalloc char[MaxChars];
        var written = Write(buffer, values, header, footer, decimals);
        return written < 0 ? string.Empty : new string(buffer[..written]);
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
