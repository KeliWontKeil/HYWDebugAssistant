using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.CommandSender;

/// <summary>HEX 文本转文本的结果。</summary>
public enum HexToTextStatus
{
    /// <summary>转换成功。</summary>
    Ok = 0,

    /// <summary>HEX 文本本身不合法。</summary>
    InvalidHex = 1,

    /// <summary>字节不是合法 UTF-8，不能无损转成文本。</summary>
    NotUtf8 = 2,
}

/// <summary>
/// **一条逻辑指令 → 若干 <c>{ len, data }</c> 数据包**，以及 HEX↔文本转换。
/// </summary>
/// <remarks>
/// <para>
/// 文本与两个发送器同口径：C 转义始终解析、UTF-8 编码、按字符边界拆包，绝不把多字节字符切开；
/// HEX 是“每段恰好两位、空白只是分隔”。包是底层机制，一条逻辑指令可以对应多个包。
/// </para>
/// <para>
/// 文本不追加任何结尾：CR/LF 勾选只决定输入框按 Enter 写什么、以及已有真实换行的归一。
/// </para>
/// </remarks>
public static class CommandSenderCodec
{
    /// <summary>把文本指令拆成数据包。</summary>
    /// <param name="content">内容原文（文本按 C 转义解释）。</param>
    /// <param name="hex">是否按 HEX 解释。</param>
    /// <param name="capacity">单包 <c>data</c> 区长度（1472）。</param>
    /// <param name="packets">组装好的定长载荷（每个 <c>2 + capacity</c> 字节）。</param>
    /// <param name="failure">失败原因（成功为空）。</param>
    /// <returns>是否成功。成功但内容为空时 <paramref name="packets"/> 为空。</returns>
    public static bool TryBuildPackets(
        string? content,
        bool hex,
        int capacity,
        out List<byte[]> packets,
        out string? failure)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        packets = [];
        failure = null;

        if (!TryEncodeContent(content, hex, out var bytes, out failure))
        {
            return false;
        }

