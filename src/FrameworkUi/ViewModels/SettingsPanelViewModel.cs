using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Tuning;
using HYWDebugAssistant.Ui.Hotkeys;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;
namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>快捷键编辑行（动作 · 当前绑定 · 默认绑定）。</summary>
public sealed partial class HotkeyRowViewModel : ViewModelBase
{
    /// <summary>创建快捷键行。</summary>
    /// <param name="actionId">动作标识。</param>
    /// <param name="title">标题。</param>
    /// <param name="defaultBinding">默认绑定。</param>
    /// <param name="binding">当前绑定（空 = 不绑定）。</param>
    public HotkeyRowViewModel(string actionId, string title, string defaultBinding, string binding)
    {
        ActionId = actionId;
        Title = title;
        DefaultBinding = defaultBinding;
        Binding = binding;
    }

    /// <summary>动作标识。</summary>
    public string ActionId { get; }

    /// <summary>动作标题。</summary>
    public string Title { get; }

    /// <summary>默认绑定（恢复默认用）。</summary>
    public string DefaultBinding { get; }

    /// <summary>默认绑定的显示文本（"默认 {0}"；界面用，不参与持久化）。</summary>
    public string DefaultBindingText =>
        TextCatalog.Get(typeof(HotkeyRowViewModel).Assembly, "settings.hotkeys.defaultBinding", DefaultBinding);

    /// <summary>当前绑定（文本；空 = 不绑定）。</summary>
    [ObservableProperty]
    public partial string Binding { get; set; }

    /// <summary>是否处于"等待按键"状态（界面提示用）。</summary>
    [ObservableProperty]
    public partial bool IsCapturing { get; set; }
}

