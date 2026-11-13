using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>原始 → 波形 的实例配置。</summary>
/// <remarks>
/// 只有协议是配置字段（决定解析形状）；帧头/帧尾/前缀/组号/采样率是运行期参数，
/// 由模块用值声明持有（见 <c>RawToWaveformModule</c>），不存放在配置对象里。
/// </remarks>
public sealed class RawToWaveformConfig
{
    /// <summary>协议。</summary>
    [ConfigurationField(TitleKey = RawToWaveformText.FieldProtocol)]
    public RawToWaveformProtocol Protocol { get; set; } = RawToWaveformProtocol.TextFrame;
}
