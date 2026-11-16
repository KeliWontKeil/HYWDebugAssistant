using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.TextSender.Views;

/// <summary>文本发送器窗口的视图（布局在 <c>TextSenderView.axaml</c>）。</summary>
public sealed partial class TextSenderView : ModuleView, IModuleViewElement
{
    /// <summary>构造视图。</summary>
    public TextSenderView() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context) =>
        ModuleViewDefaults.EnsureSubmitKey(context, TextSenderViewKeys.SubmitKey);
}

/// <summary>文本发送器的视图工厂。</summary>
public sealed class TextSenderViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new TextSenderView();
}
