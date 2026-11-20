using System.Globalization;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui;

/// <summary>
/// **颜色文本的唯一换算**：<c>0xAARRGGBB</c> 与界面文本互相转换。
/// </summary>
/// <remarks>
/// 取色器给出的是 <c>uint</c>，值层存的是文本；两个方向都只走这里，
/// 模块、视图与颜色控件不再各写一份十六进制解析。
/// </remarks>
public static class ColorText
{
    /// <summary>把界面文本解析成 ARGB；接受 6 位或 8 位十六进制（可带 <c>#</c>）。</summary>
    /// <param name="text">候选文本。</param>
    /// <param name="argb">解析结果（<c>0xAARRGGBB</c>）。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParseArgb(string? text, out uint argb)
    {
        argb = 0;
        var token = text?.Trim().TrimStart('#') ?? string.Empty;
        if (token.Length == 6)
        {
            token = "FF" + token;
        }

        return token.Length == 8
            && uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb);
    }

    /// <summary>把 ARGB 格式化成稳定的 8 位大写十六进制（不带前缀）。</summary>
    /// <param name="argb">颜色（<c>0xAARRGGBB</c>）。</param>
    /// <returns>8 位文本。</returns>
    public static string FormatArgb(uint argb) =>
        argb.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>把 Avalonia 颜色打包成 ARGB。</summary>
    /// <param name="color">颜色。</param>
    /// <returns><c>0xAARRGGBB</c>。</returns>
    public static uint ToArgb(Color color) =>
        ((uint)color.A << 24)
        | ((uint)color.R << 16)
        | ((uint)color.G << 8)
        | color.B;
}
