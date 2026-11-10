using System.Globalization;

namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>
/// **X 轴单位换算的唯一口径**：样本是内部事实，Index 直接显示样本，Time 显示秒。
/// </summary>
/// <remarks>
/// 模块和视图都只能通过这里换算 X 起止、跟踪范围和刻度，避免各自写一套四舍五入。
/// </remarks>
internal static class DisplayAxisUnits
{
    /// <summary>时间模式是否可用（速率必须为正有限值）。</summary>
    public static bool UsesTime(DisplayXAxisMode mode, double rate) =>
        mode == DisplayXAxisMode.Time && double.IsFinite(rate) && rate > 0;

    /// <summary>样本 → X 轴显示值。</summary>
    public static double SamplesToAxis(long samples, DisplayXAxisMode mode, double rate) =>
        UsesTime(mode, rate) ? samples / rate : samples;

    /// <summary>X 轴输入值 → 样本。</summary>
    public static long AxisToSamples(double value, DisplayXAxisMode mode, double rate) =>
        (long)Math.Round(UsesTime(mode, rate) ? value * rate : value);

    /// <summary>样本 → 当前轴单位的文本。</summary>
    public static string FormatSamples(long samples, DisplayXAxisMode mode, double rate) =>
        samples < 0
            ? string.Empty
            : UsesTime(mode, rate)
                ? (samples / rate).ToString("0.######", CultureInfo.InvariantCulture)
                : samples.ToString(CultureInfo.InvariantCulture);

    /// <summary>跟踪范围（样本）→ 当前轴单位的文本。</summary>
    public static string FormatFollowRange(int samples, DisplayXAxisMode mode, double rate) =>
        FormatSamples(samples, mode, rate);
}
