using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **框架外壳窗口**：节拍器 + 弹窗编排 + 文件菜单 + 快捷键。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>本窗口**不承载模块**：模块由 <see cref="ModuleWindowManager"/> 开成独立无边框窗口；</item>
///   <item>快捷键（Gate C 定稿前为临时取值）：<c>M</c> 模块清单 · <c>I</c> Issue · <c>S</c> 设置 ·
///     <c>T</c> 拓扑诊断 · <c>L</c> 口标记 · <c>G</c> 吸附 · <c>Ctrl+L</c> 全部锁定；</item>
///   <item>Esc 在拖动中取消由模块窗口自行处理（各窗口独立）。</item>
/// </list>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
    private readonly Dictionary<DialogKind, DialogWindow> _dialogs = [];

    /// <summary>创建外壳窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();

        // 点外壳 = 点"别处" → **清除模块选中**【M1-16 / ADR-0039 D1】（用户反馈：此前无法取消选中）
        AddHandler(PointerPressedEvent, OnShellPressed, RoutingStrategies.Tunnel);

        _refreshTimer.Tick += (_, _) => Shell?.Refresh();
        Opened += (_, _) =>
        {
            Shell?.LoadSettings();

            // **尺寸默认值在 XAML**；设置只在"用户显式改过比例"时覆盖
            ApplyShellSizeFromSettings();

            Shell?.Refresh();
            _refreshTimer.Start();

            // 快捷键：按设置重建绑定，并挂到**本应用每一个窗口**（外壳 + 模块窗口 + 弹窗）
            if (Shell is { } shell)
            {
                _ = shell.Hotkeys.Rebuild(shell.GetHotkeyBindings());
                shell.Hotkeys.Attach(this);

                if (WindowManager is { } manager)
                {
                    manager.HotkeyAttached += (_, window) => shell.Hotkeys.Attach(window);
                    manager.SyncAll();
                }
            }
        };

        Closed += (_, _) => _refreshTimer.Stop();
        DataContextChanged += (_, _) => Hook();

    }

    /// <summary>模块窗口管理器（由组合根在显示前注入并负责释放）。</summary>
    public ModuleWindowManager? WindowManager { get; set; }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    /// <summary>
    /// 按设置里的「主窗口比例」覆盖窗口尺寸（**默认值在 XAML**，此处只在设置与默认不一致时才改）。
    /// </summary>
    /// <remarks>
    /// 【用户第九轮反馈 · UI 开发方式】外层容器尺寸**必须固定**：默认值写在 `MainWindow.axaml`；
    /// 设置只承载"用户显式选择的比例档"；**不存在任何按内容计算尺寸的逻辑**。
    /// </remarks>
    private void ApplyShellSizeFromSettings()
    {
        if (Shell is not { } shell)
        {
            return;
        }

        var (width, height) = Ui.Theming.ShellLayout.Measure(shell.ShellAspect);

        if (Math.Abs(Width - width) > 0.5 || Math.Abs(Height - height) > 0.5)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>外壳内任意按下 → 清除模块选中（"点别处取消选中"）。</summary>
    private void OnShellPressed(object? sender, PointerPressedEventArgs e) => Shell?.Windows.ClearSelection();

    private void Hook()
    {
        if (Shell is { } shell)
        {
            shell.DialogRequested += (_, kind) => ShowDialog(kind);
            shell.RaiseAllWindowsRequested += (_, _) => WindowManager?.WindowGroup.RaiseAll();
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
                WindowManager?.WindowGroup.SetTopmost(shell.AppTopmost);
            };
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

        // 【2026-09 用户裁定 · 设置三键】「确认 / 取消」由视图模型发起关闭请求 ⇒ 关掉这个弹窗
        //（订阅在弹窗关闭时摘掉：视图模型是**长生命周期**的，不摘会随每次打开累积）
        if (kind == DialogKind.Settings)
        {
            void OnCloseRequested(object? sender, EventArgs e) => dialog.Close();

            shell.Settings.CloseRequested += OnCloseRequested;
            dialog.Closed += (_, _) => shell.Settings.CloseRequested -= OnCloseRequested;
        }

        _dialogs[kind] = dialog;
        dialog.Show(this);

        // 弹窗也纳入应用窗口组：层级统一（激活任一窗口 → 其余一并抬升；"全体置顶"一起生效）
        WindowManager?.WindowGroup.Track(dialog);
        Shell?.Hotkeys.Attach(dialog);
    }

    // ---------------------------------------------------------------- 文件

    private async void OnOpenComposite(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开组合件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("组合件") { Patterns = ["*.json"] }],
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        var result = shell.Load(path);
        WindowManager?.SyncAll();

        await ShowMessageAsync(
            "加载组合件",
            result.Succeeded
                ? $"已加载：{path}" + (result.Notices.Count > 0 ? $"\n{string.Join("\n", result.Notices)}" : string.Empty)
                : $"被拒绝：{string.Join("；", result.Failures)}");
    }

    private async void OnSaveComposite(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存全部为组合件",
            SuggestedFileName = "workspace.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("组合件") { Patterns = ["*.json"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        var result = shell.SaveAll(path);

        await ShowMessageAsync(
            "保存组合件",
            result.Succeeded
                ? $"已保存：{path}" + (result.Notices.Count > 0 ? $"\n{string.Join("\n", result.Notices)}" : string.Empty)
                : $"失败：{string.Join("；", result.Failures)}");
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnShowHotkeys(object? sender, RoutedEventArgs e) => _ = ShowMessageAsync(
        "快捷键与用法",
        "M 模块清单 · I Issue · S 设置 · T 拓扑诊断\n" +
        "L 叠加信息层 · G 几何吸附 · Ctrl+L 全部锁定\n\n" +
        "模块窗口：拖边框移动（Ctrl 独立拖动、Alt 临时不吸附）；边中点/角点缩放；\n" +
        "右下角按钮：停（运行/停用）· 锁（锁定/解锁）· 隐（隐藏）· ×（移除模块）\n\n" +
        "建网：双击窗口边缘的口标记块 → 填网络名 → 两侧同名即自动连成一条网络；\n" +
        "清空网络名即退出该网络。连接关系可在「拓扑诊断」中查看（只读）。");

    private void OnShowAbout(object? sender, RoutedEventArgs e) => _ = ShowMessageAsync(
        "关于",
        "何译位调试助手（HYWDebugAssistant）\n模块 = 独立窗口的拼图式工程调试助手");

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

        await window.ShowDialog(this);
    }

    // ---------------------------------------------------------------- 快捷键

}
