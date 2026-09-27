using System.Globalization;

namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **角色方块与角色信息的文本度量**（纯函数、确定值）【ADR-0053 · 用户第三十四轮】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要它</b>：用户裁定角色方块"**太大、观感不好**"，要求"**文本自适应宽度**"——
///     方块宽度必须跟着文字走，不能再是固定 128；</item>
///   <item><b>为什么是估算而不是测量</b>：方块宽度同时被**两端**用来算连线锚点
///     （本窗画自己的方块、对端窗用同一个函数算对端锚点）⇒ 必须是**确定性纯函数**，
///     不能依赖渲染期的排版结果（字体缺失/主题不同会让两侧算出不同宽度 ⇒ 线又错位）；
///     且它要能在**无头测试**里断言；</item>
///   <item><b>估算规则</b>：CJK 与全角字符按**一个字宽 = 字号**、其余按 **0.6 字号**累加，再留 5% 余量；
///     方块绘制时对超宽文本**居中省略**（`Ellipsize`）兜底，因此略微高估是安全的（低估才会截断）。</item>
/// </list>
/// </remarks>
public static class RoleTextMetrics
{
    /// <summary>角色方块 / 角色信息**第一行**字号（功能名、类型短名）——与 `ConnectionOverlay` 的绘制一致。</summary>
    public const double TitleFontSize = 11;

    /// <summary>
    /// **角色信息里的速率字号**（大字）【ADR-0053 · 用户第三十四轮："字体更大一些"】。
    /// </summary>
    public const double RateFontSize = 15;

    /// <summary>角色方块 / 角色信息**次要行**字号（`m/n`、口数、类型标签）。</summary>
    public const double DetailFontSize = 10;

    /// <summary>类型标签的最大字数（**禁止外显过长描述** —— 用户第三十四轮："当前第三行太长了"）。</summary>
    public const int MaxTypeTagLength = 8;

    /// <summary>类型标签：只取**第一个**语义标签，并按字数截断（超长加省略号）。</summary>
    /// <param name="semanticText">模块声明的语义标签（`a / b / c` 形式；可空）。</param>
    /// <returns>`类型：normal` 形式；未声明 ⇒ 空串（不占位）。</returns>
    public static string TypeTagOf(string? semanticText)
    {
        if (string.IsNullOrWhiteSpace(semanticText))
        {
            return string.Empty;
        }

        var first = semanticText
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(first))
        {
            return string.Empty;
        }

        var tag = first.Length > MaxTypeTagLength ? first[..MaxTypeTagLength] + "…" : first;
        return $"类型：{tag}";
    }

    /// <summary>方块最小宽度（放得下三个 CJK 字）——再窄就不像"块"了。</summary>
    public const double MinBoxWidth = 64;

    /// <summary>方块最大宽度（超长功能名靠省略号，不撑破窗口）。</summary>
    public const double MaxBoxWidth = 176;

    /// <summary>方块左右内边距之和。</summary>
    public const double BoxPadding = 16;

    /// <summary>估算一段文本的渲染宽度（DIP）。</summary>
    /// <param name="text">文本（可空）。</param>
    /// <param name="fontSize">字号。</param>
    /// <returns>估算宽度。</returns>
    public static double Estimate(string? text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var width = 0.0;

        foreach (var ch in text)
        {
            // 拉丁/希腊/西里尔等（≤ U+2E7F）按 0.6 字号；CJK 与全角标点（> U+2E7F）按 1 字号
            width += fontSize * (ch > 0x2E7F ? 1.0 : 0.6);
        }

        return width * 1.05;
    }

    /// <summary>按文本估算**方块宽度**（夹在 <see cref="MinBoxWidth"/>–<see cref="MaxBoxWidth"/> 之间）。</summary>
    /// <param name="title">第一行（功能名或类型短名）。</param>
    /// <param name="counts">第二行（`m/n`）。</param>
    /// <returns>方块宽度（DIP）。</returns>
    public static double BoxWidth(string? title, string? counts)
    {
        var needed = Math.Max(
            Estimate(title, TitleFontSize),
            Estimate(counts, DetailFontSize)) + BoxPadding;

        return Math.Clamp(needed, MinBoxWidth, MaxBoxWidth);
    }

    /// <summary>数字文本（千分位）——投递/丢弃这类计数在界面上统一用它（当前角色信息行未展示，先留着给后续信息位用）。</summary>
    /// <param name="value">数值。</param>
    /// <returns>`1,234` 形式。</returns>
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
