using System;
using Avalonia.Layout;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块文字的垂直度量**：把文字放在控件矩形里的哪一条基线上。
/// </summary>
/// <remarks>
/// <para>
/// 不能直接按字体行盒居中：`FontUi`（Segoe UI / Microsoft YaHei UI 回退）的行盒里
/// ascent 远大于 CJK 字形实际用到的上沿，而 `FontData`（Cascadia Mono / 宋体）的行盒更紧。
/// 两者各自居中后，同一行里的标签和读数会差 2px 左右（实测：标签墨迹中心在框中心下方
/// 1.0px，读数在框中心）。
/// </para>
/// <para>
/// 这里改用**与字体无关的光学基线**：基线固定落在内容盒中心下方
/// <see cref="OpticalBaselineEm"/> × 字号处。CJK 表意字盒与等宽数字的视觉中心都落在
/// 基线附近，标签与读数因此共享同一条基线；行盒的 ascent/descent 只影响不越界判定，
/// 不再参与"居中的基准"。多行文本仍按整块行盒居中，避免首行基线锚定把末行推出框外。
/// </para>
/// </remarks>
internal static class ModuleTextVerticalMetrics
{
    /// <summary>光学基线相对内容盒中心的下移量（× 字号）；0.35 是 CJK/等宽数字两种字体的折中。</summary>
    internal const double OpticalBaselineEm = 0.35;

    /// <summary>按对齐方式算文字原点在内容盒内需要下移多少。</summary>
    /// <param name="contentHeight">内容盒高度（已扣上下内边距）。</param>
    /// <param name="lineHeight">字体行盒高度。</param>
    /// <param name="baseline">首行基线到行盒顶部的距离。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="lineCount">行数。</param>
    /// <param name="alignment">文字在矩形内的垂直对齐。</param>
    /// <returns>相对内容盒顶部的下移量；调用方的绘制原点已经包含 `Padding.Top`。</returns>
    internal static double ComputeOffset(
        double contentHeight,
        double lineHeight,
        double baseline,
        double fontSize,
        int lineCount,
        VerticalAlignment alignment)
    {
        if (alignment == VerticalAlignment.Top)
        {
            return 0;
        }

        // 文字比内容盒还高时交给 TextBlock 自己的默认行为，不再二次偏移。
        var free = contentHeight - lineHeight;

        if (free <= 0)
        {
            return 0;
        }

        if (alignment == VerticalAlignment.Bottom)
        {
            return free;
        }

        // 多行：整块行盒居中。单行：按光学基线居中，让不同字体的标签/读数共享基线。
        if (lineCount > 1)
        {
            return free / 2;
        }

        var desired = (contentHeight / 2) + (fontSize * OpticalBaselineEm) - baseline;

        // 钳在内容盒内：不会把首行抬到内边距之上，也不会让末行越过底边。
        return Math.Clamp(desired, 0, free);
    }
}
