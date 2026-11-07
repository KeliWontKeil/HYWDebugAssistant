using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Samples.UiModule.Views;

/// <summary>带界面样例的视图根（继承 <see cref="ModuleView"/>，预览器自动获得主题与设计时 token）。</summary>
public partial class UiSampleView : ModuleView
{
    /// <summary>创建视图。</summary>
    public UiSampleView() => AvaloniaXamlLoader.Load(this);
}
