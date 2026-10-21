using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.HexSender.Views;

/// <summary>HEX 发送器窗口的视图（布局在 <c>HexSenderView.axaml</c>）。</summary>
public sealed partial class HexSenderView : ModuleView
{
    /// <summary>构造视图。</summary>
    public HexSenderView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>HEX 发送器的视图工厂。</summary>
public sealed class HexSenderViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new HexSenderView();
}

