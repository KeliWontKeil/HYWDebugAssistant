using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.DataMonitor.Views;

/// <summary>数据显示器窗口的视图（布局在 <c>DataMonitorView.axaml</c>）。</summary>
public sealed partial class DataMonitorView : ModuleView
{
    /// <summary>构造视图。</summary>
    public DataMonitorView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>数据显示器的视图工厂。</summary>
public sealed class DataMonitorViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new DataMonitorView();
}

