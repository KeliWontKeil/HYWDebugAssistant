using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.WaveformToRaw.Views;

/// <summary>波形 → 原始 的视图根。</summary>
/// <remarks>
/// 预览器没有宿主上下文；这里声明配置类型，设计期才能从同一份标注反射出字段标题与编辑器。
/// </remarks>
[ModuleViewDesignData(typeof(WaveformToRawConfig))]
public partial class WaveformToRawView : ModuleView, IModuleViewElement
{
    private Control? _textParams;
    private Control? _fireWaterParams;
    private ModuleLabel? _definition;
    private ModuleLabel? _sample;
    private WaveformToRawProtocol? _lastProtocol;

    /// <summary>创建视图。</summary>
    public WaveformToRawView() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _textParams ??= this.FindControl<Control>("TextParams");
        _fireWaterParams ??= this.FindControl<Control>("FireWaterParams");
        _definition ??= this.FindControl<ModuleLabel>("ProtocolDefinition");
        _sample ??= this.FindControl<ModuleLabel>("ProtocolSample");

        var protocol = ProtocolText.Parse(context.ReadField(WaveformToRawModule.ProtocolField)?.Value);
        if (protocol == _lastProtocol)
        {
            return;
        }

        _lastProtocol = protocol;
        if (_textParams is not null)
        {
            _textParams.IsVisible = protocol is null or WaveformToRawProtocol.TextFrame;
        }

        if (_fireWaterParams is not null)
        {
            _fireWaterParams.IsVisible = protocol is null or WaveformToRawProtocol.FireWater;
        }

        if (protocol is { } kind && _definition is not null && _sample is not null)
        {
            var help = WaveformToRawText.ProtocolHelp(kind);
            _definition.Text = help.Definition;
            _sample.Text = help.Sample;
        }
    }
}
