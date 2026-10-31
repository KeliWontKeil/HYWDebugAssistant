using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Ui.ModuleViews;
using HYWDebugAssistant.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HYWDebugAssistant.App;

/// <summary>
/// Avalonia 应用入口：初始化（资源就绪）→ 组合根装配（`CompositionRoot`）→ 显示外壳窗口
/// + 启动**模块窗口管理器**（拼图式多窗口）。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 焦点只在点到其他控件或窗口时离开，**点其他空白不释放焦点** ⇒
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

            // **先落外壳启动尺寸，再建管理器**：自动恢复的模块要等外壳显示后的真实外框就绪才创建；
            // 启动位置在 `Opened` 中以“程序性放置”作用域应用，不会被当成用户拖动外壳。
            window.PrepareForShow();

            // 模块窗口管理器：把模块集合映射为一批无边框顶层窗口（外壳是可被贴靠的"起始点"）
            var windowManager = new ModuleWindowManager(shell.Windows, window);
            window.WindowManager = windowManager;

            // **进程级屏幕缩放**：只给"读旧组合件（< v3）时把布局从物理像素换成 DIP"用一次。
            // 放在窗口建好之后取（那时才有 RenderScaling）；窗口在这一步还没 `Show`，
            // 但 `Screens` 已经可用，取不到就保持 1（新格式的组合件不受影响）。
            CompositeService.ScreenScale = window.Screens.Primary?.Scaling ?? 1.0;

            // **文件选择请求面**：按实例解析到模块窗口，选定后把路径经**输入通道**投回模块。
            // 挂在这里的理由：宿主窗口只有 UI 侧知道（`ModuleWindowManager` 持有实例 → 窗口的映射）。
            var command = provider.GetRequiredService<HYWDebugAssistant.Core.Api.IRuntimeCommand>();

            provider.GetRequiredService<HYWDebugAssistant.Core.Runtime.ModuleRuntime>().FileAccessFor =
                instanceId => new ModuleFileAccess(
                    instanceId,
                    id => windowManager.WindowFor(id),
                    (source, path) => _ = command.SubmitInput(instanceId, source, path));

            desktop.MainWindow = window;

            // **收尾只在真正退出时执行**：`ShutdownRequested` 是可取消的请求，不是终局；
            // 在那里拆窗口会让"取消关闭"之后留下半拆状态。`Exit` 是收尾决定之后的终局事件。
            var shutdown = new AppShutdown(() =>
            {
                windowManager.Dispose();   // 合并整体平移的落盘必须先于服务容器释放
                _provider?.Dispose();
            });

            // **退出唯一出口**：询问 → 保存 → 关模块窗 → 放行外壳；平台各条关闭路径都汇到这里。
            var exit = new ShellExitCoordinator(
                window,
                window,
                window,
                windowManager.CloseAll,
                () => window.Close(),
                shutdown.Run,
                () => shell.Windows.ModuleCount);

            window.ExitCoordinator = exit;

            desktop.Exit += (_, _) => exit.OnAppExit();

            desktop.ShutdownRequested += (_, e) =>
            {
                if (exit.CanClose)
                {
                    return;   // 已经走过退出流程：不再取消，避免与平台互相拉扯
                }

                // 走到这里只可能是系统关机或程序化 Shutdown（用户的关窗请求先经过 `MainWindow.Closing`）：
                // 不弹窗，静默保存后放行。`IsOSShutdown` 在 Avalonia 12.1.2 是 internal，无法据此分支。
                e.Cancel = true;
                _ = exit.RequestAsync(isOsShutdown: true);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
