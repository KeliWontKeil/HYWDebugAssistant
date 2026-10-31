using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **应用窗口层**：外壳固定底带；模块与功能面板同属浮动带（按最近激活排序）；模态弹窗固定顶带。
/// </summary>
public enum AppWindowLayer
{
    /// <summary>外壳（固定底带）。</summary>
    Shell = 0,

    /// <summary>模块窗口（浮动带）。</summary>
    Module = 1,

    /// <summary>模型化功能面板（清单 / Issue / 设置 / 拓扑；浮动带）。</summary>
    Panel = 2,

    /// <summary>模态弹窗（固定顶带；打开时禁用低带）。</summary>
    Modal = 3,
}

/// <summary>
/// **应用窗口组**：把一个应用的全部顶层窗口当作**一个整体**来管理层级。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>应用内</b>：模块窗口以**外壳为 Owner**（外壳永在模块窗口之下；最小化/恢复随外壳），
///     弹窗同样以外壳为 Owner；模块与功能面板同属**浮动带**，最近激活者在上；确认/消息这类模态弹窗
///     在固定顶带，打开时禁用低带窗口；</item>
///   <item><b>应用整体（相对其他应用）只有两种统一状态</b>：
///     <b>普通</b> —— 任一本应用窗口被激活时，把低带窗口（含外壳/浮动带）**一并抬到最前**
///     （不抢焦点、保持内部次序）；<b>全体置顶</b> —— 本组所有窗口一起 <see cref="Window.Topmost"/>
///     （含之后新开的窗口），**不提供"单窗口置顶"**；</item>
///   <item><b>平台代价</b>：普通状态下的"低带抬升"需要 <c>SetWindowPos(HWND_TOP, SWP_NOACTIVATE)</c>
///     （Avalonia 未提供"抬升但不激活"的 API）；非 Windows 平台自动降级为不做组抬升。</item>
/// </list>
/// </remarks>
public sealed class AppWindowGroup : IDisposable
{
    private readonly List<Window> _windows = [];
    private readonly Dictionary<Window, AppWindowLayer> _layers = [];
    private readonly List<Window> _floatingOrder = [];
    private static readonly ConditionalWeakTable<Window, AppWindowGroup> Registry = new();
    private readonly Window _shell;
    private int _modalDepth;
    private bool _disposed;

    /// <summary>创建窗口组（外壳窗口必须在首位）。</summary>
    /// <param name="shell">外壳窗口。</param>
    public AppWindowGroup(Window shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        Track(shell, AppWindowLayer.Shell);
    }

    /// <summary>本组是否处于"全体置顶"状态。</summary>
    public bool Topmost { get; private set; }

    /// <summary>当前纳入本组的窗口（只读快照；按纳入顺序）。</summary>
    public IReadOnlyList<Window> Windows => [.. _windows];

    /// <summary>浮动带的内部顺序（底 → 顶；调试/测试用，调用方不应依赖它做业务判断）。</summary>
    public IReadOnlyList<Window> FloatingOrder => [.. _floatingOrder];

    /// <summary>
    /// **抬起一个模块组**：整组移到浮动带末尾（保持传入顺序，最后一个在最上），逐个抬前但不抢焦点。
    /// </summary>
    /// <param name="group">组内窗口（外壳/模态会被过滤掉；顺序 = 抬升顺序）。</param>
    public void RaiseFloatingGroup(IReadOnlyList<Window> group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var ordered = new List<Window>(group.Count);
        var seen = new HashSet<Window>();

        foreach (var window in group)
        {
            if (window is null
                || !_windows.Contains(window)
                || Band(LayerOf(window)) != 1
                || !seen.Add(window))
            {
                continue;
            }

            ordered.Add(window);
        }

        if (ordered.Count == 0)
        {
            return;
        }

        // 整组连续地放到浮动带末尾：组内相对顺序由调用方给出，组外保持原顺序。
        foreach (var window in ordered)
        {
            _ = _floatingOrder.Remove(window);
        }

        _floatingOrder.AddRange(ordered);

        foreach (var window in ordered)
        {
            WindowZOrder.RaiseWithoutActivating(window);
        }

        // 浮动组抬升不能把模块压到模态之上；模态永远最后抬。
        RaiseModals();
    }

    /// <summary>新窗口纳入本组后触发（便于宿主给所有窗口挂统一能力，如快捷键）。</summary>
    public event EventHandler<Window>? WindowTracked;

    /// <summary>取某个窗口所属的窗口组（未纳入 ⇒ <see langword="null"/>）。</summary>
    /// <param name="window">窗口。</param>
    public static AppWindowGroup? Of(Window? window) =>
        window is not null && Registry.TryGetValue(window, out var group) ? group : null;

