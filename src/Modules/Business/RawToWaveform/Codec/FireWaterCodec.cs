using System.Text;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>
/// VOFA+ FireWater 编解码：逗号分隔的文本行，可选 <c>&lt;any&gt;:</c> 前缀，换行结尾。
/// </summary>
internal static class FireWaterCodec
{
    /// <summary>单行字符上界：64 个数值的最长表示 + 前缀 + 换行。</summary>
    public const int MaxChars = 2048;

    /// <summary>把一行写入目标字符区间，返回写入字符数；返回负数表示目标过小。</summary>
    public static int Write(Span<char> destination, ReadOnlySpan<float> values, string prefix, int decimals)
    {
        var position = 0;
        if (!string.IsNullOrEmpty(prefix))
        {
            prefix.AsSpan().CopyTo(destination);
            position += prefix.Length;
            destination[position++] = ':';
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                destination[position++] = ',';
            }

            if (!ValueFormat.TryFormat(values[i], decimals, destination[position..], out var written))
            {
                return -1;
            }

            position += written;
        }

        destination[position++] = '\n';
        return position;
    }

    /// <summary>编码一行为字符串（测试与文档示例用；热路径用 <see cref="Write"/>）。</summary>
    public static string Encode(ReadOnlySpan<float> values, string prefix, int decimals)
    {
        Span<char> buffer = stackalloc char[MaxChars];
        var written = Write(buffer, values, prefix, decimals);
        return written < 0 ? string.Empty : new string(buffer[..written]);
    }
}

/// <summary>FireWater 的流式解码器：按行切分，忽略 <c>image:</c> 前导帧。</summary>
internal sealed class FireWaterDecoder
{
    private readonly FrameBuffer _buffer;
    private long _errors;
    private long _ignored;

    /// <summary>创建解码器。</summary>
    public FireWaterDecoder(int capacityBytes = 8192) => _buffer = new FrameBuffer(capacityBytes);

    /// <summary>累计坏行数。</summary>
    public long Errors => _errors;

    /// <summary>累计忽略的图片前导帧数。</summary>
    public long Ignored => _ignored;

    /// <summary>累计丢弃字节数。</summary>
    public long DiscardedBytes => _buffer.DiscardedBytes;

    /// <summary>追加输入。</summary>
    public void Append(ReadOnlySpan<byte> data) => _buffer.Append(data);

    /// <summary>清空内部缓冲。</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>尝试取出一帧；没有完整行时返回 <c>false</c>。</summary>
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

            var newline = span.IndexOf((byte)'\n');
            if (newline < 0)
            {
                return false;
            }

            var line = span[..newline];
            _buffer.Consume(newline + 1);
            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (line.IsEmpty)
            {
                continue;
            }

            if (IsImageFrame(line))
            {
                _ignored++;
                continue;
            }

            if (TryParseLine(line, values, out count))
            {
                return true;
            }

            _errors++;
        }
    }

    private static bool IsImageFrame(ReadOnlySpan<byte> line)
    {
        const string ImagePrefix = "image:";
        if (line.Length < ImagePrefix.Length)
        {
            return false;
        }

        for (var i = 0; i < ImagePrefix.Length; i++)
        {
            var c = line[i];
            if (c is >= (byte)'A' and <= (byte)'Z')
            {
                c = (byte)(c + 32);
            }

            if (c != (byte)ImagePrefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseLine(ReadOnlySpan<byte> line, Span<float> values, out int count)
    {
        count = 0;
        line = Trim(line);

        // 可选前缀 "any:"：冒号必须出现在第一个逗号之前。
        var colon = line.IndexOf((byte)':');
        if (colon > 0)
        {
            var comma = line.IndexOf((byte)',');
            if (comma < 0 || colon < comma)
            {
                line = Trim(line[(colon + 1)..]);
            }
        }

        if (line.IsEmpty)
        {
            return false;
        }

        var i = 0;
        while (i <= line.Length)
        {
            var comma = line[i..].IndexOf((byte)',');
            var end = comma < 0 ? line.Length : i + comma;
            var token = Trim(line[i..end]);
            if (token.IsEmpty)
            {
                return false;
            }

            if (count >= values.Length || !ValueFormat.TryParse(token, out var value))
            {
                count = 0;
                return false;
            }

            values[count++] = value;
            if (comma < 0)
            {
                break;
            }

            i = end + 1;
        }

        return count > 0;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        var start = 0;
        while (start < span.Length && ValueFormat.IsSpace(span[start]))
        {
            start++;
        }

        var end = span.Length;
        while (end > start && ValueFormat.IsSpace(span[end - 1]))
        {
            end--;
        }

        return span[start..end];
    }
}
