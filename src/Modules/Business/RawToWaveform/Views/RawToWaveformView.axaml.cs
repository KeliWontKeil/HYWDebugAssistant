using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.RawToWaveform.Views;

/// <summary>原始 → 波形 的视图根。</summary>
/// <remarks>
/// 预览器没有宿主上下文；这里声明配置类型，设计期才能从同一份标注反射出字段标题与编辑器。
/// 参数行的可见性由协议决定：文本帧只显示帧头/帧尾，FireWater 只显示前缀，JustFloat 两者都不显示。
/// </remarks>
[ModuleViewDesignData(typeof(RawToWaveformConfig))]
public partial class RawToWaveformView : ModuleView, IModuleViewElement
{
    private Control? _textParams;
    private Control? _fireWaterParams;
    private ModuleLabel? _definition;
    private ModuleLabel? _sample;

    /// <summary>创建视图。</summary>
    public RawToWaveformView() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _definition ??= this.FindControl<ModuleLabel>("ProtocolDefinition");
        _sample ??= this.FindControl<ModuleLabel>("ProtocolSample");

        var protocol = ProtocolText.Parse(context.ReadField(RawToWaveformModule.ProtocolField)?.Value);

        ApplyProtocol(protocol);

        if (protocol is { } kind && _definition is not null && _sample is not null)
        {
            var help = RawToWaveformText.ProtocolHelp(kind);
            _definition.Text = help.Definition;
            _sample.Text = help.Sample;
        }
    }

    /// <summary>按协议切换参数行可见性；认不出协议时两行都收起。</summary>
    internal void ApplyProtocol(RawToWaveformProtocol? protocol)
    {
        _textParams ??= this.FindControl<Control>("TextParams");
        _fireWaterParams ??= this.FindControl<Control>("FireWaterParams");

        if (_textParams is not null)
        {
            _textParams.IsVisible = protocol is RawToWaveformProtocol.TextFrame;
        }

        if (_fireWaterParams is not null)
        {
            _fireWaterParams.IsVisible = protocol is RawToWaveformProtocol.FireWater;
        }
    }
}