/// <summary>主题方案下拉项（“跟随系统”的浅色 / 深色模式各选一套）。</summary>
/// <param name="Id">方案 id（内置或自定义）。</param>
/// <param name="Title">显示名。</param>
/// <remarks>下拉框直接显示 <see cref="ToString"/> ⇒ 覆写成显示名。</remarks>
public sealed record ThemeSchemeChoice(string Id, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>关闭行为选项（值 = 持久化字符串，标题 = 界面文案）。</summary>
public sealed record WorkspaceExitChoice(string Value, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>缓冲满时的取舍选项（值 = 持久化字符串，标题 = 界面文案）。</summary>
public sealed record DropPolicyChoice(string Value, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>界面语言选项（值 = 持久化的语言标识，标题 = 显示名）。</summary>
/// <param name="Id">语言标识（`system` 或文化名）。</param>
/// <param name="Title">显示名。</param>
/// <remarks>下拉框直接显示 <see cref="ToString"/> ⇒ 覆写成显示名。</remarks>
public sealed record LanguageChoice(string Id, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>
/// 设置面板（应用级设置；界面属 `FrameworkUi`）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>与"模块配置"严格区分</b>：本面板改的是**应用行为偏好**（跨组合件、不入组合件）；
///     模块实例的参数在"模块"页里改；</item>
///   <item><b>三键机制</b>：任何改动都只是待生效；「应用」= 校验 → 生效 → 落盘并留在本页，
///     「确认」= 应用并关闭，「取消」= 丢弃本次改动并关闭。**主题也走这三键**，
///     主题卡只切换设置页里显示的色板草稿，不提前改全应用；</item>
///   <item><b>主题草稿 = 一组主题文件夹的内存副本</b>：每个主题一份草稿（标题 / 基类型 / 色板 /
///     壁纸字节 / 遮罩 / 表面）；「应用 / 确认」时**只写被编辑过的那几个**（脏集合），
///     删除单独走 `DeleteTheme`（不经设置文件）；「取消」= 丢掉草稿重新从磁盘读；</item>
///   <item><b>功能与设置分开</b>：模块来源管理与重扫在模块清单弹窗；日志查看 / Issue / 拓扑是独立功能；
///     本页只保留真正的设置项。</item>
/// </list>
/// </remarks>
public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>外观标量草稿：主题选择 + 跟随系统两套 + 已投放 + 字体；由「应用 / 确认」统一落盘。</summary>
    private AppearanceSettings _appearanceDraft = new();

    /// <summary>主题草稿（id → 草稿）；磁盘上的主题在 <see cref="Load"/> 时全部读进来。</summary>
    private readonly Dictionary<string, ThemeDraft> _themeDrafts = new(StringComparer.Ordinal);

    /// <summary>草稿里已经移除、等「应用 / 确认」时才真正删文件夹的主题 id。</summary>
    private readonly HashSet<string> _pendingDeletes = new(StringComparer.Ordinal);

    /// <summary>载入/回读期间抑制属性变更带来的即时写盘。</summary>
    private bool _loading;

    /// <summary>切换选中主题时抑制标题 TextBox 的改名回调。</summary>
    private bool _suppressTitleSync;

    /// <summary>重建“跟随系统”下拉项时抑制选择变更回调。</summary>
    private bool _suppressSystemChoiceSync;

    /// <summary>
    /// **一个主题的内存草稿**：磁盘主题的副本，或设置页里刚建出来还没落盘的新主题。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>脏集合</b>：<see cref="Dirty"/> 只对**被改过**的主题为真 ⇒「应用」只写它们
    ///     （没改过的主题文件一个字节都不动）；</item>
    ///   <item><b>壁纸字节暂存</b>：选图后先放在 <see cref="PendingWallpaper"/>，应用时随该主题写入；
    ///     「取消」= 丢掉草稿 ⇒ 字节一起丢，磁盘不变；</item>
    ///   <item><b>色板就是该主题的稀疏覆盖</b>：token → `#RRGGBB`（没写的跟随基类型内置值）。</item>
    /// </list>
    /// </remarks>
    private sealed class ThemeDraft
    {
        public required string Id { get; init; }

        public string Title { get; set; } = string.Empty;

        public required ThemeBaseKind Base { get; set; }

        public ThemeSource Source { get; set; } = ThemeSource.User;

        public Dictionary<string, string> Palette { get; } = new(StringComparer.Ordinal);

        public string? WallpaperFileName { get; set; }

        public byte[]? PendingWallpaper { get; set; }

        public bool ClearWallpaper { get; set; }

        public int WallpaperRevision { get; set; }

        public double WallpaperVeilOpacity { get; set; } = 0.55;

        public double SurfaceOpacity { get; set; } = 0.70;

        public bool Dirty { get; set; }

        /// <summary>草稿身份（色块现算 / 卡片预览用；色板是**同一个字典实例**，改了立刻能刷新）。</summary>
        public ThemeDescriptor ToDescriptor() => new(
            Id,
            string.IsNullOrWhiteSpace(Title) ? Loc("appearance.theme.custom.default") : Title.Trim(),
            Base == ThemeBaseKind.Dark ? ThemeKind.Dark : ThemeKind.Light,
            Source,
            Palette,
            WallpaperFileName,
            WallpaperVeilOpacity,
            SurfaceOpacity);
    }

    /// <summary>创建设置面板视图模型。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="command">命令面。</param>
    public SettingsPanelViewModel(IRuntimeQuery query, IRuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
    }

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(SettingsPanelViewModel).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(SettingsPanelViewModel).Assembly, key, args);

    // ---- 框架行为 ----

    /// <summary>是否启用对齐自动建网。</summary>
    [ObservableProperty]
    public partial bool AlignAutoNetworkEnabled { get; set; } = true;

    // ---- 数据面（缓冲与流控） ----

    /// <summary>每个接收口一条队列的容量上限（单元数；对**新建**队列生效）。</summary>
    [ObservableProperty]
    public partial int QueueCapacity { get; set; } = FlowDefaults.QueueCapacity;

    /// <summary>缓冲满时的取舍（下拉用；持久化值取自 <see cref="DropPolicyChoice.Value"/>）。</summary>
    public IReadOnlyList<DropPolicyChoice> DropPolicyChoices { get; } =
    [
        new("DropOldest", Loc("settings.flow.dropPolicy.oldest")),
        new("DropNewest", Loc("settings.flow.dropPolicy.newest")),
    ];

    /// <summary>选中的满时策略（草稿）。</summary>
    [ObservableProperty]
    public partial DropPolicyChoice? SelectedDropPolicy { get; set; }

    /// <summary>停止协议的有界等待时限（秒）。</summary>
    [ObservableProperty]
    public partial double StopTimeoutSeconds { get; set; } = FlowDefaults.StopTimeoutSeconds;

    /// <summary>饱和判定深度（单元数；立即生效；大于队列容量时按容量算）。</summary>
    [ObservableProperty]
    public partial int HintDepth { get; set; } = FlowDefaults.HintDepth;

    /// <summary>饱和判定持续时长（毫秒；立即生效）。</summary>
    [ObservableProperty]
    public partial int HintHoldMs { get; set; } = FlowDefaults.HintHoldMs;

    /// <summary>最大滞留时间（毫秒；0 = 不限，立即生效）。</summary>
    [ObservableProperty]
    public partial int MaxBacklogAgeMs { get; set; } = FlowDefaults.MaxBacklogAgeMs;

    /// <summary>外部写超时（毫秒；对新建 / 重新打开的会话生效）。</summary>
    [ObservableProperty]
    public partial int ExternalWriteTimeoutMs { get; set; } = FlowDefaults.ExternalWriteTimeoutMs;

    /// <summary>探活节拍（毫秒；立即生效）。</summary>
    [ObservableProperty]
    public partial int LivenessIntervalMs { get; set; } = FlowDefaults.LivenessIntervalMs;

    /// <summary>会话发送积压容量（字节；对新建 / 重新打开的会话生效）。</summary>
    [ObservableProperty]
    public partial int SendBacklogBytes { get; set; } = FlowDefaults.SendBacklogBytes;

    /// <summary>适配件暂存容量（字节；对新建 / 重新打开的会话生效）。</summary>
    [ObservableProperty]
    public partial int AdapterBufferBytes { get; set; } = FlowDefaults.AdapterBufferBytes;

    /// <summary>适配件关闭等待（毫秒；立即生效 / 下次关闭时使用）。</summary>
    [ObservableProperty]
    public partial int AccessCloseTimeoutMs { get; set; } = FlowDefaults.AccessCloseTimeoutMs;

    /// <summary>设备丢失判定次数（立即生效）。</summary>
    [ObservableProperty]
    public partial int DeviceLostMisses { get; set; } = FlowDefaults.DeviceLostMisses;

    /// <summary>积压显示进入深度（单元数；仅影响状态呈现）。</summary>
    [ObservableProperty]
    public partial int StatusBacklogEnterDepth { get; set; } = FlowDefaults.StatusBacklogEnterDepth;

    /// <summary>积压显示退出深度（单元数；仅影响状态呈现）。</summary>
    [ObservableProperty]
    public partial int StatusBacklogExitDepth { get; set; } = FlowDefaults.StatusBacklogExitDepth;

    /// <summary>状态滞回时间（毫秒）。</summary>
    [ObservableProperty]
    public partial int StatusHoldMs { get; set; } = FlowDefaults.StatusHoldMs;

    /// <summary>丢弃显示保持时间（毫秒）。</summary>
    [ObservableProperty]
    public partial int StatusDropHoldMs { get; set; } = FlowDefaults.StatusDropHoldMs;

    /// <summary>文本显示行上限。</summary>
    [ObservableProperty]
    public partial int TextLineCapacity { get; set; } = DisplayDefaults.TextLineCapacity;

    /// <summary>文本显示总字符预算。</summary>
    [ObservableProperty]
    public partial int TextCharBudget { get; set; } = DisplayDefaults.TextCharBudget;

    // ---- UI 偏好 ----

    /// <summary>UI 刷新率（Hz）。</summary>
    [ObservableProperty]
    public partial int UiRefreshRateHz { get; set; } = 60;

    /// <summary>是否在侧栏底部显示轻量性能监视。</summary>
    [ObservableProperty]
    public partial bool ShowPerformanceMonitor { get; set; } = true;

    /// <summary>
    /// **吸附距离**（px，2–8，默认 4）——拖动时的「磁吸半径」：
    /// 越小越跟手（最多滞后一个容差就松手），越大越容易吸上；贴合（相邻边接触）用它的 2 倍。
    /// </summary>
    [ObservableProperty]
    public partial double SnapDistance { get; set; } = 4;

    /// <summary>
    /// 设置页草稿：是否显示**叠加信息层**（应用/确认后落到运行时；交互页的直接开关是另一入口）。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    /// <summary>设置页草稿：应用整体置顶（应用/确认后落到运行时）。</summary>
    [ObservableProperty]
    public partial bool AppTopmost { get; set; }

    // ---- 外壳尺寸 ----

    /// <summary>外壳窗口比例档位。</summary>
    [ObservableProperty]
    public partial string ShellAspect { get; set; } = "9:19.5";

    /// <summary>外壳尺寸是否固定（默认固定）。</summary>
    [ObservableProperty]
    public partial bool ShellFixedSize { get; set; } = true;

    /// <summary>比例档位选项（下拉用）。</summary>
    public IReadOnlyList<string> ShellAspectOptions { get; } = ShellLayout.Aspects;

    /// <summary>关闭主窗口时的行为选项。</summary>
    public IReadOnlyList<WorkspaceExitChoice> WorkspaceExitChoices { get; } =
    [
        new("Ask", Loc("settings.interaction.exit.ask")),
        new("Save", Loc("settings.interaction.exit.save")),
        new("Discard", Loc("settings.interaction.exit.discard")),
    ];

    /// <summary>选中的关闭行为（草稿）。</summary>
    [ObservableProperty]
    public partial WorkspaceExitChoice? SelectedWorkspaceExitChoice { get; set; }

    // ---- 界面语言 ----

    /// <summary>可选界面语言：「跟随系统」+ 文本表里实际存在的语言（显示名用该语言自己的名字）。</summary>
    public IReadOnlyList<LanguageChoice> Languages { get; } = BuildLanguageChoices();

    /// <summary>语言下拉的选中项（草稿）。</summary>
    [ObservableProperty]
    public partial LanguageChoice? SelectedLanguageChoice { get; set; }

    /// <summary>界面语言草稿（持久化值 = 选项 id；「应用 / 确认」写回 <see cref="ApplicationSettings.UiLanguage"/>）。</summary>
    public string SelectedLanguage => SelectedLanguageChoice?.Id ?? TextLanguages.System;

    private static List<LanguageChoice> BuildLanguageChoices()
    {
        var choices = new List<LanguageChoice>(TextCatalog.SupportedLanguages.Count + 1)
        {
            new(TextLanguages.System, TextCatalog.Get(typeof(SettingsPanelViewModel).Assembly, "settings.general.language.title")),
        };

        foreach (var language in TextCatalog.SupportedLanguages)
        {
            choices.Add(new LanguageChoice(language, NativeName(language)));
        }

        return choices;
    }

    /// <summary>语言自己的显示名（取不到就退回语言标识）。</summary>
    /// <param name="language">语言标识。</param>
    private static string NativeName(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    // ---- 多行提交键 ----

    /// <summary>
    /// **多行文本输入的提交键**——三档字符串（值域与 `ApplicationSettings` 一致）。
    /// </summary>
    /// <remarks>
    /// 换行是极常见的行为，且**不同用户习惯不一样** ⇒ 做成**框架级可配置项**，
    /// 而**不是**模块声明（模块只声明「我要多行」）。**只影响多行输入框**；单行输入仍「回车 / 失焦一次提交」。
    /// </remarks>
    [ObservableProperty]
    public partial string MultilineSubmitKey { get; set; } = "CtrlEnter";

    /// <summary>多行提交键选项（下拉用）：`CtrlEnter`（回车换行 / Ctrl+Enter 提交 / 失焦不提交）· `Enter`（回车提交 / Shift+Enter 换行）· `None`（键盘都不提交）。</summary>
    public IReadOnlyList<string> MultilineSubmitKeyOptions { get; } = ["CtrlEnter", "Enter", "None"];

    /// <summary>把多行提交键收敛到三档之一（非法/空 ⇒ 默认 `CtrlEnter`；与外观类设置同一容错口径：不校验、不提示）。</summary>
    /// <param name="value">用户选择或文件里的值。</param>
    private static string NormalizeSubmitKey(string? value) =>
        value is "Enter" or "None" or "CtrlEnter" ? value : "CtrlEnter";

    // ---- 字体 ----

    /// <summary>**界面字体 · 西文**（一条字体轴的一半；资源键 `FontUi`）。</summary>
    [ObservableProperty]
    public partial string UiFontLatin { get; set; } = DefaultUiFontLatin;

    /// <summary>**界面字体 · 中文**（一条字体轴的另一半；资源键 `FontUi`）。</summary>
    [ObservableProperty]
    public partial string UiFontCjk { get; set; } = DefaultUiFontCjk;

    /// <summary>**数据字体 · 西文**（**所有输入框与显示框**；资源键 `FontData`）。</summary>
    [ObservableProperty]
    public partial string DataFontLatin { get; set; } = DefaultDataFontLatin;

    /// <summary>**数据字体 · 中文**（**所有输入框与显示框**；资源键 `FontData`）。</summary>
    [ObservableProperty]
    public partial string DataFontCjk { get; set; } = DefaultDataFontCjk;

    /// <summary>
    /// **系统已有字体**（下拉候选项）——直接读系统已有字体即可。
    /// </summary>
    /// <remarks>
    /// 取 Avalonia 的 <c>FontManager.Current.SystemFonts</c>；无字体服务时（无头环境）回落到四个默认字体名。
    /// 每个下拉只筛选一个字段（一个字体名），不再把"回退列表"当成一个选项。
    /// </remarks>
    public IReadOnlyList<string> FontFamilyOptions { get; } =
        new[] { DefaultUiFontLatin, DefaultUiFontCjk, DefaultDataFontLatin, DefaultDataFontCjk }
            .Concat(LoadSystemFonts())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>框架默认外观（唯一来源 `ApplicationSettings`；静态缓存，避免每次访问都分配）。</summary>
    private static readonly AppearanceSettings DefaultAppearance = new();

    /// <summary>界面字体 · 西文的框架默认值（与 `ApplicationSettings` 的缺省同源）。</summary>
    public static string DefaultUiFontLatin => DefaultAppearance.UiFonts.Latin;

    /// <summary>界面字体 · 中文的框架默认值。</summary>
    public static string DefaultUiFontCjk => DefaultAppearance.UiFonts.Cjk;

    /// <summary>数据字体 · 西文的框架默认值。</summary>
    public static string DefaultDataFontLatin => DefaultAppearance.DataFonts.Latin;

    /// <summary>数据字体 · 中文的框架默认值。</summary>
    public static string DefaultDataFontCjk => DefaultAppearance.DataFonts.Cjk;

    /// <summary>
    /// **当前构建版本**（四段：大.小.修订.轮次）：来自本程序集的 `InformationalVersion`，
    /// 与仓库根 `version.props` 同源（构建期派生，运行期不读文件）。
    /// </summary>
    public static string VersionText { get; } = TextCatalog.Get(
        typeof(SettingsPanelViewModel).Assembly,
        "settings.about.version",
        typeof(SettingsPanelViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "—");

    private static List<string> LoadSystemFonts()
    {
        try
        {
            var names = Avalonia.Media.FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
            {
                return names;
            }
        }
        catch (Exception)
        {
            // 无头/无字体服务：回落默认（**不阻断设置界面**）
        }

        return [DefaultUiFontLatin, DefaultUiFontCjk, DefaultDataFontLatin, DefaultDataFontCjk];
    }

    partial void OnUiFontLatinChanged(string value)
    {
        if (!_loading) { _appearanceDraft.UiFonts.Latin = value; }
    }

    partial void OnUiFontCjkChanged(string value)
    {
        if (!_loading) { _appearanceDraft.UiFonts.Cjk = value; }
    }

    partial void OnDataFontLatinChanged(string value)
    {
        if (!_loading) { _appearanceDraft.DataFonts.Latin = value; }
    }

    partial void OnDataFontCjkChanged(string value)
    {
        if (!_loading) { _appearanceDraft.DataFonts.Cjk = value; }
    }

    // ---- 壁纸 / 主题材质 ----

    /// <summary>当前主题的壁纸遮罩浓度（0–0.9；随主题保存）。</summary>
    [ObservableProperty]
    public partial double WallpaperVeilOpacity { get; set; } = 0.55;

    /// <summary>当前主题的控件表面不透明度（0–1；随主题保存）。</summary>
    [ObservableProperty]
    public partial double SurfaceOpacity { get; set; } = 0.70;

    private bool _syncingThemeMaterials;

    partial void OnWallpaperVeilOpacityChanged(double value) => ApplyThemeMaterials();

    partial void OnSurfaceOpacityChanged(double value) => ApplyThemeMaterials();

    /// <summary>把当前主题的遮罩/表面值读到界面（装载、切换主题时调用）。</summary>
    private void SyncThemeMaterials()
    {
        var theme = CurrentDisplayTheme();
        _syncingThemeMaterials = true;
        try
        {
            WallpaperVeilOpacity = Math.Clamp(theme.WallpaperVeilOpacity, 0, 0.9);
            SurfaceOpacity = Math.Clamp(theme.SurfaceOpacity, 0, 1);
        }
        finally
        {
            _syncingThemeMaterials = false;
        }
    }

    /// <summary>把界面值写回当前主题草稿；内置主题先复制成自定义主题。</summary>
    private void ApplyThemeMaterials()
    {
        if (_loading || _syncingThemeMaterials)
        {
            return;
        }

        var draft = EnsureEditableDraft();
        draft.WallpaperVeilOpacity = Math.Clamp(WallpaperVeilOpacity, 0, 0.9);
        draft.SurfaceOpacity = Math.Clamp(SurfaceOpacity, 0, 1);
        draft.Dirty = true;

        RefreshThemeCards();
    }

    /// <summary>当前主题壁纸预览（null = 无）。</summary>
    [ObservableProperty]
    public partial ImageBrush? ThemeWallpaperPreview { get; set; }

    /// <summary>当前主题是否有壁纸。</summary>
    public bool HasThemeWallpaper => ThemeWallpaperPreview is not null;

    /// <summary>当前主题壁纸能否清除（有壁纸才可）。</summary>
    public bool CanClearThemeWallpaper => HasThemeWallpaper;

    partial void OnThemeWallpaperPreviewChanged(ImageBrush? value)
    {
        OnPropertyChanged(nameof(HasThemeWallpaper));
        OnPropertyChanged(nameof(CanClearThemeWallpaper));
    }

    // ---- 主题草稿 / 色板（应用 / 确认才落盘、才切换全应用） ----

    /// <summary>主题卡（跟随系统 + 内置两套 + 自定义主题）。</summary>
    public ObservableCollection<ThemeCardViewModel> ThemeCards { get; } = [];

    /// <summary>“跟随系统 · 浅色模式”可选主题（内置 + 浅色基类型的自定义主题）。</summary>
    public ObservableCollection<ThemeSchemeChoice> SystemLightChoices { get; } = [];

    /// <summary>“跟随系统 · 深色模式”可选主题（内置 + 深色基类型的自定义主题）。</summary>
    public ObservableCollection<ThemeSchemeChoice> SystemDarkChoices { get; } = [];

    /// <summary>跟随系统 · 浅色模式选中的主题。</summary>
    [ObservableProperty]
    public partial ThemeSchemeChoice? SelectedSystemLightChoice { get; set; }

    /// <summary>跟随系统 · 深色模式选中的主题。</summary>
    [ObservableProperty]
    public partial ThemeSchemeChoice? SelectedSystemDarkChoice { get; set; }

    partial void OnSelectedSystemLightChoiceChanged(ThemeSchemeChoice? value) =>
        ApplySystemChoice(ThemeBaseKind.Light, value);

    partial void OnSelectedSystemDarkChoiceChanged(ThemeSchemeChoice? value) =>
        ApplySystemChoice(ThemeBaseKind.Dark, value);

    private void ApplySystemChoice(ThemeBaseKind baseKind, ThemeSchemeChoice? choice)
    {
        if (_loading || _suppressSystemChoiceSync || choice is null || !IsSystemSelected)
        {
            return;
        }

        _appearanceDraft.SetSystemScheme(baseKind, choice.Id);
        SyncPaletteFromDraft();
        RefreshThemeCards();
        SetStatus(true, baseKind == ThemeBaseKind.Dark
            ? Loc("settings.status.systemScheme.dark")
            : Loc("settings.status.systemScheme.light"));
    }

    /// <summary>
    /// **色板分区**：按色板契约（`Styles/palette.json`）生成**全部**相对色。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>一行 = 一支 token × 当前主题的一格</b>：留空 = 跟随（派生支跟随源、其余跟随内置值）；</item>
    ///   <item><b>没有例外</b>：主题色 / 发送 / 接收 / 背景就是普通的几支色；</item>
    ///   <item><b>只在「应用 / 确认」时生效</b>：主题卡切换只换本页色板，不提前改全应用。</item>
    /// </list>
    /// </remarks>
    public ObservableCollection<PaletteGroupViewModel> PaletteGroups { get; } = [];

    /// <summary>当前在设置页里选中的主题 id（<see cref="ThemeIds.System"/> / 内置名 / 自定义 id）。</summary>
    public string SelectedThemeId
    {
        get => _selectedThemeId;
        private set
        {
            if (string.Equals(_selectedThemeId, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedThemeId = value;
            OnPropertyChanged();
            NotifySchemeState();
        }
    }

    private string _selectedThemeId = ThemeIds.System;

    /// <summary>当前主题的显示名（内置 / 系统时只读显示其标题）。</summary>
    [ObservableProperty]
    public partial string SelectedSchemeTitle { get; set; } = string.Empty;

    partial void OnSelectedSchemeTitleChanged(string value)
    {
        if (_loading || _suppressTitleSync || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!_themeDrafts.TryGetValue(ThemeIds.Normalize(SelectedThemeId), out var draft))
        {
            return;
        }

        var title = value.Trim();
        if (string.Equals(draft.Title, title, StringComparison.Ordinal))
        {
            return;
        }

        draft.Title = title;
        draft.Dirty = true;

        RefreshThemeCards();
        RebuildSystemChoices();
    }

    /// <summary>当前选中的是不是自定义主题（磁盘上的或草稿）。</summary>
    public bool IsCustomSelected =>
        ThemeCatalog.Find(AllThemes(), SelectedThemeId) is { Source: not ThemeSource.BuiltIn };

    /// <summary>当前是不是“跟随系统”（选择项，不是主题）。</summary>
    public bool IsSystemSelected => ThemeIds.IsSystem(SelectedThemeId);

    /// <summary>是否显示“保存方案”（仅自定义主题；等价于底部「应用」）。</summary>
    public bool CanSaveScheme => IsCustomSelected;

    /// <summary>主题名是否可编辑（仅自定义主题；内置/系统不提供改名输入框）。</summary>
    public bool CanEditSchemeTitle => IsCustomSelected;

    /// <summary>是否可新建主题（跟随系统时隐藏；内置/自定义可新建）。</summary>
    public bool CanCreateScheme => !IsSystemSelected;

    /// <summary>是否可“恢复此主题默认”（仅自定义主题）。</summary>
    public bool CanResetScheme => IsCustomSelected;

    /// <summary>是否可“删除此主题”（仅自定义主题）。</summary>
    public bool CanDeleteScheme => IsCustomSelected;

    /// <summary>**内置两套 + 全部主题草稿**（设置页的本地清单：色块现算与卡片预览都用它）。</summary>
    private List<ThemeDescriptor> AllThemes()
    {
        var themes = new List<ThemeDescriptor>(ThemeCatalog.Themes);

        foreach (var draft in _themeDrafts.Values
            .Where(draft => !ThemeIds.IsReserved(draft.Id))
            .OrderBy(draft => draft.Id, StringComparer.Ordinal))
        {
            themes.Add(draft.ToDescriptor());
        }

        return themes;
    }

    /// <summary>当前显示用主题（跟随系统 ⇒ 按系统浅/深解析到具体主题）。</summary>
    private ThemeDescriptor CurrentDisplayTheme() =>
        ThemeCatalog.ResolveDisplayTheme(AllThemes(), _appearanceDraft, SelectedThemeId, CurrentSystemVariant());

    /// <summary>系统当前浅 / 深；无平台设置（无头/设计器）⇒ 浅色。</summary>
    private static ThemeVariant CurrentSystemVariant()
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

    /// <summary>建色板行（**只建一次**：装载与生效只改值、不重建集合 —— 重建会把折叠状态一起丢掉）。</summary>
    private void BuildPaletteGroups()
    {
        if (PaletteGroups.Count > 0)
        {
            return;
        }

        foreach (var (group, tokens) in PaletteManifest.Sections)
        {
            var rows = tokens.Select(token => new PaletteRowViewModel(token)).ToList();

            if (rows.Count > 0)
            {
                PaletteGroups.Add(new PaletteGroupViewModel(group, rows, group.Expanded));
            }
        }
    }

    /// <summary>把草稿里当前主题的色板写进各行（打开设置 / 切主题 / 取消 / 生效后回读时调）。</summary>
    private void SyncPaletteFromDraft()
    {
        var theme = CurrentDisplayTheme();

        foreach (var row in PaletteGroups.SelectMany(group => group.Rows))
        {
            row.Sync(theme, theme.PaletteValue(row.Key));
        }

        RefreshPalette();
    }

    /// <summary>让色块 / 状态文本 / 分区计数重新求值（内置值会随主题与主题色变，故不能只在改值时刷）。</summary>
    private void RefreshPalette()
    {
        foreach (var group in PaletteGroups)
        {
            foreach (var row in group.Rows)
            {
                row.Cell.Refresh();
            }

            group.Refresh();
        }
    }

    /// <summary>重建主题卡（主题增删 / 载入 / 生效后回读时调；改色只刷新、不重建）。</summary>
    private void RebuildThemeCards()
    {
        ThemeCards.Clear();
        ThemeCards.Add(new ThemeCardViewModel(ThemeIds.System, isSystem: true, isBuiltIn: false));

        foreach (var theme in AllThemes())
        {
            ThemeCards.Add(new ThemeCardViewModel(
                theme.Id,
                isSystem: false,
                isBuiltIn: theme.Source == ThemeSource.BuiltIn));
        }

        RefreshThemeCards();
        RebuildSystemChoices();
    }

    /// <summary>刷新主题卡的标题 / 预览色 / 选中态（不重建集合）。</summary>
    private void RefreshThemeCards()
    {
        var themes = AllThemes();
        var light = ThemeCatalog.ResolveSystemScheme(themes, _appearanceDraft, ThemeKind.Light);
        var dark = ThemeCatalog.ResolveSystemScheme(themes, _appearanceDraft, ThemeKind.Dark);

        foreach (var card in ThemeCards)
        {
            var theme = card.IsSystem ? null : ThemeCatalog.Find(themes, card.Id);
            card.Refresh(theme, light, dark, string.Equals(card.Id, SelectedThemeId, StringComparison.OrdinalIgnoreCase));
        }

        NotifySchemeState();
    }

    /// <summary>让“能否保存/删除/恢复/改名”等派生状态重新求值。</summary>
    private void NotifySchemeState()
    {
        OnPropertyChanged(nameof(IsCustomSelected));
        OnPropertyChanged(nameof(IsSystemSelected));
        OnPropertyChanged(nameof(CanSaveScheme));
        OnPropertyChanged(nameof(CanEditSchemeTitle));
        OnPropertyChanged(nameof(CanCreateScheme));
        OnPropertyChanged(nameof(CanResetScheme));
        OnPropertyChanged(nameof(CanDeleteScheme));
    }

    /// <summary>
    /// 重建“跟随系统”的两组下拉项，并按草稿里的系统主题设置选中项。
    /// </summary>
    private void RebuildSystemChoices()
    {
        _suppressSystemChoiceSync = true;

        var lightId = ThemeIds.Normalize(_appearanceDraft.SystemLightScheme);
        var darkId = ThemeIds.Normalize(_appearanceDraft.SystemDarkScheme);

        SystemLightChoices.Clear();
        SystemDarkChoices.Clear();

        foreach (var theme in AllThemes())
        {
            var choice = new ThemeSchemeChoice(theme.Id, theme.Title);

            if (theme.Kind == ThemeKind.Light)
            {
                SystemLightChoices.Add(choice);
            }
            else
            {
                SystemDarkChoices.Add(choice);
            }
        }

        SelectedSystemLightChoice = SystemLightChoices.FirstOrDefault(choice =>
            string.Equals(choice.Id, lightId, StringComparison.OrdinalIgnoreCase));
        SelectedSystemDarkChoice = SystemDarkChoices.FirstOrDefault(choice =>
            string.Equals(choice.Id, darkId, StringComparison.OrdinalIgnoreCase));

        _suppressSystemChoiceSync = false;
    }

    /// <summary>当前显示名（用于主题标题框；内置/系统为对应标题）。</summary>
    private void SyncSelectedSchemeTitle()
    {
        _suppressTitleSync = true;
        SelectedSchemeTitle = ThemeIds.IsSystem(SelectedThemeId)
            ? Loc("palette.card.system.title")
            : ThemeCatalog.Find(AllThemes(), SelectedThemeId)?.Title ?? CurrentDisplayTheme().Title;
        _suppressTitleSync = false;
    }

    /// <summary>切换设置页里预览/编辑的主题（**不调用 ThemeApplier**；全应用等「应用 / 确认」）。</summary>
    /// <param name="id">主题 id（含 <see cref="ThemeIds.System"/>）。</param>
    public void SelectTheme(string? id)
    {
        var normalized = ThemeIds.Normalize(id);

        if (!ThemeIds.IsSystem(normalized) && ThemeCatalog.Find(AllThemes(), normalized) is null)
        {
            return;
        }

        SelectedThemeId = normalized;
        _appearanceDraft.ThemeSelection = normalized;
        RebuildSystemChoices();
        SyncSelectedSchemeTitle();
        SyncThemeMaterials();
        SyncPaletteFromDraft();
        RefreshThemeCards();
        RefreshThemeWallpaperPreview();
        SetStatus(true, Loc("settings.status.themePreview"));
    }

    /// <summary>
    /// 为**当前主题**选择一张主窗口壁纸（草稿；「应用/确认」才写进主题文件夹）。
    /// </summary>
    /// <param name="png">裁切后的 PNG 字节。</param>
    /// <remarks>
    /// 内置主题只读：选择壁纸会按现有规则复制成自定义主题；跟随系统时保持 System，
    /// 只更新当前浅/深模式对应的主题。只影响主窗口壁纸，不改任何模块壁纸。
    /// </remarks>
    public void SetThemeWallpaper(byte[] png)
    {
        if (png is not { Length: > 0 })
        {
            return;
        }

        var draft = EnsureEditableDraft();
        draft.PendingWallpaper = png;
        draft.ClearWallpaper = false;
        draft.WallpaperRevision++;
        draft.Dirty = true;

        ThemeWallpaperPreview = WallpaperImageCache.FromBytes(PendingWallpaperKey(draft), png);

        RefreshThemeCards();
        SetStatus(true, Loc("settings.status.wallpaperSelected"));
    }

    /// <summary>清除当前主题的主窗口壁纸（草稿）。</summary>
    public void ClearThemeWallpaper()
    {
        var theme = CurrentDisplayTheme();
        var hasPending = _themeDrafts.TryGetValue(theme.Id, out var existing)
            && existing.PendingWallpaper is { Length: > 0 };

        if (theme.WallpaperFileName is not { Length: > 0 } && !hasPending)
        {
            return;
        }

        var draft = EnsureEditableDraft();
        draft.PendingWallpaper = null;
        draft.ClearWallpaper = true;
        draft.WallpaperFileName = null;
        draft.WallpaperRevision++;
        draft.Dirty = true;

        ThemeWallpaperPreview = null;

        RefreshThemeCards();
        SetStatus(true, Loc("settings.status.wallpaperCleared"));
    }

    /// <summary>按当前草稿刷新主窗口壁纸预览。</summary>
    private void RefreshThemeWallpaperPreview() =>
        ThemeWallpaperPreview = ResolveThemeWallpaperBrush(CurrentDisplayTheme());

    /// <summary>解析一个主题的壁纸画刷（草稿里的新图优先，其次读主题文件夹里的同名文件）。</summary>
    private ImageBrush? ResolveThemeWallpaperBrush(ThemeDescriptor theme)
    {
        if (_themeDrafts.TryGetValue(theme.Id, out var draft) && draft.PendingWallpaper is { Length: > 0 } pending)
        {
            return WallpaperImageCache.FromBytes(PendingWallpaperKey(draft), pending);
        }

        if (theme.WallpaperFileName is not { Length: > 0 })
        {
            return null;
        }

        return _query.TryGetThemeWallpaper(theme.Id, out var bytes) && bytes is { Length: > 0 }
            ? WallpaperImageCache.FromBytes(WallpaperCacheKey(theme.Id, bytes), bytes)
            : null;
    }

    /// <summary>待保存壁纸的缓存键（同一主题换图 ⇒ 换键，避免读到上一张）。</summary>
    private static string PendingWallpaperKey(ThemeDraft draft) =>
        $"pending:{draft.Id}:{draft.WallpaperRevision}";

    /// <summary>磁盘壁纸的缓存键（**按内容寻址**：同一主题换图后不会读到缓存里的旧图）。</summary>
    private static string WallpaperCacheKey(string themeId, byte[] png) =>
        themeId + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));

    /// <summary>
    /// 改某一支色（<paramref name="hex"/> = null/空 ⇒ 恢复跟随）。
    /// </summary>
    /// <param name="row">色板行。</param>
    /// <param name="hex">新色值。</param>
    /// <remarks>
    /// 内置 ⇒ 先自动复制成自定义主题（草稿），再把这一支写进去；
    /// 跟随系统 ⇒ 复制后把该模式（浅/深）的系统主题指向新主题，保持“跟随系统”选择不变。
    /// 真正的全应用切换与落盘仍走「应用 / 确认」。
    /// </remarks>
    public void SetPaletteColor(PaletteRowViewModel row, string? hex)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (CurrentDisplayTheme().Source == ThemeSource.BuiltIn && string.IsNullOrWhiteSpace(hex))
        {
            return;   // 内置主题本来就没有覆盖可清
        }

        var draft = EnsureEditableDraft();

        if (string.IsNullOrWhiteSpace(hex))
        {
            _ = draft.Palette.Remove(row.Key);
        }
        else
        {
            draft.Palette[row.Key] = hex.Trim();
        }

        draft.Dirty = true;

        SyncPaletteFromDraft();
        RefreshThemeCards();

        SetStatus(true, hex is { Length: > 0 }
            ? Loc("settings.status.colorChanged")
            : Loc("settings.status.colorReverted"));
    }

    /// <summary>
    /// **取当前显示主题的可变草稿**；内置主题先复制成自定义主题，并把选择/跟随系统指向新主题。
    /// </summary>
    /// <returns>可写草稿。</returns>
    private ThemeDraft EnsureEditableDraft()
    {
        var theme = CurrentDisplayTheme();
        if (theme.Source != ThemeSource.BuiltIn
            && _themeDrafts.TryGetValue(theme.Id, out var existing))
        {
            return existing;
        }

        var wasSystem = IsSystemSelected;
        var modeKind = theme.Kind;

        var draft = new ThemeDraft
        {
            Id = NextThemeId(),
            Title = NextSchemeTitle(),
            Base = modeKind == ThemeKind.Dark ? ThemeBaseKind.Dark : ThemeBaseKind.Light,
            Source = ThemeSource.User,
            WallpaperFileName = theme.WallpaperFileName,
            WallpaperVeilOpacity = theme.WallpaperVeilOpacity,
            SurfaceOpacity = theme.SurfaceOpacity,
            Dirty = true,
        };

        if (theme.Palette is { } palette)
        {
            foreach (var (token, hex) in palette)
            {
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(hex))
                {
                    draft.Palette[token] = hex;
                }
            }
        }

        _themeDrafts[draft.Id] = draft;

        if (wasSystem)
        {
            _appearanceDraft.SetSystemScheme(
                modeKind == ThemeKind.Dark ? ThemeBaseKind.Dark : ThemeBaseKind.Light,
                draft.Id);
            SelectedThemeId = ThemeIds.System;
            _appearanceDraft.ThemeSelection = ThemeIds.System;
            RebuildSystemChoices();
            SyncSelectedSchemeTitle();
        }
        else
        {
            SelectedThemeId = draft.Id;
            _appearanceDraft.ThemeSelection = draft.Id;
            SyncSelectedSchemeTitle();
        }

        RebuildThemeCards();
        SetStatus(true, Loc("settings.status.schemeConverted", draft.Title));

        return draft;
    }

    /// <summary>新建一个自定义主题（从当前主题复制色板 / 材质；草稿）。</summary>
    [RelayCommand]
    private void NewScheme()
    {
        if (IsSystemSelected)
        {
            return;   // 跟随系统用两个下拉框选主题；新建在具体主题（内置/自定义）下进行
        }

        var baseTheme = CurrentDisplayTheme();

        var draft = new ThemeDraft
        {
            Id = NextThemeId(),
            Title = NextSchemeTitle(),
            Base = baseTheme.Kind == ThemeKind.Dark ? ThemeBaseKind.Dark : ThemeBaseKind.Light,
            Source = ThemeSource.User,
            WallpaperFileName = baseTheme.WallpaperFileName,
            WallpaperVeilOpacity = baseTheme.WallpaperVeilOpacity,
            SurfaceOpacity = baseTheme.SurfaceOpacity,
            Dirty = true,
        };

        if (baseTheme.Palette is { } palette)
        {
            foreach (var (token, hex) in palette)
            {
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(hex))
                {
                    draft.Palette[token] = hex;
                }
            }
        }

        _themeDrafts[draft.Id] = draft;

        SelectedThemeId = draft.Id;
        _appearanceDraft.ThemeSelection = draft.Id;
        RebuildThemeCards();
        RebuildSystemChoices();
        SyncSelectedSchemeTitle();
        SyncThemeMaterials();
        SyncPaletteFromDraft();
        RefreshThemeWallpaperPreview();
        SetStatus(true, Loc("settings.status.schemeCreated", draft.Title));
    }

    /// <summary>保存当前主题（**等同底部「应用」**；写入主题文件夹与设置文件，不关闭弹窗）。</summary>
    [RelayCommand]
    private void SaveScheme()
    {
        if (!IsCustomSelected)
        {
            return;
        }

        Apply();

        if (StatusIsOk)
        {
            SetStatus(true, Loc("settings.status.schemeSaved"));
        }
    }

    /// <summary>恢复当前自定义主题为基类型默认（清空该主题的色板；草稿）。</summary>
    [RelayCommand]
    private void ResetScheme()
    {
        if (!_themeDrafts.TryGetValue(ThemeIds.Normalize(SelectedThemeId), out var draft))
        {
            return;
        }

        draft.Palette.Clear();
        draft.Dirty = true;

        SyncPaletteFromDraft();
        RefreshThemeCards();
        SetStatus(true, Loc("settings.status.schemeReset"));
    }

    /// <summary>移除当前自定义主题（草稿；「应用」才删文件夹，「取消」可反悔）。</summary>
    [RelayCommand]
    private void DeleteScheme()
    {
        if (!IsCustomSelected)
        {
            return;
        }

        var deletedId = ThemeIds.Normalize(SelectedThemeId);
        if (!_themeDrafts.Remove(deletedId))
        {
            return;
        }

        _ = _pendingDeletes.Add(deletedId);

        if (string.Equals(ThemeIds.Normalize(_appearanceDraft.SystemLightScheme), deletedId, StringComparison.Ordinal))
        {
            _appearanceDraft.SystemLightScheme = ThemeIds.Light;
        }

        if (string.Equals(ThemeIds.Normalize(_appearanceDraft.SystemDarkScheme), deletedId, StringComparison.Ordinal))
        {
            _appearanceDraft.SystemDarkScheme = ThemeIds.Dark;
        }

        SelectedThemeId = ThemeIds.System;
        _appearanceDraft.ThemeSelection = ThemeIds.System;
        RebuildThemeCards();
        RebuildSystemChoices();
        SyncSelectedSchemeTitle();
        SyncThemeMaterials();
        SyncPaletteFromDraft();
        RefreshThemeWallpaperPreview();
        SetStatus(true, Loc("settings.status.schemeDeleted"));
    }

    /// <summary>下一个自定义主题的默认名。</summary>
    private string NextSchemeTitle()
    {
        var count = _themeDrafts.Count;
        return count == 0
            ? Loc("appearance.theme.custom.default")
            : Loc("appearance.theme.custom.numbered", count + 1);
    }

    /// <summary>生成一个当前未被占用的主题 id（形如 <c>custom-ab12cd34</c>）。</summary>
    private string NextThemeId()
    {
        while (true)
        {
            var candidate = "custom-" + Guid.NewGuid().ToString("N")[..8];

            if (!ThemeIds.IsReserved(candidate) && !_themeDrafts.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>把磁盘主题读进草稿字典（同时清掉待删除集合）。</summary>
    private void LoadThemeDrafts()
    {
        _themeDrafts.Clear();
        _pendingDeletes.Clear();

        foreach (var file in _query.GetThemeFiles())
        {
            if (string.IsNullOrWhiteSpace(file.Id))
            {
                continue;
            }

            var id = ThemeIds.Normalize(file.Id);
            if (ThemeIds.IsReserved(id) || _themeDrafts.ContainsKey(id))
            {
                continue;
            }

            var draft = new ThemeDraft
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(file.Title) ? Loc("appearance.theme.custom.default") : file.Title.Trim(),
                Base = file.Base,
                Source = ThemeSource.File,
                WallpaperFileName = file.WallpaperFileName,
                WallpaperVeilOpacity = Math.Clamp(file.WallpaperVeilOpacity, 0, 0.9),
                SurfaceOpacity = Math.Clamp(file.SurfaceOpacity, 0, 1),
            };

            foreach (var (token, hex) in file.Palette)
            {
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(hex))
                {
                    draft.Palette[token] = hex;
                }
            }

            _themeDrafts[id] = draft;
        }
    }

    /// <summary>把主题选择收敛到真实存在的主题（否则回落到跟随系统）。</summary>
    /// <param name="id">设置里的选择。</param>
    private string NormalizeSelection(string? id)
    {
        var normalized = ThemeIds.Normalize(id);

        return ThemeIds.IsSystem(normalized) || ThemeCatalog.Find(AllThemes(), normalized) is not null
            ? normalized
            : ThemeIds.System;
    }

    /// <summary>把跟随系统某一模式的 id 收敛到同基类型的主题（否则回落内置）。</summary>
    /// <param name="baseKind">模式（浅 / 深）。</param>
    /// <param name="id">设置里的值。</param>
    private string NormalizeSystemSchemeId(ThemeBaseKind baseKind, string? id)
    {
        var normalized = ThemeIds.Normalize(id);

        if (ThemeCatalog.Find(AllThemes(), normalized) is { } theme
            && (baseKind == ThemeBaseKind.Dark ? theme.Kind == ThemeKind.Dark : theme.Kind == ThemeKind.Light))
        {
            return theme.Id;
        }

        return baseKind == ThemeBaseKind.Dark ? ThemeIds.Dark : ThemeIds.Light;
    }

    // ---- 快捷键 ----

    /// <summary>快捷键行（动作 · 当前绑定 · 修改/重置）。</summary>
    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    /// <summary>快捷键冲突提示（空 = 无冲突）。</summary>
    [ObservableProperty]
    public partial string? HotkeyConflicts { get; set; }

    /// <summary>日志级别（Trace/Debug/Information/Warning/Error）。</summary>
    [ObservableProperty]
    public partial string LogLevel { get; set; } = "Information";

    /// <summary>状态提示（应用结果 / 校验失败原因）。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>是否显示"已生效"成功提示（与失败原因区分呈现）。</summary>
    [ObservableProperty]
    public partial bool StatusIsOk { get; set; }

    /// <summary>可选日志级别。</summary>
    public IReadOnlyList<string> LogLevels { get; } =
        ["Trace", "Debug", "Information", "Warning", "Error"];

    // ---- 存储位置（只读；路径由 Core 的路径解析器唯一给出） ----

    /// <summary>程序目录。</summary>
    public string StorageAppRoot => _query.GetStorageLocations().AppRoot;

    /// <summary>应用设置文件（设置根下）。</summary>
    public string StorageSettingsFile => _query.GetStorageLocations().SettingsFile;

    /// <summary>模块包目录（内置包；外部导入在 external 子目录）。</summary>
    public string StorageModulesRoot => _query.GetStorageLocations().ModulesRoot;

    /// <summary>组合文件默认目录（与 modules 同级）。</summary>
    public string StorageCompositesRoot => _query.GetStorageLocations().CompositesRoot;

    /// <summary>工作区快照根（含一次性重启恢复快照）。</summary>
    public string StorageWorkspaceRoot => _query.GetStorageLocations().WorkspaceRoot;

    /// <summary>日志目录（预留）。</summary>
    public string StorageLogsRoot => _query.GetStorageLocations().LogsRoot;

    // ---- 生命周期 ----

    /// <summary>从当前生效值重新载入（启动与"还原"时调用）。</summary>
    public void Load()
    {
        var settings = _query.GetSettings();

        _loading = true;

        QueueCapacity = settings.Flow.QueueCapacity;
        SelectedDropPolicy = DropPolicyChoices.FirstOrDefault(choice =>
            string.Equals(choice.Value, settings.Flow.DropPolicy, StringComparison.Ordinal))
            ?? DropPolicyChoices[0];
        StopTimeoutSeconds = settings.Flow.StopTimeoutSeconds;
        HintDepth = settings.Flow.HintDepth;
        HintHoldMs = settings.Flow.HintHoldMs;
        MaxBacklogAgeMs = settings.Flow.MaxBacklogAgeMs;
        ExternalWriteTimeoutMs = settings.Flow.ExternalWriteTimeoutMs;
        LivenessIntervalMs = settings.Flow.LivenessIntervalMs;
        SendBacklogBytes = settings.Flow.SendBacklogBytes;
        AdapterBufferBytes = settings.Flow.AdapterBufferBytes;
        AccessCloseTimeoutMs = settings.Flow.AccessCloseTimeoutMs;
        DeviceLostMisses = settings.Flow.DeviceLostMisses;
        StatusBacklogEnterDepth = settings.Flow.StatusBacklogEnterDepth;
        StatusBacklogExitDepth = settings.Flow.StatusBacklogExitDepth;
        StatusHoldMs = settings.Flow.StatusHoldMs;
        StatusDropHoldMs = settings.Flow.StatusDropHoldMs;
        TextLineCapacity = settings.Display.TextLineCapacity;
        TextCharBudget = settings.Display.TextCharBudget;
        AlignAutoNetworkEnabled = settings.AlignAutoNetworkEnabled;
        UiRefreshRateHz = settings.UiRefreshRateHz;
        ShowPerformanceMonitor = settings.ShowPerformanceMonitor;
        SnapDistance = settings.SnapDistance;
        MultilineSubmitKey = settings.MultilineSubmitKey;
        ShowInfoOverlay = settings.ShowInfoOverlay;
        AppTopmost = settings.AppTopmost;
        ShellAspect = ShellLayout.Normalize(settings.ShellAspect);
        ShellFixedSize = settings.ShellFixedSize;
        SelectedWorkspaceExitChoice = WorkspaceExitChoices.FirstOrDefault(choice =>
            string.Equals(choice.Value, settings.WorkspaceExitBehavior, StringComparison.Ordinal))
            ?? WorkspaceExitChoices[0];
        LogLevel = settings.LogLevel;
        SelectedLanguageChoice = Languages.FirstOrDefault(choice =>
            string.Equals(choice.Id, settings.UiLanguage, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        UiFontLatin = settings.Appearance.UiFonts.Latin;
        UiFontCjk = settings.Appearance.UiFonts.Cjk;
        DataFontLatin = settings.Appearance.DataFonts.Latin;
        DataFontCjk = settings.Appearance.DataFonts.Cjk;

        _appearanceDraft = settings.Appearance.Clone();
        LoadThemeDrafts();

        var selection = NormalizeSelection(_appearanceDraft.ThemeSelection);
        _appearanceDraft.ThemeSelection = selection;
        _appearanceDraft.SystemLightScheme = NormalizeSystemSchemeId(ThemeBaseKind.Light, _appearanceDraft.SystemLightScheme);
        _appearanceDraft.SystemDarkScheme = NormalizeSystemSchemeId(ThemeBaseKind.Dark, _appearanceDraft.SystemDarkScheme);

        SelectedThemeId = selection;
        SyncSelectedSchemeTitle();
        SyncThemeMaterials();

        BuildPaletteGroups();
        RebuildThemeCards();
        SyncPaletteFromDraft();
        RefreshThemeWallpaperPreview();

        Hotkeys.Clear();
        foreach (var action in HotkeyCatalog.Actions)
        {
            var binding = settings.Hotkeys.TryGetValue(action.Id, out var configured) && !string.IsNullOrWhiteSpace(configured)
                ? configured
                : action.DefaultBinding;

            Hotkeys.Add(new HotkeyRowViewModel(action.Id, action.Label, action.DefaultBinding, binding));
        }

        HotkeyConflicts = DetectConflicts();

        // 装载 = 把**已生效**的设置套到界面（打开弹窗时保证"看到的 = 实际生效的"）
        ThemeApplier.Apply(settings, _query.GetThemeFiles());

        _loading = false;
        StatusIsOk = true;
        StatusMessage = Loc("settings.footer.hint");
    }

    /// <summary>设置**应用成功**后触发（供外壳重建快捷键绑定、套用窗口层级等）。</summary>
    public event EventHandler? Applied;

    /// <summary>
    /// 当前设置里的**数据结构 → 颜色**映射（键 = 类型身份，值 = `#RRGGBB`）。
    /// </summary>
    /// <returns>用户覆盖过的条目（缺省的类型走自动配色，不在此表里）。</returns>
    public IReadOnlyDictionary<string, string> TypeColorOverrides() =>
        new Dictionary<string, string>(_query.GetSettings().TypeColors, StringComparer.Ordinal);

    /// <summary>
    /// 写入/移除一条**数据结构 → 颜色**（**唯一落盘入口**；`hex = null` ⇒ 移除该条并回落到自动配色）。
    /// </summary>
    /// <param name="typeKey">类型身份。</param>
    /// <param name="hex">`#RRGGBB`；null/空 ⇒ 移除覆盖。</param>
    /// <remarks>只动 <c>TypeColors</c> 一项，其余字段从**当前设置**克隆后原样写回（不覆盖别的页面刚改的值）。</remarks>
    public void SetTypeColor(string typeKey, string? hex)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return;
        }

        var settings = _query.GetSettings().Clone();

        if (string.IsNullOrWhiteSpace(hex))
        {
            _ = settings.TypeColors.Remove(typeKey);
        }
        else
        {
            settings.TypeColors[typeKey] = hex;
        }

        _ = _command.UpdateSettings(settings);
    }

    /// <summary>取当前生效的快捷键绑定（动作标识 → 绑定文本）。</summary>
    /// <returns>绑定表。</returns>
    public IReadOnlyDictionary<string, string> HotkeyBindings() =>
        Hotkeys.ToDictionary(row => row.ActionId, row => row.Binding, StringComparer.Ordinal);

    /// <summary>设置一个动作的绑定（供界面的按键捕获调用）。</summary>
    /// <param name="actionId">动作标识。</param>
    /// <param name="binding">绑定文本（空 = 不绑定）。</param>
    public void SetHotkey(string actionId, string? binding)
    {
        if (Hotkeys.FirstOrDefault(r => string.Equals(r.ActionId, actionId, StringComparison.Ordinal)) is not { } row)
        {
            return;
        }

        row.Binding = binding ?? string.Empty;
        HotkeyConflicts = DetectConflicts();
        SetStatus(true, Loc("settings.status.hotkeyChanged"));
    }

    /// <summary>检测重复绑定（同一按键被多个动作占用 → 后者不生效，先到者胜）。</summary>
    public string? DetectConflicts()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<string>();

        foreach (var row in Hotkeys)
        {
            if (string.IsNullOrWhiteSpace(row.Binding))
            {
                continue;
            }

            if (seen.TryGetValue(row.Binding, out var owner))
            {
                conflicts.Add(Loc("settings.hotkey.conflict", row.Title, owner, row.Binding));
                continue;
            }

            seen[row.Binding] = row.Title;
        }

        return conflicts.Count == 0 ? null : string.Join("；", conflicts);
    }

    /// <summary>把全部快捷键恢复为内置默认值。</summary>
    [RelayCommand]
    private void ResetHotkeys()
    {
        foreach (var row in Hotkeys)
        {
            row.Binding = row.DefaultBinding;
        }

        HotkeyConflicts = DetectConflicts();
        SetStatus(true, Loc("settings.status.hotkeyReset"));
    }

    /// <summary>
    /// **恢复默认设置**（本对话框中的设置；不动模块来源与数据结构颜色）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>范围</b>：本页全部设置项（应用行为 / 外观 / 字体 / 快捷键 / 窗口 / 日志与诊断）；</item>
    ///   <item><b>主题草稿一并丢弃</b>（重新从主题文件夹读）：主题文件本身不删、不改；</item>
    ///   <item><b>不落盘</b>：与三键机制一致（"任何改动都只是**待生效**"）⇒ 状态栏提示"点应用/确认生效"，
    ///     用户还能用「取消」反悔；</item>
    ///   <item>**模块来源**在模块清单弹窗管理、**数据结构颜色**在交互页 ⇒ 本按钮不重置它们。</item>
    /// </list>
    /// </remarks>
    [RelayCommand]
    private void ResetAllSettings()
    {
        var defaults = new ApplicationSettings();

        _loading = true;

        QueueCapacity = defaults.Flow.QueueCapacity;
        SelectedDropPolicy = DropPolicyChoices[0];
        StopTimeoutSeconds = defaults.Flow.StopTimeoutSeconds;
        HintDepth = defaults.Flow.HintDepth;
        HintHoldMs = defaults.Flow.HintHoldMs;
        MaxBacklogAgeMs = defaults.Flow.MaxBacklogAgeMs;
        ExternalWriteTimeoutMs = defaults.Flow.ExternalWriteTimeoutMs;
        LivenessIntervalMs = defaults.Flow.LivenessIntervalMs;
        SendBacklogBytes = defaults.Flow.SendBacklogBytes;
        AdapterBufferBytes = defaults.Flow.AdapterBufferBytes;
        AccessCloseTimeoutMs = defaults.Flow.AccessCloseTimeoutMs;
        DeviceLostMisses = defaults.Flow.DeviceLostMisses;
        StatusBacklogEnterDepth = defaults.Flow.StatusBacklogEnterDepth;
        StatusBacklogExitDepth = defaults.Flow.StatusBacklogExitDepth;
        StatusHoldMs = defaults.Flow.StatusHoldMs;
        StatusDropHoldMs = defaults.Flow.StatusDropHoldMs;
        TextLineCapacity = defaults.Display.TextLineCapacity;
        TextCharBudget = defaults.Display.TextCharBudget;
        AlignAutoNetworkEnabled = defaults.AlignAutoNetworkEnabled;
        UiRefreshRateHz = defaults.UiRefreshRateHz;
        ShowPerformanceMonitor = defaults.ShowPerformanceMonitor;
        SnapDistance = defaults.SnapDistance;
        MultilineSubmitKey = NormalizeSubmitKey(defaults.MultilineSubmitKey);
        ShowInfoOverlay = defaults.ShowInfoOverlay;
        AppTopmost = defaults.AppTopmost;
        ShellAspect = ShellLayout.Normalize(defaults.ShellAspect);
        ShellFixedSize = defaults.ShellFixedSize;
        SelectedWorkspaceExitChoice = WorkspaceExitChoices[0];
        LogLevel = defaults.LogLevel;
        SelectedLanguageChoice = Languages[0];
        UiFontLatin = defaults.Appearance.UiFonts.Latin;
        UiFontCjk = defaults.Appearance.UiFonts.Cjk;
        DataFontLatin = defaults.Appearance.DataFonts.Latin;
        DataFontCjk = defaults.Appearance.DataFonts.Cjk;

        _appearanceDraft = defaults.Appearance.Clone();
        LoadThemeDrafts();

        SelectedThemeId = ThemeIds.System;
        _appearanceDraft.ThemeSelection = ThemeIds.System;
        SyncThemeMaterials();
        SyncSelectedSchemeTitle();
        RebuildThemeCards();
        SyncPaletteFromDraft();
        RefreshThemeWallpaperPreview();

        _loading = false;

        // 快捷键走既有那条路径（每行回到 `DefaultBinding`），避免默认值有两个来源。
        ResetHotkeys();
        SetStatus(true, Loc("settings.status.defaultsRestored"));
    }

    /// <summary>单个动作恢复默认（由界面行的按钮调用）。</summary>
    /// <param name="row">行。</param>
    public void ResetHotkey(HotkeyRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Binding = row.DefaultBinding;
        HotkeyConflicts = DetectConflicts();
    }

    /// <summary>
    /// **确认**：应用并关闭弹窗（校验失败 ⇒ **不关**，把原因留在状态行）。
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        Apply();

        if (StatusIsOk)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>**取消**：丢弃本次改动（回到"已生效"的值）并关闭弹窗。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Load();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>请求宿主关闭设置弹窗（由 `MainWindow` 接到后关闭对应的 `DialogWindow`）。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 应用设置（**先写脏主题文件夹** → 再删被移除的主题 → 最后写设置文件）。
    /// </summary>
    /// <remarks>
    /// 三步的顺序是语义：先保证"选中的主题真的在磁盘上"，再删掉用户移除的，最后把选择写进设置；
    /// 任何一步失败就停在状态行，**不写设置文件**（避免设置指向一个没写成功的主题）。
    /// </remarks>
    [RelayCommand]
    private void Apply()
    {
        if (!CommitThemeDrafts())
        {
            return;
        }

        var settings = _query.GetSettings();

        settings.Flow = settings.Flow with
        {
            QueueCapacity = QueueCapacity,
            DropPolicy = SelectedDropPolicy?.Value ?? "DropOldest",
            StopTimeoutSeconds = StopTimeoutSeconds,
            HintDepth = HintDepth,
            HintHoldMs = HintHoldMs,
            MaxBacklogAgeMs = MaxBacklogAgeMs,
            ExternalWriteTimeoutMs = ExternalWriteTimeoutMs,
            LivenessIntervalMs = LivenessIntervalMs,
            SendBacklogBytes = SendBacklogBytes,
            AdapterBufferBytes = AdapterBufferBytes,
            AccessCloseTimeoutMs = AccessCloseTimeoutMs,
            DeviceLostMisses = DeviceLostMisses,
            StatusBacklogEnterDepth = StatusBacklogEnterDepth,
            StatusBacklogExitDepth = StatusBacklogExitDepth,
            StatusHoldMs = StatusHoldMs,
            StatusDropHoldMs = StatusDropHoldMs,
        };

        settings.Display = new DisplaySettings
        {
            TextLineCapacity = TextLineCapacity,
            TextCharBudget = TextCharBudget,
        };

        settings.AlignAutoNetworkEnabled = AlignAutoNetworkEnabled;
        settings.UiRefreshRateHz = UiRefreshRateHz;
        settings.ShowPerformanceMonitor = ShowPerformanceMonitor;
        settings.SnapDistance = Math.Clamp(SnapDistance, 2, 8);
        settings.MultilineSubmitKey = NormalizeSubmitKey(MultilineSubmitKey);
        settings.ShowInfoOverlay = ShowInfoOverlay;
        settings.ShellAspect = ShellLayout.Normalize(ShellAspect);
        settings.ShellFixedSize = ShellFixedSize;
        settings.WorkspaceExitBehavior = SelectedWorkspaceExitChoice?.Value ?? "Ask";
        settings.LogLevel = LogLevel;
        settings.UiLanguage = SelectedLanguage;
        settings.AppTopmost = AppTopmost;
        settings.Hotkeys = new Dictionary<string, string>(HotkeyBindings(), StringComparer.Ordinal);

        // 外观：主题选择 + 跟随系统两套 + 已投放 + 字体（主题内容已在上面写进各自文件夹）
        _appearanceDraft.UiFonts = new FontFamilySetting { Latin = UiFontLatin, Cjk = UiFontCjk };
        _appearanceDraft.DataFonts = new FontFamilySetting { Latin = DataFontLatin, Cjk = DataFontCjk };
        settings.Appearance = _appearanceDraft.Clone();

        var result = _command.UpdateSettings(settings);
        var successMessage = result.Notices.Count > 0
            ? Loc("settings.status.appliedWithNotice", result.Notices[0])
            : Loc("settings.status.applied");

        StatusIsOk = result.Succeeded;
        StatusMessage = result.Succeeded
            ? successMessage
            : Loc("settings.status.notApplied", string.Join("；", result.Failures));

        if (!result.Succeeded)
        {
            return;
        }

        // 生效后回读一次，确保界面显示的就是**实际生效值**（也把草稿的脏标记一起清掉）
        Load();
        SetStatus(true, successMessage);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// **把脏主题写进各自的主题文件夹，再删掉草稿里移除的主题**（失败即返回 false，不写设置文件）。
    /// </summary>
    /// <returns>是否全部成功。</returns>
    private bool CommitThemeDrafts()
    {
        foreach (var draft in _themeDrafts.Values
            .Where(draft => draft.Dirty)
            .OrderBy(draft => draft.Id, StringComparer.Ordinal)
            .ToList())
        {
            var result = _command.SaveTheme(new ThemeSaveRequest(
                draft.Id,
                draft.Title,
                draft.Base,
                new Dictionary<string, string>(draft.Palette, StringComparer.Ordinal),
                draft.PendingWallpaper,
                draft.ClearWallpaper,
                draft.WallpaperVeilOpacity,
                draft.SurfaceOpacity));

            if (!result.Succeeded)
            {
                var reason = result.Failures.Count > 0 ? result.Failures[0] : Loc("settings.status.writeFailed");
                SetStatus(false, Loc("settings.status.themeSaveFailed", draft.Title, reason));
                return false;
            }

            draft.Dirty = false;
            draft.PendingWallpaper = null;
            draft.ClearWallpaper = false;
        }

        foreach (var id in _pendingDeletes.OrderBy(id => id, StringComparer.Ordinal).ToList())
        {
            var result = _command.DeleteTheme(id);
            if (!result.Succeeded)
            {
                SetStatus(false, result.Failures.Count > 0
                    ? result.Failures[0]
                    : Loc("settings.status.themeDeleteFailed", id));
                return false;
            }

            _ = _pendingDeletes.Remove(id);
        }

        return true;
    }

    private void SetStatus(bool ok, string message)
    {
        StatusIsOk = ok;
        StatusMessage = message;
    }
}
