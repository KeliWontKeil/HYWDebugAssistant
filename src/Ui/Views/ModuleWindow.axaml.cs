using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui.Geometry;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **拼图式模块窗口**：无边框、自定义拖动/缩放、锁定与按钮簇。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>本类只做**指针交互 → 几何数值**的翻译，几何与提交逻辑全在
///     <see cref="ModuleWindowsViewModel"/>（可独立测试）；</item>
///   <item>坐标一律换算为**屏幕物理像素**（<c>Position + 局部点 × RenderScaling</c>），
///     这样窗口在指针下移动时不会产生坐标回授；</item>
///   <item>锁定（O-3）：按住边框也**不拖动、不缩放、不选中**（内容照常刷新）；</item>
///   <item><b>口标记的标签编辑在本类</b>（M1-17 后修复）：标记层是窗口级覆盖层、压在交互带之上，
///     控制器在**手势发生时**直接取（不再依赖"装载时把控制器传给内容视图"那种时序交接——
///     <c>DataContext</c> 若先于 <c>Controller</c> 赋值，交接拿到的永远是 <c>null</c>，
///     于是提交被静默取消：弹窗一关、什么都没发生）。</item>
/// </list>
/// </remarks>
public partial class ModuleWindow : Window
{
    private bool _dragging;
    private bool _resizing;

    /// <summary>最近一次拖动/缩放的指针位置（合并到下一个渲染帧；输入事件可能远高于刷新率）。</summary>
    private (double X, double Y, bool? SnapEnabled)? _pendingPointer;

    /// <summary>是否已把一次指针冲刷排到渲染帧。</summary>
    private bool _pointerFlushScheduled;
    private ModuleViewViewModel? _hooked;

    /// <summary>锚点拖动的**启动阈值**（逻辑 px）：小于它视为"点击"，把手势留给双击改网名。</summary>
    private const double AnchorDragThreshold = 4;

    /// <summary>锚点拖动候选（按下后、尚未越过阈值）。</summary>
    private PortMarkerViewModel? _anchorCandidate;

    /// <summary>是否已越过阈值（真在拖）。</summary>
    private bool _anchorDragging;

    /// <summary>按下点（窗口坐标，只用来算阈值）。</summary>
    private Point _anchorOrigin;

    /// <summary>拖动前的边缘（提交失败时回滚）。</summary>
    private PortEdge _anchorFromEdge;

    /// <summary>拖动前的偏移（提交失败时回滚）。</summary>
    private double _anchorFromOffset;

    /// <summary>
    /// **抓取偏移** = 标记中心 − 指针（沿当前边的轴，单位 px）。
    /// </summary>
    /// <remarks>
    /// 没有它，标记会在拖动第一帧"跳"到指针中心（抓端部时能跳半个标记长）；跨边后作废重算
    /// （不同轴的归一化位置没有可比性）。
    /// <para>
    /// <b>必须用"按下时"的指针位置来算</b>：若改用**当前这次移动**的指针位置 ⇒
    /// <c>Δ = 偏移×长度 − 指针轴</c>，代回去得到 <c>新偏移 ≡ 旧偏移</c> ——
    /// **同一条边内的拖动永远原地不动**；跨边时因换轴会把 Δ 清零，才"看起来能动"，把这个缺陷藏住了。
    /// 只跨边拖的用例漏掉了这条路径，表现为「解锁了但拖不动」。
    /// </para>
    /// </remarks>
    private double _anchorDelta;

    /// <summary>**按下点**在标记面板坐标系里的位置（算抓取偏移用；见 <see cref="_anchorDelta"/>）。</summary>
    private Point _anchorPressLocal;

