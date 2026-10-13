using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Hotkeys;
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

    /// <summary>当前绑定（文本；空 = 不绑定）。</summary>
    [ObservableProperty]
    public partial string Binding { get; set; }

    /// <summary>是否处于"等待按键"状态（界面提示用）。</summary>
    [ObservableProperty]
    public partial bool IsCapturing { get; set; }
}

/// <summary>主题下拉里的一项（"跟随系统" 或某一套主题）。</summary>
/// <param name="Id">选择值：<see cref="ThemeIds.System"/> 或主题名（写进设置的就是它）。</param>
/// <param name="Title">显示名。</param>
/// <remarks>下拉框直接显示 <see cref="ToString"/> ⇒ 覆写成显示名，界面侧不必再挂模板。</remarks>
public sealed record ThemeChoice(string Id, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>模块来源根的诊断行（「模块清单」与设置页「模块与来源」共用）。</summary>
public sealed record ModuleSourceRowViewModel
{
    /// <summary>由来源根扫描结果创建。</summary>
    /// <param name="source">扫描结果。</param>
    public ModuleSourceRowViewModel(ModuleSourceView source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Directory = source.Directory;
        Summary = !source.Exists
            ? "目录不存在（已跳过）"
            : $"包 {source.PackagesFound} 个 · 跳过子目录 {source.SkippedDirectories} 个 · 失败 {source.FailedPackages} 个";
        Details = source.Messages.Count > 0 ? string.Join("\n", source.Messages) : "（没有模块包）";
        HasFailures = source.FailedPackages > 0 || !source.Exists;
    }

    /// <summary>根目录路径。</summary>
    public string Directory { get; }

    /// <summary>
    /// **紧凑显示路径**（一行一个目录，由于空间限制只显示几级，不显示的用省略号）。
    /// </summary>
    /// <remarks>
    /// 规则：层级 ≤ 3 → 原样；否则 `…\<倒数第二级>\<末级>`（形如 `…\model\modelA`），
    /// 完整路径仍在悬停提示里可见。
    /// </remarks>
    public string Display
    {
        get
        {
            var parts = Directory.TrimEnd('\\', '/').Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 3)
            {
                return Directory;
            }

            return $"…\\{parts[^2]}\\{parts[^1]}";
        }
    }

    /// <summary>一行摘要。</summary>
    public string Summary { get; }

    /// <summary>逐包结果（多行）。</summary>
    public string Details { get; }

    /// <summary>是否存在失败/缺失（界面强调用）。</summary>
    public bool HasFailures { get; }
}

