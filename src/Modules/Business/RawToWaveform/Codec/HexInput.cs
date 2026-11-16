using System.Globalization;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>
/// **模块内的十六进制输入解析**：空白分隔、每段恰好两位、大小写都收。
/// </summary>
/// <remarks>
/// 与 HEX 发送器同一条规则；不放进 SDK —— 控件只知道这是文本，字节语义归模块。
/// 空/全空白视为失败（帧头/帧尾不得为空）。
/// </remarks>
internal static class HexInput
{
    /// <summary>把十六进制文本解析成字节。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="bytes">解析结果（失败时为空数组）。</param>
    /// <returns>是否成功且至少一个字节。</returns>
    public static bool TryParse(string? text, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var segments = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var result = new byte[segments.Length];

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length != 2)
            {
                return false;
            }

            foreach (var character in segment)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            result[index] = byte.Parse(segment, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        bytes = result;
        return true;
    }
}
