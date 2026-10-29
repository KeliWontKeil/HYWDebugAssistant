using System.Globalization;
using HYWDebugAssistant.Sdk.Text;

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

        // **空白是分隔符，不是装饰**（最严判据）：每一段都必须是**恰好两位**。
        // 丢掉空白再按两位切会把 `123456 23 55` 这种"某一段少写了一个空格"的输入
        // 静默解成 5 个字节 —— 发出去的是一串莫名其妙的字节，而**没有任何报错**。
        var segments = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.Length != 2)
            {
                failure = TextCatalog.Get(
                    typeof(HexTextCodec).Assembly,
                    HexSenderText.StatusHexSegmentLength,
                    segment,
                    segment.Length);
                return false;
            }

            foreach (var ch in segment)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    failure = TextCatalog.Get(
                        typeof(HexTextCodec).Assembly,
                        HexSenderText.StatusHexNonHex,
                        segment,
                        ch);
                    return false;
                }
            }
        }

        if (segments.Length == 0)
        {
            return true;    // 全是空白 ⇒ 等价于空
        }

        var result = new byte[segments.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(segments[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        bytes = result;
        return true;
    }
}
