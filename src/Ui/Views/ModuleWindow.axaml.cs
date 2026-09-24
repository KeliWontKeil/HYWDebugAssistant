using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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
            // 按住 Alt = 本次移动临时停用吸附（Q5-1）
            controller.DragTo(x, y, snapEnabled: e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? false : null);
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
            controller.EndDrag();
            e.Pointer.Capture(null);
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

    private void OnToggleEnabled(object? sender, RoutedEventArgs e)
    {
        if (Module is { } module && Controller is { } controller)
        {
            controller.ToggleEnabled(module.InstanceId);
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
        ApplyRunState(module);
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

            case nameof(ModuleViewViewModel.StateText):
                ApplyRunState(module);
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
        SetIcon(LockIcon, module.IsLocked ? "IconUnlock" : "IconLock");

    /// <summary>运行图标 = **动作**（运行中 → 显示"停止"；已停用 → 显示"播放"）。</summary>
    private void ApplyRunState(ModuleViewViewModel module) =>
        SetIcon(RunStopIcon, module.IsEnabled ? "IconStop" : "IconPlay");

    /// <summary>从主题 token 取图标几何（ADR-0039 D7：换成图标包时只改 token）。</summary>
    private void SetIcon(PathIcon icon, string resourceKey)
    {
        // 注意：本工程内 `Geometry` 是**命名空间**（Ui.Geometry 的吸附/画布几何），故此处写全名
        if (this.TryFindResource(resourceKey, out var value) && value is Avalonia.Media.Geometry geometry)
        {
            icon.Data = geometry;
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

    /// <summary>把指针位置换算为**屏幕物理像素**（拖动中窗口随指针移动也不会回授）。</summary>
    private (double X, double Y) ScreenPoint(PointerEventArgs e)
    {
        var local = e.GetPosition(this);
        var scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;

        return (Position.X + (local.X * scaling), Position.Y + (local.Y * scaling));
    }
}
