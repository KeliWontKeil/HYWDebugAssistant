using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **框架外壳窗口**：节拍器 + 弹窗编排 + 文件菜单 + 快捷键。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>本窗口**不承载模块**：模块由 <see cref="ModuleWindowManager"/> 开成独立无边框窗口；</item>
///   <item>快捷键（临时取值）：<c>M</c> 模块清单 · <c>I</c> Issue · <c>S</c> 设置 ·
///     <c>T</c> 拓扑诊断 · <c>L</c> 口标记 · <c>G</c> 吸附 · <c>Ctrl+L</c> 全部锁定；</item>
///   <item>Esc 在拖动中取消由模块窗口自行处理（各窗口独立）。</item>
/// </list>
/// </remarks>
public partial class MainWindow : Window, IExitPrompt, IExitSaver, IExitPolicy
{
    private readonly Dictionary<DialogKind, DialogWindow> _dialogs = [];
    private string? _lastGeometryAspect;
    private bool _lastGeometryFixed;
    private bool _preparedForShow;

    /// <summary>创建外壳窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();

        // 点外壳 = 点「别处」 → **清除模块选中**（否则点空白处无法取消选中）
        AddHandler(PointerPressedEvent, OnShellPressed, RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            // 启动位置是**程序性放置**，不是用户拖动外壳：期间禁止 TranslateShellGroup。
            // 否则已按保存位置恢复的自动加载模块会再被平移一次（只有启动模块会命中的错位分支）。
            WindowManager?.BeginStartupPlacement();
            ApplyStartupPosition();
            WindowManager?.EndStartupPlacement();

            // 自动恢复的模块必须在外壳显示后的真实外框/缩放下创建。
            WindowManager?.RefreshShellFrame();

            // 快捷键必须先接线、再刷新现场：`Shell.Refresh()` 会创建启动时的模块窗口，
            // 若之后再订阅 `HotkeyAttached`，这些窗口不会再触发事件（表现为"必须点主窗口快捷键才生效"）。
            if (Shell is { } shell)
            {
                _ = shell.Hotkeys.Rebuild(shell.GetHotkeyBindings());
                shell.Hotkeys.Attach(this);

                if (WindowManager is { } manager)
                {
                    manager.HotkeyAttached += (_, window) => shell.Hotkeys.Attach(window);

                    // 新窗口统一由窗口组通知；与模块管理器事件并存是为了兼容既有调用点。
                    manager.WindowGroup.WindowTracked += (_, window) => shell.Hotkeys.Attach(window);

                    // 兜底：刷新前已经存在的窗口（工作区恢复/测试宿主）也补挂。
                    manager.AttachExistingWindows(shell.Hotkeys.Attach);
                }
            }

            Shell?.Refresh();
            UiFrameClock.Subscribe(OnRefreshTick);
            WindowManager?.SyncAll();
        };

        Closed += (_, _) => UiFrameClock.Unsubscribe(OnRefreshTick);
        Closing += OnClosing;
        DataContextChanged += (_, _) => Hook();

