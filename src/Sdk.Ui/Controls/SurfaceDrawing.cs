using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>自绘控件共用的文字与几何小工具。</summary>
/// <remarks>
/// 文字只有一种构造方式（<see cref="FormattedText"/> + 默认字体），不在控件里另立字体语义；
/// 颜色由调用方的 token 化画刷给出，这里不做任何兜底色。
/// </remarks>
internal static class SurfaceDrawing
{
    /// <summary>是否有一句真的文字（空白视为没有）。</summary>
    /// <param name="text">候选文本。</param>
    public static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>按给定字号与画刷构造一段文字。</summary>
    /// <param name="text">文本。</param>
    /// <param name="fontSize">字号（DIP）。</param>
    /// <param name="brush">画刷。</param>
    public static FormattedText Format(string text, double fontSize, IBrush brush) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            brush);

    /// <summary>以 <paramref name="centerX"/> 为中心画一行，并夹在可用宽度内。</summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="text">已构造的文字。</param>
    /// <param name="centerX">中心 X（控件本地坐标）。</param>
    /// <param name="top">顶部 Y。</param>
    /// <param name="limit">可用宽度（用于夹紧，避免跑到控件外）。</param>
    public static void DrawCentered(
        DrawingContext context,
        FormattedText text,
        double centerX,
        double top,
        double limit)
    {
        var maxLeft = Math.Max(0, limit - text.Width);
        var left = Math.Clamp(centerX - (text.Width / 2), 0, maxLeft);
        context.DrawText(text, new Point(left, top));
    }

    /// <summary>右对齐画一行。</summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="text">已构造的文字。</param>
    /// <param name="right">右边界 X。</param>
    /// <param name="top">顶部 Y。</param>
    public static void DrawRight(DrawingContext context, FormattedText text, double right, double top) =>
        context.DrawText(text, new Point(Math.Max(0, right - text.Width), top));

    /// <summary>把浮点数按小数位格式化（固定区域，不随当前区域设置变）。</summary>
    /// <param name="value">值。</param>
    /// <param name="decimals">小数位（0–6）。</param>
    public static string FormatNumber(double value, int decimals)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var digits = Math.Clamp(decimals, 0, 6);
        return digits <= 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);
    }
}
