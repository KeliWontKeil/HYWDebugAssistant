using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.HexStringConverter.Views;

/// <summary>转换器窗口的视图（布局在 <c>HexStringConverterView.axaml</c>）。</summary>
public sealed partial class HexStringConverterView : ModuleView
{
    /// <summary>构造视图。</summary>
    public HexStringConverterView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>转换器的视图工厂。</summary>
public sealed class HexStringConverterViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new HexStringConverterView();
}
