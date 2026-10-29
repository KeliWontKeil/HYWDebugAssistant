using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.Udp.Views;

/// <summary>UDP 窗口的视图（布局在 <c>UdpView.axaml</c>）。</summary>
[ModuleViewDesignData(ExternalAccessStandardIds.Udp)]
public sealed partial class UdpView : ModuleView
{
    /// <summary>构造视图。</summary>
    public UdpView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>UDP 的视图工厂。</summary>
public sealed class UdpViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new UdpView();
}
