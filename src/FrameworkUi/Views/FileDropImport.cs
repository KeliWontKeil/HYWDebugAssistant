using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **文件/文件夹拖入导入**：把 Avalonia 的文件拖放通道接到外壳的导入入口。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>只认 <see cref="DataFormats.Files"/>；识别"是模块包还是组合包"由 Core 的
///     <c>ClassifyImportPath</c> 回答，本类不写清单文件名字面量；</item>
///   <item>拖到外壳或模块窗口都有效（模块窗口把事件转给外壳视图模型），面板/模态窗不参与；</item>
///   <item>平台不支持拖放时，事件根本不会到达 ⇒ 静默退化，菜单导入仍可用。</item>
/// </list>
/// </remarks>
internal static class FileDropImport
{
    private static readonly ConditionalWeakTable<Window, object> Attached = new();
    private static readonly object Marker = new();

    /// <summary>给一个窗口挂上拖入导入（幂等）。</summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="shell">外壳视图模型（导入入口）。</param>
    public static void Attach(Window window, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shell);

        if (Attached.TryGetValue(window, out _))
        {
            return;
        }

        Attached.Add(window, Marker);
        DragDrop.SetAllowDrop(window, true);
        window.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        window.AddHandler(DragDrop.DropEvent, (sender, e) => OnDrop(sender, e, shell));
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object? sender, DragEventArgs e, ShellViewModel shell)
    {
        e.Handled = true;

        if (!e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.TryGetFiles() is not { } files)
        {
            return;
        }

        foreach (var item in files)
        {
            if (item.TryGetLocalPath() is { Length: > 0 } path)
            {
                shell.ImportPath(path);
            }
        }
    }
}
