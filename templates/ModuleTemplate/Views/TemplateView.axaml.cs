using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Template.MyModule.Views;

/// <summary>模板视图根：继承 <see cref="ModuleView"/>，预览器自动获得主题与设计时 token。</summary>
public partial class TemplateView : ModuleView
{
    /// <summary>创建视图。</summary>
    public TemplateView() => AvaloniaXamlLoader.Load(this);
}