        packets = SplitPackets(bytes, capacity, keepUtf8Boundary: !hex);
        return true;
    }

    /// <summary>把一条内容编码成原始字节（文本按 C 转义 + UTF-8，HEX 按字节解析）。</summary>
    /// <param name="content">内容原文。</param>
    /// <param name="hex">是否按 HEX 解释。</param>
    /// <param name="bytes">编码结果；空内容为空数组。</param>
    /// <param name="failure">失败原因（成功为空）。</param>
    /// <returns>是否成功。</returns>
    public static bool TryEncodeContent(
        string? content,
        bool hex,
        out byte[] bytes,
        out string? failure)
    {
        failure = null;

        if (hex)
        {
            return TryParseHex(content, out bytes, out failure);
        }

        bytes = Encoding.UTF8.GetBytes(TextEscapes.Normalize(content));
        return true;
    }

    /// <summary>把多条指令拼接后的原始字节按容量拆成数据包。</summary>
    /// <param name="data">拼接后的字节（命令之间没有分隔符）。</param>
    /// <param name="capacity">单包 <c>data</c> 区长度（1472）。</param>
    /// <returns>定长载荷；空数据返回空列表。</returns>
    /// <remarks>
    /// 合并后仍是一个逻辑内容：能按 UTF-8 解码时按字符边界拆包，避免把多字节字符切开；
    /// 任意 HEX 字节拼出的内容不保证是 UTF-8，此时按容量固定切分。
    /// </remarks>
    public static List<byte[]> BuildMergedPackets(ReadOnlySpan<byte> data, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        return SplitPackets(data, capacity, keepUtf8Boundary: TryDecodeUtf8(data, out _));
    }

    /// <summary>把一段文本按 UTF-8 转成 HEX 文本（大写、空格分隔）。</summary>
    /// <param name="text">文本；C 转义会先解析。</param>
    /// <returns>HEX 文本；空内容返回空串。</returns>
    public static string ToHexText(string? text) =>
        FormatHex(Encoding.UTF8.GetBytes(TextEscapes.Normalize(text)));

    /// <summary>把 HEX 文本转回 C 转义文本。</summary>
    /// <param name="hex">HEX 文本。</param>
    /// <param name="text">转换结果（失败时为空）。</param>
    /// <param name="failure">HEX 解析失败原因（仅 <see cref="HexToTextStatus.InvalidHex"/>）。</param>
    /// <returns>转换结果类别。</returns>
    public static HexToTextStatus TryFromHexText(string? hex, out string text, out string? failure)
    {
        text = string.Empty;
        failure = null;

        if (!TryParseHex(hex, out var bytes, out failure))
        {
            return HexToTextStatus.InvalidHex;
        }

        if (!TryDecodeUtf8(bytes, out text))
        {
            return HexToTextStatus.NotUtf8;
        }

        text = EscapeText(text);
        return HexToTextStatus.Ok;
    }

    /// <summary>解析 HEX 文本（空白分隔、每段两位、大小写均可）。</summary>
    /// <param name="text">HEX 文本。</param>
    /// <param name="bytes">解析结果。</param>
    /// <param name="failure">失败原因。</param>
    /// <returns>是否成功；空/全空白返回成功且 <paramref name="bytes"/> 为空。</returns>
    public static bool TryParseHex(string? text, out byte[] bytes, out string? failure)
    {
        bytes = [];
        failure = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var segments = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.Length != 2)
            {
                failure = TextCatalog.Get(
                    typeof(CommandSenderCodec).Assembly,
                    CommandSenderText.StatusHexSegmentLength,
                    segment,
                    segment.Length);
                return false;
            }

            foreach (var ch in segment)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    failure = TextCatalog.Get(
                        typeof(CommandSenderCodec).Assembly,
                        CommandSenderText.StatusHexNonHex,
                        segment,
                        ch);
                    return false;
                }
            }
        }

        if (segments.Length == 0)
        {
            return true;
        }

        var result = new byte[segments.Length];

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = byte.Parse(segments[index], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        bytes = result;
        return true;
    }

    /// <summary>把字节写成大写、空格分隔的 HEX 文本。</summary>
    /// <param name="bytes">字节。</param>
    /// <returns>HEX 文本；空字节返回空串。</returns>
    public static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3 - 1);

        for (var index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                _ = builder.Append(' ');
            }

            _ = builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>严格解码 UTF-8；非法序列返回 <see langword="false"/>。</summary>
    /// <param name="bytes">字节。</param>
    /// <param name="text">解码结果。</param>
    /// <returns>是否合法。</returns>
    public static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>把合法 UTF-8 文本写成 C 转义文本（保证 HEX→文本→HEX 字节不变）。</summary>
    /// <param name="text">文本。</param>
    /// <returns>C 转义文本。</returns>
    public static string EscapeText(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    _ = builder.Append("\\\\");
                    break;
                case '\r':
                    _ = builder.Append("\\r");
                    break;
                case '\n':
                    _ = builder.Append("\\n");
                    break;
                case '\t':
                    _ = builder.Append("\\t");
                    break;
                default:
                    if (ch < 0x20 || ch == 0x7F || (ch >= 0x80 && ch <= 0x9F))
                    {
                        _ = builder.Append("\\x");
                        _ = builder.Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _ = builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>组装一个定长载荷：`len` + 有效字节 + 尾部补零。</summary>
    /// <param name="chunk">有效字节。</param>
    /// <param name="capacity">`data` 区长度。</param>
    /// <returns>载荷。</returns>
    public static byte[] BuildPayload(ReadOnlySpan<byte> chunk, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunk.Length, capacity);

        var payload = new byte[sizeof(ushort) + capacity];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)chunk.Length);
        chunk.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>按容量切包；需要时把边界回退到 UTF-8 字符起点。</summary>
    private static List<byte[]> SplitPackets(
        ReadOnlySpan<byte> bytes,
        int capacity,
        bool keepUtf8Boundary)
    {
        var packets = new List<byte[]>((bytes.Length + capacity - 1) / capacity);
        var offset = 0;

        while (offset < bytes.Length)
        {
            var count = Math.Min(capacity, bytes.Length - offset);

            if (keepUtf8Boundary && offset + count < bytes.Length)
            {
                // 下一包的首字节若是续字节，说明边界落在多字节字符内部：向前回退到字符起点。
                while (count > 0 && offset + count < bytes.Length
                    && (bytes[offset + count] & 0xC0) == 0x80)
                {
                    count--;
                }

                if (count == 0)
                {
                    // capacity < 4 的理论情形：单字符都放不下，按容量硬切而不是死循环。
                    count = Math.Min(capacity, bytes.Length - offset);
                }
            }

            packets.Add(BuildPayload(bytes.Slice(offset, count), capacity));
            offset += count;
        }

        return packets;
    }
}