    /// <summary>创建窗口。</summary>
    public ModuleWindow()
    {
        InitializeComponent();

        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Hook();

        // **真实缩放变化**（窗口被拖到别块屏、系统改 DPI）：重新解析并发布一次 ——
        // 这不是"settle"：DPI 确实变了，几何理应跟着重算，而重算的触发点是这里、不是"下一帧顺手发现"。
        ScalingChanged += (_, _) => PublishScaling();

        // 显示之后再解析一次：那时窗口自己的 `RenderScaling` 才是事实（见 `PublishScaling`）。
        Opened += (_, _) => PublishScaling();

        // 窄窗 chrome 的判定事实 = **窗口实际宽度**（不是模型里的 Width）：
        // 布局器、预览器改的都是 Bounds，只有它如实反映"现在这块地方有多宽"。
        Opened += (_, _) => ApplyChromeMode();

        // 任何关闭路径都先摘掉缓存视图的父级（隐藏后再“显示全部”会创建新窗口）。
        // 用 `Closed` 而不是 `Closing`：外壳取消关闭时子窗的 `Closing` 不会触发 ——
        // 视图必须留到"窗口真的关掉"之后再摘，取消后窗口仍可见时内容不能空。
        Closed += (_, _) =>
        {
            DetachModuleView();
            Unhook();
        };
    }

    /// <summary>按**窗口实际宽度**切换窄窗 chrome（按钮簇竖排 / 选中标题竖排）。</summary>
    /// <remarks>
    /// 阈值只有 <see cref="ModuleSurfaceMetrics.NarrowWidth"/> 一处；状态就用窗口自己的 `narrow` 类
    /// （不另存字段），样式负责排布方向，因此不会出现"模型说窄、屏幕上不窄"的第二份事实。
    /// </remarks>
    private void ApplyChromeMode()
    {
        var narrow = Bounds.Width > 0 && Bounds.Width < ModuleSurfaceMetrics.NarrowWidth;
        if (narrow != Classes.Contains("narrow"))
        {
            Classes.Set("narrow", narrow);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            ApplyChromeMode();
        }
    }

    /// <summary>
    /// **这块窗口的缩放**（一次解析、一份发布）—— 窗口落位与自绘几何都读这一份。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么不能各处自己读 <see cref="TopLevel.RenderScaling"/>：`Show` **之前**它不可信
    /// （项目里已有同类先例：`App` 取进程级缩放用的是 `Screens.Primary.Scaling`，注释写明
    /// "窗口在这一步还没 `Show`，但 `Screens` 已经可用"）。开窗时按 1.0 算，显示之后就会被平台
    /// 或自绘几何各纠正一次 —— 用户看到的就是"实例化后 0.x 秒动 1px、然后归位"。
    /// </para>
    /// <para>
    /// 于是：开窗前由 <see cref="ModuleWindowManager"/> 调 <see cref="PublishScalingFor"/> 按**目标屏**解析一次；
    /// `Show` 之后与真实缩放变化时由本类按窗口自己的值更新。**只有这一处解析、只有这一份发布。**
    /// </para>
    /// </remarks>
    public double ResolvedScaling { get; private set; } = 1.0;

    /// <summary>已发布的缩放发生变化（真实变化：开窗后拿到真值、窗口被拖到另一块屏）。</summary>
    public event EventHandler? ResolvedScalingChanged;

    /// <summary>
    /// **按 DIP 位置解析"这块窗口将落在哪块屏"，并发布其缩放**（开窗前调用一次）。
    /// </summary>
    /// <param name="dipX">模型位置 X（DIP）。</param>
    /// <param name="dipY">模型位置 Y（DIP）。</param>
    /// <remarks>
    /// DIP → 物理像素需要缩放，而"该用哪块屏的缩放"又取决于落点 ⇒ 不能固定用主屏缩放投影。
    /// 这里改为**按 DIP 找屏**：把每块屏的物理 <c>Bounds</c> 用该屏自己的缩放换成 DIP 后判断包含关系。
    /// 未显示时 `Screens` 已经可用；显示之后由 `PublishScaling()` 用本窗口的真值覆盖。
    /// </remarks>
    public void PublishScalingFor(double dipX, double dipY) => PublishScalingValue(
        ScreenGeometry.ResolveScalingForDip(
            Screens.All,
            dipX,
            dipY,
            Screens.Primary is { Scaling: > 0 } primary ? primary.Scaling : 1.0));

    /// <summary>按"当前位置 / 窗口自己的值"重新解析并发布缩放（`Show` 之后、真实缩放变化时）。</summary>
    public void PublishScaling() => PublishScaling(Position);

