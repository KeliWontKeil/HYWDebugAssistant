using System.Text;

namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **C 风格字符串转义**：文本发送器与字符/HEX 转换器共用同一套解析与高亮识别。
/// </summary>
/// <remarks>
/// <para>
/// 支持 C 的简单转义 <c>\\ \' \" \? \a \b \f \n \r \t \v</c>、十六进制 <c>\x</c>、
/// 八进制 <c>\ooo</c>（1–3 位）与通用字符名 <c>\uXXXX</c> / <c>\UXXXXXXXX</c>。
/// </para>
/// <para>
/// **识别规则**：<c>\x</c> 取 **1–2 位**十六进制（窄字符范围 <c>0x00–0xFF</c>），
/// 八进制最多 3 位；<c>\u</c>/<c>\U</c> 分别恰好 4/8 位。不完整或非法的序列**不视为转义**，
/// 反斜杠与后续字符按字面文本保留。这样 <c>\x30</c> 后面紧跟普通数字时不会把后续文本一起吞掉。
/// 文本方向按 UTF-8 发送，因此 <c>\xHH</c> 落到对应的字符码位，不向 UTF-8 流里塞裸字节；
/// 需要任意原始字节仍应使用 HEX 发送器。
/// </para>
/// <para>
/// <see cref="FindEscapes"/> 与 <see cref="Normalize(string?)"/> 必须使用同一个识别器：
/// 输入面画成蓝色的区间，就是实际会被解释的区间。
/// </para>
/// </remarks>
public static class TextEscapes
{
    /// <summary>
    /// 按 C 风格转义规范化文本；这是应用内**始终解析**的入口。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <returns>规范化后的文本；没有反斜杠或无需改动时尽量返回原引用。</returns>
    public static string Normalize(string? text) => NormalizeCore(text, interpret: true);

    /// <summary>
    /// **兼容重载**：旧调用方可以显式关闭解析；应用 UI 不再使用关闭分支。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="escape">是否解释字面反斜杠序列。</param>
    /// <returns>规范化后的文本。</returns>
    public static string Normalize(string? text, bool escape) =>
        escape ? Normalize(text) : text ?? string.Empty;

    /// <summary>
    /// 找出文本里全部**合法**的 C 转义组（含反斜杠与完整序列）。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <returns>按出现顺序排列的区间；没有则返回空表。</returns>
    public static IReadOnlyList<TextEscapeSpan> FindEscapes(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
        {
            return [];
        }

        var spans = new List<TextEscapeSpan>();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '\\'
                && TryReadEscape(text.AsSpan(), index, out var consumed, out _))
            {
                spans.Add(new TextEscapeSpan(index, consumed));
                index += consumed;
                continue;
            }

            index++;
        }

        return spans;
    }

    private static string NormalizeCore(string? text, bool interpret)
    {
        if (string.IsNullOrEmpty(text) || !interpret || text.IndexOf('\\') < 0)
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '\\'
                && TryReadEscape(text.AsSpan(), index, out var consumed, out var rune))
            {
                AppendRune(builder, rune);
                index += consumed;
                continue;
            }

            _ = builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// **唯一的转义识别器**：从 <paramref name="index"/> 开始读取一个合法转义。
    /// </summary>
    /// <param name="text">文本。</param>
    /// <param name="index">反斜杠所在位置。</param>
    /// <param name="consumed">合法时消耗的字符数。</param>
    /// <param name="rune">合法时解码出的字符。</param>
    /// <returns>是否是一个合法转义。</returns>
    /// <remarks>
    /// 非法/不完整/超出范围的序列返回 <see langword="false"/>，调用方按字面反斜杠继续；
    /// 因此着色与解码不会出现“画蓝了却不解释”或“解释了却没画蓝”的分叉。
    /// </remarks>
    private static bool TryReadEscape(ReadOnlySpan<char> text, int index, out int consumed, out Rune rune)
    {
        consumed = 0;
        rune = default;

        if (index < 0 || index + 1 >= text.Length || text[index] != '\\')
        {
            return false;
        }

        var next = text[index + 1];

        // C 的简单转义优先于数值转义：`\b` 是退格，不是十六进制。
        switch (next)
        {
            case '\\':
                rune = new Rune('\\');
                consumed = 2;
                return true;
            case '\'':
                rune = new Rune('\'');
                consumed = 2;
                return true;
            case '"':
                rune = new Rune('"');
                consumed = 2;
                return true;
            case '?':
                rune = new Rune('?');
                consumed = 2;
                return true;
            case 'a':
                rune = new Rune('\a');
                consumed = 2;
                return true;
            case 'b':
                rune = new Rune('\b');
                consumed = 2;
                return true;
            case 'f':
                rune = new Rune('\f');
                consumed = 2;
                return true;
            case 'n':
                rune = new Rune('\n');
                consumed = 2;
                return true;
            case 'r':
                rune = new Rune('\r');
                consumed = 2;
                return true;
            case 't':
                rune = new Rune('\t');
                consumed = 2;
                return true;
            case 'v':
                rune = new Rune('\v');
                consumed = 2;
                return true;
        }

        if (next == 'x')
        {
            // 窄字符转义最多两位十六进制：`\x30` 后面的普通数字不再被吞掉。
            var cursor = index + 2;
            var value = 0;
            var digits = 0;

            while (cursor < text.Length && digits < 2 && TryHexValue(text[cursor], out var digit))
            {
                value = (value * 16) + digit;
                cursor++;
                digits++;
            }

            if (digits == 0 || value > 0xFF)
            {
                return false;
            }

            rune = new Rune(value);
            consumed = cursor - index;
            return true;
        }

        if (next is 'u' or 'U')
        {
            var digitCount = next == 'u' ? 4 : 8;
            var cursor = index + 2;

            if (cursor + digitCount > text.Length)
            {
                return false;
            }

            long value = 0;

            for (var i = 0; i < digitCount; i++)
            {
                if (!TryHexValue(text[cursor + i], out var digit))
                {
                    return false;
                }

                value = (value * 16) + digit;
            }

            if (!Rune.IsValid((int)value))
            {
                return false;
            }

            rune = new Rune((int)value);
            consumed = 2 + digitCount;
            return true;
        }

        if (next is >= '0' and <= '7')
        {
            // C：八进制最多 3 位；值必须落在窄字符范围。
            var cursor = index + 1;
            var value = 0;
            var digits = 0;

            while (cursor < text.Length && digits < 3 && text[cursor] is >= '0' and <= '7')
            {
                value = (value * 8) + (text[cursor] - '0');
                cursor++;
                digits++;
            }

            if (value > 0xFF)
            {
                return false;
            }

            rune = new Rune(value);
            consumed = 1 + digits;
            return true;
        }

        // 不认识的转义原样保留反斜杠；下一字符按普通字符重新处理。
        return false;
    }

    private static bool TryHexValue(char value, out int digit)
    {
        switch (value)
        {
            case >= '0' and <= '9':
                digit = value - '0';
                return true;
            case >= 'a' and <= 'f':
                digit = value - 'a' + 10;
                return true;
            case >= 'A' and <= 'F':
                digit = value - 'A' + 10;
                return true;
            default:
                digit = 0;
                return false;
        }
    }

    private static void AppendRune(StringBuilder builder, Rune rune)
    {
        if (rune.IsBmp)
        {
            _ = builder.Append((char)rune.Value);
            return;
        }

        Span<char> pair = stackalloc char[2];
        var written = rune.EncodeToUtf16(pair);
        _ = builder.Append(pair[..written]);
    }
}
