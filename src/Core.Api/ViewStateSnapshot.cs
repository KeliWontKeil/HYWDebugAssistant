namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// 一个实例的**可持久化视图状态**：用户输入文本（仅声明持久化的输入）、当前页、折叠态，
/// 以及控件自己持久化的图片状态。
/// </summary>
/// <param name="InputTexts">输入源名 → 当前文本。</param>
/// <param name="SelectedPages">容器状态键 → 当前页（0 基）。</param>
/// <param name="Collapsed">容器状态键 → 是否折叠。</param>
public sealed record ViewStateSnapshot(
    IReadOnlyDictionary<string, string> InputTexts,
    IReadOnlyDictionary<string, int> SelectedPages,
    IReadOnlyDictionary<string, bool> Collapsed)
{
    /// <summary>
    /// 控件持久化的图片状态（无 ⇒ null）。图片属于视图/控件状态，不属于模块壁纸。
    /// </summary>
    public ViewImageStateSnapshot? Image { get; init; }

    /// <summary>空快照。</summary>
    public static ViewStateSnapshot Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, int>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>是否没有任何内容。</summary>
    public bool IsEmpty => InputTexts.Count == 0 && SelectedPages.Count == 0 && Collapsed.Count == 0 && Image is null;
}

/// <summary>视图/控件持久化的图片状态：包内资产引用 + 归一化裁切。</summary>
/// <param name="AssetRef">包内资产引用（<c>assets/&lt;sha256&gt;.&lt;ext&gt;</c>，扩展名取真实图片格式）。</param>
/// <param name="Crop">归一化裁切（0–1，整图为 0/0/1/1）。</param>
public sealed record ViewImageStateSnapshot(string AssetRef, ViewImageCrop Crop);

/// <summary>归一化图片裁切（0–1）。</summary>
public readonly record struct ViewImageCrop(double X, double Y, double Width, double Height)
{
    /// <summary>整图。</summary>
    public static ViewImageCrop Full => new(0, 0, 1, 1);
}
