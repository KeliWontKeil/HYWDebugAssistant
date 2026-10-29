using System.Globalization;

namespace HYWDebugAssistant.Sdk.Ui.Formatting;

/// <summary>字节数量的**值 + 单位**（单位已含可选 <c>/s</c>）。</summary>
/// <param name="Value">给人看的数值文本（不含单位）。</param>
/// <param name="Unit">单位（<c>B</c> / <c>KB</c> / <c>MB</c> / <c>GB</c>，可带 <c>/s</c>）。</param>
public readonly record struct ByteQuantityParts(string Value, string Unit);

/// <summary>
/// **字节数量的唯一格式化实现**（B / KB / MB / GB，可选每秒）。
/// </summary>
/// <remarks>
/// 三种调用方 —— **模块控件**（<c>ModuleNumeric</c>）、**模块窗口指标**（<c>ModuleWindowsViewModel</c>）、
/// **拓扑指标**（<c>TopologyViewModel</c>）—— 共用这一份；禁止再写第二套 B/KB/MB 分支。
/// 规则：非有限/负数按 0；&lt;1024 整数 B；之后每 1024 进一档；
/// 缩放后 &lt;10 保留 2 位小数（尾随 0 收敛到 1 位，如 <c>1.00 → 1.0</c>），否则 1 位。
/// </remarks>
public static class ByteQuantity
{
    private const double Kibi = 1024;
    private const double Mebi = 1024 * 1024;
    private const double Gibi = 1024 * 1024 * 1024;

    /// <summary>拆分字节数量为值 + 单位。</summary>
    /// <param name="value">字节数（可带小数；负数/非有限按 0）。</param>
    /// <param name="perSecond">是否为速率（单位加 <c>/s</c>）。</param>
    public static ByteQuantityParts Split(double value, bool perSecond = false)
    {
        var suffix = perSecond ? "/s" : string.Empty;

        if (!double.IsFinite(value) || value < 0)
        {
            value = 0;
        }

        if (value < Kibi)
        {
            return new ByteQuantityParts(
                value.ToString("0", CultureInfo.CurrentCulture),
                "B" + suffix);
        }

        var (scaled, unit) = value switch
        {
            < Mebi => (value / Kibi, "KB"),
            < Gibi => (value / Mebi, "MB"),
            _ => (value / Gibi, "GB"),
        };

        var text = scaled < 10
            ? TrimTrailingZero(scaled.ToString("0.00", CultureInfo.CurrentCulture))
            : scaled.ToString("0.0", CultureInfo.CurrentCulture);

        return new ByteQuantityParts(text, unit + suffix);
    }

    /// <summary>一次性字符串：<c>Value + " " + Unit</c>（供上层直接用）。</summary>
    /// <param name="value">字节数。</param>
    /// <param name="perSecond">是否为速率。</param>
    public static string Format(double value, bool perSecond = false)
    {
        var parts = Split(value, perSecond);
        return parts.Value + " " + parts.Unit;
    }

    /// <summary>把 <c>x.00</c> 收敛成 <c>x.0</c>（保留一位小数；不是 2 位四舍五入后再抹掉有效位）。</summary>
    private static string TrimTrailingZero(string text) =>
        text.EndsWith('0') && text.Contains('.', StringComparison.Ordinal)
            ? text[..^1]
            : text;
}