        // 跟随系统时壁纸随平台浅/深切；显式主题由 Settings.Applied 刷新。
        ActualThemeVariantChanged += (_, _) => Shell?.RefreshThemeWallpaper();

    }

    private void OnRefreshTick() => Shell?.Refresh();

    /// <summary>
    /// 关闭外壳：交给 <see cref="ExitCoordinator"/> 的单一出口（先决定、再关模块窗、最后放行本窗）。
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ExitCoordinator is null || ExitCoordinator.CanClose || Shell is null)
        {
            return;
        }

        e.Cancel = true;
        _ = ExitCoordinator.RequestAsync();
    }

    /// <summary>退出协调器（由组合根注入；为空 = 宿主未接线，按普通窗口关闭）。</summary>
    public ShellExitCoordinator? ExitCoordinator { get; set; }

    /// <inheritdoc />
    Task<WorkspaceCloseResult> IExitPrompt.AskAsync(int moduleCount) =>
        WorkspaceCloseDialog.ShowAsync(this, moduleCount);

    /// <inheritdoc />
    async Task<bool> IExitSaver.SaveAsync() =>
        Shell is { } shell && await SaveWorkspaceAndReportAsync(shell);

    /// <inheritdoc />
    string IExitPolicy.Current => Shell?.WorkspaceExitBehavior ?? string.Empty;

    /// <inheritdoc />
    async Task<bool> IExitPolicy.RememberAsync(string policy)
    {
        if (Shell is not { } shell)
        {
            return false;
        }

        var persisted = shell.SetWorkspaceExitBehavior(policy);
        if (persisted.Succeeded)
        {
            return true;
        }

        await ShowMessageAsync(
            Loc("shell.message.exitPolicySaveFailed.title"),
            Loc("shell.message.exitPolicySaveFailed.message", string.Join("；", persisted.Failures)));
        return false;
    }

    /// <summary>保存工作区；失败时显示原因并返回 false（调用方不得关闭）。</summary>
    private async Task<bool> SaveWorkspaceAndReportAsync(ShellViewModel shell)
    {
        var result = shell.SaveWorkspace(CurrentWorkspacePlacement());
        if (result.Succeeded)
        {
            return true;
        }

        await ShowMessageAsync(
            Loc("shell.message.workspaceSaveFailed.title"),
            Loc("shell.message.workspaceSaveFailed", string.Join("；", result.Failures)));
        return false;
    }

    /// <summary>模块窗口管理器（由组合根在显示前注入并负责释放）。</summary>
    public ModuleWindowManager? WindowManager { get; set; }

    /// <summary>
    /// **首次显示前**载入设置并落定几何：有工作区按保存的尺寸，无工作区按比例设置。
    /// 组合根在 `Show` 之前调用一次；避免首帧默认尺寸跳变。
    /// </summary>
    public void PrepareForShow()
    {
        if (_preparedForShow || Shell is not { } shell)
        {
            return;
        }

        _preparedForShow = true;
        shell.LoadSettings();

        var settings = shell.GetCommittedSettings();
        var geometry = ShellGeometryPlanner.Startup(
            settings.ShellAspect,
            settings.ShellFixedSize,
            shell.GetWorkspacePlacement());

        ApplyShellGeometry(geometry);
        _lastGeometryAspect = settings.ShellAspect;
        _lastGeometryFixed = settings.ShellFixedSize;
    }

    /// <summary>把纯规划结果落到窗口；几何写入只在这里和设置应用回调发生。</summary>
    private void ApplyShellGeometry(ShellGeometry geometry)
    {
        var width = Math.Max(1, geometry.Width);
        var height = Math.Max(1, geometry.Height);

        CanResize = geometry.CanResize;

        if (Math.Abs(Width - width) > 0.01)
        {
            Width = width;
        }

        if (Math.Abs(Height - height) > 0.01)
        {
            Height = height;
        }
    }

    /// <summary>设置应用后：显式改比例才重算尺寸；只切固定不改变尺寸。</summary>
    private void ApplyGeometryAfterSettings()
    {
        if (Shell is not { } shell)
        {
            return;
        }

        var settings = shell.GetCommittedSettings();
        var geometry = ShellGeometryPlanner.AfterSettings(
            _lastGeometryAspect,
            _lastGeometryFixed,
            settings.ShellAspect,
            settings.ShellFixedSize,
            Width,
            Height);

        ApplyShellGeometry(geometry);
        _lastGeometryAspect = settings.ShellAspect;
        _lastGeometryFixed = settings.ShellFixedSize;
    }

    /// <summary>当前外壳在工作区快照里的绝对矩形（DIP）。</summary>
    private WorkspacePlacementView CurrentWorkspacePlacement()
    {
        var scaling = RenderScaling > 0 ? RenderScaling : 1.0;
        return new WorkspacePlacementView(
            Position.X / scaling,
            Position.Y / scaling,
            ClientSize.Width,
            ClientSize.Height);
    }

    /// <summary>应用工作区里保存的外壳位置；尺寸已由 PrepareForShow 落定，这里绝不改尺寸。</summary>
    private void ApplyStartupPosition()
    {
        if (Shell?.GetWorkspacePlacement() is not { } placement)
        {
            return;
        }

        var scaling = ScreenGeometry.ResolveScalingForDip(
            Screens.All,
            placement.X,
            placement.Y,
            Screens.Primary is { Scaling: > 0 } primary ? primary.Scaling : 1.0);

        Position = new PixelPoint(
            ScreenPixels.ToPixel(placement.X * scaling),
            ScreenPixels.ToPixel(placement.Y * scaling));
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    /// <summary>外壳内任意按下 → 清除模块选中（"点别处取消选中"）；侧边栏与页面内的点击不在此列。</summary>
    private void OnShellPressed(object? sender, PointerPressedEventArgs e)
    {
        // 侧边栏按钮与常驻页面上的点击（含组合页复选框）都是有效操作，不能触发“点外壳清空选择”。
        if (e.Source is Visual visual
            && visual.GetVisualAncestors().Any(ancestor =>
                ancestor is ModuleCatalogView or CompositePageView or InteractionView
                || ancestor is StackPanel { Name: "RailButtons" }))
        {
            return;
        }

        Shell?.Windows.ClearSelection();
    }

    private void Hook()
    {
        if (Shell is { } shell)
        {
            shell.DialogRequested += (_, kind) => ShowDialog(kind);
            shell.RaiseAllWindowsRequested += (_, _) => WindowManager?.WindowGroup.RaiseAll();
            shell.RestartRequested += (_, _) => _ = RestartAsync();
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.AppTopmost))
                {
                    WindowManager?.WindowGroup.SetTopmost(shell.AppTopmost);
                }
            };

            // 设置应用成功 → 重建快捷键绑定 + 套用窗口层级（含"应用整体置顶"）
            shell.Settings.Applied += (_, _) =>
            {
                shell.ReloadHotkeys();
                shell.AppTopmost = shell.Settings.AppTopmost;
                ApplyGeometryAfterSettings();
                shell.RefreshThemeWallpaper();
                WindowManager?.WindowGroup.SetTopmost(shell.AppTopmost);
            };
        }
    }

    // ---------------------------------------------------------------- 重启

    /// <summary>
    /// **重新加载**：保存一次性现场快照 → 启动新进程 → 关闭当前进程。
    /// </summary>
    private async Task RestartAsync()
    {
        if (Shell is not { } shell)
        {
            return;
        }

        var confirmed = await ConfirmDialog.ShowAsync(
            this,
            Loc("shell.dialog.restart.title"),
            Loc("shell.dialog.restart.message"),
            Loc("shell.dialog.restart.confirm"),
            Loc("shell.dialog.cancel"));

        if (!confirmed)
        {
            return;
        }

        var snapshot = shell.SaveWorkspace(CurrentWorkspacePlacement());
        if (!snapshot.Succeeded)
        {
            await ShowMessageAsync(
                Loc("shell.message.restartFailed.title"),
                Loc("shell.message.restartWorkspaceFailed", string.Join("；", snapshot.Failures)));
            return;
        }

        if (!TryStartNewProcess(out var error))
        {
            await ShowMessageAsync(
                Loc("shell.message.restartFailed.title"),
                Loc("shell.message.restartProcessFailed", error));
            return;
        }

        if (ExitCoordinator is { } coordinator)
        {
            // 重启已自行确认并保存过一次：直接关闭，不再询问、不再写第二遍快照。
            await coordinator.CloseWithoutSavingAsync();
            return;
        }

        Close();
    }

    /// <summary>启动一个与当前进程同参数的新实例（处理 dotnet 宿主与普通 exe 两种形态）。</summary>
    private static bool TryStartNewProcess(out string? error)
    {
        error = null;
        var processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath))
        {
            error = Loc("shell.error.processPathUnknown");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Normal,
            };

            var arguments = Environment.GetCommandLineArgs();
            var isDotnetHost = string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase);

            if (isDotnetHost && arguments.Length > 0)
            {
                startInfo.ArgumentList.Add(arguments[0]);
            }

            for (var index = 1; index < arguments.Length; index++)
            {
                startInfo.ArgumentList.Add(arguments[index]);
            }

            _ = Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or System.ComponentModel.Win32Exception
                                      or FileNotFoundException)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- 弹窗

    private void OnOpenCatalog(object? sender, RoutedEventArgs e) => ShowDialog(DialogKind.ModuleCatalog);

    private void OnOpenIssues(object? sender, RoutedEventArgs e) => ShowDialog(DialogKind.Issues);

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => ShowDialog(DialogKind.Settings);

    private void OnOpenTopology(object? sender, RoutedEventArgs e) => ShowDialog(DialogKind.Topology);

    /// <summary>打开（或聚焦已打开的）弹窗。</summary>
    /// <param name="kind">弹窗种类。</param>
    public void ShowDialog(DialogKind kind)
    {
        if (Shell is not { } shell)
        {
            return;
        }

        if (_dialogs.TryGetValue(kind, out var existing) && existing.IsVisible)
        {
            existing.Activate();
            return;
        }

        var dialog = new DialogWindow(kind)
        {
            DataContext = kind switch
            {
                DialogKind.ModuleCatalog => shell.Catalog,
                DialogKind.Issues => shell.Issues,
                DialogKind.Settings => shell.Settings,
                _ => shell.Topology,
            },
        };

        dialog.Closed += (_, _) => _ = _dialogs.Remove(kind);

        // 设置三键里的「确认 / 取消」由视图模型发起关闭请求 ⇒ 关掉这个弹窗
        //（订阅在弹窗关闭时摘掉：视图模型是**长生命周期**的，不摘会随每次打开累积）
        if (kind == DialogKind.Settings)
        {
            void OnCloseRequested(object? sender, EventArgs e) => dialog.Close();

            shell.Settings.CloseRequested += OnCloseRequested;
            dialog.Closed += (_, _) => shell.Settings.CloseRequested -= OnCloseRequested;
        }

        _dialogs[kind] = dialog;
        dialog.Show(this);

        // 功能面板属于浮动带：与模块窗口按最近激活互相抬前；"全体置顶"一起生效。
        WindowManager?.WindowGroup.Track(dialog, AppWindowLayer.Panel);
        Shell?.Hotkeys.Attach(dialog);
    }

    // ---------------------------------------------------------------- 文件
    private void OnExit(object? sender, RoutedEventArgs e) => _ = ExitCoordinator?.RequestAsync();

    private void OnShowHotkeys(object? sender, RoutedEventArgs e)
    {
        // **快捷键清单不写死**：按当前生效的绑定生成（改过设置后这里跟着变）。
        var list = Shell?.Hotkeys.HelpText ?? string.Empty;

        _ = ShowMessageAsync(
            Loc("shell.help.hotkeys.title"),
            Loc("hotkey.help.scope") + Environment.NewLine + Environment.NewLine
                + list + Environment.NewLine + Environment.NewLine
                + Loc("shell.help.hotkeys.tips"));
    }

    private void OnShowAbout(object? sender, RoutedEventArgs e) => _ = ShowMessageAsync(
        Loc("shell.help.about.title"),
        Loc("shell.help.about.body"));

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(MainWindow).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(MainWindow).Assembly, key, args);

    private async Task ShowMessageAsync(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Padding = new Avalonia.Thickness(12),
                Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            },
        };

        var group = AppWindowGroup.Of(this);
        group?.Track(window, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(window);

        await window.ShowDialog(this);
    }

    // ---------------------------------------------------------------- 快捷键

}
