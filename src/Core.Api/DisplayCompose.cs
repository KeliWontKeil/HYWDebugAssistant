namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **显示文本的拼装规则**（唯一一份）："追加文本"与"追加一行"只差**换行归谁表达**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁用它</b>：① <see cref="DisplayBuffer"/> 真正落地时；② 模块**回读**（保存）时把"还没被 UI 取走
///     的挂起追加"叠到快照上 —— 两处必须是**同一条规则**，否则"刚收到就保存"会与屏幕上看到的不一致；</item>
///   <item><b>规则只有一行</b>：换行（或缓冲还是空的）⇒ 起一条新的；否则 ⇒ 接在当前最后一条的后面
///     （"追加数据应直接追加在后面，不做任何标记" —— 2026-09 用户裁定）；</item>
///   <item>文本里的换行**字符**不在这里处理：那是数据自己带的，原样保留。</item>
/// </list>
/// </remarks>
public static class DisplayCompose
{
    /// <summary>把一段文本按"要不要换行"并入行表（**唯一一份拼装实现**）。</summary>
    /// <param name="lines">行表（就地修改）。</param>
    /// <param name="text">文本。</param>
    /// <param name="lineBreak">这一段之后是否由**通道**换行。</param>
    public static void Apply(List<string> lines, string text, bool lineBreak)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(text);

        if (lineBreak || lines.Count == 0)
        {
            lines.Add(text);
            return;
        }

        lines[^1] += text;
    }
}
