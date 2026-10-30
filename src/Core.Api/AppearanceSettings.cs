namespace HYWDebugAssistant.Core.Api;

/// <summary>一套主题的**基类型**：决定它继承哪一套内置色板（浅 / 深）。</summary>
public enum ThemeBaseKind
{
    /// <summary>浅色基底。</summary>
    Light = 0,

    /// <summary>深色基底。</summary>
    Dark = 1,
}

/// <summary>
/// **一条字体轴**：西文与中文各一个字体名。
/// </summary>
/// <remarks>
/// <para>
/// 默认值是"西文一个、中文一个"：界面用系统 UI 字体族，数据用系统等宽字体族。
/// 以前保存的是一整串 FontFamily 回退列表（`西文, 中文`），用户只能从下拉里选一个名字、
/// 一选就把中文回退丢掉；现在拆成两个字段，一个字段就是一个字体，组合规则只在一处。
/// </para>
/// <para>
/// <see cref="Compose"/> 的产物才是交给 Avalonia 的 FontFamily 字符串（西文在前、中文在后）；
/// 任一侧为空时只取非空的一侧；两侧相同只写一次。
/// </para>
/// </remarks>
public sealed class FontFamilySetting
{
    /// <summary>西文字体名（空 = 用框架默认）。</summary>
    public string Latin { get; set; } = string.Empty;

    /// <summary>中文字体名（空 = 用框架默认）。</summary>
    public string Cjk { get; set; } = string.Empty;

    /// <summary>两侧都为空（= 没有用户覆盖）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Latin) && string.IsNullOrWhiteSpace(Cjk);

    /// <summary>组合成交给 Avalonia 的 FontFamily 字符串（`西文, 中文`）。</summary>
    /// <returns>组合结果；两侧都为空时为空串。</returns>
    public string Compose()
    {
        var latin = Latin.Trim();
        var cjk = Cjk.Trim();

        if (latin.Length == 0)
        {
            return cjk;
        }

        if (cjk.Length == 0 || string.Equals(latin, cjk, StringComparison.OrdinalIgnoreCase))
        {
            return latin;
        }

        return $"{latin}, {cjk}";
    }

    /// <summary>复制一份（值语义；外观草稿与生效设置不共享同一实例）。</summary>
    public FontFamilySetting Clone() => new()
    {
        Latin = Latin,
        Cjk = Cjk,
    };

    /// <summary>
    /// 把旧的**回退列表**折叠成一条字体轴：首个名字 = 西文，其余 = 中文。
    /// </summary>
    /// <param name="value">旧值（逗号分隔；单个名字也合法）。</param>
    /// <remarks>
    /// 旧形状是"一整个 FontFamily 回退列表"，不是"两个字体叠加"：`Segoe UI, Microsoft YaHei UI`
    /// 折叠后组合回去仍是同一串，渲染结果不变。单个名字（用户只选过一个）⇒ 两侧都取它，
    /// 保持"用户只选一个字体"的原样。
    /// </remarks>
    public static FontFamilySetting FromLegacy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new FontFamilySetting();
        }

        var parts = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return new FontFamilySetting();
        }

        return new FontFamilySetting
        {
            Latin = parts[0],
            Cjk = parts.Length == 1 ? parts[0] : string.Join(", ", parts.Skip(1)),
        };
    }
}

