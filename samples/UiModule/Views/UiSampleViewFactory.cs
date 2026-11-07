using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Samples.UiModule.Views;

/// <summary>视图工厂：框架按**每个实例**调用一次。</summary>
/// <remarks>工厂必须无状态：它可能被多个实例复用。</remarks>
public sealed class UiSampleViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new UiSampleView();
}