    /// <summary>把窗口纳入本组（模块窗口与弹窗在创建后调用）。</summary>
    /// <param name="window">窗口。</param>
    /// <param name="layer">窗口层；默认模块窗口。</param>
    public void Track(Window window, AppWindowLayer layer = AppWindowLayer.Module)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windows.Contains(window))
        {
            if (_layers.TryGetValue(window, out var existing) && existing != layer)
            {
                _layers[window] = layer;
                SyncFloatingOrder(window, layer);
            }

            return;
        }

        _windows.Add(window);
        _layers[window] = layer;
        SyncFloatingOrder(window, layer);
        window.Activated += OnAnyWindowActivated;
        window.Closed += OnWindowClosed;
        window.Topmost = Topmost;

        _ = Registry.Remove(window);
        Registry.Add(window, this);
        WindowTracked?.Invoke(this, window);
    }

    /// <summary>取窗口的层（未登记 ⇒ <see cref="AppWindowLayer.Shell"/>，调用方不应依赖该兜底）。</summary>
    /// <param name="window">窗口。</param>
    public AppWindowLayer LayerOf(Window window) =>
        _layers.TryGetValue(window, out var layer) ? layer : AppWindowLayer.Shell;

    /// <summary>切换"全体置顶"（本组全部窗口一起，含弹窗）。</summary>
    /// <param name="topmost">是否置顶。</param>
    public void SetTopmost(bool topmost)
    {
        Topmost = topmost;

        foreach (var window in _windows)
        {
            window.Topmost = topmost;
        }

        // 本应用处于普通状态时，模态仍必须是本应用最高层；Topmost 只在本应用内保证。
        if (_modalDepth > 0)
        {
            foreach (var window in _windows.Where(window => Band(LayerOf(window)) == 2))
            {
                window.Topmost = true;
            }

            RaiseModals();
        }
    }

    /// <summary>把本组全部窗口抬到最前（不改变焦点；低带在前，模态最后）。</summary>
    public void RaiseAll()
    {
        foreach (var window in RaiseOrder())
        {
            WindowZOrder.RaiseWithoutActivating(window);
        }
    }

    /// <summary>
    /// **进入模态**：禁用除模态外的所有本组窗口，返回恢复作用域（支持嵌套）。
    /// </summary>
    /// <param name="modal">模态窗口（会先纳入本组）。</param>
    /// <returns>释放后恢复低带窗口的启用状态。</returns>
    public IDisposable BeginModal(Window modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        Track(modal, AppWindowLayer.Modal);

        var disabled = new List<Window>();

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window, modal) || !window.IsEnabled)
            {
                continue;
            }

            window.IsEnabled = false;
            disabled.Add(window);
        }

        _modalDepth++;
        var previousTopmost = modal.Topmost;
        modal.Topmost = true;
        RaiseModals();

        return new ModalScope(this, modal, previousTopmost, disabled);
    }

    private void SyncFloatingOrder(Window window, AppWindowLayer layer)
    {
        if (Band(layer) != 1)
        {
            _ = _floatingOrder.Remove(window);
            return;
        }

        if (!_floatingOrder.Contains(window))
        {
            _floatingOrder.Add(window);
        }
    }

    private void MoveFloatingToEnd(Window window)
    {
        if (_floatingOrder.Remove(window))
        {
            _floatingOrder.Add(window);
        }
    }

    /// <summary>
    /// 任一本应用窗口被激活 → 把**低于它的带**抬前：外壳固定底、浮动带按最近激活、模态固定顶。
    /// </summary>
    private void OnAnyWindowActivated(object? sender, EventArgs e)
    {
        if (_disposed || sender is not Window activated)
        {
            return;
        }

        var layer = LayerOf(activated);
        if (Band(layer) == 1)
        {
            MoveFloatingToEnd(activated);
        }

        var activeBand = Band(layer);
        foreach (var window in RaiseOrder().Where(window => Band(LayerOf(window)) < activeBand))
        {
            WindowZOrder.RaiseWithoutActivating(window);
        }

        RaiseModals();
    }

    /// <summary>把本组全部模态窗口按进入顺序抬到最上（不抢焦点）。</summary>
    private void RaiseModals()
    {
        foreach (var window in _windows.Where(window => Band(LayerOf(window)) == 2))
        {
            WindowZOrder.RaiseWithoutActivating(window);
        }
    }

    private IEnumerable<Window> RaiseOrder()
    {
        foreach (var window in _windows.Where(window => Band(LayerOf(window)) == 0))
        {
            yield return window;
        }

        foreach (var window in _floatingOrder.Where(window => _windows.Contains(window)))
        {
            yield return window;
        }

        foreach (var window in _windows.Where(window => Band(LayerOf(window)) == 2))
        {
            yield return window;
        }

        // 兜底：任何未归入三带的窗口（理论上不存在）仍被抬起，避免静默漏掉。
        foreach (var window in _windows)
        {
            var band = Band(LayerOf(window));
            if (band is not (0 or 1 or 2))
            {
                yield return window;
            }
        }
    }

    private static int Band(AppWindowLayer layer) => layer switch
    {
        AppWindowLayer.Shell => 0,
        AppWindowLayer.Modal => 2,
        _ => 1,
    };

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Activated -= OnAnyWindowActivated;
            window.Closed -= OnWindowClosed;
            _ = _windows.Remove(window);
            _ = _layers.Remove(window);
            _ = _floatingOrder.Remove(window);
            _ = Registry.Remove(window);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var window in _windows)
        {
            window.Activated -= OnAnyWindowActivated;
            window.Closed -= OnWindowClosed;
            _ = Registry.Remove(window);
        }

        _windows.Clear();
        _layers.Clear();
        _floatingOrder.Clear();
        _ = _shell;
    }

    /// <summary>模态作用域：恢复"进入时确实由本作用域禁用"的窗口与模态自身的 Topmost。</summary>
    private sealed class ModalScope(
        AppWindowGroup owner,
        Window modal,
        bool previousTopmost,
        List<Window> disabled) : IDisposable
    {
        private readonly AppWindowGroup _owner = owner;
        private readonly Window _modal = modal;
        private readonly bool _previousTopmost = previousTopmost;
        private readonly List<Window> _disabled = disabled;
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var window in _disabled)
            {
                window.IsEnabled = true;
            }

            _disabled.Clear();
            _modal.Topmost = _previousTopmost;
            _owner._modalDepth = Math.Max(0, _owner._modalDepth - 1);
            _owner.RaiseModals();
        }
    }
}

/// <summary>窗口 z 序的**平台适配**（仅 Windows 生效；其他平台为无操作）。</summary>
internal static class WindowZOrder
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>把窗口抬到最前，但**不激活**它（不抢焦点）。</summary>
    /// <param name="window">目标窗口。</param>
    internal static void RaiseWithoutActivating(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
