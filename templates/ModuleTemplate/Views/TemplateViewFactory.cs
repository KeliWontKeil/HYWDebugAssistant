using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Template.MyModule.Views;

/// <summary>视图工厂：框架按**每个实例**调用一次；工厂必须无状态。</summary>
public sealed class TemplateViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new TemplateView();
}
