using System;
using Avalonia;

namespace HYWDebugAssistant;

internal sealed class Program
{
    // 初始化代码。在 AppMain 调用之前，不要使用任何 Avalonia/第三方 API
    // 或依赖 SynchronizationContext 的代码：此时框架尚未初始化。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 配置（请勿删除；可视化设计器也会使用）。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 注：模板默认在 DEBUG 下调用 .WithDeveloperTools()，该 API 来自 AvaloniaUI.DiagnosticsSupport
            // （Avalonia Accelerate 商业线，超出许可约束）——已按 ADR-0024 移除；开发期诊断改用日志与 VS 调试。
            .WithInterFont()
            .LogToTrace();
}
