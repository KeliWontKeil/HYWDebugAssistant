using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.AttitudeDisplay.Views;

/// <summary>三轴姿态的设置面板正文（独立视图：预览器可单独打开）。</summary>
[ModuleViewDesignData(typeof(AttitudeDesignData))]
public sealed partial class AttitudeSettingsPanel : ModuleView
{
    /// <summary>构造面板正文。</summary>
    public AttitudeSettingsPanel() => AvaloniaXamlLoader.Load(this);
}
