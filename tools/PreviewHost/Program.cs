using Avalonia;

namespace HYWDebugAssistant.PreviewHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>预览器通过这个入口配置 Application；方法名与签名遵循 Avalonia 约定。</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PreviewApp>()
        .UsePlatformDetect()
        .LogToTrace();
}
