namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **主题与外观的规范名**（唯一一处）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>主题 ≠ 模式</b>：一套主题有**身份**（<see cref="Light"/> / <see cref="Dark"/>，将来还有用户/文件来的），
///     而"浅还是深"是它的**类型**；<see cref="System"/> 不是主题，是**选择**（跟随系统 ⇒ 按系统类型挑一套）；</item>
///   <item><b>大小写不敏感</b>：<see cref="Normalize"/> 把旧值（`Light` / `Dark` / `System`）与手写的大小写
///     统一成规范名 ⇒ 旧的设置文件与旧的覆盖键**不需要迁移脚本**就继续有效；</item>
///   <item><b>类型</b>（浅/深）住在主题目录里（`Ui/Theming/ThemeCatalog.cs`），不在这里 —— 这里只有名字。</item>
/// </list>
/// </remarks>
public static class ThemeIds
{
    /// <summary>内置主题：浅色。</summary>
    public const string Light = "light";

    /// <summary>内置主题：深色。</summary>
    public const string Dark = "dark";

    /// <summary>**选择项**：跟随系统（不是一套主题，故不受"覆盖"影响）。</summary>
    public const string System = "System";

    /// <summary>内置主题（顺序 = 设置里列出的顺序 = 浅色在前）。</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = [Light, Dark];

    /// <summary>
    /// 规范化主题名：空 ⇒ <see cref="System"/>；`System`（任意大小写）⇒ <see cref="System"/>；其余 ⇒ 小写。
    /// </summary>
    /// <param name="id">用户/文件里写的名字。</param>
    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return System;
        }

        var trimmed = id.Trim();
        return trimmed.Equals(System, StringComparison.OrdinalIgnoreCase) ? System : trimmed.ToLowerInvariant();
    }
}
