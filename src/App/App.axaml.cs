using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HYWDebugAssistant.App;

/// <summary>
/// Avalonia 应用入口：初始化（资源就绪）→ 组合根装配（`CompositionRoot`）→ 显示外壳窗口
/// + 启动**模块窗口管理器**（拼图式多窗口，ADR-0034）。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 【2026-09 用户裁定】"鼠标必须点其他控件/窗口才能离开焦点，**点其他空白不行**" ⇒
        // 全局一处安装（`Window` 的类处理器 + 隧道阶段）：主窗口与模块窗口同时生效。
        // 放在 `Initialize`（而不是 `OnFrameworkInitializationCompleted`）里，是因为无头测试会话
        // 不一定走到后者 —— 规则必须**与窗口是否存在无关地**就位。
        PointerFocusRelease.Install();
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var (shell, provider) = CompositionRoot.Build();
            _provider = provider;

            var window = new MainWindow { DataContext = shell };

            // 模块窗口管理器：把模块集合映射为一批无边框顶层窗口（外壳是可被贴靠的"起始点"）
            var windowManager = new ModuleWindowManager(shell.Windows, window);
            window.WindowManager = windowManager;

            desktop.MainWindow = window;

            desktop.ShutdownRequested += (_, _) =>
            {
                windowManager.Dispose();
                _provider?.Dispose();
            };

            // 【临时自检】HYW_DRAG_SELFTEST=1 时脚本化拖动，用于定位"靠近时抖动"（定位后删除）
            DragSelfTest.RunIfRequested(desktop, provider, shell);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
