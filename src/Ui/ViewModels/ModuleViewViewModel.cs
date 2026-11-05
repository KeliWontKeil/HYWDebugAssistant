using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.ModuleViews;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 模块视图视图模型：**一个模块 = 一个无边框顶层窗口**——
/// 屏幕位置/尺寸/锁定 + 按声明渲染的元素 + 口上的标记块。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>视图 = 实例的派生物</b>：实例出现即出现、实例移除即移除，可独立隐藏（不影响运行）；</item>
///   <item><b>坐标单位 = 物理像素（屏幕坐标）</b>：窗口位置本身是 <c>PixelPoint</c>，用同一单位做吸附比较才自洽；
///     窗口的宽高属性（DIP）由窗口管理器按各自缩放换算（多显示器不同 DPI 是风险点）；</item>
///   <item><b>没有"工作区"容器</b>：本类不持有画布、不做边界钳制——背景就是用户桌面；</item>
///   <item><b>锁定</b> = 不可拖动 / 不可缩放 / 不可选中（仍显示数据）；锁定**不持久化**（属会话内交互状态）。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleViewViewModel : ViewModelBase
{
    /// <summary>最小宽（DIP；缩放下限）—— **模块代码声明**；<c>0</c> = 未声明。</summary>
    /// <remarks>
    /// **地板为什么存在**：端口标记沿边长度 150（<see cref="PortMarkerMetrics.Length"/>），
    /// 窗口比它短就会把标记**裁掉一截**（面板把起点夹到 0 并让它溢出）
    /// ⇒ 地板由标记长度推出来（<see cref="PortMarkerMetrics.MinWindowExtent"/> = 160），不能是随手取的一个数。
    /// </remarks>
    public double MinWidth { get; }

    /// <summary>最小高（DIP；缩放下限）—— **模块代码声明**，含义与 <see cref="MinWidth"/> 完全相同。</summary>
    public double MinHeight { get; }

    /// <summary>
    /// 本模块**代码声明**的默认窗口尺寸（DIP）——新窗口按它开，「还原」也回到它。
    /// </summary>
    /// <remarks>
    /// 尺寸的唯一来源是 <c>[ModuleDefinition(WindowWidth/WindowHeight = …)]</c>；
    /// <c>0</c> = 未声明，由框架地板兜底。代码里**没有"按内容估算尺寸"的逻辑**（内容排不下由内容区滚动）。
    /// </remarks>
    public double PreferredWidth { get; }

    /// <summary>默认窗口高度（DIP；同上来自 <c>[ModuleDefinition]</c>）。</summary>
    public double PreferredHeight { get; }

    /// <summary>模块视图**自身声明**的内容最小宽（DIP；来自视图根 `MinWidth`）。</summary>
    public double ContentMinWidth { get; private set; }

    /// <summary>模块视图**自身声明**的内容最小高（DIP；来自视图根 `MinHeight`）。</summary>
    public double ContentMinHeight { get; private set; }

    /// <summary>**有效最小宽**（DIP）= 内容/声明值 + 窗口 chrome，且不低于框架地板。</summary>
    /// <remarks>
    /// 内容最小尺寸的**唯一来源是模块 `.axaml` 的视图根**（<see cref="ContentMinWidth"/>）；
    /// 框架只加窗口 chrome（描边 + 端口预留带）并守住端口标记地板。
    /// </remarks>
    public double EffectiveMinWidth => Math.Max(
        PortMarkerMetrics.MinWindowExtent,
        Math.Max(MinWidth, ContentMinWidth + (2 * ModuleSurfaceMetrics.FrameBorder)
            + ModuleSurfaceMetrics.InsetLeft + ModuleSurfaceMetrics.InsetRight));

    /// <summary>**有效最小高**（DIP）= 内容/声明值 + 窗口 chrome，且不低于框架地板。</summary>
    public double EffectiveMinHeight => Math.Max(
        PortMarkerMetrics.MinWindowExtent,
        Math.Max(MinHeight, ContentMinHeight + (2 * ModuleSurfaceMetrics.FrameBorder)
            + ModuleSurfaceMetrics.InsetTop + ModuleSurfaceMetrics.InsetBottom));

    /// <summary>视图根声明内容最小尺寸后回写；窗口最小尺寸据此重算（内容才是外观事实）。</summary>
    /// <param name="width">内容最小宽。</param>
    /// <param name="height">内容最小高。</param>
    public void SetContentMinimum(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || (width <= 0 && height <= 0))
        {
            return;
        }

        if (Math.Abs(ContentMinWidth - width) < 0.01 && Math.Abs(ContentMinHeight - height) < 0.01)
        {
            return;
        }

        ContentMinWidth = Math.Max(0, width);
        ContentMinHeight = Math.Max(0, height);
        OnPropertyChanged(nameof(ContentMinWidth));
        OnPropertyChanged(nameof(ContentMinHeight));
        OnPropertyChanged(nameof(EffectiveMinWidth));
        OnPropertyChanged(nameof(EffectiveMinHeight));
    }

    /// <summary>**有效最大宽**（DIP；<see langword="null"/> = 无上限 = 声明面没给）。</summary>
    public double? EffectiveMaxWidth { get; }

    /// <summary>**有效最大高**（DIP；<see langword="null"/> = 无上限）。</summary>
    public double? EffectiveMaxHeight { get; }

    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>视图状态存储（**可空**：设计期与未接线的调用点不传）。</summary>
    private readonly IViewStateAccess? _viewState;

    /// <summary>
    /// **多行文本输入的提交键**——由 <see cref="ModuleWindowsViewModel"/> 从应用级设置下发。
    /// </summary>
    /// <remarks>
    /// 三档：<c>CtrlEnter</c>（默认：回车换行 / Ctrl+Enter 提交 / **失焦不提交** —— 否则"点按钮 ⇒ 先失焦提交 ⇒ 按钮又发"会双发）·
    /// <c>Enter</c>（回车提交 / Shift+Enter 换行）· <c>None</c>（键盘都不提交）。
    /// **只影响多行输入**；单行输入保持"回车 / 失焦一次提交"。
    /// 新表面**每次按键现读这个值**（经 <see cref="MultilineSubmitKey"/>），
    /// 因此改设置**不重建任何东西** —— 重建会丢掉焦点、未提交文本与滚动位置。
    /// </remarks>
    public string MultilineSubmitKey
    {
        get => _multilineSubmitKey;
        set
        {
            if (string.Equals(_multilineSubmitKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _multilineSubmitKey = value;
            OnPropertyChanged();
        }
    }

    private string _multilineSubmitKey = "CtrlEnter";

    /// <summary>创建模块视图视图模型。</summary>
    /// <param name="view">实例视图（声明来源）。</param>
    /// <param name="layout">布局（可为空 = 未摆放）。</param>
    /// <param name="query">查询面（配置 Schema 读取）。</param>
    /// <param name="command">命令面（按字段写回配置）。</param>
    /// <param name="fallbackX">未摆放时的默认 X（**DIP**）。</param>
    /// <param name="fallbackY">未摆放时的默认 Y（**DIP**）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：切页的选中态、输入框**未提交**的文本、
    /// 文本显示的共享缓冲都放在这里 —— 元素表重建（切页 / 改设置）**不丢内容**（对齐 WinForms"控件活着，数据就在"）。
    /// `null`（设计期 / 未接线的调用点）⇒ 回落为：选中态只记在本地、显示用各自的本地缓冲。
    /// </param>
    /// <remarks>
    /// **没有"屏幕缩放"参数**：模型四数一律 DIP（见 `ModuleLayoutView`），尺寸与上下限同单位 ⇒ 不需要它；
    /// 位置换物理像素只发生在窗口边界（<c>ModuleWindowManager</c>，用**该窗口自己**的 <c>RenderScaling</c>）。
    /// 一个"全局按主屏取缩放"的参数会让每块屏都按主屏的缩放换算 —— 那正是"跨屏后右边框被遮住"的成因，
    /// 所以这里刻意**不接受**这样一个值：换算的输入只能是"这个窗口所在的屏"。
    /// </remarks>
    public ModuleViewViewModel(
        ModuleInstanceView view,
        ModuleLayoutView? layout,
        IRuntimeQuery query,
        IRuntimeCommand command,
        double fallbackX,
        double fallbackY,
        IViewStateAccess? viewState = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
        _viewState = viewState;
        // 尺寸来源优先级：① 组合件里的布局（用户拖过）② 模块**代码声明**的窗口尺寸
        // ③ 框架地板（0 = 未声明 ⇒ 由地板兜底）。
        var window = view.Window;
        MinWidth = window is { MinWidth: > 0 } declaredMin ? declaredMin.MinWidth : 0;
        MinHeight = window is { MinHeight: > 0 } declaredMinHeight ? declaredMinHeight.MinHeight : 0;
        PreferredWidth = window is { Width: > 0 } declaredWidth ? declaredWidth.Width : MinWidth;
        PreferredHeight = window is { Height: > 0 } declaredHeight ? declaredHeight.Height : MinHeight;

        EffectiveMaxWidth = null;
        EffectiveMaxHeight = null;

        InstanceId = view.InstanceId;
        ModuleId = view.ModuleId;
        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        HasLayout = layout is not null;
        Scaling = layout is { Scale: > 0 } ? layout.Scale : 1;

        // **四个数一律 DIP**（口径见 `ModuleLayoutView`）：位置直接取用、不取整（DIP 不是像素，
        // 取整只会让"贴齐"在缩放 ≠100% 时差几个物理像素）；落窗时由 `ModuleWindowManager`
        // 乘该窗口的 `RenderScaling` 换成 `PixelPoint`。
        var x = layout?.X ?? fallbackX;
        var y = layout?.Y ?? fallbackY;

        // 低于 -20000 的坐标不是用户可达的布局：哨兵/坏数据一律回退到默认摆放位，
        // 避免把不可见窗口的平台位置当成用户布局。
        if (x < -20000 || y < -20000)
        {
            x = fallbackX;
            y = fallbackY;
        }

        X = x;
        Y = y;

        // 尺寸夹进有效区间（上下限本身就是 DIP，与模型同单位 ⇒ 不乘缩放）。
        double ClampToBounds(double dip, double minDip, double? maxDip)
        {
            var value = Math.Max(dip, minDip);
            return maxDip is { } upper ? Math.Min(value, upper) : value;
        }

        Width = ClampToBounds(
            layout is { Width: > 0 } sized ? sized.Width : PreferredWidth,
            EffectiveMinWidth,
            EffectiveMaxWidth);
        Height = ClampToBounds(
            layout is { Height: > 0 } sizedHeight ? sizedHeight.Height : PreferredHeight,
            EffectiveMinHeight,
            EffectiveMaxHeight);
        IsViewVisible = layout?.IsViewVisible ?? true;

        ApplyState(view.State);
        Rebuild(view);
        SyncWallpaper(view.WallpaperRevision);
    }

    /// <summary>实例 ID（工作现场内唯一、稳定；**不直接展示给用户**）。</summary>
    public string InstanceId { get; }

    /// <summary>模块标识字面量（同一模块类型的所有实例共享；用于外部展示编号的分组）。</summary>
    public string ModuleId { get; }

    /// <summary>展示名（内容区顶部一行轻量文本）。</summary>
    public string Title { get; private set; }

    /// <summary>是否占位实例（模块定义缺失/版本不兼容）。</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary>是否已有布局记录（false = 由 UI 取默认位并写回）。</summary>
    public bool HasLayout { get; set; }

    /// <summary>
    /// **该窗口当前所在屏的缩放**：位置的物理投影 = X/Y × Scaling；尺寸仍是 DIP。
    /// 由窗口管理器在开窗前/真实缩放变化时发布，不参与持久化判定。
    /// </summary>
    public double Scaling { get; set; } = 1;

    /// <summary>位置与尺寸换算到屏幕物理像素后的矩形（组内贴合/物理位移用）。</summary>
    public CanvasRect PhysicalRect => new(X * Scaling, Y * Scaling, Width * Scaling, Height * Scaling);

    /// <summary>X（**屏幕坐标，DIP**；DIP → 物理像素只在窗口边界换算一次）。</summary>
    [ObservableProperty]
    public partial double X { get; set; }

    /// <summary>Y（屏幕坐标，DIP）。</summary>
    [ObservableProperty]
    public partial double Y { get; set; }

    /// <summary>宽（DIP）。</summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>高（DIP）。</summary>
    [ObservableProperty]
    public partial double Height { get; set; }

    /// <summary>状态文本（Active/Disabled/Missing…）。</summary>
    [ObservableProperty]
    public partial string StateText { get; set; } = TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.active");

    private string _stateKey = "Active";

    /// <summary>状态机键（`Active`/`Disabled`/…；界面文本只是它的显示，两者不可混用）。</summary>
    public string StateKey => _stateKey;

    /// <summary>状态语义（颜色 + 形状双重编码）。</summary>
    [ObservableProperty]
    public partial StateVisual State { get; set; } = StateVisual.Active;

    /// <summary>是否选中（点击边框带/Shift+点击；选中即显示选择框）。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>是否**锚点**（多选里第一个选中的模块；按钮簇与缩放手柄的归属）。</summary>
    [ObservableProperty]
    public partial bool IsPrimary { get; set; }

    /// <summary>是否处于运行态（Active）；false = 已停用（保留实例/拓扑/配置，只停流）。</summary>
    public bool IsEnabled => string.Equals(StateKey, "Active", StringComparison.Ordinal);

    /// <summary>是否隐藏视图（false 时窗口关闭但模块仍在运行）。</summary>
    [ObservableProperty]
    public partial bool IsViewVisible { get; set; } = true;

    /// <summary>窗口是否已有宿主窗口（隐藏/占位实例为 false；由窗口管理器回写）。</summary>
    [ObservableProperty]
    public partial bool HasWindow { get; set; }

    /// <summary>当前矩形是否落在任一屏幕的可见工作区内（窗口管理器发布工作区后计算）。</summary>
    [ObservableProperty]
    public partial bool IsOffScreen { get; set; }

    /// <summary>是否锁定（不可拖动/缩放；仍显示数据；**可被选中**，以便纳入组合范围）。</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    /// <summary>模块壁纸画刷（null = 无；只影响本模块）。</summary>
    [ObservableProperty]
    public partial ImageBrush? WallpaperBrush { get; set; }

    /// <summary>本模块是否有壁纸。</summary>
    public bool HasWallpaper => WallpaperBrush is not null;

    /// <summary>当前主题的壁纸遮罩浓度（由控制器下发）。</summary>
    [ObservableProperty]
    public partial double WallpaperVeilOpacity { get; set; } = 0.55;

    partial void OnWallpaperBrushChanged(ImageBrush? value) => OnPropertyChanged(nameof(HasWallpaper));

    private long _wallpaperRevision = -1;

    /// <summary>按实例视图的壁纸版本懒加载（同版本不重复解码）。</summary>
    private void SyncWallpaper(long revision)
    {
        if (revision == _wallpaperRevision)
        {
            return;
        }

        _wallpaperRevision = revision;

        // 同一实例的壁纸键随版本变化；这里只摘缓存引用，不 Dispose（可能还有一帧在使用）。
        WallpaperImageCache.EvictModule(InstanceId);

        if (revision <= 0 || _query.GetModuleWallpaper(InstanceId) is not { } wallpaper)
        {
            WallpaperBrush = null;
            return;
        }

        WallpaperBrush = WallpaperImageCache.FromBytes(
            $"module:{InstanceId}:{wallpaper.Revision}",
            wallpaper.Png);
    }

    /// <summary>释放该实例独占的图片缓存（模块被移除时调用）。</summary>
    public void ReleaseCachedResources() => WallpaperImageCache.EvictModule(InstanceId);

    /// <summary>命令设置壁纸后立即重读（版本变化时重建画刷）。</summary>
    public void ReloadWallpaper()
    {
        _wallpaperRevision = -1;
        if (_query.TryGetInstance(InstanceId, out var view) && view is not null)
        {
            SyncWallpaper(view.WallpaperRevision);
        }
    }

    /// <summary>本窗口的**对齐辅助线**（**屏幕 DIP**，与模型几何同单位；拖动中命中吸附时由控制器写入，窗内以虚线绘制）。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GuideLine> Guides { get; set; } = [];

    /// <summary>正在缩放的边（位标志：1 左 · 2 右 · 4 上 · 8 下；0 = 不在缩放）。</summary>
    [ObservableProperty]
    public partial int ResizeEdges { get; set; }

    /// <summary>是否正在被拖动或缩放（视觉反馈用）。</summary>
    [ObservableProperty]
    public partial bool IsInteracting { get; set; }

    /// <summary>
    /// 是否显示**叠加信息层**（窗内连线 + 中心类型方块 + 信息；由控制器按应用级设置同步写入）。
    /// **默认关闭**：它是调试/观测层，不进常驻界面。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    /// <summary>口上的标记块（边缘小块 = 口名/网络名 + 信息类型 + 方向 + 连接状态）。</summary>
    public ObservableCollection<PortMarkerViewModel> Ports { get; } = [];

    /// <summary>
    /// **角色方块**清单（叠加层画功能分块与 `m/n` 的依据）。
    /// </summary>
    /// <remarks>
    /// **只读投影**：由 <c>IRuntimeQuery.GetRoles</c> 派生（Core 算、UI 只画），顺序 = 模块**声明顺序**。
    /// 每次刷新**整体替换**；先刷新**所有模块的方块**，再算连线（连线的对端锚点要用到对端方块的位置）。
    /// </remarks>
    public IReadOnlyList<RoleBoxViewModel> Roles
    {
        get => _roles;
        private set
        {
            _roles = value;
            OnPropertyChanged();
        }
    }

    /// <summary>**角色间数据流**清单（叠加层画线的依据）；已按（本端角色, 对端实例, 对端角色）聚合。</summary>
    public IReadOnlyList<RoleLinkViewModel> RoleLinks
    {
        get => _roleLinks;
        private set
        {
            _roleLinks = value;
            OnPropertyChanged();
        }
    }

    /// <summary>**内部处理顺序**清单（`角色 → 角色`；零声明 ⇒ 空）。</summary>
    public IReadOnlyList<RoleOrderViewModel> RoleOrder
    {
        get => _roleOrder;
        private set
        {
            _roleOrder = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 叠加层**信息区**（左上身份 + 必要时一行说明）。
    /// </summary>
    /// <remarks>**由视图模型算**（它才知道声明了几个角色/几个口），叠加层只负责画。文字层次：身份 12px / 说明 10px。</remarks>
    public RoleOverlayInfo OverlayInfo
    {
        get => _overlayInfo;
        private set
        {
            _overlayInfo = value;
            OnPropertyChanged();
        }
    }

    private IReadOnlyList<RoleBoxViewModel> _roles = [];
    private IReadOnlyList<RoleLinkViewModel> _roleLinks = [];
    private IReadOnlyList<RoleOrderViewModel> _roleOrder = [];
    private RoleOverlayInfo _overlayInfo;

    /// <summary>
    /// 接受**角色方块 + 信息区**（投影第一遍：所有模块先摆好方块，连线才拿得到对端锚点）。
    /// </summary>
    /// <param name="roles">角色方块。</param>
    /// <param name="info">信息区内容。</param>
    public void ApplyRoleBoxes(IReadOnlyList<RoleBoxViewModel> roles, RoleOverlayInfo info)
    {
        Roles = roles ?? [];
        OverlayInfo = info;
    }

    /// <summary>
    /// 接受**角色间数据流 + 内部顺序**（投影第二遍：此时所有模块的方块都已就位）。
    /// </summary>
    /// <param name="roleLinks">角色间数据流。</param>
    /// <param name="order">内部处理顺序。</param>
    public void ApplyRoleLinks(IReadOnlyList<RoleLinkViewModel> roleLinks, IReadOnlyList<RoleOrderViewModel> order)
    {
        RoleLinks = roleLinks ?? [];
        RoleOrder = order ?? [];
    }

    /// <summary>当前矩形（屏幕坐标；吸附与命中计算用）。</summary>
    public CanvasRect Rect => new(X, Y, Width, Height);

    /// <summary>
    /// **叠加层画布宽** —— 就是 <see cref="Width"/>（**DIP**）。
    /// </summary>
    /// <remarks>
    /// 叠加层几何的单位是 DIP，而模型自"全 DIP"改造后**本身就是 DIP** ⇒ 两者同单位，
    /// 这里只做转发。它保留下来是因为它是叠加层的**唯一尺寸来源**（`ConnectionOverlay` 绑它，
    /// 避免各处自己拿 `Bounds` —— 那会差 `Frame` 边框的 2 DIP，两端算出的方块位置不同 ⇒ 连线错位）。
    /// </remarks>
    public double CanvasWidth => Width;

    /// <summary>**叠加层画布高（DIP）** = <see cref="Height"/>（口径见 <see cref="CanvasWidth"/>）。</summary>
    public double CanvasHeight => Height;

    /// <summary>刷新实例状态（实例视图变化时调用；视图类型在实例生命周期内不变）。</summary>
    /// <remarks>
    /// **视图类型换了就重建**：本方法每次刷新都走，而视图只在实例的代码声明改变时才换。
    /// 判据取"工厂类型是否换了"：同一种视图反复刷新不会重建（重建会丢焦点与滚动位置）。
    /// </remarks>
    public void Apply(ModuleInstanceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // 视图对象由查询面按修订号缓存：引用没变 ⇒ 这份现场快照一个字都没变，整段同步都可以跳过。
        if (ReferenceEquals(_lastAppliedView, view))
        {
            return;
        }

        _lastAppliedView = view;

        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        ApplyState(view.State);
        SyncPorts(view);
        SyncWallpaper(view.WallpaperRevision);

        if (!ReferenceEquals(view.ViewType, ViewType))
        {
            Rebuild(view);
        }

        // 输入反馈（红框 / 就地结果 / 日志角落）不在这里：它属于内容区，
        // 由控件库在每帧刷新时按各自的控件分派。

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsPlaceholder));
    }

    /// <summary>上一份已同步的实例视图（引用比较用；查询面保证"没变化 ⇒ 同一个对象"）。</summary>
    private ModuleInstanceView? _lastAppliedView;

    /// <summary>接受外部几何（来自布局数据；拖动/缩放中不调用）。四数一律 **DIP**。</summary>
    public void ApplyLayout(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        X = layout.X;
        Y = layout.Y;
        Width = layout.Width > 0 ? layout.Width : PreferredWidth;
        Height = layout.Height > 0 ? layout.Height : PreferredHeight;
        IsViewVisible = layout.IsViewVisible;
        HasLayout = true;

        if (layout.Scale > 0)
        {
            Scaling = layout.Scale;
        }
    }

    /// <summary>导出当前几何（保存/提交用）。</summary>
    public ModuleLayoutView ToLayout() => new(InstanceId, X, Y, Width, Height, IsViewVisible)
    {
        Scale = Scaling,
    };

    private void ApplyState(string state)
    {
        _stateKey = state;
        StateText = state switch
        {
            "Active" => TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.active"),
            "Disabled" => TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.disabled"),
            "Missing" => TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.missing"),
            "Placeholder" => TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.placeholder"),
            "Removed" => TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.state.removed"),
            _ => state,
        };

        State = state switch
        {
            "Active" => StateVisual.Active,
            "Disabled" => StateVisual.Idle,
            "Missing" or "Placeholder" => StateVisual.Placeholder,
            "Removed" => StateVisual.Error,
            _ => StateVisual.Warn,
        };

        OnPropertyChanged(nameof(StateKey));
        OnPropertyChanged(nameof(IsEnabled));
    }


    private void Rebuild(ModuleInstanceView view)
    {
        // 模块自带的视图也一起作废（它绑的是同一个实例的上下文）。
        _view = null;
        _viewContext = null;
        ViewType = view.ViewType;
        OnPropertyChanged(nameof(ViewType));
        OnPropertyChanged(nameof(HasModuleView));

        // 内容宿主监听的是 `ModuleView`：换了视图类型必须让它重新 `EnsureView`，
        // 否则窗口还挂着上一个工厂造的控件树（看似"改了模块没生效"）。
        OnPropertyChanged(nameof(ModuleView));

        SyncPorts(view);

        // **每个模块实例都有一个窗口**：声明了视图工厂就按它渲染；没有视图工厂的模块也照常开窗
        // （至少显示模块名行），不引入"无界面模块"这一例外概念。
        //
        // **窗口尺寸不由内容计算**：默认尺寸来自模块的 `[ModuleDefinition]`，内容排不下时由内容区滚动。
    }


    /// <summary>模块自带的视图宿主（没有自带视图 ⇒ <see langword="null"/>）。</summary>
    private ModuleViewHost? _view;

    /// <summary>模块视图的上下文（每个实例一份；视图重建时一起换）。</summary>
    private ModuleViewContext? _viewContext;


    /// <summary>
    /// 本实例的**视图工厂类型**（模块自带视图）；<see langword="null"/> = 这个模块没有自绘界面。
    /// </summary>
    public Type? ViewType { get; private set; }

    /// <summary>这个实例是否有模块自带的视图。</summary>
    public bool HasModuleView => ViewType is not null;

    /// <summary>取模块自带的视图宿主；没有就建一个（幂等）。</summary>
    /// <returns>视图宿主；这个模块没有自带视图 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// <para>
    /// 视图**每个实例造一次**：它绑的是这个实例的上下文（取数、命令、配置、外观），
    /// 换实例必须换视图。构造失败**不让窗口崩**：给一块写明原因的内容 + 一条 Issue。
    /// </para>
    /// <para>
    /// 它**不在这里刷新**（见 <see cref="RefreshView"/>）：隐藏的窗口不该为刷新付钱。
    /// </para>
    /// </remarks>
    public ModuleViewHost? EnsureView()
    {
        if (_view is not null || ViewType is null)
        {
            return _view;
        }

        _viewContext ??= new ModuleViewContext(
            InstanceId,
            _query,
            _command,
            _viewState,
            () => MultilineSubmitKey);

        try
        {
            var control = Activator.CreateInstance(ViewType) is IModuleViewFactory factory
                ? factory.Create(_viewContext)
                : BuildViewFailure(TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.failure.factory", ViewType.Name));

            _view = new ModuleViewHost(_viewContext, control);
        }
        catch (Exception ex)
        {
            _view = new ModuleViewHost(
                _viewContext,
                BuildViewFailure(TextCatalog.Get(typeof(ModuleViewViewModel).Assembly, "moduleView.failure.construct", ex.GetType().Name, ex.Message)));
        }

        OnPropertyChanged(nameof(ModuleView));
        return _view;
    }

    /// <summary>当前模块自带的视图宿主（没有 ⇒ <see langword="null"/>）。</summary>
    public ModuleViewHost? ModuleView => _view;

    /// <summary>驱动一次模块视图刷新（没有自带视图 ⇒ 什么都不做）。</summary>
    /// <param name="sources">本帧的订阅源快照（窗口集统一取一次；<see langword="null"/> = 每个视图自己取）。</param>
    public void RefreshView(IReadOnlyCollection<SubscriptionSourceView>? sources = null)
    {
        if (!IsViewVisible)
        {
            return;
        }

        if (_view is { } view)
        {
            // 每帧先刷新数据源快照，再让视图读它（数据源视图对象是每帧重建的）。
            _viewContext?.BeginFrame(sources);
            view.Refresh();
        }
    }

    /// <summary>造一块"这个模块的界面没起来"的说明（**窗口不崩**，原因写清楚）。</summary>
    /// <param name="reason">一句话原因。</param>
    private static TextBlock BuildViewFailure(string reason) => new()
    {
        Text = reason,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Thickness(8),
    };

    /// <summary>驱动一次表面刷新（取数 + 就地更新）。框架每帧调一次。</summary>
    /// <param name="sources">本帧的订阅源快照。</param>
    public void RefreshSurface(IReadOnlyCollection<SubscriptionSourceView>? sources = null) => RefreshView(sources);


    /// <summary>
    /// 同步口标记的**身份与几何**（口名/方向/标签/锚点位置/类型）——**不写任何"当前状态"**。
    /// </summary>
    /// <remarks>
    /// 连接状态 / 停流状态 / 投递量 / 速率都是**指标**，唯一生产者是
    /// <see cref="ModuleWindowsViewModel"/> 的标记刷新（按 `GetNetworks()` 与框架诊断）。
    /// 这里**不许再写一遍**：两处都写会变成"谁最后跑谁赢"，且口径不同（按绑定 vs 按网络）。
    /// </remarks>
    private void SyncPorts(ModuleInstanceView view)
    {
        foreach (var port in view.Ports)
        {
            var existing = Ports.FirstOrDefault(m => string.Equals(m.PortName, port.Name, StringComparison.Ordinal));
            if (existing is null)
            {
                Ports.Add(new PortMarkerViewModel(
                    InstanceId,
                    port.Name,
                    string.Equals(port.Direction, "Send", StringComparison.Ordinal),
                    port.NetworkLabel,
                    port.AnchorOffset,
                    typeText: port.ContractText,
                    typeKey: port.TypeKey,
                    // 边的**有效值**（Core 已按"用户覆盖 > 模块声明的默认边 > 数据流惯例"解析）
                    edge: port.Edge,
                    isAnchorFixed: port.IsAnchorFixed));
                continue;
            }

            // 拖动中：本地 `Edge`/`Offset` 是**待提交的意图** ⇒ 节拍同步不得覆盖
            // （与 `IsEditing` 保护 `Label` 同一纪律；否则拖到一半会被框架权威值拽回原位）
            if (!existing.IsDraggingAnchor)
            {
                existing.Offset = port.AnchorOffset;

                // 边同源：**框架是唯一权威**（用户覆盖 > 模块声明的默认边 > 数据流惯例）。
                // 少这一行 ⇒ 载入组合件/撤销后标记会停在旧的边（本地值成了第二个来源）。
                existing.Edge = port.Edge;
            }

            // 「固定」是可变的呈现事实（模块声明 + 全局快捷键都会改它）⇒ 每次同步都跟一遍
            existing.IsAnchorFixed = port.IsAnchorFixed;

            if (!existing.IsEditing && !string.Equals(existing.Label, port.NetworkLabel, StringComparison.Ordinal))
            {
                existing.Label = port.NetworkLabel;
                existing.EditText = port.NetworkLabel ?? string.Empty;
            }
        }

        // 口消失（占位 → 真实定义等）：移除对应标记
        foreach (var stale in Ports
            .Where(m => !view.Ports.Any(p => string.Equals(p.Name, m.PortName, StringComparison.Ordinal)))
            .ToList())
        {
            _ = Ports.Remove(stale);
        }
    }

}