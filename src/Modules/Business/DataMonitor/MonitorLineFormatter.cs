using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk.Text;

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
///     （`⟨非法字节⟩`），**不静默替换成 `�`**；<b>十六进制方向永不丢</b>（原样呈现每个字节）；
///     合法的控制/格式/私用/未分配字符**原样写进显示缓冲**，由文本控件决定显示成标记还是方框；</item>
///   <item><b>不按 `0x00` 截断</b>（用户纪律）：一切按到达的 `len` 切片 —— `0x00` 是合法数据。</item>
/// </list>
/// <para>
/// <b>为什么逐字符不产生字符串</b>：这是每包都要走一遍的热路径，一个可见字符一次
/// <c>rune.ToString()</c> 等于"每个字节一个小对象"；高频数据下这些短命对象会把 GC 压满。
/// 这里所有字符都直接写进同一个 builder，容量按最坏情况一次预留。
/// </para>
/// </remarks>
internal sealed class MonitorLineFormatter(MonitorFormat format)
{
    private const string HexDigits = "0123456789ABCDEF";

    // 跨包的 CRLF 可能被拆在两次数据里：末尾 CR 先不落标记，等下一包首字节决定 CRLF/CR。
    private bool _pendingCarriageReturn;

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
    /// <param name="destination">目标缓冲（调用方持有；本方法先清空再写）。</param>
    /// <remarks>
    /// **渲染只写目标、不返回字符串**：一包一个 `StringBuilder` + `string` 在 1 kHz 下是主要的
    /// GC 来源之一；目标由模块按 tick 复用，真正的字符串只在"要交给框架/落盘"时生成一次。
    /// </remarks>
    public void Render(ReadOnlySpan<byte> payload, DateTimeOffset timestamp, StringBuilder destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        destination.Clear();

        if (payload.Length == 0)
        {
            return;
        }

        if (format.Timestamp)
        {
            _ = destination.Append(timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("  ");
        }

        if (format.Hex)
        {
            foreach (var value in payload)
            {
                AppendHexByte(destination, value);
                _ = destination.Append(' ');
            }

            // **末尾那个空格就是分隔空格**（每字节后面已经各带一个）：
            // 十六进制是**一组数字的并列**，不隔开就分不出包边界 ——
            // `21 41 43` 接 `65 0C` 会粘成 `21 41 4365 0C`，读起来是一个长数。
            // 它是**呈现层的分隔符**（不是数据里的字节），只在十六进制方向出现。
            return;
        }

        // **文本方向一个字节都不加**：文本就是数据本身，多一个空格就是**无中生有**
        // （它会进显示缓冲、也会进保存的文件）。文本方向不隔开会粘在一起，
        // 那是"要不要每包一行"的事 —— 由 `BreakAfterPacket`（通道换行）表达，不由字符表达。
        AppendDecoded(destination, payload);
    }

    /// <summary>
    /// 把挂起的包尾 CR 落成**单独 CR** 的显示标记；没有挂起时返回 <see langword="false"/>。
    /// </summary>
    /// <param name="destination">目标（追加，不清空）。</param>
    /// <remarks>
    /// 包尾 CR 先挂起是为了把跨包的 `0D 0A` 合成一个 CRLF；但批次/tick 结束后不能再等，
    /// 否则最后一包的 CR 会永远不显示。调用点：tick 末、单包入口、通道换行前。
    /// </remarks>
    public bool FlushPending(StringBuilder destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!_pendingCarriageReturn)
        {
            return false;
        }

        _pendingCarriageReturn = false;
        _ = destination.Append('\uF001').Append('\n');
        return true;
    }

