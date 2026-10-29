using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Hotkeys;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// 侧边界面（B 区）的页标识。
/// </summary>
/// <remarks>
/// 模块清单、组合与「交互」是**常驻侧边页面**；设置 / 拓扑诊断 / Issue / 帮助仍为弹窗。
/// </remarks>
public static class ShellPages
{
    /// <summary>模块清单页。</summary>
    public const string Catalog = "catalog";

    /// <summary>组合页（选择模块并保存/加载组合关系）。</summary>
    public const string Composite = "composite";

    /// <summary>交互页（显示 / 吸附 / 窗口三组开关与说明）。</summary>
    public const string Interaction = "interaction";
}

/// <summary>要打开的弹窗种类（Issue / 设置 / 模块清单 / 拓扑诊断一律弹窗）。</summary>
public enum DialogKind
{
    /// <summary>模块清单 / 加载 / 管理。</summary>
    ModuleCatalog = 0,

    /// <summary>Issue 汇总。</summary>
    Issues = 1,

    /// <summary>应用级设置。</summary>
    Settings = 2,

    /// <summary>拓扑诊断（**只读**）。</summary>
    Topology = 3,
}

/// <summary>
/// **框架外壳**视图模型（`FrameworkUi` = 外层主窗口）：
/// 菜单/工具栏/状态栏 + 四个弹窗的编排 + 模块窗口集的控制入口。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>外壳**不画模块**：模块各自是独立的无边框窗口（`Ui` 的模块窗口集负责）；
///     外壳自身是可被贴靠的一块（拼图"起始点"）；</item>
///   <item>开关按**用途分组**：显示（口标记）/ 交互（几何吸附）/ 窗口（锁定全部）；</item>
///   <item>一切读写只经 <c>Core.Api</c>；本类不含机制逻辑。</item>
/// </list>
/// </remarks>
public sealed partial class ShellViewModel : ViewModelBase, IHotkeyActions
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>创建外壳视图模型。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="command">命令面。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：透传给模块窗口集
    /// （切页选中态 / 输入框未提交文本 / 显示缓冲）。`null` ⇒ 回落今天的行为。
    /// </param>
    /// <param name="noticeLifetime">
    /// **状态栏临时提示的存活时长**（缺省 = <see cref="DefaultNoticeLifetime"/>，8 秒）。
    /// 显式参数而不是可变静态字段：生产用默认值，用例把它调成几十毫秒就能测"到点自动过期"
    /// （否则那种用例要么慢、要么偶发）。
    /// </param>
    public ShellViewModel(
        IRuntimeQuery query,
        IRuntimeCommand command,
        IViewStateAccess? viewState = null,
        TimeSpan? noticeLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
        _noticeLifetime = noticeLifetime ?? DefaultNoticeLifetime;

        Hotkeys = new HotkeyService(this);
        Windows = new ModuleWindowsViewModel(query, command, viewState);
        Composites = new CompositePageViewModel(query, command, Windows);

        // 模块窗口发出的**面向用户提示**（建网/端口标签结果）→ 状态栏
        Windows.NoticeRaised += (_, notice) =>
        {
            StatusNotice = notice.Message;
            StatusNoticeIsError = notice.IsError;
            _statusNoticeRaisedAt = Stopwatch.GetTimestamp();
            UpdateStatus();
        };
        Catalog = new ModuleCatalogViewModel(query, command);
        Catalog.RestartRequested += (_, _) => RestartRequested?.Invoke(this, EventArgs.Empty);
        Issues = new IssuesPanelViewModel(query, command);
        Settings = new SettingsPanelViewModel(query, command);
        Topology = new TopologyViewModel(query);

        // 占位实例属**诊断** → 其列表与"指定替代"命令由模块清单视图模型提供，在拓扑诊断弹窗里呈现
        Topology.Catalog = Catalog;

        // 开关类设置（叠加信息层 / 应用置顶）**以本视图模型为唯一事实源**，
        // 设置页与交互页因此完全联动（各持一份副本会让两边不一致）。
        Settings.Owner = this;
    }

    /// <summary>应用内快捷键服务（挂到本应用每一个窗口上）。</summary>
    public HotkeyService Hotkeys { get; }

    /// <summary>模块窗口集（模块区 = 一批独立窗口）。</summary>
    public ModuleWindowsViewModel Windows { get; }

    /// <summary>组合页：选择模块并保存/加载一次性的组合关系。</summary>
    public CompositePageViewModel Composites { get; }

    /// <summary>模块清单弹窗视图模型。</summary>
    public ModuleCatalogViewModel Catalog { get; }

    /// <summary>Issue 弹窗视图模型。</summary>
    public IssuesPanelViewModel Issues { get; }

    /// <summary>设置弹窗视图模型。</summary>
    public SettingsPanelViewModel Settings { get; }

    /// <summary>拓扑诊断弹窗视图模型（只读）。</summary>
    public TopologyViewModel Topology { get; }

    /// <summary>
    /// **数据结构 → 颜色** 映射表（交互页）。
    /// </summary>
    /// <remarks>
    /// 行 = 当前现场出现过的**类型身份**（`标识@v版本`）；点色块可改色、可"恢复自动"。
    /// 改色只影响呈现（端口标记的窄色条 + 叠加层角色方块的填充同源）。
    /// </remarks>
    public DataTypeColorTableViewModel DataTypeColors { get; } = new();

    /// <summary>
    /// 刷新**数据结构 → 颜色**表（现场的类型集合 + 各自的覆盖色）。
    /// </summary>
    /// <remarks>
    /// 类型来源 = **端口视图的类型身份**（`PortView.TypeKey`，与色板/角色键同源）——只列**现场真出现过的**类型，
    /// 不列"声明了但没用上"的（那张表要与用户看到的东西对得上）。按类型身份排序，保证刷新后顺序稳定。
    /// </remarks>
    public void RefreshDataTypeColors()
    {
        var usage = new Dictionary<string, (string Shape, int Ports, int Roles)>(StringComparer.Ordinal);

        foreach (var instance in _query.GetInstances())
        {
            foreach (var port in instance.Ports)
            {
                if (port.TypeKey is not { Length: > 0 } key)
                {
                    continue;
                }

                usage.TryGetValue(key, out var current);
                usage[key] = (
                    current.Shape is { Length: > 0 } ? current.Shape : port.ContractText ?? string.Empty,
                    current.Ports + 1,
                    current.Roles);
            }

            foreach (var role in _query.GetRoles(instance.InstanceId))
            {
                if (role.TypeKey is not { Length: > 0 } key)
                {
                    continue;
                }

                usage.TryGetValue(key, out var current);
                usage[key] = (current.Shape, current.Ports, current.Roles + 1);
            }
        }

        var overrides = Settings.TypeColorOverrides();

        // **就地更新**（否则鼠标指到色块上时不停闪烁）：
        // 每次刷新都 `Clear()` + 重建整张表 ⇒ 半个刷新周期换一批控件，正在打开的取色弹窗
        // 目标被销毁 ⇒ 反复开关 = 闪烁（也让人以为"改不了色"）。
        // ⇒ 行对象**只在类型集合变化时**增删；其余情况就地改值（集合稳定 ⇒ 弹窗不会被拆掉）。
        var existing = DataTypeColors.Rows.ToDictionary(row => row.TypeKey, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, info) in usage.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _ = overrides.TryGetValue(key, out var hex);
            _ = seen.Add(key);

            var text = info.Roles > 0
                ? Loc("shell.dataTypeColors.usage.portsRoles", info.Ports, info.Roles)
                : Loc("shell.dataTypeColors.usage.ports", info.Ports);

            if (existing.TryGetValue(key, out var row))
            {
                row.Update(info.Shape, text, hex);
                continue;
            }

            var added = new DataTypeColorRowViewModel(key, info.Shape, text, hex, ApplyDataTypeColor);
            DataTypeColors.Rows.Insert(IndexOf(key), added);
        }

        for (var index = DataTypeColors.Rows.Count - 1; index >= 0; index--)
        {
            if (!seen.Contains(DataTypeColors.Rows[index].TypeKey))
            {
                DataTypeColors.Rows.RemoveAt(index);
            }
        }

        DataTypeColors.IsEmpty = DataTypeColors.Rows.Count == 0;
        DataTypeColors.EmptyText = Loc("shell.dataTypeColors.empty");

        // 按类型身份排序插入（新类型插到正确位置，不打断既有行的实例）
        int IndexOf(string typeKey)
        {
            for (var index = 0; index < DataTypeColors.Rows.Count; index++)
            {
                if (string.CompareOrdinal(DataTypeColors.Rows[index].TypeKey, typeKey) > 0)
                {
                    return index;
                }
            }

            return DataTypeColors.Rows.Count;
        }
    }

    /// <summary>
    /// 应用一行的颜色（`hex = null` ⇒ 恢复自动配色）：**唯一入口** —— 同时更新色板、落盘、并让界面重画。
    /// </summary>
    /// <param name="typeKey">类型身份。</param>
    /// <param name="hex">`#RRGGBB`；null/空 ⇒ 移除覆盖。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>色板是唯一色彩来源</b>：端口标记的窄色条与叠加层角色方块都经 `PacketTypePalette` 取色
    ///     ⇒ 这里只改色板，两处自然同步；</item>
    ///   <item><b>落盘</b>：写进应用设置（`typeColors`）⇒ 重启仍生效；</item>
    ///   <item><b>重画</b>：让模块窗口重新读一次类型画刷（标记）并重设角色列表（叠加层触发重绘）。</item>
    /// </list>
    /// </remarks>
    public void ApplyDataTypeColor(string typeKey, string? hex)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return;
        }

        if (ThemeApplier.Parse(hex) is { } color)
        {
            PacketTypePalette.SetOverride(typeKey, color);
            Settings.SetTypeColor(typeKey, DataTypeColorRowViewModel.ToHex(color));
        }
        else
        {
            _ = PacketTypePalette.RemoveOverride(typeKey);
            Settings.SetTypeColor(typeKey, null);
        }

        Windows.RefreshTypeColors();
        RefreshDataTypeColors();
    }

    /// <summary>请求打开某个弹窗（由视图创建/显示窗口）。</summary>
    public event EventHandler<DialogKind>? DialogRequested;

    /// <summary>请求“保存现场并重启应用”（由主窗口执行进程重启）。</summary>
    public event EventHandler? RestartRequested;

    /// <summary>请求把本应用全部窗口抬到最前（由视图执行；组抬升是平台行为）。</summary>
    public event EventHandler? RaiseAllWindowsRequested;

    /// <summary>
    /// **应用整体置顶**：为 <see langword="true"/> 时本应用**全部**窗口
    /// （外壳 + 模块窗口 + 弹窗，含之后新开的）一起置顶——**不提供单窗口置顶**，
    /// 避免"一部分被覆盖、一部分置顶"的混合状态。由视图落到窗口组上。
    /// </summary>
    [ObservableProperty]
    public partial bool AppTopmost { get; set; }

    /// <summary>最近一次面向用户的提示（建网结果、端口标签提交结果等；空 = 无）。</summary>
    /// <remarks>
    /// 这条提示若**没有任何过期机制**（只有下一条提示才会替换它）⇒
    /// 一条陈旧提示会长期占据状态栏；又因为它被拼在摘要文本**末尾**、而主窗口只有 396 宽，
    /// 摘要占满后提示被 `TextTrimming` 截成「拖动…」⇒ 看起来是**未完成显示的信息**。
    /// ⇒ 提示必须<b>独占一列</b>（摘要先省略，见 `MainWindow.axaml` 状态栏）+ <b>到点自动过期</b>。
    /// </remarks>
    [ObservableProperty]
    public partial string? StatusNotice { get; set; }

    /// <summary>
    /// **临时提示的默认存活时长**（8 秒；到点由 <see cref="Refresh"/> 清掉）。
    /// </summary>
    /// <remarks>
    /// 提示若**没有过期机制**（只有下一条提示才会替换它）⇒ 一条陈旧提示会长期占据状态栏
    /// （被截断时看起来像未完成显示的信息）。
    /// </remarks>
    public static readonly TimeSpan DefaultNoticeLifetime = TimeSpan.FromSeconds(8);

    /// <summary>本实例的提示存活时长（构造时注入；见 <see cref="ShellViewModel"/> 的 `noticeLifetime`）。</summary>
    private readonly TimeSpan _noticeLifetime;

    /// <summary>提示写入时刻（<see cref="Stopwatch.GetTimestamp"/>；无提示时为 0）。</summary>
    private long _statusNoticeRaisedAt;

    /// <summary>提示是否已到点（纯函数：给时刻算结论）。</summary>
    private static bool IsNoticeExpired(long raisedAt, long now, TimeSpan lifetime) =>
        raisedAt > 0 && (now - raisedAt) >= (long)(lifetime.TotalSeconds * Stopwatch.Frequency);

    /// <summary>把到点的提示清掉（每个刷新节拍调一次）。</summary>
    private void ExpireStatusNotice()
    {
        if (!IsNoticeExpired(_statusNoticeRaisedAt, Stopwatch.GetTimestamp(), _noticeLifetime))
        {
            return;
        }

        _statusNoticeRaisedAt = 0;
        StatusNotice = null;
        StatusNoticeIsError = false;
    }

    /// <summary>最近提示是否为错误（状态栏以警示色显示）。</summary>
    [ObservableProperty]
    public partial bool StatusNoticeIsError { get; set; }

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = Loc("shell.status.initial");

    /// <summary>当前主题的主窗口壁纸画刷（null = 无壁纸）。</summary>
    [ObservableProperty]
    public partial ImageBrush? ThemeWallpaperBrush { get; set; }

    /// <summary>当前主题是否有主窗口壁纸。</summary>
    public bool HasThemeWallpaper => ThemeWallpaperBrush is not null;

    /// <summary>当前主题的壁纸遮罩浓度（0–0.9）。</summary>
    [ObservableProperty]
    public partial double WallpaperVeilOpacity { get; set; } = 0.55;

    partial void OnThemeWallpaperBrushChanged(ImageBrush? value) => OnPropertyChanged(nameof(HasThemeWallpaper));

    /// <summary>按当前生效主题刷新主窗口壁纸（主题切换/设置应用/系统浅深切换时调用）。</summary>
    public void RefreshThemeWallpaper()
    {
        var settings = _query.GetSettings();
        var theme = ThemeCatalog.ResolveDisplayTheme(
            ThemeCatalog.All(_query.GetThemeFiles()),
            settings.Appearance,
            settings.Appearance.ThemeSelection,
            ThemeCatalog.CurrentSystemVariant());

        ImageBrush? brush = null;
        if (theme.WallpaperFileName is { Length: > 0 }
            && _query.TryGetThemeWallpaper(theme.Id, out var png)
            && png is { Length: > 0 })
        {
            // 按内容寻址缓存：同一主题换图后不会读到缓存里的旧图
            var key = theme.Id + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));
            brush = WallpaperImageCache.FromBytes(key, png);
        }

        ThemeWallpaperBrush = brush;
        WallpaperVeilOpacity = Math.Clamp(theme.WallpaperVeilOpacity, 0, 0.9);
        Windows.WallpaperVeilOpacity = WallpaperVeilOpacity;
    }

    /// <summary>Issue 计数（状态栏可点击打开 Issue 弹窗）。</summary>
    [ObservableProperty]
    public partial int IssueCount { get; set; }

    /// <summary>错误级 Issue 计数（活动栏徽标与状态栏用）。</summary>
    [ObservableProperty]
    public partial int ErrorIssueCount { get; set; }

    /// <summary>是否存在错误级 Issue（活动栏徽标显隐）。</summary>
    public bool HasErrors => ErrorIssueCount > 0;

    /// <summary>徽标悬停说明（只描述错误；警告/信息不参与角标）。</summary>
    public string IssueBadgeToolTip => ErrorIssueCount > 0
        ? Loc("shell.issueBadge.tip.errors", ErrorIssueCount)
        : Loc("shell.issueBadge.tip.none");

    partial void OnErrorIssueCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(IssueBadgeToolTip));
    }

    /// <summary>显示组：是否显示**叠加信息层**（窗内连接线；端口标记本身常显）。</summary>
    public bool ShowInfoOverlay
    {
        get => Windows.ShowInfoOverlay;
        set
        {
            if (Windows.ShowInfoOverlay == value)
            {
                return;
            }

            Windows.ShowInfoOverlay = value;
            OnPropertyChanged();
            UpdateStatus();
        }
    }

    /// <summary>交互组：几何吸附开关（按住 Alt 可临时停用）。</summary>
    public bool SnapEnabled
    {
        get => Windows.SnapEnabled;
        set
        {
            if (Windows.SnapEnabled == value)
            {
                return;
            }

            Windows.SnapEnabled = value;
            OnPropertyChanged();
            UpdateStatus();
        }
    }

    // ---- 侧边栏形态 ----

    /// <summary>当前侧边界面页：<c>catalog</c>（模块清单，默认）· <c>composite</c>（组合）· <c>interaction</c>（交互）。</summary>
    [ObservableProperty]
    public partial string ActivePage { get; set; } = ShellPages.Catalog;

    /// <summary>是否显示**模块清单**页（B 区）。</summary>
    public bool IsCatalogPage => string.Equals(ActivePage, ShellPages.Catalog, StringComparison.Ordinal);

    /// <summary>是否显示**组合**页（B 区）。</summary>
    public bool IsCompositePage => string.Equals(ActivePage, ShellPages.Composite, StringComparison.Ordinal);

    /// <summary>是否显示**交互**页（B 区）。</summary>
    public bool IsInteractionPage => string.Equals(ActivePage, ShellPages.Interaction, StringComparison.Ordinal);

    /// <summary>当前**已生效**的应用设置（不是设置页草稿）。</summary>
    public ApplicationSettings GetCommittedSettings() => _query.GetSettings();

    /// <summary>已生效的关闭行为：Ask / Save / Discard。</summary>
    public string WorkspaceExitBehavior => GetCommittedSettings().WorkspaceExitBehavior;

    /// <summary>严格持久化关闭行为；失败不回退为成功。</summary>
    public CommandResult SetWorkspaceExitBehavior(string behavior) =>
        _command.SetWorkspaceExitBehavior(behavior);

    /// <summary>本次启动从工作区恢复的外壳矩形。</summary>
    public WorkspacePlacementView? GetWorkspacePlacement() => _query.GetWorkspacePlacement();

    /// <summary>切换侧边界面页（活动栏按钮用）。</summary>
    /// <param name="page">页标识（<see cref="ShellPages"/>）。</param>
    [RelayCommand]
    private void ShowPage(string? page)
    {
        if (!string.IsNullOrWhiteSpace(page))
        {
            ActivePage = page;
        }
    }

    partial void OnActivePageChanged(string value)
    {
        OnPropertyChanged(nameof(IsCatalogPage));
        OnPropertyChanged(nameof(IsCompositePage));
        OnPropertyChanged(nameof(IsInteractionPage));

        if (string.Equals(value, ShellPages.Composite, StringComparison.Ordinal))
        {
            Composites.Activate();
        }
    }

    /// <summary>窗口组：全部锁定。</summary>
    public bool AllLocked
    {
        get => Windows.AllLocked;
        set
        {
            if (Windows.AllLocked == value)
            {
                return;
            }

            Windows.SetAllLocked(value);
            OnPropertyChanged();
            UpdateStatus();
        }
    }

    /// <summary>按刷新节拍刷新（UI 线程；由外壳窗口的节拍器调用，测试可直接调用）。</summary>
    public void Refresh()
    {
        Windows.Refresh();

        if (IsCompositePage)
        {
            Composites.Refresh();
        }

        Catalog.Refresh();
        Issues.Refresh();

        // 拓扑**必须在这里一起刷新**：漏掉它时拓扑视图模型只被设计时样例与测试手工调用过
        // ⇒ 弹窗打开后表格恒为空、摘要恒为「网络 0」（与是否建网无关）。
        Topology.Refresh();

        RefreshDataTypeColors();

        IssueCount = Issues.TotalCount;
        ErrorIssueCount = Issues.ErrorCount;
        ExpireStatusNotice();
        UpdateStatus();
    }

    /// <summary>
    /// 首次载入时把界面偏好对齐到当前生效值（设置弹窗 + 口标记默认可见性）。
    /// </summary>
    /// <remarks>
    /// 工具栏上的"显示/交互"开关是**会话内的视图开关**（不写回设置）；设置弹窗里的对应项是偏好，
    /// 启动时生效（分组原则：显示、交互、窗口三类互不混淆）。
    /// </remarks>
    public void LoadSettings()
    {
        Settings.Load();
        RefreshThemeWallpaper();
        Windows.ShowInfoOverlay = Settings.ShowInfoOverlay;

        // **数据结构 → 颜色**：把设置里的映射灌进色板（启动即生效；色板是唯一色彩来源）
        PacketTypePalette.ClearOverrides();
        foreach (var (typeKey, hex) in Settings.TypeColorOverrides())
        {
            if (ThemeApplier.Parse(hex) is { } color)
            {
                PacketTypePalette.SetOverride(typeKey, color);
            }
        }

        RefreshDataTypeColors();

    }

    /// <summary>打开模块清单弹窗。</summary>
    [RelayCommand]
    private void OpenCatalog() => DialogRequested?.Invoke(this, DialogKind.ModuleCatalog);

    /// <summary>打开 Issue 弹窗。</summary>
    [RelayCommand]
    private void OpenIssues() => DialogRequested?.Invoke(this, DialogKind.Issues);

    /// <summary>打开设置弹窗。</summary>
    [RelayCommand]
    private void OpenSettings() => DialogRequested?.Invoke(this, DialogKind.Settings);

    /// <summary>打开拓扑诊断弹窗（只读）。</summary>
    [RelayCommand]
    private void OpenTopology() => DialogRequested?.Invoke(this, DialogKind.Topology);

    /// <summary>取当前生效的快捷键绑定（设置里的值；缺失的动作由服务回落到内置默认）。</summary>
    public IReadOnlyDictionary<string, string> GetHotkeyBindings() => Settings.HotkeyBindings();

    /// <summary>快捷键/设置变更后重建绑定（由外壳窗口在设置应用后调用）。</summary>
    public void ReloadHotkeys() => _ = Hotkeys.Rebuild(GetHotkeyBindings());

    /// <summary>切换口标记显示。</summary>
    [RelayCommand]
    private void ToggleMarkers() => ShowInfoOverlay = !ShowInfoOverlay;

    /// <summary>切换几何吸附。</summary>
    [RelayCommand]
    private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    /// <summary>锁定全部窗口。</summary>
    [RelayCommand]
    private void LockAll() => AllLocked = true;

    /// <summary>解锁全部窗口。</summary>
    [RelayCommand]
    private void UnlockAll() => AllLocked = false;

    /// <summary>把全部被隐藏的模块窗口重新显示出来（窗口关掉后的回收入口）。</summary>
    [RelayCommand]
    private void ShowAllWindows()
    {
        foreach (var instance in _query.GetInstances())
        {
            Windows.ShowWindow(instance.InstanceId);
        }

        UpdateStatus();
    }

    /// <summary>把本应用全部窗口抬到最前（不改变焦点；组抬升 = 平台行为，由视图执行）。</summary>
    [RelayCommand]
    private void RaiseAllWindows() => RaiseAllWindowsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>切换应用整体置顶（全体窗口一起，含弹窗）。</summary>
    [RelayCommand]
    private void ToggleAppTopmost() => AppTopmost = !AppTopmost;

    /// <inheritdoc />
    public void Execute(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        switch (actionId)
        {
            case HotkeyCatalog.OpenCatalog: OpenCatalogCommand.Execute(null); break;
            case HotkeyCatalog.OpenIssues: OpenIssuesCommand.Execute(null); break;
            case HotkeyCatalog.OpenSettings: OpenSettingsCommand.Execute(null); break;
            case HotkeyCatalog.OpenTopology: OpenTopologyCommand.Execute(null); break;
            case HotkeyCatalog.ToggleMarkers: ToggleMarkersCommand.Execute(null); break;
            case HotkeyCatalog.ToggleSnap: ToggleSnapCommand.Execute(null); break;
            case HotkeyCatalog.ToggleLockAll: AllLocked = !AllLocked; break;
            case HotkeyCatalog.ToggleAnchorFixed: Windows.ToggleAllAnchorsFixed(); break;
            case HotkeyCatalog.ToggleAppTopmost: ToggleAppTopmostCommand.Execute(null); break;
            case HotkeyCatalog.RaiseAllWindows: RaiseAllWindowsCommand.Execute(null); break;
            case HotkeyCatalog.ShowAllWindows: ShowAllWindowsCommand.Execute(null); break;
            default: break;
        }
    }

    /// <summary>加载一个组合件文件（降级式）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>命令结果。</returns>
    public CommandResult Load(string filePath) => _command.LoadComposite(filePath);

    /// <summary>保存当前工作区（先提交未落盘的整体平移）。</summary>
    /// <param name="placement">外壳绝对矩形（DIP）。</param>
    public CommandResult SaveWorkspace(WorkspacePlacementView placement)
    {
        _ = Windows.CommitSceneTranslation();
        return _command.SaveWorkspace(placement);
    }

    /// <summary>保存一次性重启恢复快照（旧入口；重新加载已改用 <see cref="SaveWorkspace"/>）。</summary>
    public CommandResult SaveRestartSnapshot()
    {
        _ = Windows.CommitSceneTranslation();
        return _command.SaveRestartSnapshot();
    }

    /// <summary>重启进程启动失败时删除快照，避免下次正常启动误恢复。</summary>
    public CommandResult DiscardRestartSnapshot() => _command.DeleteRestartSnapshot();

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(ShellViewModel).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(ShellViewModel).Assembly, key, args);

    private void UpdateStatus()
    {
        var links = Loc(ShowInfoOverlay ? "shell.status.overlay.on" : "shell.status.overlay.off");
        var snap = Loc(SnapEnabled ? "shell.status.snap.on" : "shell.status.snap.off");
        var locked = AllLocked ? " · " + Loc("shell.status.locked") : string.Empty;
        var issues = ErrorIssueCount > 0
            ? " · " + Loc("shell.status.issuesWithErrors", IssueCount, ErrorIssueCount)
            : " · " + Loc("shell.status.issues", IssueCount);

        // 提示**不拼进摘要**（那样会被截成「拖动…」）⇒ 摘要只放长期事实，提示另占一列。
        // Issue 计数留在摘要里（信息），**入口**只剩活动栏那个图标（状态栏右侧那个按钮不保留）。
        StatusText = Loc("shell.status.summary", Windows.ModuleCount, links, snap, Windows.RefreshRateHz, locked, issues);
    }
}