    private void PublishScaling(PixelPoint intended)
    {
        var scaling = 1.0;

        if (IsVisible && RenderScaling > 0)
        {
            // 已显示 ⇒ 窗口自己的值就是事实。
            scaling = RenderScaling;
        }
        else if (Screens.ScreenFromPoint(intended) is { Scaling: > 0 } screen)
        {
            // 未显示 ⇒ 按它将要落在的那块屏取（`Screens` 在 `Show` 之前就可用）。
            scaling = screen.Scaling;
        }
        else if (Screens.Primary is { Scaling: > 0 } primary)
        {
            scaling = primary.Scaling;
        }

        PublishScalingValue(scaling);
    }

    private void PublishScalingValue(double scaling)
    {
        if (Math.Abs(scaling - ResolvedScaling) < 1e-6)
        {
            return;
        }

        ResolvedScaling = scaling;
        ResolvedScalingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>模块窗口集控制器（由窗口管理器在显示前注入）。</summary>
    public ModuleWindowsViewModel? Controller { get; set; }

    private ModuleViewViewModel? Module => DataContext as ModuleViewViewModel;

    // ---------------------------------------------------------------- 交互入口

    /// <summary>
    /// 边框带按下：拖动（Ctrl = 独立拖动，不带动拼接组）；**Shift+点击 = 加入/移出保存范围**。
    /// 普通点击选中并置顶**整组**；Shift/Ctrl 语义不变。
    /// </summary>
    private void OnBandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Module is not { } module || Controller is not { } controller)
        {
            return;
        }