/// <summary>
/// 设置面板（应用级设置；界面属 `FrameworkUi`）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>与"模块配置"严格区分</b>：本面板改的是**应用行为偏好**（跨组合件、不入组合件）；
///     模块实例的参数在"模块"页里改；</item>
///   <item><b>即时生效</b>：应用后立刻作用于运行时；校验失败**整体拒绝**并显示原因；</item>
///   <item>键位/吸附模式等 UI 偏好项暂未纳入。</item>
/// </list>
/// </remarks>
public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>最近一次载入/生效的设置快照（外观即时生效时以此为基础，避免把未应用的编辑一起写盘）。</summary>
    private ApplicationSettings? _lastLoaded;

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

    // ---- 框架行为 ----

    /// <summary>停止协议的有界等待时限（秒）。</summary>
    [ObservableProperty]
    public partial double StopTimeoutSeconds { get; set; } = 5;

    /// <summary>输入队列容量（单元数；对**新建**队列生效）。</summary>
    [ObservableProperty]
    public partial int InputQueueCapacity { get; set; } = 1024;

    /// <summary>是否启用快速建网（R0）。</summary>
    [ObservableProperty]
    public partial bool AlignAutoNetworkEnabled { get; set; } = true;

    /// <summary>诊断提示阈值（队列深度持续达到该值即提示）。</summary>
    [ObservableProperty]
    public partial int DiagnosticHintThreshold { get; set; } = 8;

    // ---- UI 偏好 ----

    /// <summary>UI 刷新率（Hz）。</summary>
    [ObservableProperty]
    public partial int UiRefreshRateHz { get; set; } = 60;

    /// <summary>
    /// **吸附距离**（px，2–8，默认 4）——拖动时的「磁吸半径」：
    /// 越小越跟手（最多滞后一个容差就松手），越大越容易吸上；贴合（相邻边接触）用它的 2 倍。
    /// </summary>
    [ObservableProperty]
    public partial double SnapDistance { get; set; } = 4;

    /// <summary>
    /// **外壳视图模型**（这些「开关类」设置的**唯一事实源**）——
    /// 交互页与设置页必须**完全联动**（各自持一份副本会让两边不一致 = 冲突）。
    /// </summary>
    public ShellViewModel? Owner { get; set; }

    /// <summary>无 <see cref="Owner"/> 时的回退值（**默认关闭**）。</summary>
    private bool _showInfoOverlayFallback;
    private bool _appTopmostFallback;

    /// <summary>
    /// 是否显示**叠加信息层**——**与交互页同一个开关**：有 <see cref="Owner"/> 时以它为准。
    /// </summary>
    public bool ShowInfoOverlay
    {
        get => Owner?.ShowInfoOverlay ?? _showInfoOverlayFallback;
        set
        {
            if (Owner is { } owner)
            {
                if (owner.ShowInfoOverlay == value)
                {
                    return;
                }

                owner.ShowInfoOverlay = value;
            }
            else
            {
                if (_showInfoOverlayFallback == value)
                {
                    return;
                }

                _showInfoOverlayFallback = value;
            }

            OnPropertyChanged();
            PersistLiveSwitches();
        }
    }

    /// <summary>应用整体置顶——**与交互页同一个开关**。</summary>
    public bool AppTopmost
    {
        get => Owner?.AppTopmost ?? _appTopmostFallback;
        set
        {
            if (Owner is { } owner)
            {
                if (owner.AppTopmost == value)
                {
                    return;
                }

                owner.AppTopmost = value;
            }
            else
            {
                if (_appTopmostFallback == value)
                {
                    return;
                }

                _appTopmostFallback = value;
            }

            OnPropertyChanged();
            PersistLiveSwitches();
        }
    }

    /// <summary>把"即时生效的开关"写回设置文件（点击即落盘，不需要按「应用」）。</summary>
    private void PersistLiveSwitches()
    {
        var settings = _lastLoaded?.Clone() ?? _query.GetSettings();
        settings.ShowInfoOverlay = ShowInfoOverlay;
        settings.AppTopmost = AppTopmost;

        _lastLoaded = settings;
        _ = _command.UpdateSettings(settings);
    }

    /// <summary>
    /// **可选主题**（"跟随系统" + 目录里的每一套）——改动**即时生效**。
    /// </summary>
    /// <remarks>
    /// 列表来自 <see cref="ThemeCatalog"/>：**加一套主题 = 目录里多一条**，本页不改。
    /// "跟随系统"不是一个主题（它只是"按系统类型挑一套"），故排在最前。
    /// </remarks>
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new(ThemeIds.System, "跟随系统"),
        .. ThemeCatalog.Themes.Select(theme => new ThemeChoice(theme.Id, theme.Title)),
    ];

    /// <summary>当前选中的主题项（**唯一状态**；写设置时取它的 <see cref="ThemeChoice.Id"/>）。</summary>
    [ObservableProperty]
    public partial ThemeChoice? SelectedThemeChoice { get; set; }

    /// <summary>
    /// **界面字体**：框架自身界面文字用的字体族（资源键 `FontUi`）。
    /// </summary>
    /// <remarks>可写**回退列表**（逗号分隔，如 <c>Times New Roman, 宋体</c>）；改动即时生效。</remarks>
    [ObservableProperty]
    public partial string UiFontFamily { get; set; } = DefaultUiFontFamily;

    /// <summary>
    /// **数据字体**：**所有输入框与显示框**用的字体族（资源键 `FontData`）。
    /// </summary>
    [ObservableProperty]
    public partial string DataFontFamily { get; set; } = DefaultDataFontFamily;

    /// <summary>
    /// **系统已有字体**（下拉候选项）——直接读系统已有字体即可。
    /// </summary>
    /// <remarks>
    /// <para>取 Avalonia 的 <c>FontManager.Current.SystemFonts</c>；无字体服务时（无头环境）回落到默认两项。</para>
    /// <para>
    /// 候选表若**只有系统字体名（单个名字）**，而设置值是**回退列表**
    /// （`Segoe UI, Microsoft YaHei UI` / `Times New Roman, 宋体`）⇒ 非编辑下拉框的
    /// `SelectedItem` 匹配不到任何一项 ⇒ **显示为空**。
    /// ⇒ ① 两条**默认回退列表**排在最前面（写回默认值时也选得中）；② 下拉框改为**可编辑**（`IsEditable`）并绑
    /// `Text` ⇒ 任何值都显示得出来、也能手输回退列表。
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> FontFamilyOptions { get; } =
        [DefaultUiFontFamily, DefaultDataFontFamily, .. LoadSystemFonts()];

    /// <summary>界面字体的**框架默认值**（回退列表；与 `ApplicationSettings` 的缺省一致）。</summary>
    public const string DefaultUiFontFamily = "Segoe UI, Microsoft YaHei UI";

    /// <summary>数据字体的**框架默认值**（回退列表；与 `ApplicationSettings` 的缺省一致）。</summary>
    public const string DefaultDataFontFamily = "Times New Roman, 宋体";

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

        return ["Segoe UI", "Microsoft YaHei UI", "Times New Roman", "宋体"];
    }

    partial void OnUiFontFamilyChanged(string value) { /* 只改本页下拉框的选中项；生效走「应用 / 确认」 */ }

    partial void OnDataFontFamilyChanged(string value) { /* 同上 */ }

    /// <summary>外壳窗口比例档位。</summary>
    [ObservableProperty]
    public partial string ShellAspect { get; set; } = "9:19.5";

    /// <summary>外壳尺寸是否固定（默认固定）。</summary>
    [ObservableProperty]
    public partial bool ShellFixedSize { get; set; } = true;

    /// <summary>比例档位选项（下拉用）。</summary>
    public IReadOnlyList<string> ShellAspectOptions { get; } = ShellLayout.Aspects;

    /// <summary>
    /// **背景预设**（两档）：一键把三支底色写进**两套主题**的覆盖里。
    /// </summary>
    /// <remarks>
    /// "背景档位"这个字段已经删掉：底就是三支普通的色（工作区 / 模块 / 外壳），
    /// 逐项可改、可跟随。预设只是"一次写六格"的**动作**（不是第二处状态）。
    /// </remarks>
    public IReadOnlyList<ThemePresets.BackgroundPreset> BackgroundPresets { get; } = ThemePresets.Backgrounds;

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

    // ---- 主题色板（逐项可改；面板按"浅 / 深"两栏呈现两套内置主题） ----

    /// <summary>
    /// **色板分区**：按色板契约（`Styles/palette.json`）生成**全部**相对色。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>一行 = 一支 token × 浅深两格</b>：留空 = 跟随（派生支跟随源、其余跟随内置值）；</item>
    ///   <item><b>没有例外</b>：主题色 / 背景 / 方向色就是普通的几支色（以前它们是独立字段 + 独立控件，
    ///     于是"一支色两处状态"）；</item>
    ///   <item><b>只在「应用 / 确认」时生效</b>：与外观区其余项同一机制（改完能「取消」反悔）。</item>
    /// </list>
    /// </remarks>
    public ObservableCollection<PaletteGroupViewModel> PaletteGroups { get; } = [];

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

    /// <summary>把设置里的覆盖写进各行（打开设置 / 取消 / 生效后回读时调）。</summary>
    /// <param name="settings">当前设置。</param>
    private void SyncPaletteFrom(ApplicationSettings settings)
    {
        foreach (var row in PaletteGroups.SelectMany(group => group.Rows))
        {
            row.Light.Hex = settings.Appearance.Override(row.Light.Theme, row.Key);
            row.Dark.Hex = settings.Appearance.Override(row.Dark.Theme, row.Key);
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
                row.Light.Refresh();
                row.Dark.Refresh();
            }

            group.Refresh();
        }
    }

    /// <summary>
    /// 把各行当前的覆盖写进**外观块**（只写非空 ⇒ **跟随不落盘**，不产生死数据）。
    /// </summary>
    /// <param name="appearance">目标外观块（通常是当前设置的那一份副本）。</param>
    private void CollectPaletteInto(AppearanceSettings appearance)
    {
        appearance.PaletteOverrides.Clear();

        foreach (var row in PaletteGroups.SelectMany(group => group.Rows))
        {
            foreach (var cell in new[] { row.Light, row.Dark })
            {
                if (cell.Hex is { Length: > 0 } hex)
                {
                    appearance.SetOverride(cell.Theme, cell.Token, hex);
                }
            }
        }
    }

    /// <summary>把全部色板的改动清掉（"恢复默认"用：回到跟随内置值）。</summary>
    private void ClearPaletteEdits()
    {
        foreach (var row in PaletteGroups.SelectMany(group => group.Rows))
        {
            row.Light.Hex = null;
            row.Dark.Hex = null;
        }

        RefreshPalette();
    }

    /// <summary>改某一格（<paramref name="hex"/> = null/空 ⇒ 恢复跟随内置值）。</summary>
    /// <param name="row">色板行。</param>
    /// <param name="dark">true = 深色那一格。</param>
    /// <param name="hex">新色值。</param>
    public void SetPaletteColor(PaletteRowViewModel row, bool dark, string? hex)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Cell(dark).Hex = string.IsNullOrWhiteSpace(hex) ? null : hex;
        RefreshPalette();
    }

    /// <summary>
    /// **两套同色**：把浅色那一格的当前有效色写成深色那一格的覆盖值。
    /// </summary>
    /// <param name="row">色板行。</param>
    /// <remarks>
    /// 常见诉求是"这一支两套用同一个色"（而不是"浅深各自调"）；浅色为准是刻意的 ——
    /// 浅色那一套是默认主题，先调它、再同步过去，比"先想清楚哪一套是源"少一步。
    /// 深色那一格因此变成**显式覆盖**（不再跟主题色派生），这是用户看得见的结果。
    /// </remarks>
    public void SyncPaletteToDark(PaletteRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Light.EffectiveHex is { } hex)
        {
            SetPaletteColor(row, dark: true, hex);
        }
    }

    /// <summary>某一区全部恢复跟随。</summary>
    /// <param name="group">色板分区。</param>
    public void ResetPaletteGroup(PaletteGroupViewModel group)
    {
        ArgumentNullException.ThrowIfNull(group);

        foreach (var row in group.Rows)
        {
            row.Light.Hex = null;
            row.Dark.Hex = null;
        }

        RefreshPalette();
    }

    /// <summary>**色板全部恢复默认**（全部回到跟随内置值）。</summary>
    [RelayCommand]
    private void ResetPalette()
    {
        ClearPaletteEdits();
        SetStatus(true, "色板已全部恢复默认（跟随内置值）—— 点「应用」或「确认」后生效");
    }

    // 外观改动**不是「改即生效」**：真正生效统一走「应用 / 确认」按钮。
    // 注意：换主题后色块要重读资源（跟随项取的是「当前主题的有效值」）⇒ 仍在这里发通知。
    partial void OnSelectedThemeChoiceChanged(ThemeChoice? value) => RefreshPalette();

    /// <summary>把设置里的主题选择落到下拉的选中项（选择值没有对应项时回落到"跟随系统"）。</summary>
    /// <param name="selection">设置里的选择值（<see cref="ThemeIds.System"/> 或某套主题的名字）。</param>
    private ThemeChoice? FindThemeChoice(string? selection)
    {
        var normalized = ThemeIds.Normalize(selection);

        return ThemeChoices.FirstOrDefault(choice => string.Equals(choice.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?? ThemeChoices.FirstOrDefault(choice => string.Equals(choice.Id, ThemeIds.System, StringComparison.Ordinal));
    }

    /// <summary>
    /// **套一档背景预设**：把三支底色写进**两套主题**（"浅灰就是浅灰"，与深浅主题无关）。
    /// </summary>
    /// <param name="preset">预设（工作区 / 模块 / 外壳三支底色）。</param>
    /// <remarks>
    /// 它是**动作**而不是第二处状态：写的就是色板里那三支的覆盖值，用户之后照样能逐项改。
    /// </remarks>
    public void ApplyBackgroundPreset(ThemePresets.BackgroundPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        foreach (var row in PaletteGroups.SelectMany(group => group.Rows))
        {
            switch (row.Key)
            {
                case "BgWorkspace":
                    row.Light.Hex = preset.Workspace;
                    row.Dark.Hex = preset.Workspace;
                    break;

                case "BgModule":
                    row.Light.Hex = preset.Module;
                    row.Dark.Hex = preset.Module;
                    break;

                case "BgChrome":
                    row.Light.Hex = preset.Chrome;
                    row.Dark.Hex = preset.Chrome;
                    break;

                default:
                    break;
            }
        }

        RefreshPalette();
        SetStatus(true, $"已套用背景预设「{preset.Title}」（两套主题）—— 点「应用」或「确认」后生效");
    }

    // ---- 模块来源 / 日志 ----

    /// <summary>
    /// 模块来源**根列表**（有序）。内置包目录（应用目录 `modules\`）**始终在首位**、不在此列表中显示为可编辑项。
    /// </summary>
    public ObservableCollection<string> ModuleSourceDirectories { get; } = [];

    /// <summary>待添加的来源根（输入框）。</summary>
    [ObservableProperty]
    public partial string? NewSourceDirectory { get; set; }

    /// <summary>选中的来源根（删除/排序用）。</summary>
    [ObservableProperty]
    public partial string? SelectedSourceDirectory { get; set; }

    /// <summary>来源根扫描诊断（每根的路径 · 包数 · 失败数 · 逐包结果）。</summary>
    public ObservableCollection<ModuleSourceRowViewModel> ModuleSources { get; } = [];

    // ---- 窗口与交互 ----
    // 注：「应用整体置顶」与「叠加信息层」定义为**联动属性**（见文件上方），此处不再声明本地字段。

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

    /// <summary>从当前生效值重新载入（启动与"还原"时调用）。</summary>
    public void Load()
    {
        var settings = _query.GetSettings();

        StopTimeoutSeconds = settings.StopTimeoutSeconds;
        InputQueueCapacity = settings.InputQueueCapacity;
        AlignAutoNetworkEnabled = settings.AlignAutoNetworkEnabled;
        DiagnosticHintThreshold = settings.DiagnosticHintThreshold;
        UiRefreshRateHz = settings.UiRefreshRateHz;
        SnapDistance = settings.SnapDistance;
        MultilineSubmitKey = settings.MultilineSubmitKey;
        ShowInfoOverlay = settings.ShowInfoOverlay;
        SelectedThemeChoice = FindThemeChoice(settings.Appearance.ThemeSelection);
        UiFontFamily = settings.Appearance.UiFontFamily;
        DataFontFamily = settings.Appearance.DataFontFamily;

        // 色板：行只建一次（不重建 ⇒ 折叠状态与滚动位置保住），这里只把生效值写进各行
        BuildPaletteGroups();
        SyncPaletteFrom(settings);
        ShellAspect = settings.ShellAspect;
        ShellFixedSize = settings.ShellFixedSize;
        LogLevel = settings.LogLevel;

        _lastLoaded = settings;

        // 装载 = 把**已生效**的设置套到界面（打开弹窗时保证"看到的 = 实际生效的"）
        ThemeApplier.Apply(settings);

        ModuleSourceDirectories.Clear();
        foreach (var directory in settings.ModuleSourceDirectories)
        {
            ModuleSourceDirectories.Add(directory);
        }

        AppTopmost = settings.AppTopmost;

        Hotkeys.Clear();
        foreach (var action in HotkeyCatalog.Actions)
        {
            var binding = settings.Hotkeys.TryGetValue(action.Id, out var configured) && !string.IsNullOrWhiteSpace(configured)
                ? configured
                : action.DefaultBinding;

            Hotkeys.Add(new HotkeyRowViewModel(action.Id, action.Title, action.DefaultBinding, binding));
        }

        HotkeyConflicts = DetectConflicts();

        RefreshModuleSources();
        StatusMessage = null;
    }

    /// <summary>设置**应用成功**后触发（供外壳重建快捷键绑定、套用窗口层级等）。</summary>
    public event EventHandler? Applied;

    /// <summary>当前生效的快捷键绑定（供外壳重建绑定；空绑定 = 不绑定，值为空串）。</summary>
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
        SetStatus(true, "快捷键已修改 —— 点「应用」后生效");
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
                conflicts.Add($"「{row.Title}」与「{owner}」都是 {row.Binding}（前者生效）");
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
        SetStatus(true, "已恢复默认快捷键 —— 点「应用」后生效");
    }

    /// <summary>
    /// **全局恢复默认**（一键还原**所有**设置，含快捷键与外观，做成全局式按钮）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>范围</b>：本页全部可配置项（应用行为 / 外观 / 字体 / 快捷键 / 模块来源根 / 日志级别 / 置顶）；
    ///     取的是 `ApplicationSettings` 的**缺省值本身**（不另写一份"默认值清单"——两份迟早会漂）；</item>
    ///   <item><b>不落盘</b>：与设置页既有的三键机制一致（"任何改动都只是**待生效**"）⇒ 状态栏提示"点应用/确认生效"，
    ///     用户还能用「取消」反悔；</item>
    ///   <item>**数据结构颜色表**（类型 → 颜色）不在本页 ⇒ 不动它（那是另一处入口的映射表）。</item>
    /// </list>
    /// </remarks>
    [RelayCommand]
    private void ResetAllSettings()
    {
        var defaults = new ApplicationSettings();

        StopTimeoutSeconds = defaults.StopTimeoutSeconds;
        InputQueueCapacity = defaults.InputQueueCapacity;
        AlignAutoNetworkEnabled = defaults.AlignAutoNetworkEnabled;
        DiagnosticHintThreshold = defaults.DiagnosticHintThreshold;
        UiRefreshRateHz = defaults.UiRefreshRateHz;
        SnapDistance = defaults.SnapDistance;
        MultilineSubmitKey = NormalizeSubmitKey(defaults.MultilineSubmitKey);
        ShowInfoOverlay = defaults.ShowInfoOverlay;
        SelectedThemeChoice = FindThemeChoice(defaults.Appearance.ThemeSelection);
        UiFontFamily = defaults.Appearance.UiFontFamily;
        DataFontFamily = defaults.Appearance.DataFontFamily;
        // 色板也回默认：全部回到"跟随内置值"（缺这一段时，"恢复全部默认"对色板毫无作用）
        ClearPaletteEdits();
        ShellAspect = defaults.ShellAspect;
        ShellFixedSize = defaults.ShellFixedSize;
        LogLevel = defaults.LogLevel;
        AppTopmost = defaults.AppTopmost;

        ModuleSourceDirectories.Clear();
        foreach (var directory in defaults.ModuleSourceDirectories)
        {
            ModuleSourceDirectories.Add(directory);
        }

        // 快捷键走既有那条路径（每行回到 `DefaultBinding`），避免默认值有两个来源。
        ResetHotkeys();
        SetStatus(true, "已恢复全部默认设置（含快捷键与外观）—— 点「应用」或「确认」后生效");
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
    /// 刷新来源根扫描诊断（来自运行时的最近一次扫描结果）。
    /// </summary>
    /// <remarks>增量同步：内容没变就不动列表（避免每帧重建导致悬停/选中状态闪烁）。</remarks>
    public void RefreshModuleSources()
    {
        var sources = _query.GetModuleSources().Select(source => new ModuleSourceRowViewModel(source)).ToList();
        _ = CollectionSync.Sync(ModuleSources, sources);
    }

    /// <summary>添加来源根（目录不存在也允许添加：扫描时会明确提示"目录不存在"）。</summary>
    [RelayCommand]
    private void AddSourceDirectory()
    {
        if (string.IsNullOrWhiteSpace(NewSourceDirectory))
        {
            SetStatus(false, "请先填写来源根目录路径");
            return;
        }

        var directory = NewSourceDirectory.Trim();
        if (ModuleSourceDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus(false, $"该来源根已在列表中：{directory}");
            return;
        }

        ModuleSourceDirectories.Add(directory);
        SelectedSourceDirectory = directory;
        NewSourceDirectory = null;
        SetStatus(true, "已加入列表 —— 点「应用」后生效，「立即重扫」可立刻扫描");
    }

    /// <summary>移除选中的来源根。</summary>
    [RelayCommand]
    private void RemoveSourceDirectory()
    {
        if (SelectedSourceDirectory is not { Length: > 0 } directory)
        {
            SetStatus(false, "请先选中一个来源根");
            return;
        }

        _ = ModuleSourceDirectories.Remove(directory);
        SelectedSourceDirectory = ModuleSourceDirectories.FirstOrDefault();
        SetStatus(true, "已移除 —— 点「应用」后生效");
    }

    /// <summary>上移 / 下移来源根（顺序 = 优先级：先扫到的包胜出）。</summary>
    [RelayCommand]
    private void MoveSourceDirectoryUp() => MoveSourceDirectory(-1);

    /// <summary>下移来源根。</summary>
    [RelayCommand]
    private void MoveSourceDirectoryDown() => MoveSourceDirectory(1);

    /// <summary>
    /// **立刻重扫**来源根（不重启应用）：装入新增的模块包并刷新诊断。
    /// </summary>
    /// <remarks>改过代码的包需要重启应用才生效（框架不实现热重载）——结果里会给出提示。</remarks>
    [RelayCommand]
    private void RescanModuleSources()
    {
        var result = _command.RescanModuleSources();

        RefreshModuleSources();
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? result.Notices.Count > 0 ? result.Notices[0] : "重扫完成"
                : $"重扫失败：{string.Join("；", result.Failures)}");
    }

    private void MoveSourceDirectory(int delta)
    {
        if (SelectedSourceDirectory is not { Length: > 0 } directory)
        {
            SetStatus(false, "请先选中一个来源根");
            return;
        }

        var index = ModuleSourceDirectories.IndexOf(directory);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ModuleSourceDirectories.Count)
        {
            return;
        }

        ModuleSourceDirectories.Move(index, target);
        SetStatus(true, "顺序已调整 —— 点「应用」后生效（顺序 = 优先级）");
    }

    /// <summary>
    /// **确认**：应用并关闭弹窗（校验失败 ⇒ **不关**，把原因留在状态行）。
    /// </summary>
    /// <remarks>设置生效机制是常规三键：**确认 / 取消 / 应用**
    /// （不是「改一下即时生效」）。三者语义：
    /// <list type="bullet">
    ///   <item><b>应用</b> = 校验 → 生效 → 落盘，**弹窗留着**（继续改）；</item>
    ///   <item><b>确认</b> = 应用 + 关闭；失败则不关（用户能看见原因）；</item>
    ///   <item><b>取消</b> = 丢弃本次改动（回到已生效值）并关闭。</item>
    /// </list>
    /// </remarks>
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

    /// <summary>应用设置（校验 → 即时生效 → 原子写盘）。</summary>
    [RelayCommand]
    private void Apply()
    {
        var settings = _query.GetSettings();

        settings.StopTimeoutSeconds = StopTimeoutSeconds;
        settings.InputQueueCapacity = InputQueueCapacity;
        settings.AlignAutoNetworkEnabled = AlignAutoNetworkEnabled;
        settings.DiagnosticHintThreshold = DiagnosticHintThreshold;
        settings.UiRefreshRateHz = UiRefreshRateHz;
        settings.SnapDistance = Math.Clamp(SnapDistance, 2, 8);
        settings.MultilineSubmitKey = NormalizeSubmitKey(MultilineSubmitKey);
        settings.ShowInfoOverlay = ShowInfoOverlay;
        settings.Appearance.ThemeSelection = SelectedThemeChoice?.Id ?? ThemeIds.System;
        settings.Appearance.UiFontFamily = UiFontFamily;
        settings.Appearance.DataFontFamily = DataFontFamily;
        // 主题色板：只写用户逐项改过的那几支（跟随 = 不写 ⇒ 不产生死数据）
        CollectPaletteInto(settings.Appearance);
        settings.ShellAspect = ShellAspect;
        settings.ShellFixedSize = ShellFixedSize;
        settings.ModuleSourceDirectories = [.. ModuleSourceDirectories];
        settings.LogLevel = LogLevel;
        settings.AppTopmost = AppTopmost;
        settings.Hotkeys = new Dictionary<string, string>(HotkeyBindings(), StringComparer.Ordinal);

        var result = _command.UpdateSettings(settings);

        StatusIsOk = result.Succeeded;
        StatusMessage = result.Succeeded
            ? result.Notices.Count > 0
                ? $"已生效（{result.Notices[0]}）"
                : "已生效"
            : $"未生效：{string.Join("；", result.Failures)}";

        if (result.Succeeded)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }

        if (_query is { } query && result.Succeeded)
        {
            // 生效后回读一次，确保界面显示的就是**实际生效值**
            var current = query.GetSettings();
            StopTimeoutSeconds = current.StopTimeoutSeconds;
            InputQueueCapacity = current.InputQueueCapacity;
            UiRefreshRateHz = current.UiRefreshRateHz;
            DiagnosticHintThreshold = current.DiagnosticHintThreshold;
            AlignAutoNetworkEnabled = current.AlignAutoNetworkEnabled;
            ShowInfoOverlay = current.ShowInfoOverlay;
            SelectedThemeChoice = FindThemeChoice(current.Appearance.ThemeSelection);
            // 字体也必须回读：缺这两行时，界面上显示的是「你刚选的」，而实际生效的是默认值
            // （真因可能是 `ApplicationSettings.Clone` 漏字段，但这里不同步回读会让
            //  「界面显示 = 实际生效值」这条纪律再破一次）。
            UiFontFamily = current.Appearance.UiFontFamily;
            DataFontFamily = current.Appearance.DataFontFamily;
            // 色板也要回读：生效值就是我们刚写进去的那张表，回读让"界面上显示的 = 实际生效的"
            SyncPaletteFrom(current);
            ShellAspect = current.ShellAspect;
            ShellFixedSize = current.ShellFixedSize;
            _lastLoaded = current;
            ThemeApplier.Apply(current);
            LogLevel = current.LogLevel;
        }
    }

    /// <summary>还原为当前生效值。</summary>
    [RelayCommand]
    private void Reload() => Load();

    private void SetStatus(bool ok, string message)
    {
        StatusIsOk = ok;
        StatusMessage = message;
    }
}
