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
/// **外观块**（设置里"外观"这一层的模型，v4）：选了哪套主题 + 跟随系统的两套方案 + 投放过什么 + 字体轴。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>主题不再是内联数据</b>：主题是设置内部的一个子项，住
///     `settings\appearance\themes\&lt;id&gt;\theme.json`（+ 同目录壁纸）；本块只记**选择**与**投放过什么**，
///     不再内联方案目录、色板覆盖与壁纸引用 —— 于是"导出一个主题 = 复制一个文件夹"；</item>
///   <item><b>内置 `light` / `dark` 不落盘</b>：它们是代码里的色板，永远只读；自定义主题一律是文件夹；</item>
///   <item><b>跟随系统是两个选择</b>：浅 / 深模式各指向一套同基类型的主题（内置或自定义 id），
///     解析时的容错（缺失 / 基类型不匹配 ⇒ 回落内置）由主题目录负责；</item>
///   <item><b>字体不是主题</b>：字体是外观的第二个轴（全局、不随主题切换），仍住在本块；</item>
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
    public string UiFontFamily { get; set; } = "Segoe UI, Microsoft YaHei UI";

    /// <summary>**数据字体**（外观·字体轴的用户值；资源键 `FontData`）。</summary>
    // i18n-exempt: 字体族名（“宋体”是字体标识，不是界面文案）
    public string DataFontFamily { get; set; } = "Times New Roman, 宋体";

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

    /// <summary>复制一份（深拷贝：<see cref="Seeded"/> 换新实例，避免外部持有共享状态）。</summary>
    public AppearanceSettings Clone() => new()
    {
        ThemeSelection = ThemeSelection,
        SystemLightScheme = SystemLightScheme,
        SystemDarkScheme = SystemDarkScheme,
        UiFontFamily = UiFontFamily,
        DataFontFamily = DataFontFamily,
        Seeded = [.. Seeded],
    };
}
