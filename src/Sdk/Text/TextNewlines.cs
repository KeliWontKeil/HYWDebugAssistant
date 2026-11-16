namespace HYWDebugAssistant.Sdk.Text;

/// <summary>多行文本的换行档位。</summary>
public enum NewlineMode
{
    /// <summary><c>\r\n</c>。</summary>
    CrLf = 0,

    /// <summary>单独 <c>\r</c>。</summary>
    Cr = 1,

    /// <summary>单独 <c>\n</c>。</summary>
    Lf = 2,

    /// <summary>不写换行；归一已有文本时删除全部换行字符。</summary>
    None = 3,
}

/// <summary>
/// **多行文本的换行策略**：CR/LF 勾选框 → 换行档 → 归一文本。
/// </summary>
/// <remarks>
/// 这里只有一套实现：文本发送器、字符/HEX 转换器和文本输入控件都调用它，
/// 避免同一种"回车到底写什么"在不同模块里各算一遍。
/// </remarks>
public static class TextNewlines
{
    /// <summary>两个勾选框 → 换行档（都勾选 CRLF，仅 CR，仅 LF，都不勾 None）。</summary>
    /// <param name="cr">CR 是否勾选。</param>
    /// <param name="lf">LF 是否勾选。</param>
    /// <returns>换行档。</returns>
    public static NewlineMode FromCheckboxes(bool cr, bool lf) => (cr, lf) switch
    {
        (true, true) => NewlineMode.CrLf,
        (true, false) => NewlineMode.Cr,
        (false, true) => NewlineMode.Lf,
        _ => NewlineMode.None,
    };

    /// <summary>取换行档对应的字符序列；<see cref="NewlineMode.None"/> 为空串。</summary>
    /// <param name="mode">换行档。</param>
    /// <returns>字符序列。</returns>
    public static string SequenceOf(NewlineMode mode) => mode switch
    {
        NewlineMode.CrLf => "\r\n",
        NewlineMode.Cr => "\r",
        NewlineMode.Lf => "\n",
        _ => string.Empty,
    };

    /// <summary>值名使用的文本档位（<c>crlf</c> / <c>cr</c> / <c>lf</c> / <c>none</c>）。</summary>
    /// <param name="mode">换行档。</param>
    /// <returns>稳定文本键。</returns>
    public static string ChoiceOf(NewlineMode mode) => mode switch
    {
        NewlineMode.CrLf => "crlf",
        NewlineMode.Cr => "cr",
        NewlineMode.Lf => "lf",
        _ => "none",
    };

    /// <summary>解析值名里的文本档位；未知/空 ⇒ <see cref="NewlineMode.CrLf"/>。</summary>
    /// <param name="choice">文本档位。</param>
    /// <returns>换行档。</returns>
    public static NewlineMode ParseChoice(string? choice) => choice switch
    {
        "cr" => NewlineMode.Cr,
        "lf" => NewlineMode.Lf,
        "none" => NewlineMode.None,
        _ => NewlineMode.CrLf,
    };

    /// <summary>
    /// 把文本里的所有换行序列（CRLF / CR / LF）统一成目标换行；
    /// <see cref="NewlineMode.None"/> 时删除全部换行字符。
    /// </summary>
    /// <param name="text">文本；null 视为空。</param>
    /// <param name="mode">目标换行档。</param>
    /// <returns>归一后的文本；没有换行或无需改动时尽量返回原引用。</returns>
    public static string Normalize(string? text, NewlineMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var target = SequenceOf(mode);
        var changed = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (current == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                _ = builder.Append(target);
                continue;
            }

            if (current == '\n')
            {
                _ = builder.Append(target);
                continue;
            }

            _ = builder.Append(current);
        }

        return builder.ToString();
    }
}
