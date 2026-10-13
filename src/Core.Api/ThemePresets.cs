namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **推荐色目录**：给用户"一键挑一个"的成色值集合（主题色的推荐档 + 背景预设）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它不是"主题色值"</b>：主题色值住在色板里（`Ui/Styles/Tokens.axaml`），
///     随主题走、可被用户覆盖；这里只是**递给用户的一份候选**（canned colors）；
///   <item><b>为什么住在 Core.Api</b>：它是**数据**（设置页的按钮、取色窗口的推荐栏、
///     以及"旧设置字段读时迁移"三处共用一份）⇒ 放在设置模型旁边，三处就不会各写一遍；
///   <item><b>为何用字符串</b>：这一层（`Core.Api`）零框架依赖（AR-6）⇒ 只存 `#RRGGBB` 文本，
///     解析交给界面层。</item>
/// </list>
/// </remarks>
public static class ThemePresets
{
    /// <summary>一档推荐主题色。</summary>
    /// <param name="Name">显示名。</param>
    /// <param name="Hex">色值文本。</param>
    public sealed record AccentPreset(string Name, string Hex)
    {
        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>推荐主题色（8 档）。</summary>
    public static IReadOnlyList<AccentPreset> Accents { get; } =
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

    /// <summary>一档背景预设（三支底色一起给）。</summary>
    /// <param name="Id">稳定标识（旧设置里的背景档位名，如 <c>LightGray</c>；读时迁移按它找）。</param>
    /// <param name="Title">显示名。</param>
    /// <param name="Workspace">工作区底。</param>
    /// <param name="Module">模块窗口底。</param>
    /// <param name="Chrome">外壳底。</param>
    public sealed record BackgroundPreset(string Id, string Title, string Workspace, string Module, string Chrome);

    /// <summary>
    /// 背景预设（两档）。**档位本身就是"哪种灰"**，与浅/深主题无关 ⇒ 两套主题写同一组值。
    /// </summary>
    public static IReadOnlyList<BackgroundPreset> Backgrounds { get; } =
    [
        new("LightGray", "浅灰底", "#E8EAED", "#F7F8FA", "#F1F2F5"),
        new("DarkGray", "深灰底", "#17181A", "#202124", "#1B1C1F"),
    ];

    /// <summary>按标识找一档背景预设（读时迁移用）。</summary>
    /// <param name="id">标识（如 <c>LightGray</c>）。</param>
    public static BackgroundPreset? FindBackground(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Backgrounds.FirstOrDefault(preset => string.Equals(preset.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 由一支**自定义背景色**推出三支底色（工作区 = 它本身；模块/外壳按比例提亮，形成层次）。
    /// </summary>
    /// <param name="hex">用户填的底（`#RRGGBB`）。</param>
    /// <returns>三支底色（解析失败 ⇒ 原样返回三支 = <paramref name="hex"/>）。</returns>
    /// <remarks>旧设置里的"背景档位 = 自定义"在读时迁移时用它，行为与迁移前一致。</remarks>
    public static (string Workspace, string Module, string Chrome) CustomBackground(string hex) =>
        (hex, Lighten(hex, 0.06), Lighten(hex, 0.03));

    /// <summary>把 `#RRGGBB` 按比例向白靠近（解析不了 ⇒ 原样返回）。</summary>
    /// <param name="hex">色值文本。</param>
    /// <param name="amount">0–1。</param>
    private static string Lighten(string hex, double amount)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            return hex;
        }

        return $"#{Blend(r, amount):X2}{Blend(g, amount):X2}{Blend(b, amount):X2}";

        static byte Blend(byte channel, double ratio) => (byte)Math.Clamp(channel + ((255 - channel) * ratio), 0, 255);
    }

    /// <summary>解析 `#RRGGBB`（也接受 `RRGGBB`）。</summary>
    private static bool TryParse(string? hex, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6)
        {
            return false;
        }

        return byte.TryParse(digits.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && byte.TryParse(digits.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && byte.TryParse(digits.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }
}
