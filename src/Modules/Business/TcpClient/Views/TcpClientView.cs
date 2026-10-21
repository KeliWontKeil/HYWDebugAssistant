using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.TcpClient.Views;

/// <summary>TCP 客户端窗口的视图（布局在 <c>TcpClientView.axaml</c>）。</summary>
[ModuleViewDesignData(typeof(TcpClientConfig))]
public sealed partial class TcpClientView : ModuleView
{
    /// <summary>构造视图。</summary>
    public TcpClientView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>TCP 客户端的视图工厂。</summary>
public sealed class TcpClientViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new TcpClientView();
}

