using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using HYWDebugAssistant.Core.Api;

using HYWDebugAssistant.Sdk.Text;

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

    /// <summary>从主题文件夹（`theme.json`）加载的一套。</summary>
    File,

    /// <summary>设置页里刚建出来、**还没落盘**的草稿。</summary>
    User,
}

/// <summary>
/// **一套主题的"身份"** —— 名字 · 显示名 · 类型 · 来源 · 调色板 · 壁纸文件名。
/// </summary>
/// <param name="Id">规范名（小写；见 <see cref="ThemeIds"/> = 文件夹名）。</param>
/// <param name="Title">设置里显示的名字。</param>
/// <param name="Kind">浅色 / 深色（决定继承哪一套）。</param>
/// <param name="Source">来源。</param>
/// <param name="Palette">主题的**稀疏**色板覆盖（token → `#RRGGBB(AA)`；内置两套为 <see langword="null"/>）。</param>
/// <param name="WallpaperFileName">同目录壁纸文件名（内置两套为 <see langword="null"/>）。</param>
/// <param name="WallpaperVeilOpacity">壁纸遮罩浓度（0–0.9）。</param>
/// <param name="SurfaceOpacity">控件表面不透明度（0–1）。</param>
/// <remarks>
/// <b>主题的内容就是 <paramref name="Palette"/></b>：内置值仍来自色板文件，用户/种子主题的差异只在
/// 这份稀疏表里 ⇒ 应用器不需要认识"覆盖存在设置文件的哪一层"。
/// </remarks>
public sealed record ThemeDescriptor(
    string Id,
    string Title,
    ThemeKind Kind,
    ThemeSource Source,
    IReadOnlyDictionary<string, string>? Palette = null,
    string? WallpaperFileName = null,
    double WallpaperVeilOpacity = 0.55,
    double SurfaceOpacity = 0.70)
{
    /// <summary>
    /// 这套主题的**基类型变体**（= 读内置值时的资源键）。
    /// </summary>
    /// <remarks>
    /// 内置两套直接用静态变体；**自定义主题**也以基类型变体读内置值，再叠加自己的色板。
    /// </remarks>
    public ThemeVariant Variant => Kind == ThemeKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>
    /// 这套主题在 Avalonia 里的**查找变体**（= 资源字典的键）。
    /// </summary>
    /// <remarks>
    /// 内置两套直接用静态变体；自定义主题用 <c>new ThemeVariant(id, 基类型变体)</c> ——
    /// 未定义的键沿 <c>InheritVariant</c> 回落到该类型的 Fluent 资源与内置色板。
    /// </remarks>
    public ThemeVariant LookupVariant =>
        Source == ThemeSource.BuiltIn ? Variant : new ThemeVariant(Id, Variant);

    /// <summary>这一支 token 的显式值（没写过 ⇒ <see langword="null"/> = 跟随内置 / 派生）。</summary>
    /// <param name="token">token 名。</param>
    public string? PaletteValue(string token) =>
        Palette is { } palette && palette.TryGetValue(token, out var hex) && !string.IsNullOrWhiteSpace(hex)
            ? hex
            : null;
}

/// <summary>
/// **主题目录**：有哪几套主题、各是什么类型。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>内置两套是代码里的常量</b>；**自定义主题全部来自主题文件夹**
///     （<see cref="IRuntimeQuery.GetThemeFiles"/> 的结果，磁盘上是 `themes\&lt;id&gt;\theme.json`）；</item>
///   <item><b>选择 ≠ 主题</b>：<c>跟随系统</c> 由 <see cref="VariantFor"/> 映射成 Avalonia 的
///     <see cref="ThemeVariant.Default"/>（由平台决定浅/深），它不是目录里的一条；</item>
///   <item><b>谁读它</b>：设置页（列可选主题 / 现算草稿色块）、主题应用器（按主题名取变体 / 色板）、
///     外壳（按当前主题取壁纸文件名）。</item>
/// </list>
/// </remarks>
public static class ThemeCatalog
{
    /// <summary>内置主题（顺序 = 设置里的列出顺序）。</summary>
    public static IReadOnlyList<ThemeDescriptor> Themes { get; } =
    [
        new(ThemeIds.Light, TextCatalog.Get(typeof(ThemeCatalog).Assembly, "ui.theme.light"), ThemeKind.Light, ThemeSource.BuiltIn),
        new(ThemeIds.Dark, TextCatalog.Get(typeof(ThemeCatalog).Assembly, "ui.theme.dark"), ThemeKind.Dark, ThemeSource.BuiltIn),
    ];

