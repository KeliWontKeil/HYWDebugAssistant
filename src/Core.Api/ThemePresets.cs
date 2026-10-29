namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **推荐色目录**：给用户"一键挑一个"的成色值集合（主题色的推荐档）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它不是"主题色值"</b>：主题色值住在色板里（`Ui/Styles/Tokens.axaml`），
///     随主题走、可被用户覆盖；这里只是**递给用户的一份候选**（canned colors）；</item>
///   <item><b>为什么住在 Core.Api</b>：它是**数据**（取色窗口的推荐栏等）⇒ 放在设置模型旁边；</item>
///   <item><b>为何用字符串</b>：这一层（`Core.Api`）零框架依赖（AR-6）⇒ 只存 `#RRGGBB` 文本，
///     解析交给界面层；**显示名不在这里**（随语言走文本表：`Ui` 表的 `palette.preset.&lt;id&gt;`）。</item>
/// </list>
/// </remarks>
public static class ThemePresets
{
    /// <summary>一档推荐主题色。</summary>
    /// <param name="Id">稳定标识（小写 ASCII；文本表键 `palette.preset.&lt;id&gt;`）。</param>
    /// <param name="Hex">色值文本。</param>
    public sealed record AccentPreset(string Id, string Hex);

    /// <summary>推荐主题色（8 档）。</summary>
    public static IReadOnlyList<AccentPreset> Accents { get; } =
    [
        new("blue", "#2563EB"),
        new("cyan", "#0891B2"),
        new("green", "#16A34A"),
        new("orange", "#EA580C"),
        new("red", "#DC2626"),
        new("purple", "#7C3AED"),
        new("pink", "#DB2777"),
        new("gray", "#52525B"),
    ];
}
