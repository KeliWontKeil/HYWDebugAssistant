using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Images;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// 组合页代码后置：默认目录保存、另存为选择器、组合导入、列表刷新/加载。
/// </summary>
public partial class CompositePageView : UserControl
{
    /// <summary>创建视图。</summary>
    public CompositePageView() => InitializeComponent();

    private CompositePageViewModel? ViewModel => DataContext as CompositePageViewModel;

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(CompositePageView).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(CompositePageView).Assembly, key, args);

    private static readonly FilePickerFileType ImageFileType = new(Loc("composite.imageFileType"))
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
    };

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(viewModel.DefaultCompositeDirectory);
            var path = viewModel.BuildDefaultSavePath();

            if (Directory.Exists(path))
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var overwrite = await ConfirmDialog.ShowAsync(
                    owner,
                    Loc("composite.overwrite.title"),
                    Loc("composite.overwrite.message", path),
                    Loc("composite.overwrite.confirm"));

                if (!overwrite)
                {
                    return;
                }
            }

            viewModel.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportError(Loc("composite.error.saveToDefault", ex.Message));
        }
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(viewModel.DefaultCompositeDirectory);
            var start = await storage.TryGetFolderFromPathAsync(viewModel.DefaultCompositeDirectory);

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc("composite.saveAs.title"),
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } parent)
            {
                var name = Path.GetFileName(viewModel.BuildDefaultSavePath());
                viewModel.Save(Path.Combine(parent, name));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportError(Loc("composite.error.saveAsDialog", ex.Message));
        }
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var start = await storage.TryGetFolderFromPathAsync(viewModel.DefaultCompositeDirectory);
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc("composite.import.title"),
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            {
                var conflicts = viewModel.GetImportConflicts(path);
                if (conflicts.Count > 0)
                {
                    var decisions = await ImportConflictDialog.ResolveAsync(
                        TopLevel.GetTopLevel(this) as Window,
                        conflicts);

                    // 取消 = 整批不导入（不要退化成"全部未决定失败"的一串报错）。
                    if (decisions is null)
                    {
                        return;
                    }

                    viewModel.ImportComposite(path, decisions);
                    return;
                }

                viewModel.ImportComposite(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportError(Loc("composite.error.importDialog", ex.Message));
        }
    }

    private void OnRefreshFilesClicked(object? sender, RoutedEventArgs e) =>
        ViewModel?.RefreshCompositeFiles();

    private void OnLoadSelectedClicked(object? sender, RoutedEventArgs e) =>
        ViewModel?.LoadSelectedComposite();

    private async void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedCompositeFile is not { } file)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            Loc("composite.delete.title"),
            Loc("composite.delete.message", file.DisplayName),
            Loc("composite.delete.confirm"));

        if (confirmed)
        {
            viewModel.DeleteSelectedComposite();
        }
    }

    private async void OnRenameClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedCompositeFile is not { } file)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var newName = await RenameDialog.ShowAsync(owner, file.DisplayName);
        if (newName is { Length: > 0 })
        {
            viewModel.RenameSelectedComposite(newName);
        }
    }

    private async void OnChangeCoverClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.SelectedCompositeFile is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        try
        {
            var png = await ImagePicker.PickAsync(owner, 9d / 5d, Loc("composite.cover.pickTitle"));
            if (png is not null)
            {
                viewModel.SetSelectedCover(png);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportError(Loc("composite.error.coverPick", ex.Message));
        }
    }

    private void OnCompositeFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<ListBoxItem>() is null)
        {
            return;
        }

        ViewModel?.LoadSelectedComposite();
    }
}
