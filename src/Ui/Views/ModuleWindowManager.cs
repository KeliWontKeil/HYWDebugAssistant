using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Ui.Controls;
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
    /// <summary>
    /// **最小化哨兵位置**（Win32 的 `ICONICWINDOWPLACEMENT`：`-32000` 附近）。只作为附加保险。
    /// </summary>
    /// <remarks>
    /// 判据的**主依据是 `WindowState == Minimized`**（跨平台）；这个常量只兜住"平台已把它挪到哨兵位置、
    /// 但状态还没落定"的过渡帧。反过来只认哨兵会在 X11/Wayland 上漏判（那里最小化后位置可能是 `(0,0)`），
    /// 把 `(0,0)` 写回模型 ⇒ 模块跳到左上角。
    /// </remarks>
    private const int MinimizedSentinel = -30000;

    private readonly ModuleWindowsViewModel _controller;
    private readonly Window _shell;
    private readonly Dictionary<string, ModuleWindow> _windows = new(StringComparer.Ordinal);
    private readonly HashSet<Window> _placedWindows = [];
    private bool _disposed;
    private bool _shellFrameReady;
    private bool _syncPending;
    private bool _suppressShellFollow;

    /// <summary>创建窗口管理器。</summary>
    /// <param name="controller">模块窗口集控制器。</param>
    /// <param name="shell">框架外壳窗口（拼图起点，可被贴靠 —— O-6）。</param>
    /// <param name="windowGroup">应用窗口组（层级统一）；为空则以外壳创建。</param>
    public ModuleWindowManager(ModuleWindowsViewModel controller, Window shell, AppWindowGroup? windowGroup = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(shell);

        _controller = controller;
        _shell = shell;
        WindowGroup = windowGroup ?? new AppWindowGroup(shell);

        _controller.GeometryChanged += OnGeometryChanged;
        _controller.VisibilityChanged += OnVisibilityChanged;
        _controller.GroupRaiseRequested += OnGroupRaiseRequested;
        _controller.WindowActivationRequested += OnWindowActivationRequested;
        _controller.Modules.CollectionChanged += (_, _) => SyncAll();

        // 外壳移动 → ① 刷新吸附参照矩形 ② **平移与外壳拼成一体的那一组**（用户第十二轮第四轮反馈）
        _shell.PositionChanged += (_, _) => OnShellMoved();
        _shell.SizeChanged += (_, _) =>
        {
            PublishShellFrame();
            _ = _controller.CommitSceneTranslation();
        };

        // 外壳自己的缩放是"它所在屏"的事实；换屏后 DIP 矩形与物理外框都要重发（不能只改缩放）。
        _shell.ScalingChanged += (_, _) =>
        {
            PublishShellScaling();
            PublishShellFrame();
        };

        // 拖动组带动外壳（模块单侧吸附在外壳上时，拖模块 = 整体一起动）
        _controller.ShellMoveRequested += OnShellMoveRequested;

        PublishWorkAreas();
        RefreshShellFrame();
    }

    /// <summary>应用窗口组（外壳 + 模块窗口 + 弹窗的**层级统一**由它负责）。</summary>
    public AppWindowGroup WindowGroup { get; }

    /// <summary>新窗口创建完成（供宿主把应用内快捷键挂上去）。</summary>
    public event EventHandler<Window>? HotkeyAttached;

    /// <summary>
    /// 外壳外框是否已就绪。未就绪时**不得创建模块窗口**：启动时自动恢复的模块必须在
    /// 外壳显示后的真实外框/缩放下创建，否则会和手动新增模块走成两条几何路径。
    /// </summary>
    public bool IsShellFrameReady => _shellFrameReady;

    /// <summary>
    /// 在外壳显示后重新发布一次真实外框/缩放；这是启动阶段与后续设置的**唯一就绪点**。
    /// </summary>
    public void RefreshShellFrame()
    {
        PublishShellScaling();
        PublishShellFrame();
        _shellFrameReady = _shell.IsVisible && _controller.ShellPhysicalRect is { IsValid: true };

        if (_shellFrameReady && _syncPending)
        {
            _syncPending = false;
            SyncAll();
        }
    }

    /// <summary>
    /// 进入/离开**程序性外壳放置**（启动位置、设置应用等）：期间只刷新外框，不把模块组当成用户拖动跟随。
    /// </summary>
    public void BeginStartupPlacement() => _suppressShellFollow = true;

    /// <summary>结束程序性外壳放置，并发布一次真实外框。</summary>
    public void EndStartupPlacement()
    {
        _suppressShellFollow = false;
        RefreshShellFrame();
    }

    /// <summary>按当前模块集合开窗/关窗（启动与模块增删时调用）。</summary>
    public void SyncAll()
    {
        if (!_shellFrameReady)
        {
            _syncPending = true;
            return;
        }

        foreach (var module in _controller.Modules)
        {
            if (module.IsViewVisible)
            {
                EnsureWindow(module);
            }
            else if (_windows.TryGetValue(module.InstanceId, out var hidden))
            {
                CloseWindow(hidden);
                _ = _windows.Remove(module.InstanceId);
            }
        }

        foreach (var staleId in _windows.Keys.Where(id => _controller.Modules.All(m => !string.Equals(m.InstanceId, id, StringComparison.Ordinal))).ToList())
        {
            if (_windows.Remove(staleId, out var window))
            {
                CloseWindow(window);
            }
        }
    }

    /// <summary>关闭当前全部模块窗口；不释放管理器（之后仍可继续同步/复用）。</summary>
    public void CloseAll()
    {
        // 快照遍历：`Close` 会同步触发窗口的 `Closed`，处理器会从 `_windows` 里移除自己。
        foreach (var window in _windows.Values.ToList())
        {
            CloseWindow(window);
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
        _controller.GroupRaiseRequested -= OnGroupRaiseRequested;
        _controller.WindowActivationRequested -= OnWindowActivationRequested;
        _controller.ShellMoveRequested -= OnShellMoveRequested;

        // 退出前把"整体平移"造成的位移合并落盘（拖动模块带走外壳时会产生）
        _shellDragEndSubscription?.Dispose();
        _shellDragEndSubscription = null;
        _followCommitSubscription?.Dispose();
        _followCommitSubscription = null;
        _ = _controller.CommitSceneTranslation();

        CloseAll();

        _windows.Clear();
        WindowGroup.Dispose();
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>
    /// **按实例取它的宿主窗口**（找不到 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <param name="instanceId">模块实例 ID。</param>
    /// <returns>该实例的模块窗口。</returns>
    /// <remarks>
    /// 存在的理由：系统选择器要一个 <c>TopLevel</c>（<c>StorageProvider</c> 在它上面），
    /// 而"哪个窗口属于哪个实例"只有本类知道 —— 模块与 L4 都拿不到窗口，也不该拿。
    /// 于是模块的"弹一次系统选择器"请求由 UI 侧按实例解析到这里（见 <c>ModuleFileAccess</c>）。
    /// </remarks>
    public ModuleWindow? WindowFor(string instanceId) =>
        _windows.TryGetValue(instanceId, out var window) ? window : null;

    /// <summary>当前已打开的模块窗口（只读快照；用于给启动前已存在的窗口补挂统一能力）。</summary>
    public IReadOnlyList<Window> AttachedWindows => [.. _windows.Values];

    /// <summary>
    /// 给**当前已存在**的窗口补挂统一能力（快捷键等）；之后新开的窗口仍由
    /// <see cref="HotkeyAttached"/> / <see cref="AppWindowGroup.WindowTracked"/> 负责。
    /// </summary>
    /// <param name="attach">对每个已有窗口执行一次。</param>
    public void AttachExistingWindows(Action<Window> attach)
    {
        ArgumentNullException.ThrowIfNull(attach);

        foreach (var window in _windows.Values)
        {
            attach(window);
        }
    }

    private void OnGeometryChanged(object? sender, ModuleWindowGeometryChange e)
    {
        if (_windows.TryGetValue(e.InstanceId, out var window)
            && window.DataContext is ModuleViewViewModel module)
        {
            // 已有布局（用户拖过/缩过）→ 按模型应用；未摆放 → 也按模型（尺寸来自 json 的 window.default）
            ApplyGeometry(window, e, module);
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
            CloseWindow(window);
        }
    }

    /// <summary>
    /// 窗口点击要求把**整组**抬到应用内最前：组内 primary 最后抬（在最上）。
    /// </summary>
    /// <remarks>
    /// **不激活、不改焦点**：抬升只改层级；非 Windows / 平台不支持时门面自动无操作。
    /// 与 <see cref="AppWindowGroup.RaiseAll"/>（全体置顶）不是同一条路径。
    /// </remarks>
    private void OnGroupRaiseRequested(object? sender, ModuleGroupRaiseRequest request)
    {
        var windows = new List<Window>(request.InstanceIds.Count);

        foreach (var instanceId in request.InstanceIds
                     .Where(id => !string.Equals(id, request.Primary, StringComparison.Ordinal))
                     .Append(request.Primary))
        {
            if (_windows.TryGetValue(instanceId, out var window))
            {
                windows.Add(window);
            }
        }

        WindowGroup.RaiseFloatingGroup(windows);
    }

    /// <summary>
    /// 焦点进入某个模块（不一定选中）⇒ 抬起它所在的**整个接触组**；单模块组等价于只抬自己。
    /// </summary>
    private void OnModuleActivated(object? sender, EventArgs e)
    {
        if (sender is not ModuleWindow window)
        {
            return;
        }

        var instanceId = _windows.FirstOrDefault(pair => ReferenceEquals(pair.Value, window)).Key;
        if (instanceId is null)
        {
            return;
        }

        var group = _controller.GroupMembersOf(instanceId).ToList();
        _ = group.Remove(instanceId);
        group.Add(instanceId);   // 被激活的窗口最后抬 ⇒ 保持在组内最上

        var windows = new List<Window>(group.Count);
        foreach (var id in group)
        {
            if (_windows.TryGetValue(id, out var member))
            {
                windows.Add(member);
            }
        }

        WindowGroup.RaiseFloatingGroup(windows);
    }

    /// <summary>关闭窗口（视图摘除由窗口自己的 <c>Closed</c> 统一完成）。</summary>
    /// <remarks>
    /// 摘视图只留一个入口：在关闭**之前**再摘一次，会让"关闭未真正发生"的路径（例如上层取消）
    /// 在窗口仍然可见时丢掉内容；把摘除绑定在 <c>Closed</c> 上则与"是否真的关"一致。
    /// </remarks>
    private static void CloseWindow(ModuleWindow window) => window.Close();

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
        _controller.SetModuleWindowPresence(module.InstanceId, hasWindow: true);

        // **记住关闭**：表里留着已关闭的窗口，下一次同步就会对它 `Show`（已关闭的窗口不能重新显示），
        // 而模块仍然可见 ⇒ 必须由新窗口接管。移除前比对引用，避免误删同 id 的新窗口。
        window.Closed += (_, _) =>
        {
            window.Activated -= OnModuleActivated;
            _ = _placedWindows.Remove(window);
            _controller.SetModuleWindowPresence(module.InstanceId, hasWindow: false);

            if (_windows.TryGetValue(module.InstanceId, out var current) && ReferenceEquals(current, window))
            {
                _ = _windows.Remove(module.InstanceId);
            }
        };

        // 模块视图根自己声明内容初始/最小尺寸（.axaml 的 InitialWidth/InitialHeight 与 MinWidth/MinHeight）；
        // 窗口 chrome 由框架加。视图是幂等懒加载的，先取到它才能定首次落位。
        if (module.EnsureView()?.Content is Control content)
        {
            module.SetContentSize(content);
        }

        // 新模块的初始尺寸由视图声明决定；已有布局则是用户拖过的尺寸，优先保留。
        if (!module.HasLayout)
        {
            module.Width = module.StartupWindowWidth;
            module.Height = module.StartupWindowHeight;
        }

        // **开窗前先定缩放**：`Show` 之前 `RenderScaling` 还不可信，而窗口落位与自绘几何
        // （滑块圆钮按设备像素取整）都要用它 ⇒ 按"将要落在的那块屏"解析并发布一次。
        // 于是**首帧就是终值**：显示之后不再出现"平台纠正位置 / 控件重摆圆钮"那两次几何应用
        // （用户症状："模块实例化后 0.x 秒动 1px、然后归位"）。
        window.PublishScalingFor(module.X, module.Y);
        _controller.SetModuleScaling(module.InstanceId, window.ResolvedScaling);
        window.ResolvedScalingChanged += (_, _) => OnModuleScalingChanged(module, window);

        ApplyGeometry(
            window,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height),
            module);

        // `Show` 可能把窗口放到默认位置：Position/Size 的布局事实从 `Opened` 生命周期点开始。
        // 位置回写由该生命周期点建立；ApplyGeometry 是此刻几何的唯一来源。
        void OnOpened(object? sender, EventArgs e)
        {
            window.Opened -= OnOpened;

            if (!module.HasLayout)
            {
                PublishActualSize(window, module);
            }

            // 用显示后的真实缩放再落一次几何，避免预显示缩放/平台默认位置把模型带偏。
            ApplyGeometry(
                window,
                new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height),
                module);

            _ = _placedWindows.Add(window);
            window.PositionChanged += (_, _) => OnWindowMoved(module, window);
        }

        window.Opened += OnOpened;

        // **从属于外壳**：外壳永在模块窗口之下（点外壳不再把模块压下去）；最小化/恢复随外壳
        window.Show(_shell);

        // 纳入应用窗口组：层级统一（激活任一窗口 → 其余一并抬升；"全体置顶"一起生效）
        WindowGroup.Track(window);
        window.Activated += OnModuleActivated;
        HotkeyAttached?.Invoke(this, window);
    }

    /// <summary>
    /// 把窗口的**实际尺寸**写回模型（未摆放过的模块用它初始化布局，之后随组合件保存）。
    /// </summary>
    /// <remarks>
    /// 尺寸来源 = XAML 默认值；这里只做"读回事实"，不做任何计算。
    /// <b>`ClientSize` 本身就是 DIP，而模型也是 DIP</b> ⇒ 直接取用，**不乘缩放**
    /// （乘了就等于把"物理像素"写进模型，而窗口在别的缩放的屏上会以 1.5 倍的物理尺寸开窗 —— 那正是"右边框被屏边遮住"的成因）。
    /// </remarks>
    private void PublishActualSize(ModuleWindow window, ModuleViewViewModel module)
    {
        // **平台取整不是几何事实**：缩放 ≠100% 时客户区尺寸会带小数（125% 下例如 400.8），
        // 把它写进模型 ⇒ 下一次刷新"读回布局"时又应用回窗口 ⇒ 内容重排一次
        // （屏幕上就是"实例化后 0.x 秒动一下"）。
        // 判据与位置回写同一口径：差不到**一个设备像素**就不写 —— 模型留"意图尺寸"，噪声留在平台。
        var tolerance = 1.0 / (window.ResolvedScaling > 0 ? window.ResolvedScaling : 1.0);

        if (Math.Abs(window.ClientSize.Width - module.Width) >= tolerance
            || Math.Abs(window.ClientSize.Height - module.Height) >= tolerance)
        {
            module.Width = window.ClientSize.Width;
            module.Height = window.ClientSize.Height;
        }

        module.HasLayout = true;

        _controller.CommitInitialLayout(module);
    }

    /// <summary>
    /// 窗口所在的屏缩放变化：模型位置改为**实际窗口位置 ÷ 新缩放**，并按物理差把同组模块一起重贴。
    /// </summary>
    private void OnModuleScalingChanged(ModuleViewViewModel module, ModuleWindow window)
    {
        var oldPhysical = module.PhysicalRect;
        var group = _controller.GroupMembersOf(module.InstanceId)
            .Where(id => !string.Equals(id, module.InstanceId, StringComparison.Ordinal))
            .ToList();

        _controller.SetModuleScaling(module.InstanceId, window.ResolvedScaling);

        if (!window.IsVisible || !_placedWindows.Contains(window))
        {
            return;   // 首次 Opened 落位之前，平台位置/缩放还不是模型事实
        }

        var scale = window.ResolvedScaling > 0 ? window.ResolvedScaling : 1;
        var position = window.Position;

        // 最小化 / 被挪到哨兵位置时，平台位置不是模型事实（判据见 `MinimizedSentinel` 的说明）。
        if (window.WindowState == WindowState.Minimized
            || position.X <= MinimizedSentinel
            || position.Y <= MinimizedSentinel)
        {
            return;
        }

        // 实际物理位置是事实：跨屏后 DIP 坐标必须按新缩放重算，否则模型会与窗口错位。
        module.X = position.X / scale;
        module.Y = position.Y / scale;

        var dxPixels = position.X - oldPhysical.X;
        var dyPixels = position.Y - oldPhysical.Y;
        _controller.TranslateModulesByPixels(group, dxPixels, dyPixels);

        ApplyGeometry(
            window,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height),
            module);
    }

    private static void OnWindowMoved(ModuleViewViewModel module, ModuleWindow window)
    {
        if (module.IsInteracting)
        {
            return;   // 拖动/缩放过程由指针主导，避免与模型互相覆盖
        }

        if (!window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            return;   // 隐藏/最小化时的平台位置不是模型事实
        }

        // 过渡帧兜底（状态已保持 Normal、但位置还是哨兵）：绝不能写回模型。
        if (window.Position.X <= MinimizedSentinel || window.Position.Y <= MinimizedSentinel)
        {
            return;
        }

        // **单位口径**：`Window.Position` 是**物理像素**，模型是 **DIP** ⇒ 在这里 ÷ 该窗口的缩放一次
        // （与 `ApplyGeometry` 里那个 × 缩放互为逆运算）。旧实现把物理像素直接写进 DIP 模型 ⇒
        // 125% 下模型位置比真实大 25% ⇒ 与持久化布局永远对不上，刷新每次都在"纠正"它。
        // 回写**只改模型、不抬几何**：几何的唯一入口是真实用户动作与刷新里的差异判据。
        var scaling = window.ResolvedScaling > 0 ? window.ResolvedScaling : 1.0;
        var x = ScreenPixels.RoundDip(window.Position.X / scaling);
        var y = ScreenPixels.RoundDip(window.Position.Y / scaling);

        if (Math.Abs(module.X - x) < 0.5 && Math.Abs(module.Y - y) < 0.5)
        {
            return;
        }

        module.X = x;
        module.Y = y;
    }

    /// <summary>
    /// 把模型几何应用到窗口（位置 + 尺寸，尺寸夹进该模块的有效区间）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **窗口尺寸的唯一来源是模型**（模型里是"用户布局"或模块 `[ModuleDefinition]` 的默认窗口尺寸，
    /// 二者在 <see cref="ModuleViewViewModel"/> 构造时已分好优先级）；
    /// `ModuleWindow.axaml` 的 `Width`/`Height` 只作设计器里的兜底，不再是运行期事实来源。
    /// 内容排不下由内容区 ScrollViewer 处理（"信息量"永远不改变外层容器尺寸）。
    /// </para>
    /// <para>
    /// <b>单位口径</b>：模型四数一律 **DIP**（见 `ModuleLayoutView`）。
    /// 窗口位置与尺寸都从**同一个整数物理矩形**推出（<see cref="ScreenPixels.PixelRectOf"/>）：
    /// 位置取 <c>Left/Top</c>，尺寸取 <c>(Right-Left)/scaling</c>。
    /// 尺寸**不是**"DIP 原样交给平台"——那样平台会对每个窗口独立取整，
    /// <c>round(x·s) + round(w·s) ≠ round((x+w)·s)</c>，125%/150% 下相邻两窗公共边差 1px。
    /// </para>
    /// </remarks>
    private static void ApplyGeometry(ModuleWindow window, ModuleWindowGeometryChange geometry, ModuleViewViewModel module)
    {
        // 缩放取**本窗口已发布的那一份**（`ModuleWindow.ResolvedScaling`）：`Show` 之前 `RenderScaling`
        // 不可信，用它算出来的位置会在显示后被平台纠正一次（"打开后跳一下"）。
        var scaling = window.ResolvedScaling > 0 ? window.ResolvedScaling : 1.0;

        // 先按现有语义夹出 DIP 尺寸，再由 (x, y, w, h) 求一个整数像素矩形。
        var widthDip = Fit(module, geometry.Width, isWidth: true);
        var heightDip = Fit(module, geometry.Height, isWidth: false);
        var pixels = ScreenPixels.PixelRectOf(
            new CanvasRect(geometry.X, geometry.Y, widthDip, heightDip),
            scaling);

        var position = new PixelPoint(pixels.Left, pixels.Top);
        if (window.Position != position)
        {
            window.Position = position;
        }

        // 尺寸由同一个整数物理矩形反推，**不加偏置**：偏置会让客户区多出不到 1px，
        // 在高缩放下表现为模块边框比吸附线大一圈（“看起来没吸上，实际模型已吸”）。
        var size = new Size(
            (pixels.Right - pixels.Left) / scaling,
            (pixels.Bottom - pixels.Top) / scaling);

        // 重设判据按**像素**比较：0.2 DIP 的修正若用 DIP 阈值会被跳过，公共边就仍可能差 1px。
        if (ScreenPixels.ToPixel(window.Width * scaling) != pixels.Right - pixels.Left)
        {
            window.Width = size.Width;
        }

        if (ScreenPixels.ToPixel(window.Height * scaling) != pixels.Bottom - pixels.Top)
        {
            window.Height = size.Height;
        }
    }

    /// <summary>把 DIP 尺寸夹进模块的有效区间（下限 = max(框架下限, json min)，上限 = json max）。</summary>
    private static double Fit(ModuleViewViewModel module, double dip, bool isWidth)
    {
        var min = isWidth ? module.EffectiveMinWidth : module.EffectiveMinHeight;
        var max = isWidth ? module.EffectiveMaxWidth : module.EffectiveMaxHeight;
        var value = Math.Max(dip, min);
        return max is { } upper ? Math.Min(value, upper) : value;
    }

    private void PublishWorkAreas()
    {
        var areas = new List<CanvasRect>(_shell.Screens.All.Count);
        CanvasRect? primaryArea = null;
        var primary = _shell.Screens.Primary;

        foreach (var screen in _shell.Screens.All)
        {
            // **换算成 DIP 再发布**：`Screens.WorkingArea` 是物理像素，而模型（模块坐标）是 DIP
            // —— 摆放与钳制都要拿它和模块坐标比，混单位会让"贴边/避让"整体差一个缩放因子。
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var area = screen.WorkingArea;
            var dip = new CanvasRect(
                area.X / scaling,
                area.Y / scaling,
                area.Width / scaling,
                area.Height / scaling);

            areas.Add(dip);

            if (ReferenceEquals(screen, primary))
            {
                primaryArea = dip;
            }
        }

        _controller.SetWorkAreas(areas);

        _controller.WorkArea = primaryArea ?? (areas.Count > 0 ? areas[0] : null);
    }

    /// <summary>列表请求"显示并置前"：已有窗口直接激活；隐藏/未开窗由控制器先补可见性。</summary>
    private void OnWindowActivationRequested(object? sender, string instanceId)
    {
        if (_windows.TryGetValue(instanceId, out var window))
        {
            if (!window.IsVisible)
            {
                window.Show(_shell);
            }

            window.Activate();
        }
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
        // **移动前**的参照矩形：分组判定必须用它（模块此刻还在旧位置上；与 implementation/02 P33 同类）。
        // 外壳跨屏时 DIP 差没有统一定义，必须用**物理外框差**平移模块组。
        var before = _controller.ShellPhysicalRect;

        PublishShellFrame();
        var after = _controller.ShellPhysicalRect;

        if (before is not { IsValid: true } previous || after is not { } current)
        {
            return;
        }

        // 启动/设置的程序性放置：只更新参照，不跟随模块组。
        if (_suppressShellFollow)
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
        _shellDragEndSubscription?.Dispose();
        _shellDragEndSubscription = UiFrameClock.ScheduleOnce(
            TimeSpan.FromMilliseconds(250),
            OnShellDragEnd);

        // 拖动中不逐帧落盘：停下后合并提交（逐帧提交会触发建网重算而卡顿）
        _followCommitSubscription?.Dispose();
        _followCommitSubscription = UiFrameClock.ScheduleOnce(
            TimeSpan.FromMilliseconds(180),
            OnFollowCommit);
    }

    /// <summary>拖动结束（静默到期）：解除分组冻结。</summary>
    private void OnShellDragEnd()
    {
        _shellDragEndSubscription?.Dispose();
        _shellDragEndSubscription = null;
        _controller.EndShellDragGesture();
    }

    /// <summary>防抖到期：把整体平移合并落盘。</summary>
    private void OnFollowCommit()
    {
        _followCommitSubscription?.Dispose();
        _followCommitSubscription = null;
        _ = _controller.CommitSceneTranslation();
    }

    /// <summary>"这次拖动结束"的静默计时订阅。</summary>
    private IDisposable? _shellDragEndSubscription;

    /// <summary>跟随落盘的防抖订阅。</summary>
    private IDisposable? _followCommitSubscription;

    /// <summary>把"拖动组带动外壳"的**绝对物理目标**落到真实窗口上（见 <see cref="ModuleWindowsViewModel.ShellMoveRequested"/>）。</summary>
    /// <remarks>
    /// 请求里已经是物理像素：**不再乘缩放、也不逐帧累加取整**。
    /// 旧实现每帧把 DIP 增量乘缩放再取整，跨屏/高缩放下会累积漂移，正是模块与外壳越拖越偏的来源之一。
    /// </remarks>
    private void OnShellMoveRequested(object? sender, PixelPoint target)
    {
        if (_shell.Position != target)
        {
            _shell.Position = target;
        }
    }

    /// <summary>
    /// 发布外壳的**可见外框**（DIP + 物理两份）及其窗口物理位置。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>吸附参照取 <see cref="WindowFrameProbe"/> 的 DWM 扩展外框（视觉边缘）⇒ 不再少一个系统边框；</item>
    ///   <item>取不到时由 <see cref="ScreenGeometry"/> 回退 `Position + ClientSize × scaling`，仍保持物理口径；</item>
    ///   <item>DIP 与物理由**同一次探测**发布，避免“模型移动了、吸附参照还是旧值”；</item>
    ///   <item>窗口的绝对移动目标是 `Window.Position`（<see cref="ModuleWindowsViewModel.ShellWindowPosition"/>），
    ///     不能拿可见外框原点当窗口位置，否则会把系统边框偏移一并算进去。</item>
    /// </list>
    /// </remarks>
    /// <summary>发布外壳自己的缩放（组内物理位移换算用）。</summary>
    private void PublishShellScaling() =>
        _controller.ShellScaling = _shell.RenderScaling > 0 ? _shell.RenderScaling : 1.0;

    private void PublishShellFrame()
    {
        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;

        if (!ScreenGeometry.TryGetPhysicalFrame(_shell, out var physical))
        {
            return;
        }

        // 两个矩形由**同一次探测**推出；DIP 只用于模型/叠加层，物理用于成组/吸附/接触判定。
        _controller.SetShellFrame(ScreenGeometry.ToDip(physical, scaling), physical);
        _controller.SetShellWindowPosition(_shell.Position);
    }

    /// <summary>
    /// 由**四条边**构造整数物理像素矩形（左/上向下取整、右/下向上取整 ⇒ **保证不留缝**）。
    /// </summary>
    /// <param name="left">左边缘（物理像素）。</param>
    /// <param name="top">上边缘。</param>
    /// <param name="right">右边缘。</param>
    /// <param name="bottom">下边缘。</param>
    private static CanvasRect ToRoundedRect(double left, double top, double right, double bottom)
    {
        var l = Math.Floor(left);
        var t = Math.Floor(top);
        var r = Math.Ceiling(right);
        var b = Math.Ceiling(bottom);

        return new CanvasRect(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }
}
