namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>文本里的一次换行来自哪种换行序列。</summary>
public enum NewlineKind
{
    /// <summary>不是数据换行（例如显示层拼行）。</summary>
    None = 0,

    /// <summary><c>\r\n</c>。</summary>
    CrLf = 1,

    /// <summary>单独的 <c>\r</c>。</summary>
    Cr = 2,

    /// <summary>单独的 <c>\n</c>。</summary>
    Lf = 3,
}

/// <summary>
/// **显示层的换行标记编码**：数据换行在显示缓冲里保留为 <c>哨兵 + \n</c>，只供只读文本视图画箭头。
/// </summary>
/// <remarks>
/// 哨兵是**私有区字符**；数据解码只会把**这三个哨兵码位本身**写成
/// <c>⟨XX⟩</c> 字面标记，其他私有区字符按普通不可显示字符显示，因此不会与用户数据碰撞。
/// 哨兵不是数据：复制/保存前必须剥离；它也不参与布局与选择偏移。
/// </remarks>
public static class NewlineEncoding
{
    /// <summary>CRLF 的哨兵。</summary>
    public const char CrLfSentinel = '\uF000';

    /// <summary>单独 CR 的哨兵。</summary>
    public const char CrSentinel = '\uF001';

    /// <summary>单独 LF 的哨兵。</summary>
    public const char LfSentinel = '\uF002';

    /// <summary>CRLF 的标记（沿用原来的换行符）。</summary>
    public const string CrLfMark = "↵";

    /// <summary>单独 CR 的标记（回车/回到行首：向左）。</summary>
    public const string CrMark = "←";

    /// <summary>单独 LF 的标记（换行/向下推进：向下）。</summary>
    public const string LfMark = "↓";

    /// <summary>取某种换行的绘制标记。</summary>
    /// <param name="kind">换行种类。</param>
    /// <returns>标记文本。</returns>
    public static string MarkOf(NewlineKind kind) => kind switch
    {
        NewlineKind.CrLf => CrLfMark,
        NewlineKind.Cr => CrMark,
        NewlineKind.Lf => LfMark,
        _ => string.Empty,
    };

    /// <summary>取某种换行的原始字符序列。</summary>
    /// <param name="kind">换行种类。</param>
    /// <returns>换行序列；<see cref="NewlineKind.None"/> 为空。</returns>
    public static string SequenceOf(NewlineKind kind) => kind switch
    {
        NewlineKind.CrLf => "\r\n",
        NewlineKind.Cr => "\r",
        NewlineKind.Lf => "\n",
        _ => string.Empty,
    };

    /// <summary>读哨兵对应的换行种类。</summary>
    /// <param name="sentinel">哨兵字符。</param>
    /// <returns>换行种类；不是哨兵 ⇒ <see cref="NewlineKind.None"/>。</returns>
    public static NewlineKind KindOf(char sentinel) => sentinel switch
    {
        CrLfSentinel => NewlineKind.CrLf,
        CrSentinel => NewlineKind.Cr,
        LfSentinel => NewlineKind.Lf,
        _ => NewlineKind.None,
    };

    /// <summary>是否是显示层哨兵。</summary>
    /// <param name="value">字符。</param>
    /// <returns>是哨兵为 <see langword="true"/>。</returns>
    public static bool IsSentinel(char value) =>
        value is CrLfSentinel or CrSentinel or LfSentinel;

    /// <summary>剥离字符串里的显示层哨兵（复制/保存前调用）。</summary>
    /// <param name="text">文本。</param>
    /// <returns>不含哨兵的文本；没有哨兵时返回原引用。</returns>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOfAny([CrLfSentinel, CrSentinel, LfSentinel]) < 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var value in text)
        {
            if (!IsSentinel(value))
            {
                _ = builder.Append(value);
            }
        }

        return builder.ToString();
    }

    /// <summary>把显示层哨兵+<c>\n</c>还原成真实换行序列，其余字符原样保留。</summary>
    /// <param name="text">显示缓冲里的一行。</param>
    /// <returns>保留 CRLF / CR / LF 种类的文本。</returns>
    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOfAny([CrLfSentinel, CrSentinel, LfSentinel]) < 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (IsSentinel(current)
                && index + 1 < text.Length
                && text[index + 1] == '\n')
            {
                _ = builder.Append(SequenceOf(KindOf(current)));
                index++;
                continue;
            }

            if (!IsSentinel(current))
            {
                _ = builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
