using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Ui.Diagnostics;
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
    private ModuleViewViewModel? _hooked;

    /// <summary>【临时诊断】上一帧指针位置（算指针位移用）。</summary>
    private double _lastPointerX;
    private double _lastPointerY;

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
    }

    /// <summary>模块窗口集控制器（由窗口管理器在显示前注入）。</summary>
    public ModuleWindowsViewModel? Controller { get; set; }

    private ModuleViewViewModel? Module => DataContext as ModuleViewViewModel;

    // ---------------------------------------------------------------- 交互入口

    /// <summary>边框带按下：拖动（Ctrl = 独立拖动，不带动拼接组）。</summary>
    private void OnBandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Module is not { IsLocked: false } module || Controller is not { } controller)
        {
            return;
        }

        var (x, y) = ScreenPoint(e);
        var independent = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (controller.BeginDrag(module.InstanceId, x, y, independent))
        {
            _dragging = true;
            _lastPointerX = x;
            _lastPointerY = y;
            DragDiagnostics.Reset();
            e.Pointer.Capture(sender as IInputElement ?? this);
            e.Handled = true;
        }
    }

    /// <summary>缩放手柄按下：按手柄方向缩放。</summary>
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Module is not { IsLocked: false } module || Controller is not { } controller || sender is not Control handle)
        {
            return;
        }

        if (controller.BeginResize(module.InstanceId, ResolveHandle(handle.Name)))
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
            controller.ResizeTo(x, y);
            e.Handled = true;
            return;
        }

        if (_dragging)
        {
            var before = Module is { } m0 ? (X: m0.X, Y: m0.Y, W: m0.Width, H: m0.Height) : default;

            // 【用户第十七轮反馈】拖动**期间**按下 Ctrl = **立刻拆分**（只拖自己、其余成员回原位），
            // 不再要求"必须拖动前按住"。幂等：已独立拖动时不做事；松手后不会重新合组。
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                controller.DetachDrag();
            }

            // 按住 Alt = 本次移动临时停用吸附（Q5-1）
            controller.DragTo(x, y, snapEnabled: e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? false : null);

            // 【临时诊断】记录该帧的指针与几何事实（拖动结束时整段落盘）
            if (Module is { } dragged)
            {
                var deltaX = x - _lastPointerX;
                var deltaY = y - _lastPointerY;
                var derived = LastWindowDerivedPoint;
                var scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;

                DragDiagnostics.Record(DragDiagnostics.Format(
                    cursor: (X: x, Y: y),
                    derived: derived,
                    pointerDelta: (X: deltaX, Y: deltaY),
                    modelBefore: before,
                    module: (X: dragged.X, Y: dragged.Y, W: dragged.Width, H: dragged.Height),
                    windowPosition: (X: Position.X, Y: Position.Y),
                    windowSize: (W: ClientSize.Width * scaling, H: ClientSize.Height * scaling),
                    sticky: $"group={controller.DragGroupSize}"));

                var errorX = Math.Abs(derived.X - x);
                var errorY = Math.Abs(derived.Y - y);
                var movedX = dragged.X - before.X;
                var movedY = dragged.Y - before.Y;

                // 异常帧就在日志里打标记（不打断拖动，也不再只写一次）
                if (errorX > 1.5 || errorY > 1.5
                    || (movedX * deltaX < 0 && Math.Abs(movedX) > 2)
                    || (movedY * deltaY < 0 && Math.Abs(movedY) > 2))
                {
                    DragDiagnostics.Record($"!!异常帧 指针换算差=({errorX:0.#},{errorY:0.#}) 模块位移=({movedX:0.#},{movedY:0.#})");
                }
            }

            _lastPointerX = x;
            _lastPointerY = y;
            e.Handled = true;
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

        if (_resizing)
        {
            _resizing = false;
            controller.EndResize();
            e.Pointer.Capture(null);
        }

        if (_dragging)
        {
            _dragging = false;
            var groupSize = controller.DragGroupSize;
            var instanceId = Module?.InstanceId ?? "-";
            controller.EndDrag();
            e.Pointer.Capture(null);

            // 【临时诊断】整段拖动落盘（每次拖动都会追加一段）
            if (DragDiagnostics.Flush($"实例={instanceId} 组={groupSize}") is { } path)
            {
                controller.RaiseNotice($"拖动诊断日志已写入：{path}", isError: false);
            }
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
                    $"{marker.PortName}：锚点已固定，不可拖动（用「固定 / 解锁端口锚点」快捷键解锁）",
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

        var edge = toLeft <= toRight && toLeft <= toTop && toLeft <= toBottom
            ? PortEdge.Left
            : toRight <= toTop && toRight <= toBottom
                ? PortEdge.Right
                : toTop <= toBottom
                    ? PortEdge.Top
                    : PortEdge.Bottom;

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
            marker.EditError = "内部错误：未连接到运行时，标签未提交";
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

        var scope = await new ResetModuleDialog(module.Title).ShowDialog<ModuleResetScope?>(this);
        if (scope is { } selected)
        {
            controller.ApplyReset(module, selected);
        }
    }

    private void OnHide(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.HideWindow(module.InstanceId);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.CloseModule(module.InstanceId);
        }
    }

    // ---------------------------------------------------------------- 数据绑定辅助

    /// <summary>装载当前 DataContext（**可重复调用**：先退订上一次，避免重复订阅）。</summary>
    private void Hook()
    {
        if (!ReferenceEquals(_hooked, Module) && _hooked is not null)
        {
            _hooked.PropertyChanged -= OnModulePropertyChanged;
            _hooked = null;
        }

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
                ApplySelection(module);
                break;

            case nameof(ModuleViewViewModel.IsLocked):
                ApplyLock(module);
                break;

            default:
                break;
        }
    }

    private void ApplySelection(ModuleViewViewModel module)
    {
        SetClass(Frame, "selected", module.IsSelected);
        SetClass(Cluster, "selected", module.IsSelected);

        foreach (var handle in Handles())
        {
            SetClass(handle, "visible", module.IsSelected);
        }
    }

    /// <summary>锁定图标 = **动作**（未锁 → 显示"上锁"；已锁 → 显示"解锁"）。</summary>
    private void ApplyLock(ModuleViewViewModel module) =>
        SetGlyph(LockIcon, module.IsLocked ? "GlyphUnlock" : "GlyphLock");

    /// <summary>从主题 token 取**图标字形**（系统图标字体；改图标只改 Tokens）。</summary>
    private void SetGlyph(TextBlock icon, string resourceKey)
    {
        if (this.TryFindResource(resourceKey, out var value) && value is string glyph)
        {
            icon.Text = glyph;
        }
    }

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
    /// 把指针位置换算为**屏幕物理像素**。
    /// </summary>
    /// <remarks>
    /// 【用户第十二轮第二轮反馈 · `architecture/15` §6.4】**优先取系统光标**：
    /// 原来只用 <c>Position + 局部坐标 × 缩放</c>，两个来源不是同一次采样（窗口位置是我们刚设的、
    /// 局部坐标来自系统消息生成时刻），拖动中窗口每帧都在动（吸附命中还会跳几像素）⇒
    /// 指针读数带上"等于窗口位移量"的误差 ⇒ 回授振荡。系统光标与本窗口自己的位置无关，能断掉这条回授。
    /// 非 Windows / 非鼠标（触摸、笔）或调用失败时回退到原换算。
    /// </remarks>
    private (double X, double Y) ScreenPoint(PointerEventArgs e)
    {
        var local = e.GetPosition(this);
        var scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;
        var derived = (X: Position.X + (local.X * scaling), Y: Position.Y + (local.Y * scaling));

        // 诊断用：把"窗口换算值"留存，便于与系统光标比对（见 DragDiagnostics）
        LastWindowDerivedPoint = derived;

        if (e.Pointer.Type == PointerType.Mouse && NativeCursor.TryGetScreenPosition(out var systemX, out var systemY))
        {
            return (systemX, systemY);
        }

        return derived;
    }

    /// <summary>【临时诊断】最近一次"窗口换算"得到的指针位置（拖动诊断用，见 `DragDiagnostics`）。</summary>
    internal (double X, double Y) LastWindowDerivedPoint { get; private set; }
}
