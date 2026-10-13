using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ModuleSurface.Hosting;

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
    /// <summary>
    /// 最小宽（DIP；缩放下限）—— **框架地板**：声明面的 <c>frame.window.minWidth</c> 只能把它抬高。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 地板与缺省**都不是这里的字面量**：唯一来源是包声明 schema 的 <c>window.minWidth</c> 缺省值，
    /// 读取器在读 <c>view.json</c> 时就已把小于地板的声明**夹到地板**（并记一条 warning），
    /// 故这里直接采用 <see cref="ViewWindowDeclaration.EffectiveMinWidth"/>。
    /// 实例连声明都没有时（整份被忽略）宿主给的是 <see cref="ViewDocument.Fallback"/>，
    /// 其窗口同样已按地板填好 ⇒ 本类**不需要、也不允许**再自带一份地板字面量。
    /// </para>
    /// <para>
    /// **地板为什么存在**：端口标记沿边长度 150（<see cref="PortMarkerMetrics.Length"/>），
    /// 窗口比它短就会把标记**裁掉一截**（面板把起点夹到 0 并让它溢出）
    /// ⇒ 地板由标记长度推出来（<see cref="PortMarkerMetrics.MinWindowExtent"/> = 160），不能是随手取的一个数。
    /// </para>
    /// <para>
    /// 这里**没有"默认尺寸"常量**：默认尺寸的唯一来源是包内 `view.json` 的 `frame.window`
    /// （缺省值由 schema 补齐），框架只在"整个实例没有任何可用声明"时按地板兜底。
    /// 代码里也**没有"按内容估算尺寸"的逻辑**（内容排不下由内容区滚动条处理）。
    /// </para>
    /// </remarks>
    public double MinWidth { get; }

    /// <summary>最小高（DIP；缩放下限）—— **框架地板**，含义与 <see cref="MinWidth"/> 完全相同。</summary>
    public double MinHeight { get; }

    /// <summary>
    /// 本模块**声明面给出的**默认窗口尺寸（DIP）——新窗口按它开，「还原」也回到它。
    /// </summary>
    /// <remarks>
    /// 尺寸的唯一来源是**声明面**（包内 `view.json` 的 `frame.window.width`，缺省由 schema 补齐）；
    /// 模块代码里**不声明** <c>PreferredWidth/PreferredHeight</c>。实例连声明都没有（整份被忽略）时
    /// 由宿主按框架地板给一份兜底声明（<see cref="ViewDocument.Fallback"/>）。
    /// </remarks>
    public double PreferredWidth { get; }

    /// <summary>默认窗口高度（DIP；同上来自 `frame.window.height`）。</summary>
    public double PreferredHeight { get; }

    /// <summary>
    /// **有效最小宽**（DIP）= <c>max(框架地板, 声明的 `window.minWidth`)</c> —— 读取器已夹好，这里等于声明值。
    /// </summary>
    /// <remarks>声明的最小尺寸只用于**约束用户缩放**；框架地板永远在（标记长度推出来的那条）。</remarks>
    public double EffectiveMinWidth { get; }

    /// <summary>**有效最小高**（DIP）= <c>max(框架地板, 声明的 `window.minHeight`)</c> —— 读取器已夹好，这里等于声明值。</summary>
    public double EffectiveMinHeight { get; }

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
    /// 新表面**每次按键现读这个值**（经 <c>ISurfaceHost.MultilineSubmitKey</c>），
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
        // 尺寸来源优先级：① 组合件里的布局（用户拖过）② **包内 `view.json` 的 `frame.window`**
        // ③ 框架地板（schema 的 `window.minWidth`/`minHeight` 缺省值，由端口标记长度推出）。
        // 三者的缺省都在读取侧就位：读取器保证 `frame.window` 恒有值、且小于地板的 min 已被**夹到地板**；
        // 整份声明被忽略时宿主给的是 `ViewDocument.Fallback`（窗口 = 地板）
        // ⇒ 本类不再自带任何尺寸字面量，也不再自己夹一次地板。
        var window = view.View?.Frame.Window;
        MinWidth = window?.EffectiveMinWidth ?? 0;
        MinHeight = window?.EffectiveMinHeight ?? 0;
        PreferredWidth = window is { Width: > 0 } declaredWidth ? declaredWidth.Width : MinWidth;
        PreferredHeight = window is { Height: > 0 } declaredHeight ? declaredHeight.Height : MinHeight;

        // 有效下限 = 声明的 min（已在读取侧夹过地板）；有效上限 = 声明的 max（缺省 = 无上限）。
        // 声明的上限若小于有效下限（例如写了 100）就**整条忽略** —— 声明自相矛盾时不该给用户造出一个 min > max 的死区。
        EffectiveMinWidth = MinWidth;
        EffectiveMinHeight = MinHeight;
        EffectiveMaxWidth = window?.MaxWidth is { } maxWidth && maxWidth >= EffectiveMinWidth ? maxWidth : null;
        EffectiveMaxHeight = window?.MaxHeight is { } maxHeight && maxHeight >= EffectiveMinHeight ? maxHeight : null;

        InstanceId = view.InstanceId;
        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        HasLayout = layout is not null;

        // **四个数一律 DIP**（口径见 `ModuleLayoutView`）：位置直接取用、不取整（DIP 不是像素，
        // 取整只会让"贴齐"在缩放 ≠100% 时差几个物理像素）；落窗时由 `ModuleWindowManager`
        // 乘该窗口的 `RenderScaling` 换成 `PixelPoint`。
        X = layout?.X ?? fallbackX;
        Y = layout?.Y ?? fallbackY;

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
    }

    /// <summary>实例 ID（工作现场内唯一、稳定）。</summary>
    public string InstanceId { get; }

    /// <summary>展示名（内容区顶部一行轻量文本）。</summary>
    public string Title { get; private set; }

    /// <summary>是否占位实例（模块定义缺失/版本不兼容）。</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary>是否已有布局记录（false = 由 UI 取默认位并写回）。</summary>
    public bool HasLayout { get; set; }

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
    public partial string StateText { get; set; } = "Active";

    /// <summary>状态语义（颜色 + 形状双重编码）。</summary>
    [ObservableProperty]
    public partial StateVisual State { get; set; } = StateVisual.Active;

    /// <summary>是否选中（点击边框带 → 主色边框 + 手柄 + 按钮簇）。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>是否处于运行态（Active）；false = 已停用（保留实例/拓扑/配置，只停流）。</summary>
    public bool IsEnabled => string.Equals(StateText, "运行中", StringComparison.Ordinal);

    /// <summary>是否隐藏视图（false 时窗口关闭但模块仍在运行）。</summary>
    [ObservableProperty]
    public partial bool IsViewVisible { get; set; } = true;

    /// <summary>是否锁定（不可拖动/缩放/选中；仍显示数据）。</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

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

    /// <summary>刷新实例状态（实例视图变化时调用；元素与数据源声明在实例生命周期内不变）。</summary>
    /// <remarks>
    /// **声明树换了就重建**：本方法每次刷新都走，而"实例先建、声明后到"是真实存在的时序
    /// （模块加入现场与包内 `view.json` 的解析不必同拍完成）。不重建的话内容区会**永远空着**
    /// —— 屏幕上表现为"模块窗口开了、里面什么都没有"，而 `RootLayout` 是 `null`、面上一句话都不报。
    /// 判据取"根节点**引用**是否换了"：同一个根节点反复刷新不会重建（重建会丢焦点与滚动位置）。
    /// </remarks>
    public void Apply(ModuleInstanceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        ApplyState(view.State);
        SyncPorts(view);

        var root = view.View?.Views.Count > 0 ? view.View.Views[0].Root : null;
        if (!ReferenceEquals(root, RootLayout))
        {
            Rebuild(view);
        }

        // 输入反馈（红框 / 就地结果 / 日志角落）不在这里：它属于内容区，
        // 由表面在每帧刷新时按 `ISurfaceFeedbackSink` 分派。

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsPlaceholder));
    }

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
    }

    /// <summary>导出当前几何（保存/提交用）。</summary>
    public ModuleLayoutView ToLayout() => new(InstanceId, X, Y, Width, Height, IsViewVisible);

    /// <summary>提交一个字段的写回（返回是否成功；失败原因由返回消息带回）。</summary>
    /// <param name="fieldName">字段名。</param>
    /// <param name="value">文本值。</param>
    public CommandResult ApplyConfigurationField(string fieldName, string value) =>
        _command.UpdateConfigurationField(InstanceId, fieldName, value);

    private void ApplyState(string state)
    {
        StateText = state switch
        {
            "Active" => "运行中",
            "Disabled" => "已停用",
            "Missing" => "定义缺失",
            "Placeholder" => "占位",
            "Removed" => "已移除",
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

        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>
    /// 模块内容**根**（声明树路径 <c>v</c>）—— 声明面里的根节点；<see langword="null"/> = 这个实例没有任何声明
    /// （整份被忽略 / 无声明文件），此时渲染面**不补任何缺省**。
    /// </summary>
    /// <remarks>布局是**加载时的一次性声明**：随实例的视图声明一起来，模块运行期不提供在线的排布调整。</remarks>
    public ViewNode? RootLayout { get; private set; }

    private void Rebuild(ModuleInstanceView view)
    {
        // 声明树换了 ⇒ 旧表面整块作废（它的排布树、控件缓存、状态都绑在旧声明上）。
        Surface = null;

        SyncPorts(view);

        // 声明面：整个实例的根节点（框架只渲染第一个视图）+ 它引用的数据源声明。
        // 每个节点的尺寸/位置/排布键都随节点一起来（**没有第二份布局**）。
        var document = view.View;
        var definition = document?.Views.Count > 0 ? document.Views[0] : null;
        RootLayout = definition?.Root;

        OnPropertyChanged(nameof(RootLayout));

        // **每个模块实例都有一个窗口**：声明了视图就按声明渲染；
        // 未声明视图的模块也照常开窗（至少显示模块名行），不引入"无界面模块"这一例外概念。
        //
        // **窗口尺寸不由内容计算**：默认尺寸来自**声明面**（包内 `view.json` 的 `frame.window`，
        // 缺省由 schema 补齐），内容排不下时由内容区的 ScrollViewer 滚动 —— 代码里不按
        // "元素类型 → 高度表"估算尺寸（那样等于把布局写死在代码里，模块作者无法在声明文件里调整）。
        //
        // 表面在这里**不构造**（见 EnsureSurface）：隐藏的窗口不该为"声明树 → 控件树"付钱。
        Surface = null;

        OnPropertyChanged(nameof(Surface));
    }

    /// <summary>新的模块内容表面（声明树 → 控件树）；没有声明视图 ⇒ <see langword="null"/>。</summary>
    /// <remarks>
    /// 由内容区在第一次渲染时构造一次（<see cref="EnsureSurface"/>）；<see cref="Rebuild"/> 会把它丢掉，
    /// 因为排布树与控件缓存都绑在旧声明上。
    /// </remarks>
    public ModuleSurfaceHost? Surface { get; private set; }

    /// <summary>取表面；没有就按当前声明建一个（幂等）。</summary>
    /// <returns>表面；没有声明视图 ⇒ <see langword="null"/>。</returns>
    public ModuleSurfaceHost? EnsureSurface()
    {
        if (Surface is null && RootLayout is { } root)
        {
            // 提交键传**取值函数**：设置改了控件下次按键就取到新值，不必重建控件树。
            Surface = new ModuleSurfaceHost(
                InstanceId,
                root,
                _query,
                _command,
                _viewState,
                () => MultilineSubmitKey);
            OnPropertyChanged(nameof(Surface));
        }

        return Surface;
    }

    /// <summary>驱动一次表面刷新（取数 + 就地更新）。框架每帧调一次。</summary>
    public void RefreshSurface() => Surface?.Refresh();

    /// <summary>告诉表面"这块缓冲被清空了"（提高世代号，让控件把过期副本整块换掉）。</summary>
    /// <param name="sourceName">显示缓冲名。</param>
    public void ForgetSurfaceBuffer(string sourceName) => Surface?.ForgetBuffer(sourceName);

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

