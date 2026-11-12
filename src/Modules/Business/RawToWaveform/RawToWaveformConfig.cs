using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>原始 → 波形 的实例配置。</summary>
public sealed class RawToWaveformConfig
{
    /// <summary>协议。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldProtocol)]
    public RawToWaveformProtocol Protocol { get; set; } = RawToWaveformProtocol.TextFrame;

    /// <summary>自定义文本帧的帧头。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldHeader)]
    public string TextHeader { get; set; } = "ab";

    /// <summary>自定义文本帧的帧尾。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldFooter)]
    public string TextFooter { get; set; } = "cd";

    /// <summary>写入波形帧的通道组编号；0 是保留值，波形显示器会丢弃整帧。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldGroup, Minimum = 1, Maximum = int.MaxValue)]
    public int GroupId { get; set; } = 1;

    /// <summary>写入波形帧的声明速率。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldRate, Minimum = 1, Maximum = 1_000_000, Unit = "Hz")]
    public double RateHz { get; set; } = 1000;
}
