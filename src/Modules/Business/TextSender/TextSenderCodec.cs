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
    /// <summary>把文本按 UTF-8 编码并**按字符边界**切成若干 ≤ <paramref name="capacity"/> 字节的块。</summary>
    /// <param name="text">要发送的文本（框架投递的原始文本，**不做任何规范化**）。</param>
    /// <param name="capacity">单块字节上限（= 契约里 `data` 的长度）。</param>
    /// <returns>若干块（空文本 ⇒ 空序列 ⇒ 调用方静默不发）。</returns>
    public static List<byte[]> SplitByCharacterBoundary(string text, int capacity)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        var chunks = new List<byte[]>();

        if (text.Length == 0)
        {
            return chunks;
        }

        var builder = new List<byte>(Math.Min(capacity, 4096));

        for (var i = 0; i < text.Length; i++)
        {
            var charLength = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? 2
                : 1;

            // 一个"字符"（含代理对）的 UTF-8 字节数：1–4
            var charBytes = Encoding.UTF8.GetByteCount(text.AsSpan(i, charLength));

            if (builder.Count + charBytes > capacity)
            {
                if (builder.Count == 0)
                {
                    // 单字符都放不下（capacity < 4 的理论情形）：放弃该字符而不是切碎它（"宁可不发也不发半个字符"）
                    continue;
                }

                chunks.Add([.. builder]);
                builder.Clear();
            }

            // 逐字符编码：代理对按 2 个 char 一起转，保证不会切在多字节序列中间
            builder.AddRange(Encoding.UTF8.GetBytes(text.AsSpan(i, charLength).ToString()));
            i += charLength - 1;
        }

        if (builder.Count > 0)
        {
            chunks.Add([.. builder]);
        }

        return chunks;
    }

    /// <summary>
    /// 把一块字节写进 `{ len, data }` 形状的定长载荷（`data` 尾部**补零**）。
    /// </summary>
    /// <param name="chunk">本块的有效字节（长度 ≤ <paramref name="capacity"/>）。</param>
    /// <param name="capacity">`data` 字节区长度。</param>
    /// <returns>载荷：`2 + capacity` 字节。</returns>
    public static byte[] BuildPayload(ReadOnlySpan<byte> chunk, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunk.Length, capacity);

        // 布局：u16 len(+0) ｜ data(+2 … +2+capacity)
        var payload = new byte[sizeof(ushort) + capacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)chunk.Length);
        chunk.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>
    /// 解析「间隔」输入（毫秒）。范围 0 – 数据类型最大值，不预设上限需求。
    /// </summary>
    /// <param name="text">输入文本（框架只保证"是数字写法"，**范围归模块**）。</param>
    /// <param name="milliseconds">解析结果（毫秒）。</param>
    /// <returns>解析成功为 <see langword="true"/>；失败 ⇒ 调用方回报失败（红框），**不改动当前值**。</returns>
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
            out milliseconds);
    }
}
