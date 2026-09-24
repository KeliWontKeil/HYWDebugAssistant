using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>模块清单弹窗视图的代码后置：**选择模块包目录**（唯一的窗口相关逻辑）。</summary>
public partial class ModuleCatalogView : UserControl
{
    /// <summary>创建视图。</summary>
    public ModuleCatalogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
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
