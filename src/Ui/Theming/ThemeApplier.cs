using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>
/// **主题应用器**：把"当前选中的主题 + 它的稀疏色板"算成一组资源值，装进一份合并字典。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>层次</b>：外观（<see cref="AppearanceSettings"/> 只记选择）→ 主题（<see cref="ThemeCatalog.All"/> 里的一套，
///     来自内置代码或主题文件夹的 `palette`）→ 渲染。本类**不认识任何具体 token 名**：
///     一支色算成什么，由色板契约（`Styles/palette.json`）说；</item>
///   <item><b>每支一条解析链</b>：<c>主题色板里的显式值 ?? 契约里的派生规则(源值) ?? 色板里的内置值</c>。
///     派生规则只吃四个输入（源当前值 / 这一支内置值 / 源内置值 / 规则参数），算法在
///     <see cref="PaletteDerivation"/>。于是"主题改了主题色 ⇒ 派生支跟着变"与
///     "主题没写 ⇒ 派生结果 == 内置值"两件事同时成立；</item>
///   <item><b>主题的承载</b>：一套主题 = Avalonia 的一个 <see cref="ThemeVariant"/>（内置两套直接用静态变体；
///     自定义主题用 <c>new ThemeVariant(id, 基类型变体)</c> —— 未定义的键沿 <c>InheritVariant</c>
///     回落到该类型的 Fluent 资源与内置色板，已实测）；</item>
///   <item><b>怎么替换</b>：本类**自己持有一份覆盖字典**（按主题分桶），每次应用时把它
///     **合并到 <c>Application.Resources.MergedDictionaries</c> 的末尾**，下一次应用前先撤掉自己那一份。
///     两条实测结论决定了这个写法：
///     <br/>① **合并字典按声明顺序找、后加的赢** —— 加在末尾才盖得住色板里的内置值
///     （加在最前面会被内置值盖住，那就是"改了没反应"）；
///     <br/>② **加 / 撤合并字典会让已经在树上的 <c>DynamicResource</c> 立刻重解析** ——
///     不必重建控件树，改色当场可见。</item>
///   <item><b>设置页草稿预览</b>：<see cref="PaletteBrush"/> 不依赖资源字典里是否已装覆盖，
///     而是按"基类型内置值 + 主题色板 + 派生"现算 —— 于是设置了但尚未「应用」的主题也能正确显示色块；</item>
///   <item><b>浓度取内置值</b>：淡底（页签、按钮指向、选中叠加）只换**色**、不换**浓度** ——
///     浓度从该套主题内置的那一支上读，于是"深浅两套各自的浓度"仍只有一个事实源（色板）。</item>
/// </list>
/// </remarks>
public static class ThemeApplier
{
    /// <summary>上一次装上去的那一份覆盖字典（每次应用前先撤掉它，再按新设置装一份）。</summary>
    private static ResourceDictionary? _installed;

