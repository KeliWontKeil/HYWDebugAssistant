using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **截图工具**：把真实渲染栈下的窗口画成 PNG，交给人眼验收（这里没有判据）。
/// </summary>
/// <remarks>
/// 无头渲染平台是**进程级**配置：本程序集的会话只建一次（见 <see cref="SnapshotSession"/>），
/// 三个生成入口串行执行（见 <c>AssemblyInfo</c>）。
/// </remarks>
internal static class SnapshotTool
{
    /// <summary>截图输出根目录（仓库内 `artifacts\snapshots\`，已被忽略，直接在资源管理器打开看）。</summary>
    public static string OutputRoot { get; } = Path.Combine(FindRepoRoot(), "artifacts", "snapshots");

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>把窗口按当前尺寸渲染成 PNG（相对 <see cref="OutputRoot"/> 的子路径）。</summary>
    public static void Save(Window window, string relativePath)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);

        var file = Path.Combine(OutputRoot, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);

#pragma warning disable CS0618   // 截图工具：旧的 Save(string) 足够
        bitmap.Save(file);
#pragma warning restore CS0618

        Console.WriteLine($"[snapshot] {file}");
    }

    /// <summary>把视觉树写成文本（排查“控件在不在、被谁盖住/折叠”用）。</summary>
    public static void DumpTree(Visual visual, string relativePath, int depth = 0, List<string>? lines = null)
    {
        lines ??= [];
        var bounds = visual.Bounds;
        lines.Add($"{new string(' ', depth * 2)}{visual.GetType().Name} " +
                  $"name={(visual as Control)?.Name ?? "-"} @({bounds.X:0},{bounds.Y:0}) {bounds.Width:0}x{bounds.Height:0}");

        foreach (var child in visual.GetVisualChildren())
        {
            DumpTree(child, relativePath, depth + 1, lines);
        }

        if (depth == 0)
        {
            var file = Path.Combine(OutputRoot, relativePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, lines);
        }
    }

    /// <summary>从程序目录向上找解决方案根（截图输出不写进程序输出目录）。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any() || directory.EnumerateFiles("*.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录（未找到 *.slnx/*.sln）");
    }
}

/// <summary>
/// 让无头会话使用**真实渲染栈**（`UseHeadlessDrawing = false` + Skia）——否则 `RenderTargetBitmap` 出不了像素。
/// </summary>
public static class SkiaHeadlessEntryPoint
{
    /// <summary>会话入口：按 Avalonia 约定提供 <c>BuildAvaloniaApp</c>。</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SnapshotApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
