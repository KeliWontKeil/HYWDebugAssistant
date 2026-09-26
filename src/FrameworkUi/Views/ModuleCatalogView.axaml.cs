using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>模块清单弹窗视图的代码后置：**选择模块包目录** + **双击列表项直接加入现场**。</summary>
public partial class ModuleCatalogView : UserControl
{
    /// <summary>创建视图。</summary>
    public ModuleCatalogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
    }

    /// <summary>
    /// **双击列表项 = 直接加入现场**【用户第十六轮反馈】。
    /// </summary>
    /// <remarks>
    /// 只在**确实双击到某一项**时生效（双击空白处不动作、也不报"请先选择"）。
    /// 动作走视图模型上的同一条路径（<see cref="ModuleCatalogViewModel.AddSelected"/>），不复制业务逻辑。
    /// </remarks>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnCatalogDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<ListBoxItem>() is null)
        {
            return;
        }

        if (DataContext is ModuleCatalogViewModel catalog)
        {
            _ = catalog.AddSelected();
        }
    }

    private void Hook()
    {
        if (DataContext is ModuleCatalogViewModel catalog)
        {
            catalog.PickPackageDirectoryRequested += OnPickDirectory;
            catalog.OpenSourceDirectoryRequested += OnOpenSourceDirectory;
        }
    }

    /// <summary>在文件管理器里打开来源根（诊断用：用户能立刻看到目录里到底有什么）。</summary>
    private static void OnOpenSourceDirectory(object? sender, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    private async void OnPickDirectory(object? sender, EventArgs e)
    {
        if (DataContext is not ModuleCatalogViewModel catalog)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择模块包目录（含模块清单文件）",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            catalog.PackageDirectory = path;
        }
    }
}
