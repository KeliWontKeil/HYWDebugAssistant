using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>原始 → 波形 支持的协议。</summary>
public enum RawToWaveformProtocol
{
    /// <summary>自定义文本帧：<c>[帧头][空格][数据…][空格][帧尾]</c>。</summary>
    [ConfigurationChoice("", TitleKey = RawToWaveformText.ChoiceTextFrame)]
    TextFrame = 0,

    /// <summary>VOFA+ FireWater：逗号分隔、换行结尾的文本流。</summary>
    [ConfigurationChoice("", TitleKey = RawToWaveformText.ChoiceFireWater)]
    FireWater = 1,

    /// <summary>VOFA+ JustFloat：小端 f32 数组 + 帧尾 <c>00 00 80 7F</c>。</summary>
    [ConfigurationChoice("", TitleKey = RawToWaveformText.ChoiceJustFloat)]
    JustFloat = 2,
}
