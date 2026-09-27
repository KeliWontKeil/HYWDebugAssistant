using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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

        // 外壳移动 → ① 刷新吸附参照矩形 ② **平移与外壳拼成一体的那一组**（用户第十二轮第四轮反馈）
        _shell.PositionChanged += (_, _) => OnShellMoved();
        _shell.SizeChanged += (_, _) =>
        {
            PublishShellRect();
            _ = _controller.CommitSceneTranslation();
        };

        // 拖动组带动外壳（模块单侧吸附在外壳上时，拖模块 = 整体一起动）
        _controller.ShellTranslationRequested += OnShellTranslationRequested;

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
        _controller.ShellTranslationRequested -= OnShellTranslationRequested;

        // 退出前把"整体平移"造成的位移合并落盘（拖动模块带走外壳时会产生）
        _shellDragEndTimer?.Stop();
        _shellDragEndTimer = null;
        _followCommitTimer?.Stop();
        _followCommitTimer = null;
        _ = _controller.CommitSceneTranslation();

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
            // 已有布局（用户拖过/缩过）→ 按模型应用；未摆放 → 只定位置（尺寸用 XAML 默认）
            ApplyGeometry(window, e, hasLayout: true);
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
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height),
            hasLayout: module.HasLayout || module.HasPreferredSize);

        // 尺寸既非来自布局、也非来自模块声明 → 用 XAML 默认值；打开后把**实际尺寸**写回模型（使布局可持久化）
        if (!module.HasLayout)
        {
            window.Opened += (_, _) => PublishActualSize(window, module);
        }

        // 窗口位置的**实际**变化（用户用系统方式移动、或高 DPI 舍入）回写控制器，保持模型与屏幕一致
        window.PositionChanged += (_, _) => OnWindowMoved(module, window);

        // **从属于外壳**：外壳永在模块窗口之下（点外壳不再把模块压下去）；最小化/恢复随外壳
        window.Show(_shell);

        // 纳入应用窗口组：层级统一（激活任一窗口 → 其余一并抬升；"全体置顶"一起生效）
        WindowGroup.Track(window);
        HotkeyAttached?.Invoke(this, window);
    }

    /// <summary>
    /// 把窗口的**实际尺寸**写回模型（未摆放过的模块用它初始化布局，之后随组合件保存）。
    /// </summary>
    /// <remarks>尺寸来源 = XAML 默认值；这里只做"读回事实"，不做任何计算。</remarks>
    private void PublishActualSize(ModuleWindow window, ModuleViewViewModel module)
    {
        var scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;

        module.Width = ScreenPixels.Round(window.ClientSize.Width * scaling);
        module.Height = ScreenPixels.Round(window.ClientSize.Height * scaling);
        module.HasLayout = true;

        _controller.CommitInitialLayout(module);
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

    /// <summary>
    /// 把模型几何应用到窗口；**未摆放过的模块只定位置、尺寸用 XAML 默认值**。
    /// </summary>
    /// <remarks>
    /// 【用户第九轮反馈 · UI 开发方式】**窗口尺寸的默认值归 XAML**（`ModuleWindow.axaml` 的 `Width`/`Height`）：
    /// · `layout is null`（首次加入现场）→ **只设 Position**，窗口按 XAML 默认尺寸打开，
    ///   随后由 <see cref="OnWindowMoved"/> / 尺寸回写把**实际尺寸**写进模型并持久化；
    /// · 已有布局（用户拖过/缩过，或从组合件恢复）→ 按布局应用（那是"用户的布局"）。
    /// 这样"信息量"永远不会改变外层容器尺寸，内容排不下由内容区 ScrollViewer 处理。
    /// </remarks>
    private static void ApplyGeometry(ModuleWindow window, ModuleWindowGeometryChange geometry, bool hasLayout)
    {
        var scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;

        // 模型几何已是**整数物理像素**（唯一量化点：`ScreenPixels`），这里只做 px → DIP 的精确换算，
        // 位置直接用整数（不再各自 Math.Round —— 口径不一致就等于没有口径，用户第十二轮反馈）。
        var position = new PixelPoint(ScreenPixels.ToPixel(geometry.X), ScreenPixels.ToPixel(geometry.Y));
        if (window.Position != position)
        {
            window.Position = position;
        }

        if (!hasLayout)
        {
            return;   // 尺寸交给 XAML 默认值
        }

        var size = new Size(
            Math.Max(ModuleViewViewModel.MinWidth, ScreenPixels.Round(geometry.Width) / scaling),
            Math.Max(ModuleViewViewModel.MinHeight, ScreenPixels.Round(geometry.Height) / scaling));

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

    /// <summary>
    /// 外壳**位置**变化：平移"**本次拖动开始时**已与外壳拼在一起"的那一组（**拖动过程中不再吸收新成员**）。
    /// </summary>
    /// <remarks>
    /// 【用户第三十四轮裁定："**单独拖动主窗口时不进行吸附行为**，更符合直觉"】
    /// 旧实现**逐帧**重算"谁与外壳接触" ⇒ 拖着外壳**经过**某个模块时，那一帧它被判成"已拼上"并跟着走
    /// ⇒ 现象正是用户说的"**经过模块时导致模块错位的粘在主窗口上，且错位粘连（没有产生吸附行为却拼上去了）**"。
    /// ⇒ 现在：分组在**一次拖动开始时冻结**（<see cref="ModuleWindowsViewModel.TranslateShellGroup"/> 内部实现），
    /// 停下 ~250ms 视为这次拖动结束（<see cref="ModuleWindowsViewModel.EndShellDragGesture"/>）——
    /// **已经拼在一起的模块照旧一起走，但拖动过程中绝不"吸收"新模块**。
    /// </remarks>
    private void OnShellMoved()
    {
        // **移动前**的参照矩形：分组判定必须用它（模块此刻还在旧位置上；与 implementation/02 P33 同类）
        var before = _controller.ShellRect;

        PublishShellRect();
        var after = _controller.ShellRect;

        if (before is not { IsValid: true } previous || after is not { } current)
        {
            return;
        }

        // 拖动组内的模块会带着外壳走，那次移动也会触发本方法 ⇒ 此时不能再平移一次（否则双重位移）
        if (_controller.IsInteracting)
        {
            return;
        }

        _controller.TranslateShellGroup(current.X - previous.X, current.Y - previous.Y, previous);

        // 静默 250ms ⇒ 这次拖动结束：解除分组冻结（下次拖动重新取组）
        _shellDragEndTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _shellDragEndTimer.Stop();
        _shellDragEndTimer.Tick -= OnShellDragEndTick;
        _shellDragEndTimer.Tick += OnShellDragEndTick;
        _shellDragEndTimer.Start();

        // 拖动中不逐帧落盘：停下后合并提交（逐帧提交会触发建网重算而卡顿）
        _followCommitTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _followCommitTimer.Stop();
        _followCommitTimer.Tick -= OnFollowCommitTick;
        _followCommitTimer.Tick += OnFollowCommitTick;
        _followCommitTimer.Start();
    }

    /// <summary>拖动结束（静默到期）：解除分组冻结。</summary>
    private void OnShellDragEndTick(object? sender, EventArgs e)
    {
        _shellDragEndTimer?.Stop();
        _controller.EndShellDragGesture();
    }

    /// <summary>防抖到期：把整体平移合并落盘。</summary>
    private void OnFollowCommitTick(object? sender, EventArgs e)
    {
        _followCommitTimer?.Stop();
        _ = _controller.CommitSceneTranslation();
    }

    /// <summary>"这次拖动结束"的静默计时器。</summary>
    private DispatcherTimer? _shellDragEndTimer;

    /// <summary>跟随落盘的防抖计时器。</summary>
    private DispatcherTimer? _followCommitTimer;

    /// <summary>把"拖动组带动外壳"的请求落到真实窗口上（见 <see cref="ModuleWindowsViewModel.ShellTranslationRequested"/>）。</summary>
    private void OnShellTranslationRequested(object? sender, (double Dx, double Dy) delta)
    {
        if (Math.Abs(delta.Dx) < 0.5 && Math.Abs(delta.Dy) < 0.5)
        {
            return;
        }

        _shell.Position = new PixelPoint(
            _shell.Position.X + ScreenPixels.ToPixel(delta.Dx),
            _shell.Position.Y + ScreenPixels.ToPixel(delta.Dy));
    }

    /// <summary>
    /// 发布外壳的**可见外框**矩形（拼图起始点的吸附参照基准）。
    /// </summary>
    /// <remarks>
    /// <b>缺陷修复 · M1-16 后</b>：曾用 <c>Position</c>（外框左上角）+ <c>Bounds</c>（客户区尺寸）拼矩形 →
    /// 右边界的实际位置少一个"窗口边框宽度"（外壳有标题栏与系统边框），表现为"模块吸附到外壳右侧差几像素"。
    /// 曾改为 <c>PointToScreen(0,0)</c> + <c>ClientSize</c>（**客户区**右边缘）—— 但用户视觉上的"窗口边缘"是
    /// **可见外框**，两者相差"可见边框（约 1px）" ⇒ **贴上去总有 1px 的缝**（用户第十二轮反馈）。
    /// 现在取 <see cref="WindowFrameProbe"/>（DWM 扩展外框 = 可见外框，物理像素），取不到才回退到客户区。
    /// </remarks>
    private void PublishShellRect()
    {
        if (WindowFrameProbe.TryGetVisibleFrame(_shell, out var frame))
        {
            _controller.ShellRect = new CanvasRect(
                ScreenPixels.Round(frame.X),
                ScreenPixels.Round(frame.Y),
                ScreenPixels.Round(frame.Width),
                ScreenPixels.Round(frame.Height));
            return;
        }

        // 回退：客户区（PointToScreen(0,0) 是客户区原点；ClientSize 是 DIP，需乘缩放回到物理像素）
        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;
        var clientOrigin = _shell.PointToScreen(new Point(0, 0));

        _controller.ShellRect = new CanvasRect(
            ScreenPixels.Round(clientOrigin.X),
            ScreenPixels.Round(clientOrigin.Y),
            ScreenPixels.Round(_shell.ClientSize.Width * scaling),
            ScreenPixels.Round(_shell.ClientSize.Height * scaling));
    }
}
