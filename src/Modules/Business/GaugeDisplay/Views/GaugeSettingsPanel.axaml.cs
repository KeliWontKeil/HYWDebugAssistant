using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.GaugeDisplay.Views;

/// <summary>仪表盘的设置面板正文（独立视图：预览器可单独打开）。</summary>
[ModuleViewDesignData(typeof(GaugeDesignData))]
public sealed partial class GaugeSettingsPanel : ModuleView
{
    /// <summary>构造面板正文。</summary>
    public GaugeSettingsPanel() => AvaloniaXamlLoader.Load(this);
}
