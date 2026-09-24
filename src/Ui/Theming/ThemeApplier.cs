using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>
/// **主题与配色的应用器**【M1-16 / ADR-0038 D4 · ADR-0039 D6】：把应用级设置里的
/// 主题（浅/深/跟随系统）· 主题色 · 背景色**落到角色 token 上**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只替换 token 值</b>：视图一律用 <c>DynamicResource</c> 取色（09 §2.1 的角色 token），
///     因此本类**不改任何视图**即可让模块窗口、弹窗、叠加层一并跟随；</item>
///   <item><b>为什么写进主题字典</b>：Avalonia 的资源查找顺序是
///     **主题字典 → 自身条目 → 合并字典（后加者优先）** —— 把覆盖放进"合并字典"会被主题字典里的
///     内置值盖住（实测：覆盖了也读不到）。故本类**直接改写主题字典中的键值**，并在每次应用前
///     **把上一次的覆盖恢复**（快照 → 还原），从而不污染内置 token；</item>
///   <item><b>不引入图片背景</b>（D-6 裁决）：背景只支持"跟随主题 / 浅灰 / 深灰 / 自定义纯色"。</item>
/// </list>
/// </remarks>
public static class ThemeApplier
{
    /// <summary>背景档位：跟随主题（默认，不覆盖）。</summary>
    public const string BackgroundTheme = "Theme";

    /// <summary>背景档位：浅灰。</summary>
    public const string BackgroundLightGray = "LightGray";

    /// <summary>背景档位：深灰。</summary>
    public const string BackgroundDarkGray = "DarkGray";

    /// <summary>背景档位：自定义（取 <see cref="ApplicationSettings.BackgroundColor"/> 的值）。</summary>
    public const string BackgroundCustom = "Custom";

    /// <summary>主题档位：跟随系统。</summary>
    public const string ThemeSystem = "System";

    /// <summary>预设主题色档（名称 + 十六进制，供设置界面"一键选色"）。</summary>
    /// <param name="Name">显示名。</param>
    /// <param name="Hex">颜色文本。</param>
    public sealed record AccentPreset(string Name, string Hex);

    /// <summary>预设主题色（8 档）。</summary>
    public static readonly IReadOnlyList<AccentPreset> Presets =
    [
        new("蓝", "#2563EB"),
        new("青", "#0891B2"),
        new("绿", "#16A34A"),
        new("橙", "#EA580C"),
        new("红", "#DC2626"),
        new("紫", "#7C3AED"),
        new("粉", "#DB2777"),
        new("灰", "#52525B"),
    ];

    /// <summary>上一次覆盖的快照（键 → 原值；原值 null 表示原本没有该键）。</summary>
    private static readonly List<(ResourceDictionary Dictionary, string Key, object? Original)> Overridden = [];

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

    private static void ApplyCore(Application app, ApplicationSettings settings)
    {
        app.RequestedThemeVariant = settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        Restore();

        var overrides = BuildOverrides(settings);
        if (overrides.Count == 0)
        {
            return;   // 全部跟随主题 → 不覆盖
        }

        ApplyToThemeDictionaries(app.Resources, overrides);
    }

    /// <summary>
    /// 递归把覆盖写入**含该键的主题字典**。
    /// </summary>
    /// <remarks>
    /// 角色 token 由 `Tokens.axaml` 提供，而它是**合并**进 `Application.Resources` 的，
    /// 因此主题字典挂在合并字典里、不在 <c>Application.Resources.ThemeDictionaries</c> 顶层
    /// （实测：只看顶层会一条都改不到）。只改"本来就有该键"的字典，避免误伤其他字典。
    /// </remarks>
    private static void ApplyToThemeDictionaries(
        IResourceDictionary root,
        IReadOnlyDictionary<string, object> overrides)
    {
        foreach (var provider in root.ThemeDictionaries.Values)
        {
            if (provider is not ResourceDictionary dictionary)
            {
                continue;
            }

            foreach (var (key, value) in overrides)
            {
                if (!dictionary.ContainsKey(key))
                {
                    continue;
                }

                Overridden.Add((dictionary, key, dictionary[key]));
                dictionary[key] = value;
            }
        }

        foreach (var merged in root.MergedDictionaries)
        {
            if (merged is ResourceDictionary nested)
            {
                ApplyToThemeDictionaries(nested, overrides);
            }
        }
    }

    /// <summary>还原上一次的覆盖（保证设置可以反复切换、并能回到内置 token）。</summary>
    private static void Restore()
    {
        foreach (var (dictionary, key, original) in Overridden)
        {
            if (original is null)
            {
                _ = dictionary.Remove(key);
            }
            else
            {
                dictionary[key] = original;
            }
        }

        Overridden.Clear();
    }

    /// <summary>构造覆盖项（空 = 全部跟随主题）。</summary>
    private static Dictionary<string, object> BuildOverrides(ApplicationSettings settings)
    {
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal);

        if (Parse(settings.AccentColor) is { } accent)
        {
            overrides["AccentPrimary"] = new SolidColorBrush(accent);
            overrides["AccentSecondary"] = new SolidColorBrush(Secondary(accent));

            // 选中/高亮覆盖色跟随主题色（透明度在 09 §2.2 已定）
            overrides["OverlaySelection"] = new SolidColorBrush(accent, 0.20);
            overrides["OverlayHighlight"] = new SolidColorBrush(accent, 0.12);
        }

        if (BackgroundPalette(settings) is { } palette)
        {
            overrides["BgWorkspace"] = new SolidColorBrush(palette.Workspace);
            overrides["BgModule"] = new SolidColorBrush(palette.Module);
            overrides["BgChrome"] = new SolidColorBrush(palette.Chrome);
        }

        return overrides;
    }

    /// <summary>背景档位 → 三档背景色（null = 跟随主题）。</summary>
    private static (Color Workspace, Color Module, Color Chrome)? BackgroundPalette(ApplicationSettings settings) =>
        settings.BackgroundMode switch
        {
            BackgroundLightGray => (Color.Parse("#E8EAED"), Color.Parse("#F7F8FA"), Color.Parse("#F1F2F5")),
            BackgroundDarkGray => (Color.Parse("#17181A"), Color.Parse("#202124"), Color.Parse("#1B1C1F")),
            BackgroundCustom when Parse(settings.BackgroundColor) is { } custom =>
                (custom, Lighten(custom, 0.06), Lighten(custom, 0.03)),
            _ => null,
        };

    /// <summary>次色 = 主题色的邻近色（避免"一片蓝"，又不引入第二个用户输入）。</summary>
    private static Color Secondary(Color accent) => Color.FromRgb(
        (byte)Math.Clamp((accent.R * 0.35) + (accent.B * 0.65), 0, 255),
        (byte)Math.Clamp((accent.G * 0.8) + (accent.B * 0.2), 0, 255),
        (byte)Math.Clamp((accent.B * 0.85) + (accent.G * 0.15), 0, 255));

    /// <summary>按比例向白色靠近（用于同色系的模块底/工具底层次）。</summary>
    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R + ((255 - color.R) * amount), 0, 255),
        (byte)Math.Clamp(color.G + ((255 - color.G) * amount), 0, 255),
        (byte)Math.Clamp(color.B + ((255 - color.B) * amount), 0, 255));
}
