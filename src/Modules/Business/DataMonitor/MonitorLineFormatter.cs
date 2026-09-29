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
///   <item><b>不再有"定宽 96 字节"这回事</b>〔2026-09 用户裁定〕："一行必须 96 字节：不应该规定一行多少字节，
///     **一行多少是由文本框的宽度决定的**" ⇒ 本类**只做"字节 → 文本"**，不切块、不补齐、不截断、
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
    /// 二者都为**关**时：数据**直接追加在后面**、不做任何标记（用户裁定原文），因此不换行；
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

        if (format.Hex)
        {
            foreach (var b in payload)
            {
                _ = builder.Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            }

            if (builder.Length > 0 && builder[^1] == ' ')
            {
                _ = builder.Length--;   // 末尾不留悬空空格
            }
        }
        else
        {
            _ = builder.Append(DecodeUtf8(payload));
        }

        return builder.ToString();
    }

    /// <summary>
    /// UTF-8 解码：**非法 / 被截断的序列明确标出**（不静默替换）。
    /// </summary>
    /// <param name="chunk">字节块。</param>
    /// <returns>文本（非法字节用 `⟨XX⟩` 标出）。</returns>
    private static string DecodeUtf8(ReadOnlySpan<byte> chunk)
    {
        var builder = new StringBuilder(chunk.Length);
        var index = 0;

        while (index < chunk.Length)
        {
            var status = Rune.DecodeFromUtf8(chunk[index..], out var rune, out var consumed);

            if (status == System.Buffers.OperationStatus.Done)
            {
                _ = builder.Append(rune.ToString());
                index += consumed;
                continue;
            }

            // 非法或被截断 ⇒ **明确标出原始字节**（用户纪律：字符串方向明确报错、十六进制方向永不丢）
            _ = builder.Append(CultureInfo.InvariantCulture, $"⟨{chunk[index]:X2}⟩");
            index++;
        }

        return builder.ToString();
    }
}
