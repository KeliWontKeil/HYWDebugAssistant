using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>文本日志视图的代码后置：新行到达时**自动滚到底**（最新的数据始终可见）。</summary>
public partial class TextLogView : UserControl
{
    /// <summary>创建视图。</summary>
    public TextLogView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    private INotifyCollectionChanged? _observed;

    private void Attach()
    {
        Detach();

        if (DataContext is TextLogElementViewModel log)
        {
            _observed = log.Lines;
            _observed.CollectionChanged += OnLinesChanged;
            ScrollToEnd();
        }
    }

    private void Detach()
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnLinesChanged;
            _observed = null;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd() =>
        Dispatcher.UIThread.Post(
            () => LogScroll.ScrollToEnd(),
            DispatcherPriority.Background);
}