    /// <summary>把一个数据包的字节渲染成一个字符串（测试/一次性路径用）。</summary>
    /// <param name="payload">本包的有效字节（已按 `len` 切片）。</param>
    /// <param name="timestamp">本包到达时间（仅当声明了时间戳时使用）。</param>
    /// <returns>显示文本（空包 ⇒ 空串）。</returns>
    public string Render(ReadOnlySpan<byte> payload, DateTimeOffset timestamp)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        // 十六进制方向每字节固定 3 个字符（两位 + 一个分隔空格），文本方向不会更长
        // （保留哨兵码位会变成标记，但那是极少数；多留一点余量）。
        var builder = new StringBuilder(payload.Length * (format.Hex ? 3 : 2) + 24);
        Render(payload, timestamp, builder);
        return builder.ToString();
    }

    /// <summary>
    /// UTF-8 解码：**非法 / 被截断的序列**明确标出原始字节；**合法字符原样写入**。
    /// </summary>
    /// <param name="builder">目标。</param>
    /// <param name="chunk">字节块。</param>
    /// <remarks>
    /// <para>
    /// <b>换行是格式声明，不是不可显示字符</b>：`0x0A` / `0x0D0A` 原样输出为真实换行，
    /// 由显示层画 `↵`；数据里真实的换行字符保留为换行。
    /// </para>
    /// <para>
    /// **不可显示字符不由模块转义**：合法的控制/格式/私用/未分配字符直接写进显示缓冲，
    /// 由文本控件按应用设置显示成 `⟨XX⟩` 或 `□`；复制与保存因此拿到原始字符。
    /// 只有内部换行哨兵码位 U+F000–U+F002 写成字面标记，避免与哨兵协议混淆；
    /// 非法字节仍写成 `⟨XX⟩` 字面文本（没有合法字符可还原）。
    /// </para>
    /// </remarks>
    private void AppendDecoded(StringBuilder builder, ReadOnlySpan<byte> chunk)
    {
        var index = 0;

        if (_pendingCarriageReturn)
        {
            _pendingCarriageReturn = false;

            if (chunk.Length > 0 && chunk[0] == (byte)'\n')
            {
                _ = builder.Append('\uF000').Append('\n');
                index = 1;
            }
            else
            {
                _ = builder.Append('\uF001').Append('\n');
            }
        }

        while (index < chunk.Length)
        {
            var status = Rune.DecodeFromUtf8(chunk[index..], out var rune, out var consumed);

            if (status == System.Buffers.OperationStatus.Done && rune.Value is '\n' or '\r')
            {
                if (rune.Value == '\r')
                {
                    if (index + consumed < chunk.Length && chunk[index + consumed] == (byte)'\n')
                    {
                        // CRLF：一个换行，标记仍用原来的 ↵。
                        _ = builder.Append('\uF000').Append('\n');
                        index += consumed + 1;
                        continue;
                    }

                    if (index + consumed >= chunk.Length)
                    {
                        // CR 落在包尾：可能是下一包 CRLF 的前半，先挂起。
                        _pendingCarriageReturn = true;
                        index += consumed;
                        continue;
                    }

                    // 单独 CR：向下箭头。
                    _ = builder.Append('\uF001').Append('\n');
                    index += consumed;
                    continue;
                }

                // 单独 LF：向右箭头。
                _ = builder.Append('\uF002').Append('\n');
                index += consumed;
                continue;
            }

            if (status == System.Buffers.OperationStatus.Done)
            {
                // **没有可见形状的字符不再由模块转义**：原样写进显示缓冲，由文本控件
                // 决定画成 ⟨XX⟩ 还是 □。只有内部换行哨兵码位要写成字面标记，避免
                // 它们与“哨兵 + 换行”的内部协议混淆。
                if (rune.Value is >= 0xF000 and <= 0xF002)
                {
                    TextControlCharacters.AppendRuneMarker(builder, rune);
                }
                else
                {
                    AppendRune(builder, rune);
                }

                index += consumed;
                continue;
            }

            // 非法或被截断 ⇒ **明确标出原始字节**（字符串方向明确报错、十六进制方向永不丢）
            TextControlCharacters.AppendByteMarker(builder, chunk[index]);
            index++;
        }
    }

    /// <summary>把一个码位写进目标（BMP 直接写字符，代理对写两个 char，不产生临时字符串）。</summary>
    private static void AppendRune(StringBuilder builder, Rune rune)
    {
        if (rune.IsBmp)
        {
            _ = builder.Append((char)rune.Value);
            return;
        }

        Span<char> pair = stackalloc char[2];
        var written = rune.EncodeToUtf16(pair);
        _ = builder.Append(pair[..written]);
    }

    /// <summary>写出两位大写十六进制。</summary>
    private static void AppendHexByte(StringBuilder builder, byte value) =>
        _ = builder.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0xF]);
}
