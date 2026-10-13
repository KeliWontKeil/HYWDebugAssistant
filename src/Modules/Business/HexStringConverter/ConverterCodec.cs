using System.Buffers;
using System.Globalization;
using System.Text;

namespace HYWDebugAssistant.Modules.HexStringConverter;

/// <summary>
/// **两个方向的转换**（本模块的业务核心 · 阶段 3）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>字符 → HEX</b>：文本按 **UTF-8** 编码成字节 ⇒ 每字节**两位大写十六进制**、**空格分隔**
///     （与 `hyw/hex-sender` 的输入格式**互为逆运算** ⇒ 结果可直接复制回去再转回来）；</item>
///   <item><b>HEX → 字符</b>：空白（空格/换行/制表）**仅排版**、一律忽略；**每字节两位** ⇒
///     奇数个半字节 **失败**；再按 **UTF-8** 解码，**非法 / 被截断的序列明确报错**（不静默替换成 `�`）；</item>
///   <item><b>空输入</b> ⇒ **静默无动作**（空在各层意义一致 —— 不报错、不提示、不改结果）；</item>
///   <item><b>失败</b> ⇒ 由调用方回报到**对应输入框**（G3 红框），**不改动结果区**。</item>
/// </list>
/// </remarks>
internal static class ConverterCodec
{
    /// <summary>字符 → HEX（两位大写、空格分隔；空文本 ⇒ 失败原因置空、结果为"无动作"）。</summary>
    /// <param name="text">输入文本（原样，不做规范化）。</param>
    /// <param name="hex">结果（形如 <c>41 42</c>）。</param>
    /// <param name="failure">失败原因（成功为 <see langword="null"/>）。</param>
    /// <returns>成功为 <see langword="true"/>（空输入也算成功，但 <paramref name="hex"/> 为空串 ⇒ 调用方无动作）。</returns>
    public static bool TryToHex(string? text, out string hex, out string? failure)
    {
        hex = string.Empty;
        failure = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;    // 空 ⇒ 无动作（不报错）
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var builder = new StringBuilder(bytes.Length * 3);

        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(' ');
            }

            _ = builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        hex = builder.ToString();
        return true;
    }

    /// <summary>HEX → 字符（空白忽略、每字节两位、按 UTF-8 解码；非法序列**明确报错**）。</summary>
    /// <param name="text">输入的十六进制文本。</param>
    /// <param name="value">结果文本。</param>
    /// <param name="failure">失败原因（成功为 <see langword="null"/>）。</param>
    /// <returns>成功为 <see langword="true"/>（空输入也算成功，但 <paramref name="value"/> 为空串 ⇒ 调用方无动作）。</returns>
    public static bool TryToText(string? text, out string value, out string? failure)
    {
        value = string.Empty;
        failure = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;    // 空 ⇒ 无动作
        }

        // **空白是分隔符，不是装饰**（最严判据）：每一段都必须是**恰好两位**。
        // 丢掉空白再按两位切会把 `123456 23 55` 这种"某一段少写了一个空格"的输入
        // 静默解成 5 个字节 —— 用户看到的是一串莫名其妙的字符，而**没有任何报错**。
        // 判据取"段"而不是"总位数"：错在哪一段就点哪一段，用户才知道改哪里。
        var segments = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment.Length != 2)
            {
                failure = $"「{segment}」是 {segment.Length} 位；每段必须恰好两位（如 `0D 0A`）";
                return false;
            }

            foreach (var ch in segment)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    failure = $"「{segment}」里有非十六进制字符 '{ch}'（只接受 0-9 / A-F / a-f）";
                    return false;
                }
            }
        }

        if (segments.Length == 0)
        {
            return true;    // 全空白 ⇒ 等价于空
        }

        var bytes = new byte[segments.Length];

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(segments[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // **按 UTF-8 严格解码**：非法 / 被截断的序列明确报错（不静默替换 —— "还原"优先）
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        try
        {
            value = strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException ex)
        {
            failure = $"不是合法的 UTF-8 字节序列（{ex.Message}）";
            return false;
        }
    }

    /// <summary>
    /// 把结果文本切成**定宽行**（A5 显示源要求"同一数据源样本长度必须一致"）。
    /// </summary>
    /// <param name="text">结果文本。</param>
    /// <param name="lineBytes">每行字节数。</param>
    /// <returns>定宽行序列（**按字符边界切**，绝不切碎多字节字符）。</returns>
    public static IReadOnlyList<string> SplitLines(string text, int lineBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineBytes, 4);

        var lines = new List<string>();

        if (text.Length == 0)
        {
            return lines;
        }

        var builder = new StringBuilder(lineBytes);
        var used = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;

            if (used + runeBytes > lineBytes)
            {
                lines.Add(Finish(builder, used, lineBytes));
                _ = builder.Clear();
                used = 0;
            }

            _ = builder.Append(rune.ToString());
            used += runeBytes;
        }

        if (used > 0)
        {
            lines.Add(Finish(builder, used, lineBytes));
        }

        return lines;
    }

    private static string Finish(StringBuilder builder, int used, int lineBytes) =>
        used == lineBytes ? builder.ToString() : builder.ToString() + new string(' ', lineBytes - used);

    /// <summary>（保留）判断字节序列是否为合法 UTF-8（供测试直接断言）。</summary>
    /// <param name="bytes">字节。</param>
    /// <returns>合法为 <see langword="true"/>。</returns>
    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes) =>
        Rune.DecodeFromUtf8(bytes, out _, out var consumed) == OperationStatus.Done && consumed == bytes.Length;
}
