using System.Text;

namespace HYWDebugAssistant.Sdk.Values;

/// <summary>文本输入的字符准入语法（由值声明给出，控件只按它过滤用户插入）。</summary>
/// <remarks>
/// 允许字符集是模块解析语法的**超集**：它只保证合法值不会被控件挡住，不负责判断整个值是否合法。
/// 结构、范围、跨字段约束仍由模块在提交/解析时判定。
/// </remarks>
public enum ValueTextFormat
{
    /// <summary>纯文本：不限制（C 转义记法也用它，转义识别由 <c>EscapeInput</c> 单独负责）。</summary>
    Text = 0,

    /// <summary>十六进制字节串：ASCII 十六进制字符 + 空白。</summary>
    Hex = 1,

    /// <summary>可负整数：ASCII 数字、正负号 + 空白。</summary>
    SignedWhole = 2,

    /// <summary>非负整数：ASCII 数字、正号 + 空白（不接受负号）。</summary>
    NonNegativeWhole = 3,

    /// <summary>正整数：准入字符集与非负整数相同；0 是否可接受由模块判定。</summary>
    PositiveWhole = 4,

    /// <summary>实数：ASCII 数字、正负号、小数点、指数标记 + 空白。</summary>
    Real = 5,

    /// <summary>IP 字面量可能出现的 ASCII 字符（字母数字、<c>. : % [ ] - _</c>）。</summary>
    IpAddress = 6,
}

/// <summary>文本格式的判定、过滤与默认映射。</summary>
/// <remarks>
/// 过滤只服务“用户插入的文本”；程序化写入（XAML/持久化/模块接受值）不经过这里。
/// <see cref="Filter"/> 在 <see cref="ValueTextFormat.Text"/> 上返回原引用，其余格式只在遇到非法字符时才分配。
/// </remarks>
public static class ValueTextFormats
{
    /// <summary>某个字符是否允许出现在该格式的值里。</summary>
    /// <param name="format">格式。</param>
    /// <param name="character">字符。</param>
    public static bool Allows(ValueTextFormat format, char character) => format switch
    {
        ValueTextFormat.Text => true,
        ValueTextFormat.Hex => char.IsAsciiHexDigit(character) || char.IsWhiteSpace(character),
        ValueTextFormat.SignedWhole => char.IsAsciiDigit(character)
            || character is '+' or '-'
            || char.IsWhiteSpace(character),
        ValueTextFormat.NonNegativeWhole or ValueTextFormat.PositiveWhole =>
            char.IsAsciiDigit(character) || character == '+' || char.IsWhiteSpace(character),
        ValueTextFormat.Real => char.IsAsciiDigit(character)
            || character is '+' or '-' or '.' or 'e' or 'E'
            || char.IsWhiteSpace(character),
        ValueTextFormat.IpAddress => char.IsAsciiLetterOrDigit(character)
            || character is '.' or ':' or '%' or '[' or ']' or '-' or '_',
        _ => true,
    };

    /// <summary>整段文本是否全部允许。</summary>
    /// <param name="format">格式。</param>
    /// <param name="text">文本。</param>
    public static bool AllowsAll(ValueTextFormat format, ReadOnlySpan<char> text)
    {
        if (format == ValueTextFormat.Text)
        {
            return true;
        }

        foreach (var character in text)
        {
            if (!Allows(format, character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>保留文本里允许的字符；全部允许时返回原引用。</summary>
    /// <param name="format">格式。</param>
    /// <param name="text">文本。</param>
    public static string Filter(ValueTextFormat format, string? text)
    {
        if (string.IsNullOrEmpty(text) || format == ValueTextFormat.Text)
        {
            return text ?? string.Empty;
        }

        var firstInvalid = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (!Allows(format, text[index]))
            {
                firstInvalid = index;
                break;
            }
        }

        if (firstInvalid < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        if (firstInvalid > 0)
        {
            _ = builder.Append(text, 0, firstInvalid);
        }

        for (var index = firstInvalid; index < text.Length; index++)
        {
            var character = text[index];

            if (Allows(format, character))
            {
                _ = builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
