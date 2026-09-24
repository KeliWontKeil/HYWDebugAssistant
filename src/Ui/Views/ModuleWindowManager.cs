using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **模块窗口管理器**：把模块集合映射为一批无边框顶层窗口，并把数值几何落到窗口上。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>职责边界：<see cref="ModuleWindowsViewModel"/> 只算几何；本类只开窗/关窗/设位置与尺寸
///     （**同一份逻辑可以脱离窗口单测**）；</item>
///   <item><b>坐标换算</b>：几何存的是屏幕物理像素，窗口的 <c>Width/Height</c> 是 DIP（按各自缩放换算）；
///     位置是 <see cref="PixelPoint"/> 直接对应像素；</item>
///   <item>屏幕工作区与外壳窗口矩形回写给控制器，作为吸附参照（"不跑出屏幕 / 贴住外壳"）。</item>
/// </list>
/// </remarks>
public sealed class ModuleWindowManager : IDisposable
{
    private readonly ModuleWindowsViewModel _controller;
    private readonly Window _shell;
    private readonly Dictionary<string, ModuleWindow> _windows = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>创建窗口管理器。</summary>
    /// <param name="controller">模块窗口集控制器。</param>
    /// <param name="shell">框架外壳窗口（拼图起点，可被贴靠 —— O-6）。</param>
    /// <param name="windowGroup">应用窗口组（层级统一：ADR-0036 D5）；为空则以外壳创建。</param>
    public ModuleWindowManager(ModuleWindowsViewModel controller, Window shell, AppWindowGroup? windowGroup = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(shell);

        _controller = controller;
        _shell = shell;
        WindowGroup = windowGroup ?? new AppWindowGroup(shell);

        _controller.GeometryChanged += OnGeometryChanged;
        _controller.VisibilityChanged += OnVisibilityChanged;
        _controller.Modules.CollectionChanged += (_, _) => SyncAll();

        _shell.PositionChanged += (_, _) => PublishShellRect();
        _shell.SizeChanged += (_, _) => PublishShellRect();

        _controller.Scale = shell.Screens.Primary?.Scaling ?? 1.0;
        PublishWorkArea();
        PublishShellRect();
    }

    /// <summary>应用窗口组（外壳 + 模块窗口 + 弹窗的**层级统一**由它负责）。</summary>
    public AppWindowGroup WindowGroup { get; }

    /// <summary>新窗口创建完成（供宿主把应用内快捷键挂上去）。</summary>
    public event EventHandler<Window>? HotkeyAttached;

    /// <summary>按当前模块集合开窗/关窗（启动与模块增删时调用）。</summary>
    public void SyncAll()
    {
        foreach (var module in _controller.Modules)
        {
            if (module.IsViewVisible)
            {
                EnsureWindow(module);
            }
            else if (_windows.TryGetValue(module.InstanceId, out var hidden))
            {
                hidden.Close();
                _ = _windows.Remove(module.InstanceId);
            }
        }

        foreach (var staleId in _windows.Keys.Where(id => _controller.Modules.All(m => !string.Equals(m.InstanceId, id, StringComparison.Ordinal))).ToList())
        {
            if (_windows.Remove(staleId, out var window))
            {
                window.Close();
            }
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
        _controller.GeometryChanged -= OnGeometryChanged;
        _controller.VisibilityChanged -= OnVisibilityChanged;

        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
        WindowGroup.Dispose();
    }

    // ---------------------------------------------------------------- 内部

    private void OnGeometryChanged(object? sender, ModuleWindowGeometryChange e)
    {
        if (_windows.TryGetValue(e.InstanceId, out var window))
        {
            ApplyGeometry(window, e);
        }
    }

    private void OnVisibilityChanged(object? sender, ModuleWindowVisibilityChange e)
    {
        if (e.IsVisible)
        {
            if (_controller.Modules.FirstOrDefault(m => string.Equals(m.InstanceId, e.InstanceId, StringComparison.Ordinal)) is { } module)
            {
                EnsureWindow(module);
            }

            return;
        }

        if (_windows.Remove(e.InstanceId, out var window))
        {
            window.Close();
        }
    }

    private void EnsureWindow(ModuleViewViewModel module)
    {
        if (_windows.TryGetValue(module.InstanceId, out var existing))
        {
            if (!existing.IsVisible)
            {
                existing.Show(_shell);
            }

            return;
        }

        var window = new ModuleWindow
        {
            DataContext = module,
            Controller = _controller,
            Title = module.Title,
        };

        _windows[module.InstanceId] = window;

        ApplyGeometry(
            window,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height));

        // 窗口位置的**实际**变化（用户用系统方式移动、或高 DPI 舍入）回写控制器，保持模型与屏幕一致
        window.PositionChanged += (_, _) => OnWindowMoved(module, window);

        // **从属于外壳**：外壳永在模块窗口之下（点外壳不再把模块压下去）；最小化/恢复随外壳
        window.Show(_shell);

        // 纳入应用窗口组：层级统一（激活任一窗口 → 其余一并抬升；"全体置顶"一起生效）
        WindowGroup.Track(window);
        HotkeyAttached?.Invoke(this, window);
    }

    private static void OnWindowMoved(ModuleViewViewModel module, ModuleWindow window)
    {
        if (module.IsInteracting)
        {
            return;   // 拖动/缩放过程由指针主导，避免与模型互相覆盖
        }

        var x = window.Position.X;
        var y = window.Position.Y;
        if (Math.Abs(module.X - x) < 0.5 && Math.Abs(module.Y - y) < 0.5)
        {
            return;
        }

        module.X = x;
        module.Y = y;
    }

    private static void ApplyGeometry(ModuleWindow window, ModuleWindowGeometryChange geometry)
    {
        var scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;

        var position = new PixelPoint((int)Math.Round(geometry.X), (int)Math.Round(geometry.Y));
        var size = new Size(
            Math.Max(ModuleViewViewModel.MinWidth, geometry.Width / scaling),
            Math.Max(ModuleViewViewModel.MinHeight, geometry.Height / scaling));

        if (window.Position != position)
        {
            window.Position = position;
        }

        if (Math.Abs(window.Width - size.Width) > 0.5)
        {
            window.Width = size.Width;
        }

        if (Math.Abs(window.Height - size.Height) > 0.5)
        {
            window.Height = size.Height;
        }
    }

    private void PublishWorkArea()
    {
        var screen = _shell.Screens.Primary;
        if (screen is null)
        {
            _controller.WorkArea = null;
            return;
        }

        var area = screen.WorkingArea;
        _controller.WorkArea = new CanvasRect(area.X, area.Y, area.Width, area.Height);
    }

    private void PublishShellRect()
    {
        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;
        _controller.ShellRect = new CanvasRect(
            _shell.Position.X,
            _shell.Position.Y,
            _shell.Bounds.Width * scaling,
            _shell.Bounds.Height * scaling);
    }
}
