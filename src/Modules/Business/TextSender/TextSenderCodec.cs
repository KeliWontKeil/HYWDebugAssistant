using System.Text;

namespace HYWDebugAssistant.Modules.TextSender;

/// <summary>
/// **文本 → `RawBytes` 载荷**的编解码（本模块的业务核心 · 阶段 3）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>编码 = UTF-8</b>：ASCII 1 字节 · 中文 3 字节 ·
///     补充平面 4 字节；**ASCII 部分与「仅支持 ASCII」时代字节完全一致**（只增）；</item>
///   <item><b>`len` = 字节数</b>（不是字符数），与 C 的 `strlen` 同口径；`data` 尾部**补零**只是定长填充，
///     **`0x00` 是合法数据、不是终止符**（收方必须按 `len` 切片，禁止 NUL 截断）；</item>
///   <item><b>超长拆包</b>：超过 <paramref name="capacity"/> 字节时**模块内部**拆成多个
///     数据包（**契约不上调**），且**只按字符边界切**（绝不切在多字节序列中间）；</item>
///   <item><b>空内容静默不发</b>：编码后长度为 0 ⇒ 返回空序列（调用方什么都不做，不报错、不提示）。</item>
/// </list>
/// </remarks>
internal static class TextSenderCodec
{
    /// <summary>
    /// 从 <paramref name="start"/> 起，最多多少个**字符**（含代理对）的 UTF-8 编码不超过 <paramref name="capacity"/> 字节。
    /// </summary>
    /// <param name="text">要发送的文本（原样，不做规范化）。</param>
    /// <param name="start">起始字符下标。</param>
    /// <param name="capacity">单块字节上限（= 契约里 `data` 的长度）。</param>
    /// <returns>本块可容纳的字符数；0 = 连一个字符都放不下（只可能出现在 capacity &lt; 4 的理论情形）。</returns>
    /// <remarks>
    /// **只量字节数、不编码**：这是每包都要走的热路径，逐字符 `ToString()` + `GetBytes()` 会给每个字符
    /// 造一个短命字符串和一个 `byte[]`（1 KB 文本约 40 KB 垃圾/包）。这里只做长度累加，
    /// 真正的编码在 <see cref="EncodeChunk"/> 里一次完成。
    /// </remarks>
    public static int CountCharsInChunk(string text, int start, int capacity)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, text.Length);

        var index = start;
        var bytes = 0;

        while (index < text.Length)
        {
            var charLength = char.IsHighSurrogate(text[index])
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;

            var charBytes = Encoding.UTF8.GetByteCount(text.AsSpan(index, charLength));

            if (bytes + charBytes > capacity)
            {
                break;
            }

            bytes += charBytes;
            index += charLength;
        }

        return index - start;
    }

    /// <summary>
    /// 把一段字符按 UTF-8 编码进目标缓冲区（**不分配**）。
    /// </summary>
    /// <param name="text">要发送的文本。</param>
    /// <param name="start">起始字符下标。</param>
    /// <param name="charCount">字符数（必须来自 <see cref="CountCharsInChunk"/>）。</param>
    /// <param name="destination">目标缓冲区（至少能容纳该块的 UTF-8 字节）。</param>
    /// <returns>实际写入的字节数。</returns>
    public static int EncodeChunk(string text, int start, int charCount, Span<byte> destination) =>
        Encoding.UTF8.GetBytes(text.AsSpan(start, charCount), destination);


    /// <summary>
    /// 解析「间隔」输入（毫秒）。范围 <c>0 … MaxIntervalMs</c>。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="milliseconds">解析结果（毫秒）。</param>
    /// <returns>解析成功为 <see langword="true"/>；失败 ⇒ 调用方报错（标红），**不改动当前值**。</returns>
    /// <remarks>
    /// **上下界与文案同源**：范围取 <see cref="TextSenderModule.MaxIntervalMs"/>，报错文案也用它 ——
    /// 三者（接受 / 存储 / 文案）是同一个数，不会出现"说合法却按别的值走"。
    /// </remarks>
    public static bool TryParseInterval(string? text, out uint milliseconds)
    {
        milliseconds = 0;

        // 空 ⇒ 视为"未设置"（不改动当前值，也不报错 —— 用户还没写完不算错）
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return uint.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out milliseconds)
            && milliseconds <= TextSenderModule.MaxIntervalMs;
    }
}
