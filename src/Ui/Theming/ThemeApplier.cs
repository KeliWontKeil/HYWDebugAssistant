using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>
/// **主题应用器**：把"当前生效的那套主题 + 用户的逐项覆盖"算成一组资源值，装进一份合并字典。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>层次</b>：外观（<see cref="AppearanceSettings"/>）→ 主题（<see cref="ThemeCatalog"/> 里的一套，
///     由 <c>ThemeSelection</c> 选中）→ 色板（主题内容 = 色板里的值；用户覆盖 = 差异）→ 渲染。
///     本类**不认识任何具体 token 名**：一支色算成什么，由色板契约（`Styles/palette.json`）说；</item>
///   <item><b>每支一条解析链</b>：<c>用户的显式覆盖 ?? 契约里的派生规则(源值) ?? 色板里的内置值</c>。
///     派生规则只吃四个输入（源当前值 / 这一支内置值 / 源内置值 / 规则参数），算法在
///     <see cref="PaletteDerivation"/>。于是"用户改了主题色 ⇒ 派生支跟着变"与
///     "用户没改 ⇒ 派生结果 == 内置值"两件事同时成立；</item>
///   <item><b>主题的承载</b>：一套主题 = Avalonia 的一个 <see cref="ThemeVariant"/>（内置两套直接用静态变体；
///     自定义 / 文件来的主题用 <c>new ThemeVariant(id, 基类型变体)</c> —— 未定义的键沿 <c>InheritVariant</c>
///     回落到该类型的 Fluent 资源与内置色板，已实测）。"加第四套主题"因此不需要改本类；</item>
///   <item><b>怎么替换</b>：本类**自己持有一份覆盖字典**（按主题分桶），每次应用时把它
///     **合并到 <c>Application.Resources.MergedDictionaries</c> 的末尾**，下一次应用前先撤掉自己那一份。
///     两条实测结论决定了这个写法：
///     <br/>① **合并字典按声明顺序找、后加的赢** —— 加在末尾才盖得住色板里的内置值
///     （加在最前面会被内置值盖住，那就是"改了没反应"）；
///     <br/>② **加 / 撤合并字典会让已经在树上的 <c>DynamicResource</c> 立刻重解析** ——
///     不必重建控件树，改色当场可见。
///     <br/>与"改写色板字典里的键值"相比，这个写法**不依赖宿主资源图的形状**：
///     XAML 里的 <c>&lt;ResourceInclude&gt;</c> 在运行期是**不可写**的 provider（实测），
///     按"找到那份字典再改键"的写法在某些宿主里一条都改不到；</item>
///   <item><b>按套</b>：同一支 token 浅深本就是两个值（<c>BgModule</c> 浅色是白、深色是深灰），
///     故每条覆盖 / 派生值都记在**它那一套的主题字典**里 —— 改浅色的 <c>BgModule</c> 不会动深色那一套；</item>
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
    /// <remarks>
    /// **线程编组**：Avalonia 资源与 <c>RequestedThemeVariant</c> 只能在 **UI 线程**改；
    /// 若从其他线程调用（例如测试代码），此处自动 <c>Post</c> 到 UI 线程，而不是抛跨线程异常。
    /// </remarks>
    public static void Apply(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (Application.Current is not { } app)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyCore(app, settings));
            return;
        }

        ApplyCore(app, settings);
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
    /// 取某一套主题里**当前**某个 token 的画刷（没被覆盖时 = 该套的内置值）。
    /// </summary>
    /// <param name="token">色板 token 名（如 <c>TextPrimary</c>）。</param>
    /// <param name="theme">主题名（见 <see cref="ThemeIds"/>，如 <c>light</c> / <c>dark</c>）。</param>
    /// <returns>画刷（找不到 ⇒ <see langword="null"/>）。</returns>
    /// <remarks>
    /// 设置面板的色块用它：**另一套主题**的内置值只能在资源里读，不能靠切主题去看。
    /// </remarks>
    public static IBrush? PaletteBrush(string token, string theme)
    {
        if (Application.Current is not { } app || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var variant = ThemeCatalog.Find(theme)?.LookupVariant;

        return variant is { } lookup && app.TryGetResource(token, lookup, out var value) && value is IBrush brush
            ? brush
            : null;
    }

    private static void ApplyCore(Application app, ApplicationSettings settings)
    {
        // **选择 → 变体**：`System` ⇒ Default（由平台决定浅/深）；某套主题 ⇒ 它自己的变体。
        app.RequestedThemeVariant = ThemeCatalog.VariantFor(settings.Appearance.ThemeSelection);

        Uninstall(app);

        var colors = BuildOverrides(app, settings);
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

        // 按套的色：一套主题一个主题字典（键 = 该套主题的变体）
        foreach (var (themeId, entries) in colors)
        {
            if (ThemeCatalog.Find(themeId) is not { } theme)
            {
                continue;
            }

            var scoped = new ResourceDictionary();

            foreach (var (key, value) in entries)
            {
                scoped[key] = value;
            }

            dictionary.ThemeDictionaries[theme.LookupVariant] = scoped;
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
    /// 值可以是**回退列表**（逗号分隔，拉丁在前 / 中文在后）—— 直接交给 <see cref="FontFamily"/> 解析；
    /// 空串 ⇒ 不覆盖（回落色板文件里的内置值）。
    /// <para>
    /// 解析链与颜色同形（用户值 ?? 主题内容 ?? 内置缺省），今天只有前后两项有值：
    /// **主题要带字体时，只是往这条链里加一项**，渲染侧不改。
    /// </para>
    /// </remarks>
    private static Dictionary<string, object> BuildFontOverrides(ApplicationSettings settings)
    {
        var appearance = settings.Appearance;
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal);

        if (appearance.UiFontFamily is { Length: > 0 } ui)
        {
            overrides["FontUi"] = new FontFamily(ui);
        }

        if (appearance.DataFontFamily is { Length: > 0 } data)
        {
            overrides["FontData"] = new FontFamily(data);
        }

        return overrides;
    }

    /// <summary>
    /// 按色板契约算出**每一套主题**要写进资源的值（主题名 → token → 画刷）。
    /// </summary>
    /// <param name="app">当前应用（读内置值用）。</param>
    /// <param name="settings">应用级设置（用户覆盖从 <see cref="AppearanceSettings.PaletteOverrides"/> 来）。</param>
    /// <remarks>
    /// <b>每支一条链</b>：显式覆盖 → 派生规则 → 内置值（第三档不写，交给色板自己）。
    /// 源色的"当前值"只看**覆盖与内置**（`from` 必须是不派生的支）⇒ 单层、无环、与顺序无关。
    /// <para>
    /// **一律使用不可变画刷**（<see cref="ImmutableSolidColorBrush"/>）：这些画刷会进入应用级资源字典，
    /// 被**渲染线程**读取；用有线程亲和性的 <c>SolidColorBrush</c> 时，
    /// 只要覆盖是在非 UI 线程上算出来的（测试、后台刷新），渲染读它就会抛
    /// <c>The calling thread cannot access this object because a different thread owns it</c>。
    /// </para>
    /// </remarks>
    private static Dictionary<string, Dictionary<string, object>> BuildOverrides(
        Application app,
        ApplicationSettings settings)
    {
        var byTheme = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        foreach (var theme in ThemeCatalog.Themes)
        {
            var variant = theme.LookupVariant;
            var overrides = new Dictionary<string, object>(StringComparer.Ordinal);

            // 内置值：每支读一次，整支契约共用（读的是色板里的值 —— 我们那份字典还没装上去）
            var builtIn = new Dictionary<string, (Color Color, double Opacity)>(StringComparer.Ordinal);
            foreach (var token in PaletteManifest.Tokens)
            {
                builtIn[token.Key] = ReadBuiltIn(app, variant, token.Key);
            }

            (Color Color, double Opacity) SourceOf(string key) =>
                settings.Appearance.Override(theme.Id, key) is { } hex && Parse(hex) is { } color
                    ? (color, color.A < 255 ? color.A / 255.0 : builtIn[key].Opacity)
                    : builtIn[key];

            foreach (var token in PaletteManifest.Tokens)
            {
                // ① 用户显式改过 ⇒ 它说了算（写 8 位十六进制时按明写的 alpha 当浓度）
                if (settings.Appearance.Override(theme.Id, token.Key) is { } explicitHex)
                {
                    if (Parse(explicitHex) is { } color)
                    {
                        overrides[token.Key] = Solid(
                            color,
                            color.A < 255 ? color.A / 255.0 : builtIn[token.Key].Opacity);
                    }

                    continue;
                }

                // ② 契约说它跟随别的支 ⇒ 按规则算（浓度一律取该支内置的不透明度）
                if (token.Derive is { Mode: not PaletteDeriveMode.None } rule
                    && builtIn.TryGetValue(rule.Source, out var sourceBuiltIn))
                {
                    var source = SourceOf(rule.Source);
                    var derived = PaletteDerivation.Derive(
                        rule,
                        source.Color,
                        builtIn[token.Key].Color,
                        sourceBuiltIn.Color);

                    overrides[token.Key] = Solid(derived, builtIn[token.Key].Opacity);
                }

                // ③ 其余：不写 ⇒ 用色板里的内置值（"没改就是设计值"由此成立）
            }

            if (overrides.Count > 0)
            {
                byTheme[theme.Id] = overrides;
            }
        }

        return byTheme;
    }

    /// <summary>读某一套主题里某一支的**内置值**（资源缺失 ⇒ 透明，且不参与派生）。</summary>
    /// <param name="app">当前应用。</param>
    /// <param name="variant">该套主题的变体。</param>
    /// <param name="key">token 名。</param>
    private static (Color Color, double Opacity) ReadBuiltIn(Application app, ThemeVariant variant, string key) =>
        app.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush
            ? (brush.Color, brush.Opacity)
            : (Colors.Transparent, 1);

    /// <summary>造一支不可变纯色画刷（先按不透明度归一，再交给画刷）。</summary>
    /// <param name="color">颜色（其 alpha 通道被忽略，统一由 <paramref name="opacity"/> 表达）。</param>
    /// <param name="opacity">不透明度（0–1）。</param>
    private static ImmutableSolidColorBrush Solid(Color color, double opacity) =>
        new(Color.FromRgb(color.R, color.G, color.B), Math.Clamp(opacity, 0, 1));
}
