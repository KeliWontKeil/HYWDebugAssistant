namespace HYWDebugAssistant.Sdk.Ui.Controls.Charting;

/// <summary>
/// 图表上的一条竖向参考线（如“一个周期”的虚线）。
/// </summary>
/// <param name="X">参考线所在的 x 值（与图表 x 轴同单位）。</param>
/// <param name="Dashed">是否虚线。</param>
/// <param name="Thickness">线宽（DIP）。</param>
public sealed record ChartReferenceLine(double X, bool Dashed = true, double Thickness = 1);
