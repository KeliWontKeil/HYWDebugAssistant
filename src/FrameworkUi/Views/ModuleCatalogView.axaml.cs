using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>模块清单弹窗视图的代码后置：**外部模块目录导入** + **双击列表项直接加入现场**。</summary>
public partial class ModuleCatalogView : UserControl
{
    /// <summary>创建视图。</summary>
    public ModuleCatalogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
    }

    /// <summary>
    /// **双击列表项 = 直接加入现场**。
    /// </summary>
    /// <remarks>
    /// 只在**确实双击到某一项**时生效（双击空白处不动作、也不报"请先选择"）。
    /// 动作走视图模型上的同一条路径（<see cref="ModuleCatalogViewModel.AddSelected"/>），不复制业务逻辑。
    /// </remarks>
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
            catalog.ImportModulesRequested += OnPickImportDirectory;
        }
    }

    /// <summary>选择外部模块目录：校验后复制到默认 `modules\external`，随后只扫描默认目录。</summary>
    private async void OnPickImportDirectory(object? sender, EventArgs e)
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
            Title = TextCatalog.Get(typeof(ModuleCatalogView).Assembly, "catalog.importTitle"),
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            catalog.ImportDirectory(path);
        }
    }
}
