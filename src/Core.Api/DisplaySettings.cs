namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **文本显示缓冲的默认值唯一事实源**（通信/内存类高级参数）。
/// </summary>
/// <remarks>
/// 这里放显示文本框的内存上限与不可显示字符的呈现方式；1472 字节模块载荷、4096 字节契约字段等属于协议常量，不在这里。
/// </remarks>
public static class DisplayDefaults
{
    /// <summary>默认文本行容量（行）。</summary>
    public const int TextLineCapacity = 20_000;

    /// <summary>默认文本总字符预算（字符）。</summary>
    public const int TextCharBudget = 1_048_576;

    /// <summary>文本行容量下限。</summary>
    public const int TextLineCapacityMin = 100;

    /// <summary>文本行容量上限。</summary>
    public const int TextLineCapacityMax = 200_000;

    /// <summary>文本字符预算下限（字符）。</summary>
    public const int TextCharBudgetMin = 64 * 1024;

    /// <summary>文本字符预算上限（字符）。</summary>
    public const int TextCharBudgetMax = 16 * 1024 * 1024;
}

/// <summary>文本显示缓冲的应用级设置：内存上限与不可显示字符的呈现方式。</summary>
public sealed record DisplaySettings
{
    /// <summary>每个文本显示缓冲的最大行数。</summary>
    public int TextLineCapacity { get; init; } = DisplayDefaults.TextLineCapacity;

    /// <summary>每个文本显示缓冲的总字符预算。</summary>
    public int TextCharBudget { get; init; } = DisplayDefaults.TextCharBudget;

    /// <summary>不可显示字符显示为警示色 <c>⟨XX⟩</c>（true）还是方框 <c>□</c>（false）。</summary>
    public bool ShowControlCharacterMarkers { get; init; } = true;
}
