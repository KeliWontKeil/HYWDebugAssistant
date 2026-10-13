namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **旧形状的色板覆盖键**（<c>主题/token</c> 的扁平表）—— **只用于读旧设置文件**。
/// </summary>
/// <remarks>
/// <para>
/// 新形状把覆盖按主题**嵌套**（见 <see cref="AppearanceSettings.PaletteOverrides"/>）：
/// 一支 token 在两套主题下是两个值，用扁平键把"哪一套"编进字符串是上一版的权宜。
/// 这个解析器留着只有一个用途：**把旧文件读成新形状**（见 `SettingsService.ReadAppearance`）。
/// 新的写入侧不再产生这种键。
/// </para>
/// <para>
/// 键的形状 = `主题名` + <see cref="Separator"/> + `token 名`（如 <c>Light/AccentPrimary</c>）。
/// </para>
/// </remarks>
public static class LegacyPaletteOverrideKeys
{
    /// <summary>主题名与 token 名之间的分隔符（旧形状专用）。</summary>
    public const char Separator = '/';

    /// <summary>解析一条旧形状的覆盖键。</summary>
    /// <param name="key">旧键。</param>
    /// <param name="themeId">主题名（已规范化）。</param>
    /// <param name="token">token 名。</param>
    /// <returns>形状合法（有分隔符、两侧都非空）⇒ <see langword="true"/>。</returns>
    public static bool TryParse(string? key, out string themeId, out string token)
    {
        themeId = string.Empty;
        token = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var at = key.IndexOf(Separator, StringComparison.Ordinal);
        if (at <= 0 || at == key.Length - 1)
        {
            return false;
        }

        themeId = ThemeIds.Normalize(key[..at]);
        token = key[(at + 1)..];
        return true;
    }
}
