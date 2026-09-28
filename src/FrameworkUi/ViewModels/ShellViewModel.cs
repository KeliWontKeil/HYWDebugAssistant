using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Hotkeys;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// 侧边界面（B 区）的页标识【M1-16 / ADR-0038 D1】。
/// </summary>
/// <remarks>
/// 模块清单与"交互"是**常驻侧边页面**；设置 / 拓扑诊断 / Issue / 帮助仍为弹窗（D1 裁决）。
/// </remarks>
public static class ShellPages
{
    /// <summary>模块清单页。</summary>
    public const string Catalog = "catalog";

    /// <summary>交互页（显示 / 吸附 / 窗口三组开关与说明）。</summary>
    public const string Interaction = "interaction";
}

/// <summary>要打开的弹窗种类（R-3：Issue / 设置 / 模块清单 / 拓扑诊断一律弹窗）。</summary>
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
/// **框架外壳**视图模型（`FrameworkUi` = 外层主窗口，ADR-0034 R-2）：
/// 菜单/工具栏/状态栏 + 四个弹窗的编排 + 模块窗口集的控制入口。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>外壳**不画模块**：模块各自是独立的无边框窗口（`Ui` 的模块窗口集负责）；
///     外壳自身是可被贴靠的一块（拼图"起始点"）；</item>
///   <item>开关按**用途分组**（意见 4 的修正）：显示（口标记）/ 交互（几何吸附）/ 窗口（锁定全部）；</item>
///   <item>一切读写只经 <c>Core.Api</c>（C4-4）；本类不含机制逻辑。</item>
/// </list>
/// </remarks>
public sealed partial class ShellViewModel : ViewModelBase, IHotkeyActions
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>创建外壳视图模型。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="command">命令面。</param>
    public ShellViewModel(IRuntimeQuery query, IRuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;

        Hotkeys = new HotkeyService(this);
        Windows = new ModuleWindowsViewModel(query, command);

        // 模块窗口发出的**面向用户提示**（建网/端口标签结果）→ 状态栏
        Windows.NoticeRaised += (_, notice) =>
        {
            StatusNotice = notice.Message;
            StatusNoticeIsError = notice.IsError;
            UpdateStatus();
        };
        Catalog = new ModuleCatalogViewModel(query, command);
        Issues = new IssuesPanelViewModel(query);
        Settings = new SettingsPanelViewModel(query, command);
        Topology = new TopologyViewModel(query);

        // 占位实例属**诊断**（用户第六轮反馈）→ 其列表与"指定替代"命令由模块清单视图模型提供，在拓扑诊断弹窗里呈现
        Topology.Catalog = Catalog;

        // 【用户第十一轮反馈】开关类设置（叠加信息层 / 应用置顶）**以本视图模型为唯一事实源**，
        // 设置页与交互页因此完全联动（此前设置页各持一份副本 → 两边可不一致）。
        Settings.Owner = this;
    }

    /// <summary>应用内快捷键服务（挂到本应用每一个窗口上；ADR-0036 D7）。</summary>
    public HotkeyService Hotkeys { get; }

    /// <summary>模块窗口集（模块区 = 一批独立窗口）。</summary>
    public ModuleWindowsViewModel Windows { get; }

    /// <summary>模块清单弹窗视图模型。</summary>
    public ModuleCatalogViewModel Catalog { get; }

    /// <summary>Issue 弹窗视图模型。</summary>
    public IssuesPanelViewModel Issues { get; }

    /// <summary>设置弹窗视图模型。</summary>
    public SettingsPanelViewModel Settings { get; }

    /// <summary>拓扑诊断弹窗视图模型（只读）。</summary>
    public TopologyViewModel Topology { get; }

    /// <summary>
    /// **数据结构 → 颜色** 映射表（交互页）【ADR-0054 · 用户第三十四轮】。
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

        // **就地更新**（用户第三十四轮实测："鼠标指到色块上时不停的闪烁"）：
        // 旧实现每次刷新都 `Clear()` + 重建整张表 ⇒ 半个刷新周期换一批控件，正在打开的取色弹窗
        // 目标被销毁 ⇒ 反复开关 = 闪烁（也让人以为"改不了色"）。
        // ⇒ 行对象**只在类型集合变化时**增删；其余情况就地改值（集合稳定 ⇒ 弹窗不会被拆掉）。
        var existing = DataTypeColors.Rows.ToDictionary(row => row.TypeKey, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, info) in usage.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _ = overrides.TryGetValue(key, out var hex);
            _ = seen.Add(key);

            var text = info.Roles > 0 ? $"{info.Ports} 个口 · {info.Roles} 个角色" : $"{info.Ports} 个口";

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
        DataTypeColors.EmptyText = "现场还没有数据类型";

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

    /// <summary>请求把本应用全部窗口抬到最前（由视图执行；组抬升是平台行为）。</summary>
    public event EventHandler? RaiseAllWindowsRequested;

    /// <summary>
    /// **应用整体置顶**（ADR-0036 D5）：为 <see langword="true"/> 时本应用**全部**窗口
    /// （外壳 + 模块窗口 + 弹窗，含之后新开的）一起置顶——**不提供单窗口置顶**，
    /// 避免"一部分被覆盖、一部分置顶"的混合状态。由视图落到窗口组上。
    /// </summary>
    [ObservableProperty]
    public partial bool AppTopmost { get; set; }

    /// <summary>最近一次面向用户的提示（建网结果、端口标签提交结果等；空 = 无）。</summary>
    [ObservableProperty]
    public partial string? StatusNotice { get; set; }

    /// <summary>最近提示是否为错误（状态栏以警示色显示）。</summary>
    [ObservableProperty]
    public partial bool StatusNoticeIsError { get; set; }

    /// <summary>状态栏文本。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "模块 0 · Issue 0";

    /// <summary>Issue 计数（状态栏可点击打开 Issue 弹窗）。</summary>
    [ObservableProperty]
    public partial int IssueCount { get; set; }

    /// <summary>错误级 Issue 计数（状态栏强调用）。</summary>
    [ObservableProperty]
    public partial int ErrorIssueCount { get; set; }

    /// <summary>是否存在错误级 Issue（活动栏徽标显隐）。</summary>
    public bool HasErrors => ErrorIssueCount > 0;

    partial void OnErrorIssueCountChanged(int value) => OnPropertyChanged(nameof(HasErrors));

    /// <summary>显示组：是否显示**叠加信息层**（窗内连接线；端口标记本身常显 —— ADR-0039 D5）。</summary>
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

    // ---- 侧边栏形态【M1-16 / ADR-0038 D1/D2】 ----

    /// <summary>当前侧边界面页：<c>catalog</c>（模块清单，默认）· <c>interaction</c>（交互）。</summary>
    [ObservableProperty]
    public partial string ActivePage { get; set; } = ShellPages.Catalog;

    /// <summary>是否显示**模块清单**页（B 区）。</summary>
    public bool IsCatalogPage => string.Equals(ActivePage, ShellPages.Catalog, StringComparison.Ordinal);

    /// <summary>是否显示**交互**页（B 区）。</summary>
    public bool IsInteractionPage => string.Equals(ActivePage, ShellPages.Interaction, StringComparison.Ordinal);

    /// <summary>外壳窗口宽度（DIP；按比例档位推导 —— 比例是可改的设置项）。</summary>
    public double ShellWidth => ShellLayout.Measure(ShellAspect).Width;

    /// <summary>外壳窗口高度（DIP）。</summary>
    public double ShellHeight => ShellLayout.Measure(ShellAspect).Height;

    /// <summary>外壳是否可缩放（设置项：默认固定）。</summary>
    public bool ShellResizable => !ShellFixedSize;

    /// <summary>窗口比例档位（设置项透传）。</summary>
    public string ShellAspect
    {
        get => Settings.ShellAspect;
        set
        {
            if (string.Equals(Settings.ShellAspect, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.ShellAspect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShellWidth));
            OnPropertyChanged(nameof(ShellHeight));
        }
    }

    /// <summary>窗口尺寸是否固定（设置项透传）。</summary>
    public bool ShellFixedSize
    {
        get => Settings.ShellFixedSize;
        set
        {
            if (Settings.ShellFixedSize == value)
            {
                return;
            }

            Settings.ShellFixedSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShellResizable));
        }
    }

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
        OnPropertyChanged(nameof(IsInteractionPage));
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
        Catalog.Refresh();
        Issues.Refresh();
        RefreshDataTypeColors();

        IssueCount = Issues.TotalCount;
        ErrorIssueCount = Issues.ErrorCount;
        UpdateStatus();
    }

    /// <summary>
    /// 首次载入时把界面偏好对齐到当前生效值（设置弹窗 + 口标记默认可见性）。
    /// </summary>
    /// <remarks>
    /// 工具栏上的"显示/交互"开关是**会话内的视图开关**（不写回设置）；设置弹窗里的对应项是偏好，
    /// 启动时生效（`11` §3 的分组原则：显示、交互、窗口三类互不混淆）。
    /// </remarks>
    public void LoadSettings()
    {
        Settings.Load();
        Windows.ShowInfoOverlay = Settings.ShowInfoOverlay;

        // **数据结构 → 颜色**：把设置里的映射灌进色板（启动即生效；色板是唯一色彩来源）【ADR-0054】
        PacketTypePalette.ClearOverrides();
        foreach (var (typeKey, hex) in Settings.TypeColorOverrides())
        {
            if (ThemeApplier.Parse(hex) is { } color)
            {
                PacketTypePalette.SetOverride(typeKey, color);
            }
        }

        RefreshDataTypeColors();

        // 外观与外壳尺寸随设置刷新（ADR-0038 D2/D4）
        OnPropertyChanged(nameof(ShellAspect));
        OnPropertyChanged(nameof(ShellFixedSize));
        OnPropertyChanged(nameof(ShellWidth));
        OnPropertyChanged(nameof(ShellHeight));
        OnPropertyChanged(nameof(ShellResizable));
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

    /// <summary>保存当前全部内容为一个组合件文件（"保存全部内容"，§9.2）。</summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <returns>命令结果。</returns>
    public CommandResult SaveAll(string filePath)
    {
        // 【用户第十二轮第四轮】先把"整体平移"的待提交位置落盘，否则刚移动完外壳就保存会丢掉最后一段位移
        _ = Windows.CommitSceneTranslation();
        return _command.SaveComposite(filePath);
    }

    /// <summary>加载一个组合件文件（降级式）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>命令结果。</returns>
    public CommandResult Load(string filePath) => _command.LoadComposite(filePath);

    private void UpdateStatus()
    {
        var links = ShowInfoOverlay ? "叠加层开" : "叠加层关";
        var snap = SnapEnabled ? "吸附开" : "吸附关";
        var locked = AllLocked ? " · 已锁定" : string.Empty;
        var issues = ErrorIssueCount > 0 ? $" · Issue {IssueCount}（错误 {ErrorIssueCount}）" : $" · Issue {IssueCount}";

        var notice = StatusNotice is { Length: > 0 } text ? $"　▏{text}" : string.Empty;
        StatusText = $"模块 {Windows.ModuleCount} · {links} · {snap} · 刷新 {Windows.RefreshRateHz} Hz{locked}{issues}{notice}";
    }
}
