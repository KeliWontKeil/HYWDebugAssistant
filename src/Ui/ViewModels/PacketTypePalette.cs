using Avalonia.Media;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **数据包类型 → 颜色** 的调色板（端口标记上那条**窄颜色条**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>【用户第十三轮反馈】标记上**不再显示类型文本**（网络标签名通常很长，放不下），改为**一条窄颜色条**表示数据包类型；
///     "这个窄条的颜色先预留可修改，具体映射方式我之后做数据包类型相关修改的时候再告诉你" ——
///     因此本类现在只有 <see cref="Placeholder"/>，**映射表在这里补**，XAML 与视图模型都不用改；</item>
///   <item>颜色遵循视觉基线：取值应来自 `Tokens.axaml` 的角色色（`SemanticOk/Warn/Error/Idle` 等）或未来的类型色板，
///     **不在这里写死任意色值**（当前占位色与"当前未映射"一致，刻意保持中性）；</item>
///   <item>映射落地时建议：`PortMarkerViewModel.TypeBrush` 改为按契约形状（<c>ContractShape.Describe()</c> 的文本，
///     或将来 SDK 暴露的类型枚举）查本表，未知类型回退 <see cref="Placeholder"/>。</item>
/// </list>
/// </remarks>
public static class PacketTypePalette
{
    /// <summary>
    /// **类型 → 颜色**映射表（**当前为空**：用户明确"映射方式之后再给"，故一律回退占位色）。
    /// </summary>
    /// <remarks>
    /// 映射落地时**只在这里加条目**（键 = 类型文本，即 `PortMarkerViewModel.TypeText` / `ContractShape.Describe()`），
    /// 未知类型自动回退 <see cref="Placeholder"/> —— **视图模型与 XAML 都不用改**。
    /// </remarks>
    private static readonly Dictionary<string, IBrush> Map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>**未映射类型**的占位色（中性灰蓝；用户明确"先预留可修改"）。</summary>
    public static IBrush Placeholder { get; } = new SolidColorBrush(Color.Parse("#9AA4B2"));

    /// <summary>取某个数据包类型对应的颜色（**未映射 → <see cref="Placeholder"/>**）。</summary>
    /// <param name="typeText">类型文本（如 <c>f32</c>；空/未知 → 占位色）。</param>
    /// <returns>颜色条用的画刷。</returns>
    public static IBrush For(string? typeText) =>
        typeText is { Length: > 0 } key && Map.TryGetValue(key, out var brush) ? brush : Placeholder;
}
