using System.Text;

namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **不可显示字符的显示口径**：分类、标记文本与方框占位符的唯一实现。
/// </summary>
/// <remarks>
/// 模块只负责把原始字符写进显示缓冲；文本控件用本类判断哪些字符需要替换成
/// <c>⟨XX⟩</c> 或 <c>□</c>。十六进制一律取该文本簇的 UTF-8 字节，字节间用
/// <see cref="MarkerSeparator"/> 隔开，避免标记在折行时被切开。
/// </remarks>
public static class TextControlCharacters
{
    /// <summary>标记载荷的起始符。</summary>
    public const char MarkerOpen = '⟨';

    /// <summary>标记载荷的结束符。</summary>
    public const char MarkerClose = '⟩';

    /// <summary>标记内部字节之间的分隔符（不换行空格）。</summary>
    public const char MarkerSeparator = '\u00A0';

    /// <summary>方框占位符。</summary>
    public const char Placeholder = '□';

    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>
    /// 这个码位在屏幕上是否有可见形状；没有则文本面用标记或方框占位。
    /// </summary>
    /// <param name="rune">码位。</param>
    /// <returns>需要占位为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 判据取 <see cref="Rune.GetUnicodeCategory(Rune)"/>：只有 <c>Cc</c>（控制）/
    /// <c>Cf</c>（格式）/<c>Cs</c>（代理）/<c>Co</c>（私用）/<c>Cn</c>（未分配）/
    /// <c>Zl</c>/<c>Zp</c>（U+2028 / U+2029 这类会跳行的分隔符）需要占位。
    /// **ASCII 空格 U+0020 不算**（它是 <c>Zs</c>）；**换行 CR/LF 不算**（由文本面按行处理）。
    /// 制表符算：它没有自己的字形，位置与宽度由制表位决定。
    /// </remarks>
    public static bool NeedsPlaceholder(Rune rune) => rune.Value switch
    {
        '\r' or '\n' => false,
        _ => Rune.GetUnicodeCategory(rune) switch
        {
            System.Globalization.UnicodeCategory.Control => true,
            System.Globalization.UnicodeCategory.Format => true,
            System.Globalization.UnicodeCategory.Surrogate => true,
            System.Globalization.UnicodeCategory.PrivateUse => true,
            System.Globalization.UnicodeCategory.OtherNotAssigned => true,
            System.Globalization.UnicodeCategory.LineSeparator => true,
            System.Globalization.UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        },
    };

    /// <summary>把一个文本簇写成 <c>⟨XX⟩</c>（UTF-8 字节，大写十六进制）。</summary>
    /// <param name="cluster">文本簇（一个或多个码位）。</param>
    /// <returns>标记文本。</returns>
    public static string FormatClusterMarker(ReadOnlySpan<char> cluster)
    {
        var builder = new StringBuilder(cluster.Length * 3 + 4);
        AppendClusterMarker(builder, cluster);
        return builder.ToString();
    }

    /// <summary>把一个文本簇写成 <c>⟨XX⟩</c> 并追加到目标。</summary>
    /// <param name="builder">目标。</param>
    /// <param name="cluster">文本簇。</param>
    public static void AppendClusterMarker(StringBuilder builder, ReadOnlySpan<char> cluster)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Append(MarkerOpen);

        var first = true;
        Span<byte> bytes = stackalloc byte[4];

        foreach (var rune in cluster.EnumerateRunes())
        {
            var count = rune.EncodeToUtf8(bytes);

            for (var i = 0; i < count; i++)
            {
                if (!first)
                {
                    _ = builder.Append(MarkerSeparator);
                }

                AppendHexByte(builder, bytes[i]);
                first = false;
            }
        }

        _ = builder.Append(MarkerClose);
    }

    /// <summary>把一个码位写成 <c>⟨XX⟩</c> 并追加到目标。</summary>
    /// <param name="builder">目标。</param>
    /// <param name="rune">码位。</param>
    public static void AppendRuneMarker(StringBuilder builder, Rune rune)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Span<char> pair = stackalloc char[2];
        var written = rune.EncodeToUtf16(pair);
        AppendClusterMarker(builder, pair[..written]);
    }

    /// <summary>把非法字节写成 <c>⟨XX⟩</c> 并追加到目标。</summary>
    /// <param name="builder">目标。</param>
    /// <param name="value">字节。</param>
    public static void AppendByteMarker(StringBuilder builder, byte value)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Append(MarkerOpen);
        AppendHexByte(builder, value);
        _ = builder.Append(MarkerClose);
    }

    /// <summary>把非法字节写成 <c>⟨XX⟩</c>。</summary>
    /// <param name="value">字节。</param>
    /// <returns>标记文本。</returns>
    public static string FormatByteMarker(byte value)
    {
        Span<char> buffer = stackalloc char[4];
        buffer[0] = MarkerOpen;
        buffer[1] = HexDigits[value >> 4];
        buffer[2] = HexDigits[value & 0xF];
        buffer[3] = MarkerClose;
        return new string(buffer);
    }

    private static void AppendHexByte(StringBuilder builder, byte value) =>
        _ = builder.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0xF]);
}
