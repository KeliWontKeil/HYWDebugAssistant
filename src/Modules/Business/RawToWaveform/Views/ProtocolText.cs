namespace HYWDebugAssistant.Modules.RawToWaveform.Views;

/// <summary>把配置字段的显示文本还原成协议值；认不出来时返回 <c>null</c>（按“全部显示”处理）。</summary>
internal static class ProtocolText
{
    public static RawToWaveformProtocol? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Enum.TryParse<RawToWaveformProtocol>(text, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        if (text.Contains("FireWater", StringComparison.OrdinalIgnoreCase))
        {
            return RawToWaveformProtocol.FireWater;
        }

        if (text.Contains("JustFloat", StringComparison.OrdinalIgnoreCase))
        {
            return RawToWaveformProtocol.JustFloat;
        }

        if (text.Contains('文'))
        {
            return RawToWaveformProtocol.TextFrame;
        }

        return null;
    }
}
