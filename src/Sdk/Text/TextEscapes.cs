using System.Text;

namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **C 风格字符串转义**：文本发送器与字符/HEX 转换器共用同一套解析。
/// </summary>
/// <remarks>
/// 支持 <c>\\ \' \" \? \a \b \f \n \r \t \v</c>、<c>\xH</c>/<c>\xHH</c>、<c>\ooo</c>。
/// 十六进制/八进制按**字符码位**解释，不把 &gt;0x7F 的字节直接塞进 UTF-8 流；
/// 需要任意原始字节仍应使用 HEX 发送器。未知转义保留字面反斜杠。
/// </remarks>
public static class TextEscapes
{
    /// <summary>
    /// 按 C 风格转义规范化文本；<paramref name="escape"/> 为 false 时原样返回。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="escape">是否解释字面反斜杠序列。</param>
    /// <returns>规范化后的文本；无需改动时尽量返回原引用。</returns>
    public static string Normalize(string? text, bool escape)
    {
        if (string.IsNullOrEmpty(text) || !escape || text.IndexOf('\\') < 0)
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length;)
        {
            var current = text[index];

            if (current != '\\' || index + 1 >= text.Length)
            {
                _ = builder.Append(current);
                index++;
                continue;
            }

            var next = text[index + 1];

            switch (next)
            {
                case '\\':
                case '\'':
                case '"':
                case '?':
                    _ = builder.Append(next);
                    index += 2;
                    continue;

                case 'a':
                    _ = builder.Append('\a');
                    index += 2;
                    continue;

                case 'b':
                    _ = builder.Append('\b');
                    index += 2;
                    continue;

                case 'f':
                    _ = builder.Append('\f');
                    index += 2;
                    continue;

                case 'n':
                    _ = builder.Append('\n');
                    index += 2;
                    continue;

                case 'r':
                    _ = builder.Append('\r');
                    index += 2;
                    continue;

                case 't':
                    _ = builder.Append('\t');
                    index += 2;
                    continue;

                case 'v':
                    _ = builder.Append('\v');
                    index += 2;
                    continue;

                case 'x':
                {
                    var end = index + 2;
                    var value = 0;
                    var digits = 0;

                    while (end < text.Length && digits < 2 && IsHexDigit(text[end]))
                    {
                        value = (value * 16) + HexValue(text[end]);
                        end++;
                        digits++;
                    }

                    if (digits == 0)
                    {
                        _ = builder.Append('\\');
                        index++;
                        continue;
                    }

                    _ = builder.Append((char)value);
                    index = end;
                    continue;
                }

                default:
                    if (next is >= '0' and <= '7')
                    {
                        var end = index + 1;
                        var value = 0;
                        var digits = 0;

                        while (end < text.Length && digits < 3 && text[end] is >= '0' and <= '7')
                        {
                            value = (value * 8) + (text[end] - '0');
                            end++;
                            digits++;
                        }

                        _ = builder.Append((char)(value & 0xFF));
                        index = end;
                        continue;
                    }

                    // 不认识的转义原样保留反斜杠，下一字符按普通字符重新处理。
                    _ = builder.Append('\\');
                    index++;
                    continue;
            }
        }

        return builder.ToString();
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => value - 'A' + 10,
    };
}
