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

            desktop.ShutdownRequested += (_, _) =>
            {
                windowManager.Dispose();
                _provider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
