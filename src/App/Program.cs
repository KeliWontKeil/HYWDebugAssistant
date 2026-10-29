using System;
using Avalonia;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Settings;

namespace HYWDebugAssistant.App;

internal sealed class Program
{
    /// <summary>隐藏开关：只写出默认设置文件后退出（**不初始化 Avalonia / 不建窗口**）。</summary>
    internal const string EmitDefaultSettingsSwitch = "--emit-default-settings";

    // 初始化代码。在 AppMain 调用之前，不要使用任何 Avalonia/第三方 API
    // 或依赖 SynchronizationContext 的代码：此时框架尚未初始化。
    [STAThread]
    public static int Main(string[] args)
    {
        // **隐藏开关必须在 Avalonia 初始化之前处理**：发布打包要能在无 UI 的机器上生成出厂设置文件。
        if (TryEmitDefaultSettings(args, out var exitCode))
        {
            return exitCode;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>
    /// 解析隐藏开关 `--emit-default-settings &lt;path&gt;`：命中即写出默认设置并返回 <see langword="true"/>。
    /// </summary>
    /// <param name="args">命令行参数（可为 null）。</param>
    /// <param name="exitCode">命中时的进程退出码（0 = 写出成功；1 = 写入失败；2 = 缺少路径）。</param>
    /// <returns>是否命中该开关（命中 ⇒ 调用方**不得**再初始化 Avalonia）。</returns>
    internal static bool TryEmitDefaultSettings(string[]? args, out int exitCode)
    {
        exitCode = 0;

        if (args is null)
        {
            return false;
        }

        var index = Array.FindIndex(
            args,
            arg => string.Equals(arg, EmitDefaultSettingsSwitch, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return false;
        }

        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            Console.Error.WriteLine($"[emit-default-settings] 缺少输出路径：{EmitDefaultSettingsSwitch} <path>");
            exitCode = 2;
            return true;
        }

        try
        {
            if (!SettingsService.TryWriteDefaults(args[index + 1], new ApplicationSettings(), out var error))
            {
                Console.Error.WriteLine($"[emit-default-settings] 写入失败：{error}");
                exitCode = 1;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"[emit-default-settings] 写入失败：{ex.Message}");
            exitCode = 1;
        }

        return true;
    }

    // Avalonia 配置（请勿删除；可视化设计器也会使用）。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 注：模板默认在 DEBUG 下调用 .WithDeveloperTools()，该 API 来自 AvaloniaUI.DiagnosticsSupport
            // （Avalonia Accelerate 商业线，超出许可约束）——故移除该调用；开发期诊断改用日志与 VS 调试。
            .LogToTrace();
}
