namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// 一个实例的**可持久化视图状态**：用户输入文本（仅声明持久化的输入）、当前页、折叠态。
/// </summary>
/// <param name="InputTexts">输入源名 → 当前文本。</param>
/// <param name="SelectedPages">容器状态键 → 当前页（0 基）。</param>
/// <param name="Collapsed">容器状态键 → 是否折叠。</param>
public sealed record ViewStateSnapshot(
    IReadOnlyDictionary<string, string> InputTexts,
    IReadOnlyDictionary<string, int> SelectedPages,
    IReadOnlyDictionary<string, bool> Collapsed)
{
    /// <summary>空快照。</summary>
    public static ViewStateSnapshot Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, int>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>是否没有任何内容。</summary>
    public bool IsEmpty => InputTexts.Count == 0 && SelectedPages.Count == 0 && Collapsed.Count == 0;
}
