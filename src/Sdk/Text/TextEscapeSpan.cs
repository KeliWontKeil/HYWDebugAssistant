namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **一个合法 C 字符串转义组**在文本里的位置。
/// </summary>
/// <remarks>
/// 它只描述“哪里是一个会被解释的转义”，供输入面着色；真正解码由
/// <see cref="TextEscapes.Normalize(string?)"/> 使用同一个识别器完成。
/// </remarks>
/// <param name="Start">起始字符下标（指向反斜杠）。</param>
/// <param name="Length">包含反斜杠在内的字符数。</param>
public readonly record struct TextEscapeSpan(int Start, int Length);
