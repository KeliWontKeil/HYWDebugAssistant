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
    private readonly ModuleWindowsViewModel _controller;
    private readonly Window _shell;
    private readonly Dictionary<string, ModuleWindow> _windows = new(StringComparer.Ordinal);
    private bool _disposed;

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
        _controller.ShellTranslationRequested -= OnShellTranslationRequested;

        // 退出前把"整体平移"造成的位移合并落盘（拖动模块带走外壳时会产生）
        _shellDragEndSubscription?.Dispose();
        _shellDragEndSubscription = null;
        _followCommitSubscription?.Dispose();
        _followCommitSubscription = null;
        _ = _controller.CommitSceneTranslation();

        foreach (var window in _windows.Values)
        {
            CloseWindow(window);
        }

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
        foreach (var instanceId in request.InstanceIds
                     .Where(id => !string.Equals(id, request.Primary, StringComparison.Ordinal))
                     .Append(request.Primary))
        {
            if (_windows.TryGetValue(instanceId, out var window))
            {
                WindowZOrder.RaiseWithoutActivating(window);
            }
        }
    }

    /// <summary>关闭窗口前先摘掉缓存视图的父级；否则“隐藏后再显示全部”会把同一控件挂到新父级。</summary>
    private static void CloseWindow(ModuleWindow window)
    {
        window.DetachModuleView();
        window.Close();
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

        // **开窗前先定缩放**：`Show` 之前 `RenderScaling` 还不可信，而窗口落位与自绘几何
        // （滑块圆钮按设备像素取整）都要用它 ⇒ 按"将要落在的那块屏"解析并发布一次。
        // 于是**首帧就是终值**：显示之后不再出现"平台纠正位置 / 控件重摆圆钮"那两次几何应用
        // （用户症状："模块实例化后 0.x 秒动 1px、然后归位"）。
        window.PublishScalingFor(module.X, module.Y);

        ApplyGeometry(
            window,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height),
            module);

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

    private static void OnWindowMoved(ModuleViewViewModel module, ModuleWindow window)
    {
        if (module.IsInteracting)
        {
            return;   // 拖动/缩放过程由指针主导，避免与模型互相覆盖
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

        // **+0.25/scaling 是对平台取整实现的稳健偏置**：客户区尺寸从整数像素反推后常带 .x 小数，
        // `round` 与 `floor` 两种实现下这个偏置都落在同一个物理像素数（不是业务尺寸，也不进模型）。
        var size = new Size(
            ((pixels.Right - pixels.Left) / scaling) + (0.25 / scaling),
            ((pixels.Bottom - pixels.Top) / scaling) + (0.25 / scaling));

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

    private void PublishWorkArea()
    {
        var screen = _shell.Screens.Primary;
        if (screen is null)
        {
            _controller.WorkArea = null;
            return;
        }

        // **换算成 DIP 再发布**：`Screens.WorkingArea` 是物理像素，而模型（模块坐标）是 DIP
        // —— 摆放与钳制都要拿它和模块坐标比，混单位会让"贴边/避让"整体差一个缩放因子。
        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;
        var area = screen.WorkingArea;

        _controller.WorkArea = new CanvasRect(
            area.X / scaling,
            area.Y / scaling,
            area.Width / scaling,
            area.Height / scaling);
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

    /// <summary>把"拖动组带动外壳"的请求落到真实窗口上（见 <see cref="ModuleWindowsViewModel.ShellTranslationRequested"/>）。</summary>
    /// <remarks>
    /// <b>单位</b>：请求里的增量是 **DIP**（外壳矩形与模型同口径），而 <c>Window.Position</c> 要整数**物理像素**
    /// ⇒ 必须乘该窗口的 <c>RenderScaling</c>（与 <see cref="ApplyGeometry"/> 的落位换算同一条口径）。
    /// 不乘的话，150% 的屏上"拖动组带动外壳"只会移动 2/3 的距离。
    /// </remarks>
    private void OnShellTranslationRequested(object? sender, (double Dx, double Dy) delta)
    {
        if (Math.Abs(delta.Dx) < 0.5 && Math.Abs(delta.Dy) < 0.5)
        {
            return;
        }

        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;

        _shell.Position = new PixelPoint(
            _shell.Position.X + ScreenPixels.ToPixel(delta.Dx * scaling),
            _shell.Position.Y + ScreenPixels.ToPixel(delta.Dy * scaling));
    }

    /// <summary>
    /// 发布外壳的**可见外框**矩形（拼图起始点的吸附参照基准）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>缺陷修复 · M1-16 后</b>：曾用 <c>Position</c>（外框左上角）+ <c>Bounds</c>（客户区尺寸）拼矩形 →
    ///     右边界的实际位置少一个"窗口边框宽度"（外壳有标题栏与系统边框），表现为"模块吸附到外壳右侧差几像素"；</item>
    ///   <item>曾改为 <c>PointToScreen(0,0)</c> + <c>ClientSize</c>（**客户区**右边缘）—— 但用户视觉上的"窗口边缘"是
    ///     **可见外框**，两者相差"可见边框（约 1px）" ⇒ **贴上去总有 1px 的缝**（用户第十二轮反馈）；</item>
    ///   <item>现在取 <see cref="WindowFrameProbe"/>（DWM 扩展外框 = 可见外框，物理像素），取不到才回退到客户区；</item>
    ///   <item><b>取整口径</b>：若对 <c>frame.X</c> 与 <c>frame.Width</c> **各自**四舍五入 ⇒
    ///     右边缘拿到的是 <c>Round(X) + Round(Width)</c>，与真实的 <c>X + Width</c> 最多差 1px（两个方向的舍入误差可以叠加），
    ///     表现为"贴上去仍有一条 1px 的缝"（用户复报）。现在改为**先把四条边各自定好，再相减求宽高**；
    ///     且左/上**向下取整**、右/下**向上取整** —— 宁可多覆盖不到 1px（被模块自身边框盖住、看不出来），
    ///     **也绝不留缝**（用户要的是"严丝合缝"）。</item>
    /// </list>
    /// </remarks>
    private void PublishShellRect()
    {
        var scaling = _shell.RenderScaling <= 0 ? 1.0 : _shell.RenderScaling;

        if (WindowFrameProbe.TryGetVisibleFrame(_shell, out var frame))
        {
            // 探测给的是**物理像素**（Win32 坐标）⇒ ÷ 缩放换成 DIP 再发布。
            // 四条边**先各自定好再相减**，不做舍入：舍入在缩放 ≠100% 时会各自放大误差，
            // 让"贴齐"变成"差几像素"（旧实现在物理像素里靠向下/向上取整来保证不留缝）。
            _controller.ShellRect = new CanvasRect(
                frame.X / scaling,
                frame.Y / scaling,
                (frame.X + frame.Width) / scaling - (frame.X / scaling),
                (frame.Y + frame.Height) / scaling - (frame.Y / scaling));

            return;
        }

        // 回退：客户区。**原点必须换算**：`PointToScreen` 给的是**物理像素**，而模型一律 DIP
        // （`ClientSize` 本身就是 DIP ⇒ 只有原点要换）。
        var clientOrigin = _shell.PointToScreen(new Point(0, 0));

        _controller.ShellRect = new CanvasRect(
            clientOrigin.X / scaling,
            clientOrigin.Y / scaling,
            _shell.ClientSize.Width,
            _shell.ClientSize.Height);
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
