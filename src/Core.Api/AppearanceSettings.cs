namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **外观块**（设置里"外观"这一层的模型）：选择哪套主题 + 逐项覆盖 + 字体轴。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>层次</b>：外观（本块）→ 主题（<see cref="ThemeSelection"/> 选中哪一套 + 该套主题的内容）
///     → 色板（<see cref="PaletteOverrides"/> 是用户覆盖层）→ 渲染。**主题是值的来源，覆盖是用户的差异**；</item>
///   <item><b>覆盖按主题分层</b>：同一支 token 在两套主题下是两个值 ⇒ 覆盖必须记在**哪一套**上，
///     所以外层键是主题名（大小写不敏感），内层是 token 名；</item>
///   <item><b>稀疏</b>：没覆盖的条目**不写**（跟随主题内容）⇒ 不产生死数据，也让"改了什么"一眼可见；</item>
///   <item><b>本轮只有两个轴有值</b>：色板（覆盖）与字体（用户值）。背景媒体 / 密度等轴留位，
///     它们的解析链与这两条同形（覆盖 ?? 主题内容 ?? 内置缺省）。</item>
/// </list>
/// </remarks>
public sealed class AppearanceSettings
{
    /// <summary>**当前生效哪套主题**：<see cref="ThemeIds.System"/>（跟随系统）或某套主题的名字。</summary>
    public string ThemeSelection { get; set; } = ThemeIds.System;

    /// <summary>
    /// **色板覆盖层**：主题名 → token 名 → `#RRGGBB(AA)`。
    /// </summary>
    /// <remarks>外层比较器大小写不敏感（旧文件里写的是 `Light` / `Dark`）；空表 = 全部跟随主题内容。</remarks>
    public IDictionary<string, IDictionary<string, string>> PaletteOverrides { get; set; } =
        new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>**界面字体**（外观·字体轴的用户值；资源键 `FontUi`）。</summary>
    public string UiFontFamily { get; set; } = "Segoe UI, Microsoft YaHei UI";

    /// <summary>**数据字体**（外观·字体轴的用户值；资源键 `FontData`）。</summary>
    public string DataFontFamily { get; set; } = "Times New Roman, 宋体";

    /// <summary>读某一套主题里一支 token 的覆盖值（没有 ⇒ <see langword="null"/> = 跟随主题内容）。</summary>
    /// <param name="themeId">主题名。</param>
    /// <param name="token">token 名。</param>
    public string? Override(string themeId, string token)
    {
        var id = ThemeIds.Normalize(themeId);

        return PaletteOverrides.TryGetValue(id, out var map)
            && map.TryGetValue(token, out var hex)
            && !string.IsNullOrWhiteSpace(hex)
                ? hex
                : null;
    }

    /// <summary>某一套主题里**全部**覆盖项（空 ⇒ 空表）。</summary>
    /// <param name="themeId">主题名。</param>
    public IDictionary<string, string> OverridesOf(string themeId) =>
        PaletteOverrides.TryGetValue(ThemeIds.Normalize(themeId), out var map)
            ? map
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 写 / 清一支覆盖（<paramref name="hex"/> 空 ⇒ 删除该条；该主题一条不剩时把空桶也删掉）。
    /// </summary>
    /// <param name="themeId">主题名；<see cref="ThemeIds.System"/> 不是主题 ⇒ 忽略。</param>
    /// <param name="token">token 名。</param>
    /// <param name="hex">色值；空 = 恢复跟随主题内容。</param>
    public void SetOverride(string themeId, string token, string? hex)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var id = ThemeIds.Normalize(themeId);
        if (string.Equals(id, ThemeIds.System, StringComparison.Ordinal))
        {
            return;   // "跟随系统"不是一个可覆盖的主题：它只是"按系统类型挑一套"
        }

        if (string.IsNullOrWhiteSpace(hex))
        {
            if (PaletteOverrides.TryGetValue(id, out var existing) && existing.Remove(token) && existing.Count == 0)
            {
                _ = PaletteOverrides.Remove(id);
            }

            return;
        }

        if (!PaletteOverrides.TryGetValue(id, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            PaletteOverrides[id] = map;
        }

        map[token] = hex.Trim();
    }

    /// <summary>复制一份（深拷贝：两层字典都换新实例，避免外部持有共享状态）。</summary>
    public AppearanceSettings Clone()
    {
        var copy = new AppearanceSettings
        {
            ThemeSelection = ThemeSelection,
            UiFontFamily = UiFontFamily,
            DataFontFamily = DataFontFamily,
            PaletteOverrides = new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
        };

        foreach (var (themeId, map) in PaletteOverrides)
        {
            copy.PaletteOverrides[themeId] = new Dictionary<string, string>(map, StringComparer.Ordinal);
        }

        return copy;
    }
}