/// <summary>
/// **外观块**（设置里"外观"这一层的模型，v5）：选了哪套主题 + 跟随系统的两套方案 + 投放过什么 + 字体轴。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>主题不再是内联数据</b>：主题是设置内部的一个子项，住
///     `settings\appearance\themes\&lt;id&gt;\theme.json`（+ 同目录壁纸）；本块只记**选择**与**投放过什么**，
///     不再内联方案目录、色板覆盖与壁纸引用 —— 于是"导出一个主题 = 复制一个文件夹"；</item>
///   <item><b>内置 `light` / `dark` 不落盘</b>：它们是代码里的色板，永远只读；自定义主题一律是文件夹；</item>
///   <item><b>跟随系统是两个选择</b>：浅 / 深模式各指向一套同基类型的主题（内置或自定义 id），
///     解析时的容错（缺失 / 基类型不匹配 ⇒ 回落内置）由主题目录负责；</item>
///   <item><b>字体不是主题</b>：字体是外观的第二个轴（全局、不随主题切换），仍住在本块；
///     每个轴 = 西文 + 中文两个字段（v5）；</item>
///   <item><b><see cref="Seeded"/></b>：出厂种子投放过的相对路径（如 `appearance/themes/sakura`）；
///     它不代表主题仍存在 —— 用户删除后由它阻止复活，因此记的是"路径"而不是"id"。</item>
/// </list>
/// </remarks>
public sealed class AppearanceSettings
{
    /// <summary>**当前生效哪套主题**：<see cref="ThemeIds.System"/>（跟随系统）、内置主题名，或某个主题文件夹的 id。</summary>
    public string ThemeSelection { get; set; } = ThemeIds.System;

    /// <summary>**跟随系统 · 浅色模式**使用哪套主题（内置 / 自定义 id；必须是浅色基类型）。</summary>
    public string SystemLightScheme { get; set; } = ThemeIds.Light;

    /// <summary>**跟随系统 · 深色模式**使用哪套主题（内置 / 自定义 id；必须是深色基类型）。</summary>
    public string SystemDarkScheme { get; set; } = ThemeIds.Dark;

    /// <summary>**界面字体**（外观·字体轴的用户值；资源键 `FontUi`）。</summary>
    public FontFamilySetting UiFonts { get; set; } = new()
    {
        Latin = "Segoe UI",
        Cjk = "Microsoft YaHei UI",
    };

    /// <summary>**数据字体**（外观·字体轴的用户值；资源键 `FontData`）。</summary>
    public FontFamilySetting DataFonts { get; set; } = new()
    {
        Latin = "Cascadia Mono",
        // i18n-exempt: 字体族名（“宋体”是字体标识，不是界面文案）
        Cjk = "宋体",
    };

    /// <summary>
    /// **已经投放过的出厂种子单元**（相对**程序目录**的路径，如 `settings/appearance/themes/sakura`）。
    /// </summary>
    /// <remarks>
    /// 只防"删了又复活"，不要求目标仍存在；值用路径而非 id，是为了与种子投放单元（叶子目录）同一口径。
    /// B 阶段写入的旧形式 `appearance/themes/&lt;id&gt;`（相对设置根）仍被识别。
    /// </remarks>
    public IList<string> Seeded { get; set; } = [];

    /// <summary>设置“跟随系统”某一模式使用哪套主题。</summary>
    /// <param name="baseKind">模式（浅 / 深）。</param>
    /// <param name="id">主题 id；空 ⇒ 回落到该模式的内置主题。</param>
    public void SetSystemScheme(ThemeBaseKind baseKind, string? id)
    {
        var normalized = string.IsNullOrWhiteSpace(id)
            ? baseKind == ThemeBaseKind.Dark ? ThemeIds.Dark : ThemeIds.Light
            : ThemeIds.Normalize(id);

        if (baseKind == ThemeBaseKind.Dark)
        {
            SystemDarkScheme = normalized;
        }
        else
        {
            SystemLightScheme = normalized;
        }
    }

    /// <summary>复制一份（深拷贝：集合与两条字体轴都换新实例，避免外部持有共享状态）。</summary>
    public AppearanceSettings Clone() => new()
    {
        ThemeSelection = ThemeSelection,
        SystemLightScheme = SystemLightScheme,
        SystemDarkScheme = SystemDarkScheme,
        UiFonts = UiFonts.Clone(),
        DataFonts = DataFonts.Clone(),
        Seeded = [.. Seeded],
    };
}
