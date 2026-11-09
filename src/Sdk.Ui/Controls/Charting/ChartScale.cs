using System.Globalization;

namespace HYWDebugAssistant.Sdk.Ui.Controls.Charting;

/// <summary>
/// 图表与范围选择器共用的纯数学：nice tick、值↔像素、夹紧。
/// </summary>
/// <remarks>
/// 这是**纯函数**，不持有窗口/序列/主题；两个控件各自保存自己的状态，
/// 只共享算法，不共享事实。
/// </remarks>
internal static class ChartScale
{
    /// <summary>取一个“好看”的刻度步长（1 / 2 / 5 × 10ⁿ）。</summary>
    /// <param name="range">轴跨度（必须 &gt; 0）。</param>
    /// <param name="targetSteps">期望的刻度段数（至少 1）。</param>
    public static double NiceStep(double range, int targetSteps)
    {
        if (!(range > 0) || double.IsNaN(range) || double.IsInfinity(range))
        {
            return 1;
        }

        var target = Math.Max(1, targetSteps);
        var raw = range / target;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;

        var nice = normalized <= 1 ? 1
            : normalized <= 2 ? 2
            : normalized <= 5 ? 5
            : 10;

        return nice * magnitude;
    }

    /// <summary>把值映射到像素；范围为 0 时返回像素区间中点。</summary>
    public static double Map(double value, double min, double max, double pixelMin, double pixelMax)
    {
        if (max <= min || double.IsNaN(max - min) || double.IsInfinity(max - min))
        {
            return (pixelMin + pixelMax) / 2;
        }

        return pixelMin + ((value - min) / (max - min) * (pixelMax - pixelMin));
    }

    /// <summary>把像素映射回值；像素区间为 0 时返回最小值。</summary>
    public static double Unmap(double pixel, double min, double max, double pixelMin, double pixelMax)
    {
        if (pixelMax <= pixelMin)
        {
            return min;
        }

        return min + ((pixel - pixelMin) / (pixelMax - pixelMin) * (max - min));
    }

    /// <summary>夹紧到闭区间；上下界反了时自动交换。</summary>
    public static double Clamp(double value, double min, double max)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }

        return value < min ? min : value > max ? max : value;
    }

    /// <summary>格式化刻度的紧凑文本。</summary>
    public static string Format(double value, ChartAxisKind kind)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "—";
        }

        return kind == ChartAxisKind.Index
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>命中容差内的最近目标；没有则返回 0。</summary>
    public static int Hit(double x, double target, double tolerance) =>
        Math.Abs(x - target) <= tolerance ? 1 : 0;
}
