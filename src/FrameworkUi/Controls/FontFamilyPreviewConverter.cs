using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HYWDebugAssistant.FrameworkUi.Controls;

/// <summary>
/// **字体名 → 可绘制的 <see cref="FontFamily"/>**（下拉项的字体预览用）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：预览项必须按"该项自己的字体"绘制，而字体名来自设置文件或用户手输，
///     可以是任意字符串；绑定里抛异常只会表现为空白，看不出原因，所以这里统一兜住；</item>
///   <item><b>非法/空名字回退默认字体</b>：这条路径只影响"预览长什么样"，不影响保存的字体名
///     （原名仍原样回写设置）；宁可用默认字体显示，也不要让一项坏名字拖垮整个下拉。</item>
/// </list>
/// </remarks>
public sealed class FontFamilyPreviewConverter : IValueConverter
{
    /// <summary>转换为字体（永不抛；无效名字 ⇒ <see cref="FontFamily.Default"/>）。</summary>
    /// <param name="value">字体名（通常来自下拉项本身）。</param>
    /// <param name="targetType">目标类型（未使用）。</param>
    /// <param name="parameter">转换参数（未使用）。</param>
    /// <param name="culture">区域（未使用；字体名不分区域）。</param>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ToFontFamily(value as string);

    /// <summary>不支持反向转换（预览是只读的显示通道）。</summary>
    /// <param name="value">目标值。</param>
    /// <param name="targetType">源类型。</param>
    /// <param name="parameter">转换参数。</param>
    /// <param name="culture">区域。</param>
    /// <exception cref="NotSupportedException">总是抛出：预览不参与回写。</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>把字体名落成 <see cref="FontFamily"/>（空/非法 ⇒ 默认字体）。</summary>
    /// <param name="name">字体名。</param>
    public static FontFamily ToFontFamily(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FontFamily.Default;
        }

        try
        {
            return new FontFamily(name.Trim());
        }
        catch (Exception)
        {
            // 预览路径必须"能显示"优先：坏名字只影响这一项的观感，不能让设置页崩掉。
            return FontFamily.Default;
        }
    }
}
