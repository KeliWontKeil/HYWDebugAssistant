using System.Globalization;

namespace HYWDebugAssistant.Modules.HexSender;

/// <summary>
/// **十六进制文本 → 字节** 的解析（本模块的业务核心 · 阶段 3）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>空白只是排版</b>：HEX 的**换行不作为字符，仅作为排版** ⇒
///     空格 / 换行 / 制表符**一律忽略**，可按字节、按 16 字节一行随意排版；</item>
///   <item><b>每字节两位十六进制</b> ⇒ **奇数个半字节无法表达** ⇒ **明确失败**，
///     不猜、不补零（宁可不发，也不发半个字节）；</item>
///   <item>**大小写都收**（数据是**字节**，没有大小写概念）；</item>
///   <item>**不支持 `0x` 前缀**（会让「每字节两位」的规则复杂化）。</item>
/// </list>
/// </remarks>
internal static class HexTextCodec
{
    /// <summary>
    /// 解析十六进制文本为字节序列。
    /// </summary>
    /// <param name="text">输入的十六进制文本（框架已保证非法字符进不了输入框）。</param>
    /// <param name="bytes">解析结果（失败时为空）。</param>
    /// <param name="failure">失败原因（成功时为 <see langword="null"/>）—— 由调用方回报到**对应输入框**（红框）。</param>
    /// <returns>解析成功为 <see langword="true"/>。</returns>
    public static bool TryParse(string? text, out byte[] bytes, out string? failure)
    {
        bytes = [];
        failure = null;

        if (string.IsNullOrEmpty(text))
        {
            // 空 ⇒ **静默不发**（空在各层意义一致）；不报错、不提示
            return true;
        }

        var digits = new List<char>(text.Length);

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;   // 排版字符
            }

            if (!Uri.IsHexDigit(ch))
            {
                failure = $"出现非十六进制字符 '{ch}'（只接受 0-9 / A-F / a-f 与空白）";
                return false;
            }

            digits.Add(ch);
        }

        if (digits.Count == 0)
        {
            return true;    // 全是空白 ⇒ 等价于空
        }

        if (digits.Count % 2 != 0)
        {
            failure = $"十六进制位数是奇数（{digits.Count} 位）—— 每字节必须两位（如 0D 0A）";
            return false;
        }

        var result = new byte[digits.Count / 2];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(
                string.Create(2, (digits[i * 2], digits[i * 2 + 1]), static (span, state) =>
                {
                    span[0] = state.Item1;
                    span[1] = state.Item2;
                }),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        bytes = result;
        return true;
    }
}