    /// <summary>把一份磁盘主题视图变成主题身份。</summary>
    /// <param name="file">磁盘主题（`theme.json` 的元数据）。</param>
    public static ThemeDescriptor FromFile(ThemeFileView file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ThemeDescriptor(
            ThemeIds.Normalize(file.Id),
            string.IsNullOrWhiteSpace(file.Title)
                ? TextCatalog.Get(typeof(ThemeCatalog).Assembly, "ui.theme.custom")
                : file.Title.Trim(),
            file.Base == ThemeBaseKind.Dark ? ThemeKind.Dark : ThemeKind.Light,
            ThemeSource.File,
            file.Palette,
            file.WallpaperFileName,
            Math.Clamp(file.WallpaperVeilOpacity, 0, 0.9),
            Math.Clamp(file.SurfaceOpacity, 0, 1));
    }

    /// <summary>
    /// **全部主题**：内置两套 + 磁盘主题（按 id 序；保留名 / 重复 id 跳过）。
    /// </summary>
    /// <param name="themeFiles">查询面给出的磁盘主题（`GetThemeFiles()`）。</param>
    public static IReadOnlyList<ThemeDescriptor> All(IReadOnlyList<ThemeFileView> themeFiles)
    {
        ArgumentNullException.ThrowIfNull(themeFiles);

        var result = new List<ThemeDescriptor>(Themes);

        foreach (var file in themeFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Id))
            {
                continue;
            }

            var id = ThemeIds.Normalize(file.Id);
            if (ThemeIds.IsReserved(id)
                || result.Any(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(FromFile(file));
        }

        return result;
    }

    /// <summary>按名字找一套主题（大小写不敏感；<c>System</c> 与未知名字 ⇒ <see langword="null"/>）。</summary>
    /// <param name="themes">主题清单（<see cref="All"/> 或设置页的草稿清单）。</param>
    /// <param name="id">主题名。</param>
    public static ThemeDescriptor? Find(IReadOnlyList<ThemeDescriptor> themes, string? id)
    {
        ArgumentNullException.ThrowIfNull(themes);

        if (ThemeIds.IsSystem(id) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = ThemeIds.Normalize(id);

        foreach (var theme in themes)
        {
            if (string.Equals(theme.Id, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return null;
    }

    /// <summary>
    /// 设置页显示用：把"选择值 + 系统当前浅/深"解析成一套具体主题。
    /// </summary>
    /// <param name="themes">主题清单。</param>
    /// <param name="appearance">外观块（取跟随系统的两套方案）。</param>
    /// <param name="selection">设置里的选择值。</param>
    /// <param name="systemVariant">系统当前变体（未知 ⇒ 浅色）；只有 <c>System</c> 选择会用到。</param>
    public static ThemeDescriptor ResolveDisplayTheme(
        IReadOnlyList<ThemeDescriptor> themes,
        AppearanceSettings appearance,
        string? selection,
        ThemeVariant? systemVariant = null)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(appearance);

        if (!ThemeIds.IsSystem(selection))
        {
            return Find(themes, selection) ?? Themes[0];
        }

        var dark = systemVariant is { } variant && variant == ThemeVariant.Dark;
        return ResolveSystemScheme(themes, appearance, dark ? ThemeKind.Dark : ThemeKind.Light);
    }

    /// <summary>
    /// 解析“跟随系统”某一模式使用的方案（必须是同基类型的主题；非法/缺失 ⇒ 回落到内置）。
    /// </summary>
    /// <param name="themes">主题清单。</param>
    /// <param name="appearance">外观块。</param>
    /// <param name="kind">模式（浅 / 深）。</param>
    public static ThemeDescriptor ResolveSystemScheme(
        IReadOnlyList<ThemeDescriptor> themes,
        AppearanceSettings appearance,
        ThemeKind kind)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(appearance);

        var id = kind == ThemeKind.Dark ? appearance.SystemDarkScheme : appearance.SystemLightScheme;

        if (Find(themes, id) is { } scheme && scheme.Kind == kind)
        {
            return scheme;
        }

        return Themes[kind == ThemeKind.Dark ? 1 : 0];
    }

    /// <summary>系统当前浅/深（无平台设置/无头环境 ⇒ 浅色）。</summary>
    public static ThemeVariant CurrentSystemVariant()
    {
        try
        {
            var values = Application.Current?.PlatformSettings?.GetColorValues();
            return values?.ThemeVariant == PlatformThemeVariant.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
        catch (Exception)
        {
            return ThemeVariant.Light;
        }
    }

    /// <summary>
    /// 把"当前该用哪套"解析成资源查找用的变体。
    /// </summary>
    /// <param name="themes">主题清单。</param>
    /// <param name="appearance">外观块（取跟随系统的两套方案）。</param>
    /// <param name="selection">设置里的选择值。</param>
    /// <returns>变体；<c>System</c> / 未知名字 ⇒ <see cref="ThemeVariant.Default"/>（跟随系统）。</returns>
    public static ThemeVariant VariantFor(
        IReadOnlyList<ThemeDescriptor> themes,
        AppearanceSettings appearance,
        string? selection)
    {
        if (ThemeIds.IsSystem(selection))
        {
            return ThemeVariant.Default;
        }

        return Find(themes, selection)?.LookupVariant ?? ThemeVariant.Default;
    }
}
