namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// 一段不可显示字符标记 <c>⟨XX⟩</c> 在文本里的位置。
/// </summary>
/// <param name="Start">起始字符下标。</param>
/// <param name="Length">字符数（含尖括号）。</param>
internal readonly record struct MarkerSpan(int Start, int Length);

/// <summary>
/// **不可显示字符标记的纯解析**：找完整的 <c>⟨…⟩</c> 段，供增量文本视图着色。
/// </summary>
/// <remarks>
/// 只认成对的尖括号；未闭合的左尖括号按普通文本处理，不吞后续内容。
/// 解析不分配中间字符串，结果供绘制层按可视行使用。
/// </remarks>
internal static class MarkerText
{
    /// <summary>找出文本里的标记段。</summary>
    /// <param name="text">可视行文本。</param>
    /// <returns>按出现顺序排列的标记段。</returns>
    public static List<MarkerSpan> FindSpans(string text)
    {
        var spans = new List<MarkerSpan>();

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '⟨')
            {
                continue;
            }

            var end = text.IndexOf('⟩', index + 1);
            if (end < 0)
            {
                break;
            }

            spans.Add(new MarkerSpan(index, end - index + 1));
            index = end;
        }

        return spans;
    }
}
