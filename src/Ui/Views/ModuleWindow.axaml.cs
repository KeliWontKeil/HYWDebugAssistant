using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Ui.Diagnostics;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **拼图式模块窗口**（ADR-0034 §2）：无边框、自定义拖动/缩放、锁定与按钮簇。
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
///     此前 <c>DataContext</c> 先于 <c>Controller</c> 赋值，交接拿到的永远是 <c>null</c>，
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

        if (_resizing)
        {
            controller.ResizeTo(x, y);
            e.Handled = true;
            return;
        }

        if (_dragging)
        {
            var before = Module is { } m0 ? (X: m0.X, Y: m0.Y, W: m0.Width, H: m0.Height) : default;

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
    /// 编辑框**挂到可视树之后**再取焦点：此前同步 <c>Focus()</c> 时 DataTemplate 还没实例化，
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

    /// <summary>提交标签编辑（结果由控制器送回状态栏；失败则**保持弹窗打开**并就地显示原因）。</summary>
    private void CommitLabel(PortMarkerViewModel marker)
    {
        if (Module is not { } module || Controller is not { } controller)
        {
            // **不许静默**：控制器缺失是内部接线错误，就地报出来（此前这里静默取消 → "改标签没反应"）
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
    /// 内容区按下 → **取消选中**【M1-16 / ADR-0039 D1】。
    /// </summary>
    /// <remarks>
    /// 用户第六轮反馈："当鼠标点击别处时（比如点击主窗口）取消选中，现在无法取消选中模块"。
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