        // Shift+点击：多选切换，不启动拖动（Ctrl 已用于“独立拖动”，Shift 空闲且符合多选直觉）。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            controller.ToggleSelection(module.InstanceId);
            e.Handled = true;
            return;
        }

        // 锁定模块可选中（组合范围可包含它），但绝不进入拖动/缩放。
        if (module.IsLocked)
        {
            controller.SelectGroup(module.InstanceId);
            e.Handled = true;
            return;
        }

        var (x, y) = ScreenPoint(e);
        var independent = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // 普通点击：先按整组选中并抬窗；随后 BeginDrag 只会把锚点移到被点者，不会打散已选中的组。
        // Ctrl 独立拖动保持原语义（不动组）。
        if (!independent)
        {
            controller.SelectGroup(module.InstanceId);
        }

        if (controller.BeginDrag(module.InstanceId, x, y, independent))
        {
            _dragging = true;
            e.Pointer.Capture(sender as IInputElement ?? this);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 最外 1px 可视边框按下：等价于边框带按下。
    /// </summary>
    /// <remarks>
    /// 只有**点在 Frame 自己身上**（= 可视外边框；内容区由 `ModuleSurfaceView` 的透明底命中）才转交，
    /// 内容控件/端口标记的事件冒泡到 Frame 时不触发窗口拖动。
    /// </remarks>
    private void OnFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender))
        {
            return;
        }

        OnBandPressed(sender, e);
    }

    /// <summary>缩放手柄按下：按手柄方向缩放。</summary>
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Module is not { IsLocked: false } module || Controller is not { } controller || sender is not Control handle)
        {
            return;
        }

        var (x, y) = ScreenPoint(e);
        if (controller.BeginResize(module.InstanceId, ResolveHandle(handle.Name), x, y))
        {
            _resizing = true;
            e.Pointer.Capture(handle);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Controller is not { } controller)
        {
            return;
        }

        var (x, y) = ScreenPoint(e);

        // 口标记锚点拖动（G5 · C5-7）：标记层压在拖动带与手柄之上，故必须先判它；
        // 它与 `_dragging`/`_resizing` 互斥（后者只由边框带/手柄按下置位）。
        if (OnAnchorDragMoved(e))
        {
            return;
        }

        if (_resizing)
        {
            _pendingPointer = (x, y, e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? false : null);
            SchedulePointerFlush();
            e.Handled = true;
            return;
        }

        if (_dragging)
        {
            // 【用户第十七轮反馈】拖动**期间**按下 Ctrl = **立刻拆分**（只拖自己、其余成员回原位），
            // 不再要求"必须拖动前按住"。幂等：已独立拖动时不做事；松手后不会重新合组。
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                controller.DetachDrag();
            }

            // 按住 Alt = 本次移动临时停用吸附（Q5-1）
            _pendingPointer = (x, y, e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? false : null);
            SchedulePointerFlush();
            e.Handled = true;
        }
    }

    /// <summary>把"最新指针位置"排到渲染帧（同帧多次移动只处理最后一次）。</summary>
    private void SchedulePointerFlush()
    {
        if (_pointerFlushScheduled)
        {
            return;
        }

        _pointerFlushScheduled = true;
        Dispatcher.UIThread.Post(FlushPointerMove, DispatcherPriority.Render);
    }

    /// <summary>渲染帧冲刷：拖动/缩放只在这一步写模型，避免输入事件洪峰直接变成窗口移动洪峰。</summary>
    private void FlushPointerMove()
    {
        _pointerFlushScheduled = false;

        if (_pendingPointer is not { } pointer || Controller is not { } controller)
        {
            return;
        }

        _pendingPointer = null;

        if (_resizing)
        {
            controller.ResizeTo(pointer.X, pointer.Y, pointer.SnapEnabled);
        }
        else if (_dragging)
        {
            controller.DragTo(pointer.X, pointer.Y, snapEnabled: pointer.SnapEnabled);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Controller is not { } controller)
        {
            return;
        }

        // 口标记锚点拖动松手：真拖过才提交（未越阈值的释放留给双击改网名）
        if (OnAnchorDragReleased(e))
        {
            return;
        }

        // 松手前先吃掉最后一帧位置（否则最后一段指针移动会丢）。
        FlushPointerMove();

        if (_resizing)
        {
            _resizing = false;
            controller.EndResize();
            e.Pointer.Capture(null);
        }

        if (_dragging)
        {
            _dragging = false;
            controller.EndDrag();
            e.Pointer.Capture(null);
        }
    }

    // ---------------------------------------------------------------- 口标记：锚点拖动（G5 · C5-7）

    /// <summary>
    /// 标记按下：**只记录候选**，不 Capture、不 Handled —— 否则双击改网络名会被拖动吃掉。
    /// </summary>
    /// <remarks>
    /// 本类是"指针交互 → 几何数值"的翻译层：这里只算出**目标边 + 目标偏移**，写入一律走
    /// <see cref="ModuleWindowsViewModel.MovePortAnchor"/>（松手时提交，失败回滚）。
    /// </remarks>
    private void OnPortMarkersPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindMarker(e.Source) is not { } marker || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 端口标记占满了边框的很大一段：单击标记也应把模块选中（否则用户点在“边框上”却不出手柄）。
        // 已在多选集合中 ⇒ 只移动主选中，不清空其余选择。
        // 不 Handled、不 Capture ⇒ 双击改网络名与锚点拖动的既有手势不受影响。
        if (Module is { } && Controller is { } selection)
        {
            selection.SelectGroup(marker.InstanceId);
        }

        _anchorCandidate = marker;
        _anchorDragging = false;
        _anchorOrigin = e.GetPosition(this);
        _anchorFromEdge = marker.Edge;
        _anchorFromOffset = marker.Offset;
        _anchorDelta = 0;

        // 抓取偏移要用**按下点**（在面板坐标系里）——不能用后续移动事件的位置（见 `_anchorDelta`）
        _anchorPressLocal = MarkerPanel is { } panel ? e.GetPosition(panel) : default;
    }

    /// <summary>标记面板（算边与偏移的坐标系）。</summary>
    private ItemsControl? MarkerPanel => this.FindControl<ItemsControl>("PortMarkers");

    /// <summary>
    /// 锚点拖动：**越过阈值**才启动（启动帧保留抓取偏移 ⇒ 标记不跳位），松手提交。
    /// </summary>
    /// <returns>是否消费了本次移动。</returns>
    private bool OnAnchorDragMoved(PointerEventArgs e)
    {
        if (_anchorCandidate is not { } marker)
        {
            return false;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _anchorOrigin.X;
        var dy = point.Y - _anchorOrigin.Y;

        if (!_anchorDragging)
        {
            if ((dx * dx) + (dy * dy) < AnchorDragThreshold * AnchorDragThreshold)
            {
                return false;   // 还在阈值内：不启动，保留"这是双击"的可能
            }

            if (marker.IsAnchorFixed)
            {
                // 固定锚点：拖不动，但**必须给反馈**，否则用户以为程序卡了（一次手势提示一次）
                _anchorCandidate = null;
                Controller?.RaiseNotice(
                    TextCatalog.Get(typeof(ModuleWindow).Assembly, "moduleWindow.anchor.dragBlocked", marker.PortName),
                    isError: false);
                return true;
            }

            _anchorDragging = true;

            // 拖动期间冻结"框架权威值 → 本地"的节拍同步（否则预览会被拽回原位；见 `SyncPorts`）
            marker.IsDraggingAnchor = true;

            if (MarkerPanel is { } startPanel && startPanel.Bounds.Height > 0)
            {
                // 标记当前中心 − **按下点** = 抓取偏移（拖动第一帧就地不动）
                var vertical = marker.Edge is PortEdge.Left or PortEdge.Right;
                var axis = vertical ? _anchorPressLocal.Y : _anchorPressLocal.X;
                var extent = vertical ? startPanel.Bounds.Height : startPanel.Bounds.Width;
                _anchorDelta = (marker.Offset * extent) - axis;
            }

            e.Pointer.Capture(MarkerPanel);
        }

        UpdateAnchorPreview(marker, e);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// 松手：真拖过 ⇒ 提交（**一次写全**偏移/边/固定标志，见 <c>MovePortAnchor</c> 的说明）。
    /// </summary>
    /// <returns>是否消费了本次释放。</returns>
    private bool OnAnchorDragReleased(PointerReleasedEventArgs e)
    {
        if (_anchorCandidate is not { } marker)
        {
            return false;
        }

        var dragged = _anchorDragging;
        _anchorCandidate = null;
        _anchorDragging = false;

        if (!dragged)
        {
            return false;   // 没越过阈值 ⇒ 交给双击
        }

        // **先解锁同步再提交**：提交后框架的权威值会经节拍同步回本地（失败则被 `MovePortAnchor` 回滚）
        marker.IsDraggingAnchor = false;

        e.Pointer.Capture(null);

        if (Module is { } module && Controller is { } controller)
        {
            _ = controller.MovePortAnchor(module, marker, _anchorFromEdge, _anchorFromOffset);

            // 收尾：清掉"连接对齐"高亮与提示去重状态（松手后由真实建网接管）
            controller.EndAnchorDockPreview();
        }

        e.Handled = true;
        return true;
    }

    /// <summary>
    /// 由指针位置算出**最近的边 + 该边上的归一化偏移**（拖动预览，就地更新标记）。
    /// </summary>
    /// <remarks>
    /// 规则刻意为"简单可预期"：**到哪条边最近就贴哪条边**，沿边位置 = 指针在该轴上的投影
    /// （归一化到 0–1，超出即夹到端部）；**不做**"必须拖到边外才换边"这类隐藏手势。
    /// </remarks>
    private void UpdateAnchorPreview(PortMarkerViewModel marker, PointerEventArgs e)
    {
        if (MarkerPanel is not { } panel)
        {
            return;
        }

        var width = panel.Bounds.Width;
        var height = panel.Bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var local = e.GetPosition(panel);

        var toLeft = local.X;
        var toRight = width - local.X;
        var toTop = local.Y;
        var toBottom = height - local.Y;

        var nearest = toLeft <= toRight && toLeft <= toTop && toLeft <= toBottom
            ? PortEdge.Left
            : toRight <= toTop && toRight <= toBottom
                ? PortEdge.Right
                : toTop <= toBottom
                    ? PortEdge.Top
                    : PortEdge.Bottom;

        // 这条边放不下标记 ⇒ **本次不落位**（标记留在当前边）。否则松手后会被 `FitEdge` 夹回去，
        // 表现为"拖过去又弹回"；指针移到放得下的边时继续正常落位。
        if (PortMarkerLayout.PickDragEdge(nearest, width, height) is not { } edge)
        {
            return;
        }

        if (edge != marker.Edge)
        {
            marker.Edge = edge;
            _anchorDelta = 0;   // 换了轴 ⇒ 抓取偏移作废（否则会按另一根轴的像素量错位）
        }

        var (axis, extent) = edge is PortEdge.Left or PortEdge.Right
            ? (local.Y, height)
            : (local.X, width);

        var proposed = Math.Clamp((axis + _anchorDelta) / extent, 0, 1);

        // "连接对齐"吸附：模块已拼在一起、两个口只差对齐时，
        // 把标记吸到**能连上**的位置并高亮两侧（判据在 Core，复用建网判定 ⇒ 吸得上就一定连得上）。
        marker.Offset = Module is { } module && Controller is { } controller
            ? controller.SnapAnchorToConnection(module, marker, edge, proposed)
            : proposed;
    }

    // ---------------------------------------------------------------- 口标记：网络标签编辑

    /// <summary>双击口标记 → 打开标签编辑弹窗。</summary>
    private void OnPortMarkersDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FindMarker(e.Source) is not { } marker)
        {
            return;
        }

        e.Handled = true;

        if (Module is not { } module)
        {
            return;
        }

        // 同时只允许一个编辑框打开，并清掉上一次的错误提示
        foreach (var other in module.Ports)
        {
            other.IsEditing = false;
            other.EditError = null;
        }

        marker.EditText = marker.Label ?? string.Empty;
        marker.EditError = null;
        marker.IsEditing = true;
    }

    /// <summary>
    /// 编辑框**挂到可视树之后**再取焦点：同步 <c>Focus()</c> 时 DataTemplate 还没实例化，
    /// 拿不到 TextBox → 用户输入落空。
    /// </summary>
    private void OnLabelEditorAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            _ = box.Focus();
            box.SelectAll();
        }
    }

    /// <summary>编辑框：Enter 提交、Esc 取消。</summary>
    private void OnPortLabelKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: PortMarkerViewModel marker })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                CommitLabel(marker);
                break;

            case Key.Escape:
                e.Handled = true;
                CancelLabel(marker);
                break;

            default:
                break;
        }
    }

    private void OnPortLabelCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PortMarkerViewModel marker })
        {
            CommitLabel(marker);
        }
    }

    private void OnPortLabelClear(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PortMarkerViewModel marker })
        {
            marker.EditText = string.Empty;
            CommitLabel(marker);
        }
    }

    private void OnPortLabelCancel(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PortMarkerViewModel marker })
        {
            CancelLabel(marker);
        }
    }

    /// <summary>
    /// 悬停标记 ⇒ 让**「固定 / 解锁」按钮**淡入；离开 ⇒ 收起。
    /// </summary>
    /// <remarks>
    /// 按钮在**另一层**（`PortAnchors`，位置由面板排布）⇒ 样式选择器够不到"悬停标记 ⇒ 显示按钮"，
    /// 故由视图把悬停态写回标记视图模型（纯呈现状态，不进组合件）。
    /// </remarks>
    private void OnMarkerPointerEntered(object? sender, PointerEventArgs e) => SetMarkerHovered(sender, hovered: true);

    /// <summary>离开标记 ⇒ 收起「固定 / 解锁」按钮（悬停在按钮自己身上时由样式保底，不会闪掉）。</summary>
    private void OnMarkerPointerExited(object? sender, PointerEventArgs e) => SetMarkerHovered(sender, hovered: false);

    private static void SetMarkerHovered(object? sender, bool hovered)
    {
        if (sender is Control { DataContext: PortMarkerViewModel marker })
        {
            marker.IsMarkerHovered = hovered;
        }
    }

    /// <summary>
    /// **固定 / 解锁该口锚点**（G5 · C5-7）：标记带里那个图标按钮。
    /// </summary>
    /// <remarks>
    /// 本类只做"指针 → 意图"的翻译，写入一律走
    /// <see cref="ModuleWindowsViewModel.SetPortAnchorFixed"/>（失败由它回滚并送状态栏）。
    /// </remarks>
    private void OnToggleAnchorFixed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PortMarkerViewModel marker }
            || Module is not { } module
            || Controller is not { } controller)
        {
            return;
        }

        _ = controller.SetPortAnchorFixed(module, marker, !marker.IsAnchorFixed);
    }

    /// <summary>提交标签编辑（结果由控制器送回状态栏；失败则**保持弹窗打开**并就地显示原因）。</summary>
    private void CommitLabel(PortMarkerViewModel marker)
    {
        if (Module is not { } module || Controller is not { } controller)
        {
            // **不许静默**：控制器缺失是内部接线错误，必须就地报出来（静默取消会表现为「改标签没反应」）
            marker.EditError = TextCatalog.Get(typeof(ModuleWindow).Assembly, "moduleWindow.portLabel.internalError");
            marker.IsEditing = true;
            return;
        }

        _ = controller.CommitPortLabel(module, marker);
    }

    private static void CancelLabel(PortMarkerViewModel marker)
    {
        marker.EditText = marker.Label ?? string.Empty;
        marker.EditError = null;
        marker.IsEditing = false;
    }

    private static PortMarkerViewModel? FindMarker(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: PortMarkerViewModel marker })
            {
                return marker;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    // ---------------------------------------------------------------- 按钮簇

    /// <summary>
    /// 内容区按下 → **取消选中**。
    /// </summary>
    /// <remarks>
    /// 点击本应用任何别处（含主窗口）都应取消选中。
    /// 边框带按下 = 选中（由拖动路径负责），内容区按下 = 取消选中 → 二者互补，任何点击都有明确结果。
    /// </remarks>
    private void OnContentPressed(object? sender, PointerPressedEventArgs e) =>
        Controller?.ClearSelection();

    private void OnToggleLock(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.ToggleLock(module.InstanceId);
        }
    }

    /// <summary>
    /// **还原**（用户第十一轮反馈 · 替代原「暂停」按钮）：弹确认框选择还原范围，再交给控制器执行。
    /// </summary>
    /// <remarks>
    /// 暂停对用户没有意义（不想用就关窗或断连），而"还原默认尺寸/配置"是常用动作；
    /// 破坏性操作（覆盖用户拖过的尺寸、丢掉改过的配置）**必须先确认**。
    /// </remarks>
    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (Module is not { } module || Controller is not { } controller)
        {
            return;
        }

        var dialog = new ResetModuleDialog(module.Title);
        var group = AppWindowGroup.Of(this);
        group?.Track(dialog, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(dialog);

        var scope = await dialog.ShowDialog<ModuleResetScope?>(this);
        if (scope is { } selected)
        {
            controller.ApplyReset(module, selected);
        }
    }

    /// <summary>选择模块壁纸：全局图片裁切器（目标比例 = 当前模块窗口比例）。</summary>
    private async void OnChooseWallpaper(object? sender, RoutedEventArgs e)
    {
        if (Module is not { } module || Controller is not { } controller)
        {
            return;
        }

        var aspect = module.Width / Math.Max(1, module.Height);
        var png = await ImagePicker.PickAsync(this, aspect, TextCatalog.Get(typeof(ModuleWindow).Assembly, "moduleWindow.wallpaper.pickTitle"));
        if (png is not null)
        {
            controller.SetWallpaper(module, png);
        }
    }

    /// <summary>清除模块壁纸。</summary>
    private void OnClearWallpaper(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.SetWallpaper(module, null);
        }
    }

    private void OnHide(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.HideWindow(module.InstanceId);
        }
    }

    /// <summary>窗口关闭前把模块视图从本窗口内容区摘下来（见 <see cref="ModuleSurfaceView.Detach"/>）。</summary>
    public void DetachModuleView() => ModuleArea.Detach();

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.CloseModule(module.InstanceId);
        }
    }

    // ---------------------------------------------------------------- 数据绑定辅助

    /// <summary>退订模块属性变化（窗口关闭后必须调用，否则模块会继续持有已关闭窗口）。</summary>
    private void Unhook()
    {
        if (_hooked is not null)
        {
            _hooked.PropertyChanged -= OnModulePropertyChanged;
            _hooked = null;
        }
    }

    /// <summary>装载当前 DataContext（**可重复调用**：先退订上一次，避免重复订阅）。</summary>
    private void Hook()
    {
        Unhook();

        if (Module is not { } module)
        {
            return;
        }

        if (!ReferenceEquals(_hooked, module))
        {
            module.PropertyChanged += OnModulePropertyChanged;
            _hooked = module;
        }

        ApplySelection(module);
        ApplyLock(module);
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ModuleViewViewModel module)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ModuleViewViewModel.IsSelected):
            case nameof(ModuleViewViewModel.IsPrimary):
                ApplySelection(module);
                break;

            case nameof(ModuleViewViewModel.IsLocked):
                ApplySelection(module);
                ApplyLock(module);
                break;

            default:
                break;
        }
    }

    private void ApplySelection(ModuleViewViewModel module)
    {
        SetClass(Frame, "selected", module.IsSelected);
        SetClass(Cluster, "selected", module.IsPrimary);

        foreach (var handle in Handles())
        {
            // 手柄只属于锚点；多选时仍然保留，只调整锚点模块。
            SetClass(handle, "visible", module.IsPrimary && !module.IsLocked);
        }
    }

    /// <summary>锁定图标 = **动作**（未锁 → 显示"上锁"；已锁 → 显示"解锁"）。</summary>
    private void ApplyLock(ModuleViewViewModel module) =>
        LockButton.Icon = module.IsLocked ? ShellIcons.Unlock : ShellIcons.Lock;

    private static void SetClass(Control control, string name, bool on)
    {
        if (on)
        {
            control.Classes.Add(name);
        }
        else
        {
            control.Classes.Remove(name);
        }
    }

    private IEnumerable<Rectangle> Handles() =>
    [
        HandleTopLeft, HandleTopRight, HandleBottomLeft, HandleBottomRight,
        HandleTop, HandleBottom, HandleLeft, HandleRight,
    ];

    private static ResizeHandle ResolveHandle(string? name) => name switch
    {
        "HandleTopLeft" => ResizeHandle.TopLeft,
        "HandleTopRight" => ResizeHandle.TopRight,
        "HandleBottomLeft" => ResizeHandle.BottomLeft,
        "HandleBottomRight" => ResizeHandle.BottomRight,
        "HandleTop" => ResizeHandle.Top,
        "HandleBottom" => ResizeHandle.Bottom,
        "HandleLeft" => ResizeHandle.Left,
        _ => ResizeHandle.Right,
    };

    /// <summary>
    /// 把指针位置换算为**屏幕坐标（DIP）** —— 与模型几何同一坐标系。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>单位口径</b>：模型四数一律 DIP（见 `ModuleLayoutView`），所以本方法**必须返回 DIP**。
    /// `Position`（窗口位置）本身就是 DIP，只有"指针在窗口内 + 乘缩放"那一路会得到物理像素，
    /// 系统光标那一路拿到的也是物理像素 ⇒ 两路都要 ÷ <c>RenderScaling</c>。
    /// </para>
    /// <para>
    /// 【用户第十二轮第二轮反馈 · `architecture/15` §6.4】**优先取系统光标**：
    /// 原来只用 <c>Position + 局部坐标 × 缩放</c>，两个来源不是同一次采样（窗口位置是我们刚设的、
    /// 局部坐标来自系统消息生成时刻），拖动中窗口每帧都在动（吸附命中还会跳几像素）⇒
    /// 指针读数带上"等于窗口位移量"的误差 ⇒ 回授振荡。系统光标与本窗口自己的位置无关，能断掉这条回授。
    /// 非 Windows / 非鼠标（触摸、笔）或调用失败时回退到原换算。
    /// </para>
    /// </remarks>
    private (double X, double Y) ScreenPoint(PointerEventArgs e)
    {
        var local = e.GetPosition(this);

        if (e.Pointer.Type == PointerType.Mouse && NativeCursor.TryGetScreenPosition(out var systemX, out var systemY))
        {
            // 系统光标是**物理像素**；控制器再按各窗口自己的缩放换算成 DIP。
            return (systemX, systemY);
        }

        // 回退值：`PointToScreen` 给的是**物理像素**（与 `Position` 同口径）。
        var derived = Avalonia.VisualExtensions.PointToScreen(this, local);
        return (derived.X, derived.Y);
    }

}
