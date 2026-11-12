using System.Globalization;
using System.Text;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>数值与文本之间的统一转换规则（不变文化、无千分位、不逐帧分配）。</summary>
internal static class ValueFormat
{
    /// <summary>小数位 → 格式串；0 表示最短往返表示。</summary>
    private static readonly string[] FormatSpecifiers =
        ["R", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9"];

    /// <summary>把数值写入目标字符区间；失败（目标过小）返回 <c>false</c>。</summary>
    public static bool TryFormat(float value, int decimals, Span<char> destination, out int written)
    {
        var specifier = FormatSpecifiers[Math.Clamp(decimals, 0, FormatSpecifiers.Length - 1)];
        return value.TryFormat(destination, out written, specifier, CultureInfo.InvariantCulture);
    }

    /// <summary>按小数位格式化；<paramref name="decimals"/> ≤ 0 表示最短往返表示。</summary>
    public static string Format(float value, int decimals)
    {
        Span<char> buffer = stackalloc char[32];
        return TryFormat(value, decimals, buffer, out var written)
            ? new string(buffer[..written])
            : string.Empty;
    }

    /// <summary>解析一个数字 token（ASCII/UTF-8，不变文化）。</summary>
    public static bool TryParse(ReadOnlySpan<byte> token, out float value)
    {
        value = 0f;
        if (token.IsEmpty || token.Length > 64)
        {
            return false;
        }

        Span<char> chars = stackalloc char[64];
        var written = Encoding.UTF8.GetChars(token, chars);
        return double.TryParse(chars[..written], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
               && TryNarrow(parsed, out value);
    }

    /// <summary>把解析出的 double 收窄为 float；超出 float 范围视为非法。</summary>
    public static bool TryNarrow(double parsed, out float value)
    {
        value = 0f;
        if (double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            // NaN/Inf 参与波形（显示器按断线处理），不做范围判断。
            value = (float)parsed;
            return true;
        }

        if (parsed > float.MaxValue || parsed < float.MinValue)
        {
            return false;
        }

        value = (float)parsed;
        return true;
    }

    /// <summary>ASCII 空白。</summary>
    public static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
