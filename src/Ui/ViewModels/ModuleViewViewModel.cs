using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;

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
    private readonly double _scale;

    /// <summary>标签页选中态（键 = 声明树路径如 <c>v/0</c>）——**纯 UI 状态，不进组合件**。</summary>
    private readonly Dictionary<string, int> _tabSelection = new(StringComparer.Ordinal);
    private readonly IViewStateAccess? _viewState;

    /// <summary>声明树路径 → 容器的 `StateKey`（切页时用它把选中态写进视图状态存储）。</summary>
    private readonly Dictionary<string, string> _tabStateKeys = new(StringComparer.Ordinal);

    /// <summary>最近一次重建用的实例视图（切换标签后需要按同一声明重建元素表）。</summary>
    private ModuleInstanceView? _lastView;

    /// <summary>
    /// **多行文本输入的提交键**——由 <see cref="ModuleWindowsViewModel"/> 从应用级设置下发。
    /// </summary>
    /// <remarks>
    /// 三档：<c>CtrlEnter</c>（默认：回车换行 / Ctrl+Enter 提交 / **失焦不提交** —— 否则"点按钮 ⇒ 先失焦提交 ⇒ 按钮又发"会双发）·
    /// <c>Enter</c>（回车提交 / Shift+Enter 换行）· <c>None</c>（键盘都不提交）。
    /// **只影响多行输入**；单行输入保持"回车 / 失焦一次提交"。
    /// 变更后需要**重建输入元素**才能生效 ⇒ 由 <see cref="ModuleWindowsViewModel"/> 在刷新时重设并触发重新渲染。
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

            // 提交键变了 ⇒ 重建元素表（输入元素的提交策略在构造时定型）
            if (_lastView is { } view)
            {
                Rebuild(view);
            }
        }
    }

    private string _multilineSubmitKey = "CtrlEnter";

    /// <summary>创建模块视图视图模型。</summary>
    /// <param name="view">实例视图（声明来源）。</param>
    /// <param name="layout">布局（可为空 = 未摆放）。</param>
    /// <param name="query">查询面（配置 Schema 读取）。</param>
    /// <param name="command">命令面（按字段写回配置）。</param>
    /// <param name="scale">屏幕缩放（1.0 = 100%；用于默认尺寸的像素换算）。</param>
    /// <param name="fallbackX">未摆放时的默认 X（物理像素）。</param>
    /// <param name="fallbackY">未摆放时的默认 Y（物理像素）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：切页的选中态、输入框**未提交**的文本、
    /// 文本显示的共享缓冲都放在这里 —— 元素表重建（切页 / 改设置）**不丢内容**（对齐 WinForms"控件活着，数据就在"）。
    /// `null`（设计期 / 未接线的调用点）⇒ 回落为：选中态只记在本地、显示用各自的本地缓冲。
    /// </param>
    public ModuleViewViewModel(
        ModuleInstanceView view,
        ModuleLayoutView? layout,
        IRuntimeQuery query,
        IRuntimeCommand command,
        double scale,
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
        _scale = scale <= 0 ? 1.0 : scale;

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

        // 尺寸与位置一律**整数物理像素**（唯一量化口径，见 `ScreenPixels`）
        X = ScreenPixels.Round(layout?.X ?? fallbackX);
        Y = ScreenPixels.Round(layout?.Y ?? fallbackY);

        // 尺寸再夹一层有效区间（DIP 上下限 × 缩放；组合件里的值是物理像素）
        double ClampToBounds(double pixels, double minDip, double? maxDip)
        {
            var value = Math.Max(pixels, minDip * _scale);
            return maxDip is { } upper ? Math.Min(value, upper * _scale) : value;
        }

        Width = ScreenPixels.Round(ClampToBounds(
            layout is { Width: > 0 } sized ? sized.Width : PreferredWidth * _scale,
            EffectiveMinWidth,
            EffectiveMaxWidth));
        Height = ScreenPixels.Round(ClampToBounds(
            layout is { Height: > 0 } sizedHeight ? sizedHeight.Height : PreferredHeight * _scale,
            EffectiveMinHeight,
            EffectiveMaxHeight));
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

    /// <summary>X（**屏幕坐标，物理像素**）。</summary>
    [ObservableProperty]
    public partial double X { get; set; }

    /// <summary>Y（屏幕坐标，物理像素）。</summary>
    [ObservableProperty]
    public partial double Y { get; set; }

    /// <summary>宽（物理像素）。</summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>高（物理像素）。</summary>
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

    /// <summary>本窗口的**对齐辅助线**（屏幕坐标；拖动中命中吸附时由控制器写入，窗内以虚线绘制）。</summary>
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

    /// <summary>元素视图模型（按声明生成）。</summary>
    public ObservableCollection<ViewElementViewModel> Elements { get; } = [];

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

    /// <summary>屏幕缩放（默认尺寸换算与最小尺寸钳制用）。</summary>
    public double Scale => _scale;

    /// <summary>
    /// **叠加层画布宽（DIP）** = <see cref="Width"/> ÷ <see cref="Scale"/> —— 叠加层几何的**唯一尺寸来源**。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须有它</b>（多套坐标系相互冲突会让连线错位，必须确定唯一事实源）：
    /// 模型几何（<see cref="X"/>/<see cref="Y"/>/<see cref="Width"/>/<see cref="Height"/>）的单位是**物理像素**
    /// （窗口位置本身就是 <c>PixelPoint</c>，见 `ModuleWindowManager`），而叠加层是 Avalonia 控件、绘制单位是 **DIP**。
    /// 在缩放 ≠ 100% 的屏幕上，两者相差一个 <see cref="Scale"/> 因子 ⇒ 若把 px 当 DIP 用，
    /// "本窗画的方块"与"对端算出来的本窗方块"就会落在不同位置 ⇒ **连线错位**（完全水平/垂直时也看得出来）。
    /// ⇒ 口径统一为：**叠加层几何一律用 DIP**；px 只在①本属性 ②`ModuleWindowsViewModel.ResolvePeerAnchor`
    /// 这两处换算（换算只发生在这两处，别处不许再乘除缩放）。
    /// </remarks>
    public double CanvasWidth => Width / _scale;

    /// <summary>**叠加层画布高（DIP）** = <see cref="Height"/> ÷ <see cref="Scale"/>（口径见 <see cref="CanvasWidth"/>）。</summary>
    public double CanvasHeight => Height / _scale;

    /// <summary>把**物理像素**长度换算成**绘制单位（DIP）**——叠加层几何一律经它换算。</summary>
    /// <param name="pixels">物理像素长度（模型几何的单位）。</param>
    /// <returns>DIP 长度。</returns>
    public double ToDip(double pixels) => pixels / _scale;

    /// <summary>刷新实例状态（实例视图变化时调用；元素与数据源声明在实例生命周期内不变）。</summary>
    public void Apply(ModuleInstanceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        ApplyState(view.State);
        SyncPorts(view);

        // 把"模块回报的输入处理结果"分发到对应输入元素（失败态 = 红框）。
        // 这里在**每刷新节拍**都会走到（`Refresh → SyncModules → Apply`）⇒ 失败态是**在线**出现的。
        ApplyInputFeedback();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsPlaceholder));
    }

    /// <summary>
    /// 把查询面里的**输入处理结果**分发到对应输入元素。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么由框架做</b>：模块不持 UI，无法自己把元素标红；模块只经 `IModuleContext.InputFeedback` 回报；</item>
    ///   <item><b>生命周期</b>：框架在**投递入队时**清除旧结果 ⇒ 查询面里没有条目 = 无失败态
    ///     （所以这里"查不到就清掉红框"，"下一次提交即刷新"自然成立）；</item>
    ///   <item><b>快路径</b>：模块没有输入元素时**不查查询面**（绝大多数模块），避免每帧无谓分配。</item>
    /// </list>
    /// </remarks>
    private void ApplyInputFeedback()
    {
        var flat = new List<ViewElementViewModel>();
        CollectElements(Elements, flat);

        var hasTarget = false;
        foreach (var element in flat)
        {
            if (element is TextInputElementViewModel or FileSelectorElementViewModel
                || (element is TextLogElementViewModel && element.FeedbackSource is { Length: > 0 }))
            {
                hasTarget = true;
                break;
            }
        }

        if (!hasTarget)
        {
            return;
        }

        var feedback = _query.GetInputFeedback(InstanceId);

        foreach (var element in flat)
        {
            // 收哪个源名的反馈（输入框与文件选择器走**同一条**通道：都是"提交了一串文本"）
            var source = element switch
            {
                TextInputElementViewModel input => input.Source,
                FileSelectorElementViewModel picker => picker.Source,
                _ => null,
            };

            InputFeedbackView? hit = null;
            if (source is { Length: > 0 })
            {
                foreach (var candidate in feedback)
                {
                    if (string.Equals(candidate.SourceName, source, StringComparison.Ordinal))
                    {
                        hit = candidate;
                        break;
                    }
                }
            }

            var invalid = hit is { Succeeded: false };

            switch (element)
            {
                case TextInputElementViewModel input:
                    input.IsInvalid = invalid;

                    // `showInlineResult: false` ⇒ 结果不挂在框上（改由声明的角落去接）。
                    if (input.ShowInlineResult && invalid && hit!.Message is { Length: > 0 } message)
                    {
                        input.LastResult = message;
                    }

                    break;

                case FileSelectorElementViewModel picker:
                    // 文件选择器的**成功也显示**（弹过系统对话框 ⇒ 给一次落点确认；
                    // 保存类操作尤其需要——"存到哪儿了"必须看得见）。失败则同色标红。
                    picker.IsInvalid = invalid;
                    if (picker.ShowInlineResult && hit is { Message: { Length: > 0 } pickerMessage })
                    {
                        picker.LastResult = pickerMessage;
                    }

                    break;

                // 文本日志的**左下角**：接声明了 `feedbackSource` 的那条源的结果。
                case TextLogElementViewModel log when log.FeedbackSource is { Length: > 0 }:
                    ApplyCornerStatus(log, feedback);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// 把某条输入源的反馈写到日志的**左下角**（命中 ⇒ 显示模块自己的文案、失败标红；查不到该源 ⇒ 清空）。
    /// </summary>
    /// <remarks>
    /// **文案原样透传**：模块的失败文案（如"保存失败：…"）本身就是给用户看的，框架不套"未生效"前缀 ——
    /// 失败与否走 <see cref="TextLogElementViewModel.CornerIsError"/>，不从字符串推断。
    /// </remarks>
    private static void ApplyCornerStatus(TextLogElementViewModel log, IReadOnlyList<InputFeedbackView> feedback)
    {
        foreach (var candidate in feedback)
        {
            if (!string.Equals(candidate.SourceName, log.FeedbackSource, StringComparison.Ordinal))
            {
                continue;
            }

            log.CornerIsError = !candidate.Succeeded;
            log.CornerStatus = candidate.Message is { Length: > 0 } message
                ? message
                : candidate.Succeeded ? null : "未生效";

            return;
        }

        log.CornerIsError = false;
        log.CornerStatus = null;
    }

    /// <summary>把元素树摊平（容器成了真容器 ⇒ 反馈分派必须看得见容器内部的元素）。</summary>
    private static void CollectElements(IEnumerable<ViewElementViewModel> source, List<ViewElementViewModel> flat)
    {
        foreach (var element in source)
        {
            flat.Add(element);

            if (element is ContainerElementViewModel container)
            {
                CollectElements(container.Children, flat);
            }
        }
    }

    /// <summary>接受外部几何（来自布局数据；拖动/缩放中不调用）。</summary>
    public void ApplyLayout(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        X = ScreenPixels.Round(layout.X);
        Y = ScreenPixels.Round(layout.Y);
        Width = ScreenPixels.Round(layout.Width > 0 ? layout.Width : PreferredWidth * _scale);
        Height = ScreenPixels.Round(layout.Height > 0 ? layout.Height : PreferredHeight * _scale);
        IsViewVisible = layout.IsViewVisible;
        HasLayout = true;
    }

    /// <summary>导出当前几何（保存/提交用）。</summary>
    public ModuleLayoutView ToLayout() => new(InstanceId, X, Y, Width, Height, IsViewVisible);

    /// <summary>按 refresh：把所有可编辑字段的当前值读回来（配置 Schema 表单）。</summary>
    public void ReloadConfigurationForms()
    {
        foreach (var element in Elements)
        {
            if (element is ConfigurationEditorElementViewModel editor)
            {
                editor.Reload(_query);
            }
        }
    }

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

    /// <summary>根作用域的排布模式（声明面的值；无声明 ⇒ 流式）。</summary>
    public ViewLayoutMode RootLayoutMode => RootLayout is { } root
        ? ViewLayoutModeExtensions.Parse(root.Layout, ViewLayoutMode.Flow)
        : ViewLayoutMode.Flow;

    /// <summary>根作用域的越界处理（声明面的值；无声明 ⇒ 交给外层内容区滚动）。</summary>
    public ViewLayoutOverflow RootOverflow => RootLayout is { } root
        ? ViewLayoutOverflowExtensions.Parse(root.Overflow, ViewLayoutOverflow.Scroll)
        : ViewLayoutOverflow.Scroll;

    /// <summary>根作用域的元素间距（声明面的值；无声明 ⇒ 框架自己的默认间距）。</summary>
    public double RootSpacing => RootLayout is { } root
        ? Math.Max(0, root.Spacing)
        : HYWDebugAssistant.Ui.Controls.ModuleElementPanel.DefaultSpacing;

    /// <summary>
    /// 根作用域的内容留白（声明面的值；无声明 ⇒ 0）。
    /// </summary>
    /// <remarks>
    /// **不含**框架自己的内容区留白（`20,22,20,24` 在 `ModuleContentView.axaml` 的 `ScrollViewer` 上，
    /// 那是"预留带"的一部分，不归模块声明管）。
    /// </remarks>
    public Thickness RootContentMargin => new(RootLayout is { Padding: > 0 } root ? root.Padding : 0);

    /// <summary>根作用域的网格列规格（声明面的值；无声明 ⇒ 单列满宽；仅网格模式用得上）。</summary>
    public IReadOnlyList<GridTrack>? RootColumns => RootLayout is { Columns.Count: > 0 } root ? root.Columns : null;

    /// <summary>根作用域的网格行规格（声明面的值；无声明 ⇒ 行数由元素推断、每行按内容）。</summary>
    public IReadOnlyList<GridTrack>? RootRows => RootLayout is { Rows.Count: > 0 } root ? root.Rows : null;

    /// <summary>根作用域的网格列间距（声明面的值；未给 ⇒ 用 <see cref="RootSpacing"/>）。</summary>
    public double RootColumnSpacing => RootLayout is { ColumnSpacing: > 0 } root ? root.ColumnSpacing : double.NaN;

    /// <summary>根作用域的网格行间距（声明面的值；未给 ⇒ 用 <see cref="RootSpacing"/>）。</summary>
    public double RootRowSpacing => RootLayout is { RowSpacing: > 0 } root ? root.RowSpacing : double.NaN;

    private void Rebuild(ModuleInstanceView view)
    {
        Elements.Clear();
        SyncPorts(view);

        // 标签页选中态是**纯 UI 状态**（不进组合件）：重建时要拿得到当前视图声明 ⇒ 留一份引用
        _lastView = view;

        // 声明面：整个实例的根节点（框架只渲染第一个视图）+ 它引用的数据源声明。
        // 元素树 = 根节点的子节点；每个节点的尺寸/位置/排布键都随节点一起来（**没有第二份布局**）。
        var document = view.View;
        var definition = document?.Views.Count > 0 ? document.Views[0] : null;
        RootLayout = definition?.Root;

        OnPropertyChanged(nameof(RootLayout));
        OnPropertyChanged(nameof(RootLayoutMode));
        OnPropertyChanged(nameof(RootOverflow));
        OnPropertyChanged(nameof(RootSpacing));
        OnPropertyChanged(nameof(RootContentMargin));
        OnPropertyChanged(nameof(RootColumns));
        OnPropertyChanged(nameof(RootRows));
        OnPropertyChanged(nameof(RootColumnSpacing));
        OnPropertyChanged(nameof(RootRowSpacing));

        // **每个模块实例都有一个窗口**：声明了视图就按声明渲染；
        // 未声明视图的模块也照常开窗（至少显示模块名行），不引入"无界面模块"这一例外概念。
        if (document is not null && definition is not null)
        {
            var sources = document.Sources.ToDictionary(s => s.Name, StringComparer.Ordinal);
            foreach (var element in BuildElements(definition.Root.Children, "v", sources))
            {
                Elements.Add(element);
            }
        }

        // **框架不自动补配置表单**：模块没声明 B1 时不替它生成一个 ——
        // 否则"有哪些配置项"这类**细节表单会显示给用户看**，"哪些值需要作为配置保存"也变成
        // 框架规定而非**模块作者声明**；而且表单里那个「还原」按钮与左上角已有入口重复。
        // ⇒ 框架**不自动补**；**配置能力完全保留**：模块若显式声明 B1（`ViewElementKind.ConfigurationEditor`），
        //    仍按声明渲染（下面 `CreateElement` 的分支照旧）。
        ReloadConfigurationForms();

        // **窗口尺寸不由内容计算**：
        // 默认尺寸来自**声明面**（包内 `view.json` 的 `frame.window`，缺省由 schema 补齐），
        // 内容排不下时由内容区的 ScrollViewer 滚动 —— 代码里不按"元素类型 → 高度表"估算尺寸
        // （那样等于把布局写死在代码里，模块作者无法在声明文件里调整）。
    }

    /// <summary>读"某容器是否折叠"（容器声明了 `StateKey` 才读写；未知 ⇒ <see langword="null"/> ⇒ 展开）。</summary>
    private bool? ReadCollapsedState(string? stateKey) =>
        stateKey is { Length: > 0 } ? _viewState?.GetCollapsed(InstanceId, stateKey) : null;

    /// <summary>写"某容器是否折叠"（未声明 `StateKey` ⇒ 什么都不做：折叠态只是本次会话的便利，不是模块状态）。</summary>
    private void WriteCollapsedState(string? stateKey, bool collapsed)
    {
        if (stateKey is { Length: > 0 })
        {
            _viewState?.SetCollapsed(InstanceId, stateKey, collapsed);
        }
    }

    /// <summary>
    /// 声明元素 → **可渲染元素表**（容器类元素带自己的子元素，形成与声明树同构的树）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>容器不展平</b>：`Group` / `Collapsible` / `Scroll` 都是**真容器** ——
    /// 各自渲染出自己的外壳（分组框 / 可折叠区 / 独立滚动区）+ 内部独立排布，子元素收在它内部
    /// （与 WinForms 的 `GroupBox` / 带展开按钮的 `Panel` / `Panel.AutoScroll` 同一思想）。
    /// </para>
    /// <para>
    /// <b>页节点按 `TabPage` 语义</b>：`TabPages` 的每个子节点**自身不渲染、不画框**，`Title` 只作页签文字，
    /// 它的子元素就是页内容（WinForms 的 `TabPage` 同构：页只是"当前页内容"的容器，标签控件由 `TabControl` 提供）。
    /// ⇒ 内置模块把 `Group` 当页包装用的写法**逐像素不变**。
    /// </para>
    /// <para>
    /// <b>`TabPages` 展开为「标签条 + <u>当前页</u>的子元素」</b>：
    /// ① 页签条由 <see cref="TabStripElementViewModel"/> 渲染；② **只渲染当前页**；
    /// ③ 页序号用**声明路径**（`v/0` 这类）作键 —— 与"每次重建元素表"一致，且**纯 UI 状态、不进组合件**。
    /// </para>
    /// <para>
    /// **已知限制**：切换标签会**重建整棵元素表**（`Elements.Clear()` + 重填）⇒ 其他元素的焦点/滚动位置会重置。
    /// v1 接受；若将来需要保留，改为"标签页用独立宿主 + 只换内容"（需把元素模板抽成共享资源字典）。
    /// </para>
    /// </remarks>
    /// <param name="nodes">声明节点（根层的子节点，或某容器的子节点）。</param>
    /// <param name="path">本层在声明树里的路径（用于标签页选中态的稳定键）。</param>
    /// <param name="sources">按名字索引的数据源声明。</param>
    private IEnumerable<ViewElementViewModel> BuildElements(
        IReadOnlyList<ViewNode> nodes,
        string path,
        Dictionary<string, ViewSourceDeclaration> sources)
    {
        var index = 0;

        foreach (var element in nodes)
        {
            var childPath = $"{path}/{index++}";
            var kind = ViewElementKindExtensions.Parse(element.Kind);

            if (kind == ViewElementKind.TabPages)
            {
                if (element.Children.Count == 0)
                {
                    continue;   // 零页 ⇒ 什么都不画（不假装支持）
                }

                // 声明了 `StateKey` ⇒ 选中态读**视图状态存储**（模块也能回读；元素表重建后仍在）；
                // 未声明 ⇒ 沿用本地字典（今天的行为，老模块零变化）。
                var stateKey = element.StateKey ?? string.Empty;
                _tabStateKeys[childPath] = stateKey;
                var stored = stateKey.Length > 0 ? _viewState?.GetSelectedPage(InstanceId, stateKey) : null;
                var selected = Math.Clamp(stored ?? _tabSelection.GetValueOrDefault(childPath), 0, element.Children.Count - 1);
                var pages = new List<TabPageItemViewModel>(element.Children.Count);

                for (var page = 0; page < element.Children.Count; page++)
                {
                    var pageTitle = element.Children[page].Title;
                    pages.Add(new TabPageItemViewModel(
                        page,
                        pageTitle is { Length: > 0 } title ? title : $"{page + 1}",
                        page == selected,
                        target => SelectTab(childPath, target)));
                }

                yield return new TabStripElementViewModel(element, pages, selected);

                // 页内容：页节点**带子元素**时用子元素；**页节点本身就是内容元素**时用它自己
                //（两种声明写法都容忍 —— 兜底一下，否则表现为"切过去是空白页"）。
                var pageNode = element.Children[selected];
                var pageContent = pageNode.Children.Count > 0
                    ? pageNode.Children
                    : (IReadOnlyList<ViewNode>)[pageNode];

                foreach (var nested in BuildElements(pageContent, childPath, sources))
                {
                    yield return nested;
                }

                continue;
            }

            // 四类容器都是**真容器**：不展平子元素，而是渲染出自己的外壳 + 内部独立排布。
            // 空容器也照此处理（渲染出一个空的壳）—— "有内容才画"那种约定会让空容器在界面上凭空消失。
            if (kind.IsContainer())
            {
                var children = BuildElements(element.Children, childPath, sources).ToList();

                // 显式声明目标类型：switch 表达式在 C# 里不会为几个兄弟类型"发明"共同基类。
                ContainerElementViewModel container = kind switch
                {
                    // 折叠态**存到外部**（元素表会整体重建）：声明面的 `expanded` 是缺省，用户拨过就以用户为准。
                    ViewElementKind.Collapsible => new CollapsibleElementViewModel(
                        element,
                        children,
                        isExpanded: ReadCollapsedState(element.StateKey) is { } collapsed
                            ? !collapsed
                            : element.Expanded,
                        collapsedChanged: value => WriteCollapsedState(element.StateKey, value)),
                    ViewElementKind.Group => new GroupElementViewModel(element, children),
                    _ => new LayoutContainerElementViewModel(element, children),
                };

                Declare(container);
                yield return container;

                continue;
            }

            // 命令按钮：元素只负责**位置**，标题/图标/说明来自命令声明；
            // 元素引用了未声明的命令 → 走"未渲染"占位（不假装支持，也不静默）。
            if (kind == ViewElementKind.CommandButton)
            {
                if (element.Command is not { Length: > 0 })
                {
                    yield return new UnsupportedElementViewModel(element);
                }
                else
                {
                    var command = CreateCommand(element);
                    Declare(command);
                    yield return command;
                }

                continue;
            }

            var created = CreateElement(element, sources);
            Declare(created);
            yield return created;
        }
    }

    /// <summary>
    /// 把声明面里**渲染期只读**的开关写进元素视图模型：
    /// 反馈来源（谁的处理结果显示在这个元素的角落）与是否就地显示结果行。
    /// </summary>
    /// <param name="viewModel">元素视图模型（节点已由构造函数带入）。</param>
    private static void Declare(ViewElementViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        viewModel.FeedbackSource = viewModel.Node.FeedbackSource ?? string.Empty;
        viewModel.ShowInlineResult = viewModel.Node.ShowInlineResult;
    }

    /// <summary>切换标签页（**纯 UI 状态**：只记在本视图模型里，不进组合件、不下发模块）并重建元素表。</summary>
    /// <param name="path">标签页在声明树里的路径键。</param>
    /// <param name="page">目标页序号。</param>
    private void SelectTab(string path, int page)
    {
        _tabSelection[path] = page;

        // 把选中页写进**视图状态存储**：① 模块可回读（"当前选中页可被感知"）
        // ② 元素表重建后仍记得（切页不丢状态）。**仍不进组合件、不持久化**（重启即丢）。
        if (_viewState is not null
            && _tabStateKeys.TryGetValue(path, out var stateKey)
            && stateKey.Length > 0)
        {
            _viewState.SetSelectedPage(InstanceId, stateKey, page);
        }

        if (_lastView is { } view)
        {
            Rebuild(view);
        }
    }

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

    private ViewElementViewModel CreateElement(ViewNode element, Dictionary<string, ViewSourceDeclaration> sources)
    {
        ViewSourceDeclaration? source = null;
        if (element.DataSource is { Length: > 0 } name)
        {
            _ = sources.TryGetValue(name, out source);
        }

        return ViewElementKindExtensions.Parse(element.Kind) switch
        {
            ViewElementKind.Numeric => new ValueElementViewModel(element, source),
            ViewElementKind.Curve => new CurveElementViewModel(element, source),

            // A5 文本日志：**共享文本缓冲**（"不清空就一直在"）——
            // 同一份给 UI 渲染与模块回读（`IModuleContext.ViewState.GetDisplayLines`）；未接线 ⇒ 本地缓冲。
            ViewElementKind.TextLog => new TextLogElementViewModel(
                element,
                source,
                DisplayFor(element.DataSource)),

            // 人机操作三通道：命令按钮 / 文本输入 / 开关 —— 全部**投递到模块线程**
            // 文本输入：把**应用级**的多行提交键档位传进去（多行提交键不属模块声明）
            // 再传数据源：只为取"**框外单位标签**"（数据源声明的 unit）与数值输入的字符放行
            // 并把"当前文本"落到**视图状态存储**（元素表重建后恢复；模块可回读）
            ViewElementKind.TextInput => new TextInputElementViewModel(
                element,
                SubmitInput,
                MultilineSubmitKey,
                source,
                _viewState,
                InstanceId),

            // B6 文件选择器：弹对话框选/存路径 → 经既有输入通道提交给模块
            ViewElementKind.FileSelector => new FileSelectorElementViewModel(element, SubmitInput),

            // 开关也把"当前值"落到**视图状态存储**：切页 / 改设置重建元素表后，
            // 开关必须停在用户上次拨到的位置 —— 否则 UI 弹回"关"、模块还在按"开"跑（表现为"连续发送"照发）。
            ViewElementKind.ToggleSlider => new ToggleElementViewModel(element, SubmitInput, _viewState, InstanceId),

            // **开关式滑块**（B7）：与 B3 语义相同、外观不同（模板按 `IsSwitch` 二选一）
            ViewElementKind.ToggleSwitch => new ToggleElementViewModel(element, SubmitInput, _viewState, InstanceId),
            ViewElementKind.ConfigurationEditor => CreateConfigurationEditor(element),
            _ => new UnsupportedElementViewModel(element),
        };
    }

    /// <summary>
    /// 取某显示源的**共享文本缓冲**（视图状态存储里那一份）；未接线或未声明源名 ⇒ <see langword="null"/>
    /// （元素自己建本地缓冲 —— 设计期与未接线的调用点行为不变）。
    /// </summary>
    /// <param name="sourceName">显示源名。</param>
    private DisplayBuffer? DisplayFor(string? sourceName) =>
        _viewState is not null && sourceName is { Length: > 0 }
            ? _viewState.Display(InstanceId, sourceName)
            : null;

    /// <summary>命令按钮：按声明节点生成（绑定的命令名来自节点的 `command` 角色键）。</summary>
    /// <param name="node">命令按钮节点。</param>
    private CommandElementViewModel CreateCommand(ViewNode node) =>
        new(node, name => _command.SendCommand(InstanceId, name));

    /// <summary>提交一个输入值（输入框/开关共用）。</summary>
    private CommandResult SubmitInput(string source, string text) =>
        _command.SubmitInput(InstanceId, source, text);

    private ConfigurationEditorElementViewModel CreateConfigurationEditor(ViewNode node)
    {
        var editor = new ConfigurationEditorElementViewModel(node);
        editor.Attach(InstanceId, ApplyConfigurationField);
        return editor;
    }

    /// <summary>预留带厚度（与 <c>PortMarkerPanel.BandThickness</c> 一致；内容区内边距用它）。</summary>
    public const double ModuleContentBands = 20;
}
