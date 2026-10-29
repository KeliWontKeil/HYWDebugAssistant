using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>Issue 弹窗视图的代码后置（剪贴板复制与严重度按钮）。</summary>
public partial class IssuesView : UserControl
{
    /// <summary>创建视图。</summary>
    public IssuesView() => InitializeComponent();

    private void OnSelectSeverity(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IssuesPanelViewModel issues
            && sender is Control { DataContext: IssueSeverityFilterViewModel filter })
        {
            issues.SelectSeverityCommand.Execute(filter);
        }
    }

    private async void OnCopyMessage(object? sender, RoutedEventArgs e) =>
        await CopyAsync(issues => issues.SelectedMessageText, Loc("issues.status.copiedMessage"));

    private async void OnCopyDetail(object? sender, RoutedEventArgs e) =>
        await CopyAsync(issues => issues.SelectedDetailText, Loc("issues.status.copiedDetail"));

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(IssuesView).Assembly, key);

    private async Task CopyAsync(Func<IssuesPanelViewModel, string?> select, string success)
    {
        if (DataContext is not IssuesPanelViewModel issues)
        {
            return;
        }

        var text = select(issues);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            issues.StatusMessage = Loc("issues.status.clipboardUnavailable");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            issues.StatusMessage = success;
        }
        catch (Exception)
        {
            issues.StatusMessage = Loc("issues.status.copyFailed");
        }
    }
}
