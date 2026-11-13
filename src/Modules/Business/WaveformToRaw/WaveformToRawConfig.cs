using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Modules.WaveformToRaw;

/// <summary>波形 → 原始 的实例配置。</summary>
/// <remarks>
/// 只有协议是配置字段；帧头/帧尾/前缀/小数位/组号是运行期参数，由模块用值声明持有
/// （见 <c>WaveformToRawModule</c>），不存放在配置对象里。
/// </remarks>
public sealed class WaveformToRawConfig
{
    /// <summary>协议。</summary>
    [ConfigurationField(TitleKey = WaveformToRawText.FieldProtocol)]
    public WaveformToRawProtocol Protocol { get; set; } = WaveformToRawProtocol.TextFrame;
}
