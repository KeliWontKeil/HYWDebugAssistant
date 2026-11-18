using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.ScaleBarVertical.Views;

/// <summary>纵向刻度条的设置面板正文（独立视图：预览器可单独打开）。</summary>
[ModuleViewDesignData(typeof(ScaleBarDesignData))]
public sealed partial class ScaleBarSettingsPanel : ModuleView
{
    /// <summary>构造面板正文。</summary>
    public ScaleBarSettingsPanel() => AvaloniaXamlLoader.Load(this);
}
