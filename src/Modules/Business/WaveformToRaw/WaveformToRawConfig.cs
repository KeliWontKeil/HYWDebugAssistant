using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.WaveformToRaw;

/// <summary>波形 → 原始 的实例配置。</summary>
public sealed class WaveformToRawConfig
{
    /// <summary>协议。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldProtocol)]
    public WaveformToRawProtocol Protocol { get; set; } = WaveformToRawProtocol.TextFrame;

    /// <summary>自定义文本帧的帧头。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldHeader)]
    public string TextHeader { get; set; } = "ab";

    /// <summary>自定义文本帧的帧尾。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldFooter)]
    public string TextFooter { get; set; } = "cd";

    /// <summary>FireWater 的行前缀（空 = 不写）。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldPrefix)]
    public string FireWaterPrefix { get; set; } = string.Empty;

    /// <summary>小数位数；0 = 最短往返表示。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldDecimals, Minimum = 0, Maximum = 9)]
    public int Decimals { get; set; }

    /// <summary>通道组编号（开启筛选时只转换该组）。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldGroup, Minimum = 1, Maximum = int.MaxValue)]
    public int GroupId { get; set; } = 1;

    /// <summary>只转换 <see cref="GroupId"/> 指定的组。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldOnlyGroup)]
    public bool OnlyThisGroup { get; set; }
}
