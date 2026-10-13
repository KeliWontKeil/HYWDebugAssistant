using Avalonia.Styling;
using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>一套主题的**类型**（决定它继承哪一套 Fluent 资源与哪一套内置值）。</summary>
public enum ThemeKind
{
    /// <summary>浅色。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,
}

/// <summary>一套主题**从哪来**（决定它能不能被改、能不能被删）。</summary>
public enum ThemeSource
{
    /// <summary>随程序出货的内置主题（只读）。</summary>
    BuiltIn,

    /// <summary>用户在设置里改出来的一套（本轮不产生，留位）。</summary>
    User,

    /// <summary>从主题文件加载的一套（本轮不加载，留位）。</summary>
    File,
}

/// <summary>
/// **一套主题的"身份"** —— 名字 · 显示名 · 类型 · 来源。
/// </summary>
/// <param name="Id">规范名（小写；见 <see cref="ThemeIds"/>）。</param>
/// <param name="Title">设置里显示的名字。</param>
/// <param name="Kind">浅色 / 深色（决定继承哪一套）。</param>
/// <param name="Source">来源。</param>
/// <remarks>
/// <b>主题的"内容"不在这里</b>：色板值住在色板文件的主题字典里（将来是主题文件），
/// 本类型只说"有哪几套、各是什么类型"。
/// </remarks>
public sealed record ThemeDescriptor(string Id, string Title, ThemeKind Kind, ThemeSource Source)
{
    /// <summary>
    /// 这套主题在 Avalonia 里的**变体**（= 资源查找时的键）。
    /// </summary>
    /// <remarks>
    /// 内置两套直接用静态变体（色板文件里的字典就是按它们建的）；
    /// **自定义主题**用 <c>new ThemeVariant(id, 基类型变体)</c> —— 未定义的键于是沿
    /// <c>InheritVariant</c> 回落到该类型的 Fluent 资源与内置色板（已实测）。
    /// </remarks>
    public ThemeVariant Variant => Kind == ThemeKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>把"这一套"变成资源查找键（自定义主题按名字造变体，见 <see cref="Variant"/> 的说明）。</summary>
    public ThemeVariant LookupVariant => Source == ThemeSource.BuiltIn
        ? Variant
        : new ThemeVariant(Id, Variant);
}

/// <summary>
/// **主题目录**：有哪几套主题、各是什么类型。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它不是"两套"的硬编码</b>：内置两套只是今天的全部内容；加一套主题 = 在这里加一条 +
///     给它一份色板值（主题文件加载在下一轮接这一处）；</item>
///   <item><b>选择 ≠ 主题</b>：<c>跟随系统</c> 由 <see cref="VariantFor"/> 映射成 Avalonia 的
///     <see cref="ThemeVariant.Default"/>（由平台决定浅/深），它不是目录里的一条；</item>
///   <item><b>谁读它</b>：设置页（列可选主题）与主题应用器（按主题名取变体）。</item>
/// </list>
/// </remarks>
public static class ThemeCatalog
{
    /// <summary>目录里的全部主题（顺序 = 设置里的列出顺序）。</summary>
    public static IReadOnlyList<ThemeDescriptor> Themes { get; } =
    [
        new(ThemeIds.Light, "浅色", ThemeKind.Light, ThemeSource.BuiltIn),
        new(ThemeIds.Dark, "深色", ThemeKind.Dark, ThemeSource.BuiltIn),
    ];

    /// <summary>按名字找一套主题（大小写不敏感；<c>System</c> 与未知名字 ⇒ <see langword="null"/>）。</summary>
    /// <param name="id">主题名。</param>
    public static ThemeDescriptor? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = ThemeIds.Normalize(id);

        foreach (var theme in Themes)
        {
            if (string.Equals(theme.Id, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return null;
    }

    /// <summary>
    /// 把"当前该用哪套"（<c>System</c> 或某套主题的名字）解析成资源查找用的变体。
    /// </summary>
    /// <param name="selection">设置里的选择值。</param>
    /// <returns>变体；<c>System</c> / 未知名字 ⇒ <see cref="ThemeVariant.Default"/>（跟随系统）。</returns>
    public static ThemeVariant VariantFor(string? selection) =>
        Find(selection)?.LookupVariant ?? ThemeVariant.Default;
}
