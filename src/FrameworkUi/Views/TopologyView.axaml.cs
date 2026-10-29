using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>拓扑诊断弹窗视图（状态过滤 / 复制诊断）。</summary>
public partial class TopologyView : UserControl
{
    /// <summary>创建视图。</summary>
    public TopologyView() => InitializeComponent();

    private void OnSelectState(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TopologyViewModel topology
            && sender is Control { DataContext: TopologyStateFilterViewModel filter })
        {
            topology.SelectStateCommand.Execute(filter);
        }
    }

    private async void OnCopyNetwork(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;   // 避免点击头部按钮时顺带折叠/展开卡片

        if (DataContext is not TopologyViewModel
            || sender is not Control { DataContext: TopologyNetworkViewModel network })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(network.ToDetailText());
        }
        catch (Exception)
        {
            // 剪贴板不可用：拓扑是只读诊断，不因复制失败打断用户。
        }
    }
}
