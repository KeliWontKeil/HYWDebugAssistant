using System.Globalization;
using System.Text;

namespace HYWDebugAssistant.Modules.DataMonitor;

/// <summary>显示格式选项（四个复选框跑出来的组合）。</summary>
/// <param name="Hex">十六进制显示（`AA BB CC`）而不是文本。</param>
/// <param name="Timestamp">每包加 `HH:mm:ss.fff` 时间戳前缀。</param>
/// <param name="LineBreakPerPacket">「数据包换行」：每个数据包一行。</param>
internal readonly record struct MonitorFormat(bool Hex, bool Timestamp, bool LineBreakPerPacket);

/// <summary>
/// **收到的字节 → 显示文本**的格式化（本模块的业务核心 · 阶段 3）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>没有「定宽 96 字节」这回事</b>：一行多少字节**不应该由模块规定**，
///     **一行多少是由文本框的宽度决定的** ⇒ 本类**只做「字节 → 文本」**，不切块、不补齐、不截断、
///     不加任何标记 —— 数据一个字节都不丢；</item>
///   <item><b>换行不由文本表达</b>：要不要换行是**通道调用**的事（见 <see cref="BreakAfterPacket"/> 与
///     `IViewDisplayAccess.AppendText` / `AppendLine`）—— 通道引进的换行**不是字符**，
///     因此显示层不会给它画 `↵`（数据里真有的 `\n` 才画）；</item>
///   <item><b>文本方向按 UTF-8 解码</b>（与其余三个模块同一口径）：非法 / 被截断的字节序列**明确标出**
///     （`⟨非法字节⟩`），**不静默替换成 `�`**；<b>十六进制方向永不丢</b>（原样呈现每个字节）；</item>
///   <item><b>不按 `0x00` 截断</b>（用户纪律）：一切按到达的 `len` 切片 —— `0x00` 是合法数据。</item>
/// </list>
/// </remarks>
internal sealed class MonitorLineFormatter(MonitorFormat format)
{
    /// <summary>
    /// 本包文本之后，换行是否**由通道**补（= 「数据包换行」或「时间戳」开着）。
    /// </summary>
    /// <remarks>
    /// 二者都为**关**时：数据**直接追加在后面**、不做任何标记，因此不换行；
    /// 于是"行"只由**文本框宽度**（自动折行）和数据里真有的换行字符决定。
    /// 时间戳必然带来换行：它是"每一包/每一行一个"的前缀，不换行的话第二包的时间戳会挤在上一包末尾。
    /// </remarks>
    public bool BreakAfterPacket => format.LineBreakPerPacket || format.Timestamp;

    /// <summary>把一个数据包的字节渲染成**显示文本**（不切块、不补齐、不加标记）。</summary>
    /// <param name="payload">本包的有效字节（已按 `len` 切片）。</param>
    /// <param name="timestamp">本包到达时间（仅当声明了时间戳时使用）。</param>
    /// <returns>显示文本（空包 ⇒ 空串）。</returns>
    public string Render(ReadOnlySpan<byte> payload, DateTimeOffset timestamp)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(payload.Length + 24);

        if (format.Timestamp)
        {
            _ = builder.Append(timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("  ");
        }

        var body = new StringBuilder(payload.Length * 3);

        if (format.Hex)
        {
            foreach (var b in payload)
            {
                _ = body.Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            }

            // **末尾那一个空格就是分隔空格**（每字节后面已经各带一个）：
            // 十六进制是**一组数字的并列**，不隔开就分不出包边界 ——
            // `21 41 43` 接 `65 0C` 会粘成 `21 41 4365 0C`，读起来是一个长数。
            // 它是**呈现层的分隔符**（不是数据里的字节），只在十六进制方向出现。
            return builder.Append(body).ToString();
        }

        // **文本方向一个字节都不加**：文本就是数据本身，多一个空格就是**无中生有**
        // （它会进显示缓冲、也会进保存的文件）。文本方向不隔开会粘在一起，
        // 那是"要不要每包一行"的事 —— 由 `BreakAfterPacket`（通道换行）表达，不由字符表达。
        _ = body.Append(DecodeUtf8(payload));

        return builder.Append(body).ToString();
    }

