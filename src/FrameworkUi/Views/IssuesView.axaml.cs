using Avalonia.Controls;
using Avalonia.Interactivity;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>Issue 弹窗视图的代码后置（"清除过滤"按钮需要一次性重置四维过滤）。</summary>
public partial class IssuesView : UserControl
{
    /// <summary>创建视图。</summary>
    public IssuesView() => InitializeComponent();

    private void OnClearFilter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IssuesPanelViewModel issues)
        {
            issues.ClearFilterCommand.Execute(null);
        }
    }
}
