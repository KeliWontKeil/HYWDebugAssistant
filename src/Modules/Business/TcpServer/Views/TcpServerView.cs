using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.TcpServer.Views;

/// <summary>TCP 服务端窗口的视图（布局在 <c>TcpServerView.axaml</c>）。</summary>
[ModuleViewDesignData(typeof(TcpServerConfig))]
public sealed partial class TcpServerView : ModuleView
{
    /// <summary>构造视图。</summary>
    public TcpServerView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>TCP 服务端的视图工厂。</summary>
public sealed class TcpServerViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new TcpServerView();
}

