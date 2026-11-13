using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.WaveformToRaw.Views;

/// <summary>波形 → 原始 的视图根。</summary>
/// <remarks>
/// 预览器没有宿主上下文；这里声明配置类型，设计期才能从同一份标注反射出字段标题与编辑器。
/// 参数行的可见性由协议决定：文本帧只显示帧头/帧尾，FireWater 只显示前缀，JustFloat 两者都不显示。
/// </remarks>
[ModuleViewDesignData(typeof(WaveformToRawConfig))]
public partial class WaveformToRawView : ModuleView, IModuleViewElement
{
    private Control? _textParams;
    private Control? _fireWaterParams;
    private ModuleLabel? _definition;
    private ModuleLabel? _sample;
    private WaveformToRawProtocol? _shownKind;

    /// <summary>创建视图。</summary>
    public WaveformToRawView() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _definition ??= this.FindControl<ModuleLabel>("ProtocolDefinition");
        _sample ??= this.FindControl<ModuleLabel>("ProtocolSample");

        var protocol = ProtocolText.Parse(context.ReadField(WaveformToRawModule.ProtocolField)?.Value);

        ApplyProtocol(protocol);

        if (protocol is { } kind && kind != _shownKind && _definition is not null && _sample is not null)
        {
            _shownKind = kind;
            var help = WaveformToRawText.ProtocolHelp(kind);
            _definition.Text = help.Definition;
            _sample.Text = help.Sample;
        }
    }

    /// <summary>按协议切换参数行可见性；认不出协议时两行都收起。</summary>
    internal void ApplyProtocol(WaveformToRawProtocol? protocol)
    {
        _textParams ??= this.FindControl<Control>("TextParams");
        _fireWaterParams ??= this.FindControl<Control>("FireWaterParams");

        if (_textParams is not null)
        {
            _textParams.IsVisible = protocol is WaveformToRawProtocol.TextFrame;
        }

        if (_fireWaterParams is not null)
        {
            _fireWaterParams.IsVisible = protocol is WaveformToRawProtocol.FireWater;
        }
    }
}
