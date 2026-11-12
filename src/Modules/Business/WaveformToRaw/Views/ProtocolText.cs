namespace HYWDebugAssistant.Modules.WaveformToRaw.Views;

/// <summary>把配置字段的显示文本还原成协议值；认不出来时返回 <c>null</c>（按“全部显示”处理）。</summary>
internal static class ProtocolText
{
    public static WaveformToRawProtocol? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Enum.TryParse<WaveformToRawProtocol>(text, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        if (text.Contains("FireWater", StringComparison.OrdinalIgnoreCase))
        {
            return WaveformToRawProtocol.FireWater;
        }

        if (text.Contains("JustFloat", StringComparison.OrdinalIgnoreCase))
        {
            return WaveformToRawProtocol.JustFloat;
        }

        if (text.Contains('文'))
        {
            return WaveformToRawProtocol.TextFrame;
        }

        return null;
    }
}
