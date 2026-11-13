using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>原始 → 波形 的实例配置。</summary>
/// <remarks>
/// 只有协议是"设置"（决定解析形状）；帧头/帧尾/前缀/组号/采样率是运行期参数，
/// 由模块用输入源持有（见 <c>RawToWaveformModule</c> 的输入声明），不落在配置里。
/// </remarks>
public sealed class RawToWaveformConfig
{
    /// <summary>协议。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldProtocol)]
    public RawToWaveformProtocol Protocol { get; set; } = RawToWaveformProtocol.TextFrame;
}