    /// <summary>
    /// UTF-8 解码：**非法 / 被截断的序列**与**不可显示的字符**都**明确标出**（不静默替换、不静默当换行）。
    /// </summary>
    /// <param name="chunk">字节块。</param>
    /// <returns>文本（不可显示的部分用 `⟨XX⟩` 标出）。</returns>
    /// <remarks>
    /// <para>
    /// <b>一条统一的标准（两个方向都一样）</b>：凡是**屏幕上没有可见形状**的东西，一律写成
    /// <c>⟨XX⟩</c>（该字符的原始字节，大写十六进制、逐字节排列）——
    /// <list type="bullet">
    ///   <item>ASCII 控制字符：`0x0C` ⇒ <c>⟨0C⟩</c>、换行 `0x0A` ⇒ <c>⟨0A⟩</c>、制表 `0x09` ⇒ <c>⟨09⟩</c>；</item>
    ///   <item>被删字符 `0x7F` ⇒ <c>⟨7F⟩</c>；</item>
    ///   <item>非 ASCII 的不可显示码位（如 U+0085）⇒ 它的 UTF-8 原始字节，如 <c>⟨C2 85⟩</c>；</item>
    ///   <item>非法 / 被截断的字节 ⇒ 单独那个字节，如 <c>⟨A1⟩</c>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为什么不能"按原样塞进去"</b>：控制字符进到文本里，显示层要么不画（看起来像丢了数据），
    /// 要么按它自己的语义走 —— `0x0C`（换页）/ `0x0A`（换行）会让**显示自己跳行**，
    /// 而那个"换行"并不是数据里的一条记录边界，用户看到的行结构于是与数据对不上。
    /// </para>
    /// <para>
    /// <b>它是"所见即所存"的一部分</b>：显示的就是存下来的 —— 保存出来的 `.txt` 里同样是 <c>⟨0A⟩</c>，
    /// 不会在文件里突然变成真实换行（那样"屏幕上看到的行数"与"文件里的行数"就再也对不上了）。
    /// </para>
    /// </remarks>
    private static string DecodeUtf8(ReadOnlySpan<byte> chunk)
    {
        var builder = new StringBuilder(chunk.Length);
        var index = 0;

        while (index < chunk.Length)
        {
            var status = Rune.DecodeFromUtf8(chunk[index..], out var rune, out var consumed);

            if (status == System.Buffers.OperationStatus.Done && IsVisible(rune))
            {
                _ = builder.Append(rune.ToString());
                index += consumed;
                continue;
            }

            if (status == System.Buffers.OperationStatus.Done)
            {
                // **合法但屏幕上没有形状** ⇒ 标出它的原始字节（逐个字节，便于与十六进制方向对照）
                for (var i = 0; i < consumed; i++)
                {
                    _ = builder.Append(CultureInfo.InvariantCulture, $"⟨{chunk[index + i]:X2}⟩");
                }

                index += consumed;
                continue;
            }

            // 非法或被截断 ⇒ **明确标出原始字节**（用户纪律：字符串方向明确报错、十六进制方向永不丢）
            _ = builder.Append(CultureInfo.InvariantCulture, $"⟨{chunk[index]:X2}⟩");
            index++;
        }

        return builder.ToString();
    }

    /// <summary>这个码位在屏幕上有可见形状吗（决定"原样显示"还是"写成 ⟨XX⟩"）。</summary>
    /// <param name="rune">码位。</param>
    /// <returns>有可见形状为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 判据取 <see cref="Rune.GetUnicodeCategory(Rune)"/>：只有 <c>Cc</c>（控制）/<c>Cf</c>（格式）/
    /// <c>Cs</c>（代理）/<c>Co</c>（私用）/<c>Cn</c>（未分配）/<c>Zl</c>/<c>Zp</c>（U+2028 / U+2029 这类会跳行的分隔符）
    /// 被算作"没有可见形状"。**ASCII 空格 U+0020 算可见**（它是 <c>Zs</c>）——
    /// 空格当然要照原样留着，否则 `41 42` 那种文本会被写成 `⟨20⟩⟨20⟩`，没法看。
    /// <b>制表符不算可见</b>：它没有自己的字形，画出来是"一段不定宽的空白"，位置与宽度由制表位决定。
    /// </remarks>
    private static bool IsVisible(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.Control => false,
        UnicodeCategory.Format => false,
        UnicodeCategory.Surrogate => false,
        UnicodeCategory.PrivateUse => false,
        UnicodeCategory.OtherNotAssigned => false,
        UnicodeCategory.LineSeparator => false,
        UnicodeCategory.ParagraphSeparator => false,
        _ => true,
    };
}