    /// <summary>把设置应用到当前应用（无应用实例时静默返回——测试/无头环境）。</summary>
    /// <param name="settings">应用级设置。</param>
    /// <param name="themeFiles">查询面给出的磁盘主题（`IRuntimeQuery.GetThemeFiles()`；内置两套不在里面）。</param>
    /// <remarks>
    /// **线程编组**：Avalonia 资源与 <c>RequestedThemeVariant</c> 只能在 **UI 线程**改；
    /// 若从其他线程调用（例如测试代码），此处自动 <c>Post</c> 到 UI 线程，而不是抛跨线程异常。
    /// </remarks>
    public static void Apply(ApplicationSettings settings, IReadOnlyList<ThemeFileView> themeFiles)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeFiles);

        if (Application.Current is not { } app)
        {
            return;
        }

        var themes = ThemeCatalog.All(themeFiles);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyCore(app, settings, themes));
            return;
        }

        ApplyCore(app, settings, themes);
    }

    /// <summary>解析颜色文本（非法则返回 null，调用方回退到主题默认）。</summary>
    /// <param name="text">颜色文本（如 <c>#2563EB</c>）。</param>
    public static Color? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Color.TryParse(text.Trim(), out var color) ? color : null;
    }

    /// <summary>
    /// 取某一套主题里**当前**某个 token 的画刷（按主题色板现算；不要求该主题已经应用）。
    /// </summary>
    /// <param name="token">色板 token 名（如 <c>TextPrimary</c>）。</param>
    /// <param name="theme">主题身份（内置或主题文件夹；也可以是设置页的草稿身份）。</param>
    /// <returns>画刷（找不到 / 无应用实例 ⇒ <see langword="null"/>）。</returns>
    public static IBrush? PaletteBrush(string token, ThemeDescriptor theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (Application.Current is not { } app || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var (color, opacity) = Resolve(app, theme, token);
        return Solid(color, opacity);
    }

    private static void ApplyCore(
        Application app,
        ApplicationSettings settings,
        IReadOnlyList<ThemeDescriptor> themes)
    {
        // **选择 → 变体**：`System` ⇒ Default（由平台决定浅/深）；某套主题 ⇒ 它自己的查找变体。
        app.RequestedThemeVariant = ThemeCatalog.VariantFor(
            themes,
            settings.Appearance,
            settings.Appearance.ThemeSelection);

        Uninstall(app);

        var colors = BuildOverrides(app, themes, settings.Appearance, settings.Appearance.ThemeSelection);
        var fonts = BuildFontOverrides(settings);

        if (colors.Count == 0 && fonts.Count == 0)
        {
            return;
        }

        var dictionary = new ResourceDictionary();

        // 平铺项（字体）：主题无关，直接住在这份字典的平铺部分
        foreach (var (key, value) in fonts)
        {
            dictionary[key] = value;
        }

        // 按套的色：一套主题一个主题字典（键 = 该套主题的查找变体）
        foreach (var (theme, variant, entries) in colors)
        {
            var scoped = new ResourceDictionary();

            foreach (var (key, value) in entries)
            {
                scoped[key] = value;
            }

            dictionary.ThemeDictionaries[variant] = scoped;
        }

        // **加到最后**：合并字典按声明顺序找，后加的赢（加在最前会被色板里的内置值盖住）
        app.Resources.MergedDictionaries.Add(dictionary);
        _installed = dictionary;
    }

    /// <summary>撤掉上一次装上去的覆盖字典（于是所有 token 回到色板里的内置值）。</summary>
    private static void Uninstall(Application app)
    {
        if (_installed is { } dictionary)
        {
            _ = app.Resources.MergedDictionaries.Remove(dictionary);
            _installed = null;
        }
    }

    /// <summary>
    /// **字体覆盖**（外观的第二个轴）：<c>FontUi</c>（界面文字）/ <c>FontData</c>（所有输入框与显示框）。
    /// </summary>
    /// <remarks>
    /// 每条轴由"西文 + 中文"两个字段组合成一个 FontFamily 串（拉丁在前 / 中文在后）；
    /// 空串 ⇒ 不覆盖（回落色板文件里的内置值）。
    /// </remarks>
    private static Dictionary<string, object> BuildFontOverrides(ApplicationSettings settings)
    {
        var appearance = settings.Appearance;
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal);

        if (appearance.UiFonts.Compose() is { Length: > 0 } ui)
        {
            overrides["FontUi"] = new FontFamily(ui);
        }

        if (appearance.DataFonts.Compose() is { Length: > 0 } data)
        {
            overrides["FontData"] = new FontFamily(data);
        }

        return overrides;
    }

    /// <summary>
    /// 按色板契约算出**当前要生效的主题**要写进资源的值（主题 → token → 画刷）。
    /// </summary>
    /// <param name="app">当前应用（读内置值用）。</param>
    /// <param name="themes">主题清单。</param>
    /// <param name="appearance">外观块（取跟随系统的两套方案）。</param>
    /// <param name="selection">当前主题选择（<c>System</c> / 内置 / 自定义）。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>直接选某套主题 ⇒ 只写这套，键 = 它的查找变体；</item>
    ///   <item><b>跟随系统</b> ⇒ 写两套：浅色模式方案键到 <see cref="ThemeVariant.Light"/>、
    ///     深色模式方案键到 <see cref="ThemeVariant.Dark"/>；系统切换时由平台挑对应字典；</item>
    ///   <item>只写"主题确实写过的 token、或派生自写过的源"的 token；其余交给色板内置值。</item>
    /// </list>
    /// </remarks>
    private static List<(ThemeDescriptor Theme, ThemeVariant Key, Dictionary<string, object> Entries)> BuildOverrides(
        Application app,
        IReadOnlyList<ThemeDescriptor> themes,
        AppearanceSettings appearance,
        string? selection)
    {
        var targets = new List<(ThemeDescriptor Theme, ThemeVariant Key)>();

        if (ThemeIds.IsSystem(selection))
        {
            targets.Add((ThemeCatalog.ResolveSystemScheme(themes, appearance, ThemeKind.Light), ThemeVariant.Light));
            targets.Add((ThemeCatalog.ResolveSystemScheme(themes, appearance, ThemeKind.Dark), ThemeVariant.Dark));
        }
        else if (ThemeCatalog.Find(themes, selection) is { } theme)
        {
            targets.Add((theme, theme.LookupVariant));
        }

        var result = new List<(ThemeDescriptor Theme, ThemeVariant Key, Dictionary<string, object> Entries)>();

        foreach (var (theme, key) in targets)
        {
            var overrides = new Dictionary<string, object>(StringComparer.Ordinal);

            var surfaceOpacity = Math.Clamp(theme.SurfaceOpacity, 0, 1);

            foreach (var token in PaletteManifest.Tokens)
            {
                var explicitHex = theme.PaletteValue(token.Key);
                var hasExplicit = explicitHex is not null && Parse(explicitHex) is not null;

                var sourceOverridden = token.Derive is { Mode: not PaletteDeriveMode.None } rule
                    && theme.PaletteValue(rule.Source) is { } sourceHex
                    && Parse(sourceHex) is not null;

                // 表面透明度变化本身也要产生覆盖条目，否则滑块改了却没有效果。
                var surfaceNeedsEntry = token.Surface && surfaceOpacity < 0.999;

                if (!hasExplicit && !sourceOverridden && !surfaceNeedsEntry)
                {
                    continue;
                }

                var (color, opacity) = Resolve(app, theme, token.Key);
                if (token.Surface)
                {
                    opacity *= surfaceOpacity;
                }

                overrides[token.Key] = Solid(color, opacity);
            }

            if (overrides.Count > 0)
            {
                result.Add((theme, key, overrides));
            }
        }

        return result;
    }

    /// <summary>
    /// 解析某一套主题里某一支的**当前有效色**（草稿也可）：主题色板 → 派生 → 基类型内置值。
    /// </summary>
    /// <param name="app">当前应用（读内置值用）。</param>
    /// <param name="theme">主题身份（内置或自定义）。</param>
    /// <param name="token">token 名。</param>
    private static (Color Color, double Opacity) Resolve(
        Application app,
        ThemeDescriptor theme,
        string token)
    {
        // 内置值一律按**基类型变体**读：自定义主题的查找变体会沿 InheritVariant 回落，
        // 但这里刻意读基类型，避免把上一轮装进去的自定义覆盖误当成"内置值"。
        var builtIn = ReadBuiltIn(app, theme, token);

        if (theme.PaletteValue(token) is { } explicitHex && Parse(explicitHex) is { } explicitColor)
        {
            return (explicitColor, explicitColor.A < 255 ? explicitColor.A / 255.0 : builtIn.Opacity);
        }

        if (PaletteManifest.Find(token)?.Derive is { Mode: not PaletteDeriveMode.None } rule)
        {
            var sourceBuiltIn = ReadBuiltIn(app, theme, rule.Source);
            var source = theme.PaletteValue(rule.Source) is { } sourceHex
                && Parse(sourceHex) is { } sourceColor
                    ? (sourceColor, sourceColor.A < 255 ? sourceColor.A / 255.0 : sourceBuiltIn.Opacity)
                    : sourceBuiltIn;

            var derived = PaletteDerivation.Derive(rule, source.Item1, builtIn.Color, sourceBuiltIn.Color);
            return (derived, builtIn.Opacity);
        }

        return builtIn;
    }

    /// <summary>读某一套主题里某一支的**内置值**（资源缺失 ⇒ 透明，且不参与派生）。</summary>
    /// <param name="app">当前应用。</param>
    /// <param name="theme">主题（自定义缺失 token 时回落基类型）。</param>
    /// <param name="key">token 名。</param>
    private static (Color Color, double Opacity) ReadBuiltIn(Application app, ThemeDescriptor theme, string key)
    {
        return app.TryGetResource(key, theme.Variant, out var value) && value is ISolidColorBrush brush
            ? (brush.Color, brush.Opacity)
            : (Colors.Transparent, 1);
    }

    /// <summary>造一支不可变纯色画刷（先按不透明度归一，再交给画刷）。</summary>
    /// <param name="color">颜色（其 alpha 通道被忽略，统一由 <paramref name="opacity"/> 表达）。</param>
    /// <param name="opacity">不透明度（0–1）。</param>
    private static ImmutableSolidColorBrush Solid(Color color, double opacity) =>
        new(Color.FromRgb(color.R, color.G, color.B), Math.Clamp(opacity, 0, 1));
}
