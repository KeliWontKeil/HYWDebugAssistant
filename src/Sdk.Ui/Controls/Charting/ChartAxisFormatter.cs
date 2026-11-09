using System.Globalization;

namespace HYWDebugAssistant.Sdk.Ui.Controls.Charting;

/// <summary>
/// 图表轴文字的**可读格式**：精度由刻度步长决定，宽度超出预算时逐级收敛，
/// 而不是靠改变绘图区大小去迁就文字。
/// </summary>
/// <remarks>
/// 轴标签的几何留白是固定的；文字必须适应这个留白。
/// 大数值减少小数、必要时用科学计数；小范围增加小数；索引轴保持整数。
/// </remarks>
internal static class ChartAxisFormatter
{
    /// <summary>按像素宽度估算可容纳的字符数（偏保守）。</summary>
    /// <param name="pixels">可用宽度（DIP）。</param>
    /// <param name="fontSize">字号（DIP）。</param>
    public static int MaxChars(double pixels, double fontSize)
    {
        if (!double.IsFinite(pixels) || pixels <= 0)
        {
            return 3;
        }

        var size = double.IsFinite(fontSize) && fontSize > 0 ? fontSize : 10;
        return Math.Max(3, (int)Math.Floor(pixels / (size * 0.55)));
    }

    /// <summary>格式化一个轴刻度（或游标读数）。</summary>
    /// <param name="value">值。</param>
    /// <param name="kind">索引/时间。</param>
    /// <param name="step">刻度步长（决定需要几位小数）。</param>
    /// <param name="maxChars">最大字符数。</param>
    public static string Format(double value, ChartAxisKind kind, double step, int maxChars)
    {
        if (!double.IsFinite(value))
        {
            return "—";
        }

        var limit = Math.Max(3, maxChars);

        if (kind == ChartAxisKind.Index)
        {
            return Fit(value, value.ToString("0", CultureInfo.InvariantCulture), limit, decimals: -1);
        }

        var decimals = DecimalsForStep(step);
        return Fit(value, FormatFixed(value, decimals), limit, decimals);
    }

    /// <summary>格式化一个普通数值轴刻度（Y 轴 / Y 游标）。</summary>
    public static string FormatNumber(double value, double step, int maxChars)
    {
        if (!double.IsFinite(value))
        {
            return "—";
        }

        var decimals = DecimalsForStep(step);
        return Fit(value, FormatFixed(value, decimals), Math.Max(3, maxChars), decimals);
    }

    /// <summary>刻度精度：步长越小，小数位越多。</summary>
    /// <param name="step">刻度步长。</param>
    public static int DecimalsForStep(double step)
    {
        var value = Math.Abs(step);
        if (!double.IsFinite(value) || value <= 0)
        {
            return 3;
        }

        var decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(value) - 1e-9));
        var scale = Math.Pow(10, decimals);
        if (Math.Abs((value * scale) - Math.Round(value * scale)) > 1e-6)
        {
            decimals++;
        }

        return Math.Clamp(decimals, 0, 8);
    }

    private static string FormatFixed(double value, int decimals) =>
        decimals <= 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);

    private static string Fit(double value, string initial, int maxChars, int decimals)
    {
        if (initial.Length <= maxChars)
        {
            return initial;
        }

        // 先逐级去掉小数位；索引轴 decimals = -1，直接进入科学计数兜底。
        for (var digits = Math.Min(decimals, 8); digits >= 0; digits--)
        {
            var text = FormatFixed(value, digits);
            if (text.Length <= maxChars)
            {
                return text;
            }
        }

        foreach (var format in new[] { "0.###E+0", "0.##E+0", "0.#E+0", "0E+0" })
        {
            var text = value.ToString(format, CultureInfo.InvariantCulture);
            if (text.Length <= maxChars)
            {
                return text;
            }
        }

        // 预算小到连最短科学计数都放不下：返回最短形式，交由调用方的密度控制减少刻度。
        return value.ToString("0E+0", CultureInfo.InvariantCulture);
    }
}
