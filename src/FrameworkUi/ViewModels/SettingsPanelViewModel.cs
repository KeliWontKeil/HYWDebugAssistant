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
    /// **紧凑显示路径**（用户第十轮反馈："一行一个目录，由于空间限制只显示几级，不显示的用省略号"）。
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
/// 设置面板（应用级设置，ADR-0030 / 04b；界面属 `FrameworkUi`，C4b-2）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>与"模块配置"严格区分</b>：本面板改的是**应用行为偏好**（跨组合件、不入组合件）；
///     模块实例的参数在"模块"页里改（§5.6）；</item>
///   <item><b>即时生效</b>：应用后立刻作用于运行时（§9.5）；校验失败**整体拒绝**并显示原因；</item>
///   <item>键位/吸附模式等 UI 偏好项暂未纳入（Gate C 定稿，`10` §5）。</item>
/// </list>
/// </remarks>
public sealed partial class SettingsPanelViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>最近一次载入/生效的设置快照（外观即时生效时以此为基础，避免把未应用的编辑一起写盘）。</summary>
    private ApplicationSettings? _lastLoaded;

    /// <summary>外观是否已就绪（避免初始化期把默认值覆盖到界面）。</summary>
    private bool _appearanceReady;

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
    /// **吸附距离**（px，2–8，默认 4）——【ADR-0042 D6 / 用户裁决】拖动时的"磁吸半径"：
    /// 越小越跟手（最多滞后一个容差就松手），越大越容易吸上；贴合（相邻边接触）用它的 2 倍。
    /// </summary>
    [ObservableProperty]
    public partial double SnapDistance { get; set; } = 4;

    /// <summary>
    /// **外壳视图模型**（这些"开关类"设置的**唯一事实源**）——【用户第十一轮反馈】
    /// 交互页与设置页必须**完全联动**（此前设置页各自持有一份副本，两边可以不一致 = "冲突"）。
    /// </summary>
    public ShellViewModel? Owner { get; set; }

    /// <summary>无 <see cref="Owner"/> 时的回退值（**默认关闭** —— ADR-0045 D1）。</summary>
    private bool _showInfoOverlayFallback;
    private bool _appTopmostFallback;

    /// <summary>
    /// 是否显示**叠加信息层**（ADR-0039 D5）——**与交互页同一个开关**：有 <see cref="Owner"/> 时以它为准。
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

    /// <summary>应用整体置顶（ADR-0036 D5）——**与交互页同一个开关**。</summary>
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

    /// <summary>主题（Light / Dark / System）——改动**即时生效**（ADR-0038 D4）。</summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "Light";

    /// <summary>主题色（十六进制文本；改动即时生效）。</summary>
    [ObservableProperty]
    public partial string AccentColor { get; set; } = "#2563EB";

    /// <summary>背景档位（Theme / LightGray / DarkGray / Custom）。</summary>
    [ObservableProperty]
    public partial string BackgroundMode { get; set; } = "Theme";

    /// <summary>自定义背景色（十六进制文本；仅 Custom 档位生效）。</summary>
    [ObservableProperty]
    public partial string BackgroundColor { get; set; } = "#F3F4F6";

    /// <summary>外壳窗口比例档位（ADR-0038 D2）。</summary>
    [ObservableProperty]
    public partial string ShellAspect { get; set; } = "9:19.5";

    /// <summary>外壳尺寸是否固定（ADR-0038 D2；默认固定）。</summary>
    [ObservableProperty]
    public partial bool ShellFixedSize { get; set; } = true;

    /// <summary>比例档位选项（下拉用）。</summary>
    public IReadOnlyList<string> ShellAspectOptions { get; } = ShellLayout.Aspects;

    /// <summary>主题档位选项（下拉用）。</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = ["Light", "Dark", "System"];

    /// <summary>背景档位选项（下拉用）。</summary>
    public IReadOnlyList<string> BackgroundOptions { get; } =
        [ThemeApplier.BackgroundTheme, ThemeApplier.BackgroundLightGray, ThemeApplier.BackgroundDarkGray, ThemeApplier.BackgroundCustom];

    /// <summary>
    /// **多行文本输入的提交键**（G2 · 2026-09）——三档字符串（值域与 `ApplicationSettings` 一致）。
    /// </summary>
    /// <remarks>
    /// 用户 2026-09 裁决："换行是一个极其常见的行为，且**不同用户习惯不一样**" ⇒ 做成**框架级可配置项**，
    /// 而**不是**模块声明（模块只声明"我要多行"）。**只影响多行输入框**；单行输入仍"回车 / 失焦一次提交"（ADR-0040 D-5）。
    /// </remarks>
    [ObservableProperty]
    public partial string MultilineSubmitKey { get; set; } = "CtrlEnter";

    /// <summary>多行提交键选项（下拉用）：`CtrlEnter`（回车换行 / Ctrl+Enter 提交 / 失焦不提交）· `Enter`（回车提交 / Shift+Enter 换行）· `None`（键盘都不提交）。</summary>
    public IReadOnlyList<string> MultilineSubmitKeyOptions { get; } = ["CtrlEnter", "Enter", "None"];

    /// <summary>把多行提交键收敛到三档之一（非法/空 ⇒ 默认 `CtrlEnter`；与外观类设置同一容错口径：不校验、不提示）。</summary>
    /// <param name="value">用户选择或文件里的值。</param>
    private static string NormalizeSubmitKey(string? value) =>
        value is "Enter" or "None" or "CtrlEnter" ? value : "CtrlEnter";

    /// <summary>当前主题色画刷（设置页的"色块"按钮显示它；用户在弹窗里改色）。</summary>
    public Avalonia.Media.IBrush AccentBrush =>
        ThemeApplier.Parse(AccentColor) is { } color
            ? new Avalonia.Media.SolidColorBrush(color)
            : Avalonia.Media.Brushes.Transparent;

    /// <summary>当前自定义背景色画刷（同上）。</summary>
    public Avalonia.Media.IBrush BackgroundBrush =>
        ThemeApplier.Parse(BackgroundColor) is { } color
            ? new Avalonia.Media.SolidColorBrush(color)
            : Avalonia.Media.Brushes.Transparent;

    // ---- 方向语义色【ADR-0044】----

    /// <summary>
    /// **发送方向色**（十六进制；**空 = 跟随主题默认值**）。
    /// </summary>
    /// <remarks>
    /// 语义 = 「发送」这一**稳定方向**，消费者：网络标签箭头 / 网络标签边框 / 建网预览描边 / 叠加层流向箭头。
    /// 与「主题色」**完全解耦**（用户第十四轮 D1a）：主题色只管选中/聚焦/活动等框架性交互。
    /// </remarks>
    [ObservableProperty]
    public partial string DirectionSendColor { get; set; } = string.Empty;

    /// <summary>**接收方向色**（十六进制；**空 = 跟随主题默认值**）。</summary>
    [ObservableProperty]
    public partial string DirectionReceiveColor { get; set; } = string.Empty;

    /// <summary>发送色片显示用的画刷（未自定义时取**当前主题的有效方向色**，故切主题会跟着变）。</summary>
    public Avalonia.Media.IBrush DirectionSendBrush => EffectiveDirectionBrush("DirectionSend", DirectionSendColor);

    /// <summary>接收色片显示用的画刷（同上）。</summary>
    public Avalonia.Media.IBrush DirectionReceiveBrush => EffectiveDirectionBrush("DirectionReceive", DirectionReceiveColor);

    /// <summary>发送色文本（空 → 显示"跟随主题"，避免"色块有色、框里却空着"的困惑）。</summary>
    public string DirectionSendText => string.IsNullOrWhiteSpace(DirectionSendColor) ? "跟随主题" : DirectionSendColor;

    /// <summary>接收色文本（同上）。</summary>
    public string DirectionReceiveText => string.IsNullOrWhiteSpace(DirectionReceiveColor) ? "跟随主题" : DirectionReceiveColor;

    /// <summary>取"用户填了就用用户的，没填就用当前主题的内置值"的画刷。</summary>
    private static Avalonia.Media.IBrush EffectiveDirectionBrush(string resourceKey, string hex)
    {
        if (ThemeApplier.Parse(hex) is { } configured)
        {
            return new Avalonia.Media.SolidColorBrush(configured);
        }

        return Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var resource) == true
            && resource is Avalonia.Media.IBrush brush
            ? brush
            : Avalonia.Media.Brushes.Transparent;
    }

    // 外观改动**即时生效**（ADR-0038 D4：主题/主题色/背景在线生效，与 §7.1 全在线变更一致）
    // 注意：主题切换后**方向色片要重读资源**（未自定义时取的是"当前主题的有效值"）⇒ 通知放到 Apply 之后
    partial void OnThemeChanged(string value)
    {
        ApplyAppearance();
        OnPropertyChanged(nameof(DirectionSendBrush));
        OnPropertyChanged(nameof(DirectionReceiveBrush));
    }

    partial void OnAccentColorChanged(string value)
    {
        OnPropertyChanged(nameof(AccentBrush));
        ApplyAppearance();
    }

    partial void OnBackgroundModeChanged(string value) => ApplyAppearance();

    partial void OnBackgroundColorChanged(string value)
    {
        OnPropertyChanged(nameof(BackgroundBrush));
        ApplyAppearance();
    }

    // 方向色【ADR-0044】：改动即时生效；**不做任何合法性/对比度提示**（裁决 D6：完全尊重用户选择）
    partial void OnDirectionSendColorChanged(string value)
    {
        OnPropertyChanged(nameof(DirectionSendBrush));
        OnPropertyChanged(nameof(DirectionSendText));
        ApplyAppearance();
    }

    partial void OnDirectionReceiveColorChanged(string value)
    {
        OnPropertyChanged(nameof(DirectionReceiveBrush));
        OnPropertyChanged(nameof(DirectionReceiveText));
        ApplyAppearance();
    }

    /// <summary>把当前外观设置应用到界面（未装载设置时不做事，避免初始化期覆盖默认）。</summary>
    private void ApplyAppearance()
    {
        if (_appearanceReady)
        {
            ThemeApplier.Apply(ToAppearanceSettings());
        }
    }

    /// <summary>导出外观设置（只含外观字段，避免把未应用的其他编辑一起落盘）。</summary>
    private ApplicationSettings ToAppearanceSettings()
    {
        var settings = _lastLoaded?.Clone() ?? new ApplicationSettings();
        settings.Theme = Theme;
        settings.AccentColor = AccentColor;
        settings.BackgroundMode = BackgroundMode;
        settings.BackgroundColor = BackgroundColor;
        settings.DirectionSendColor = DirectionSendColor;
        settings.DirectionReceiveColor = DirectionReceiveColor;
        return settings;
    }

    // ---- 模块来源 / 日志 ----

    /// <summary>
    /// 模块来源**根列表**（有序；ADR-0036 D1）。内置包目录（应用目录 `modules\`）**始终在首位**、不在此列表中显示为可编辑项。
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

    // ---- 快捷键（ADR-0036 D7） ----

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
        Theme = settings.Theme;
        AccentColor = settings.AccentColor;
        BackgroundMode = settings.BackgroundMode;
        BackgroundColor = settings.BackgroundColor;
        DirectionSendColor = settings.DirectionSendColor;
        DirectionReceiveColor = settings.DirectionReceiveColor;
        ShellAspect = settings.ShellAspect;
        ShellFixedSize = settings.ShellFixedSize;
        LogLevel = settings.LogLevel;

        _lastLoaded = settings;
        _appearanceReady = true;
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
    /// 当前设置里的**数据结构 → 颜色**映射（键 = 类型身份，值 = `#RRGGBB`）【ADR-0054】。
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
        settings.Theme = Theme;
        settings.AccentColor = AccentColor;
        settings.BackgroundMode = BackgroundMode;
        settings.BackgroundColor = BackgroundColor;
        settings.DirectionSendColor = DirectionSendColor;
        settings.DirectionReceiveColor = DirectionReceiveColor;
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
            Theme = current.Theme;
            AccentColor = current.AccentColor;
            BackgroundMode = current.BackgroundMode;
            BackgroundColor = current.BackgroundColor;
            DirectionSendColor = current.DirectionSendColor;
            DirectionReceiveColor = current.DirectionReceiveColor;
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
