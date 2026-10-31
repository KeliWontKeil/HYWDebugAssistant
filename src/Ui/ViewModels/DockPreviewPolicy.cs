using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **吸附建网预览的集合过滤**：只保留“本次拖动组会新连上的端口对”。
/// </summary>
/// <remarks>
/// 接触判定（Core）回答“几何上谁碰谁”；预览还要回答“哪一条是**现在松手会新增**的连接”：
/// <list type="bullet">
///   <item>已有网络里的端口对不再点亮；</item>
///   <item>与本次拖动组无关的静止模块对不再点亮。</item>
/// </list>
/// 这两条是展示策略，放在视图模型侧；Core 的建网判定仍保持纯几何。
/// </remarks>
public static class DockPreviewPolicy
{
    /// <summary>过滤预览候选。</summary>
    /// <param name="candidates">Core 给出的接触候选。</param>
    /// <param name="existing">已存在的 `源 → 宿` 字面量对。</param>
    /// <param name="movingInstanceIds">本次拖动组内的实例。</param>
    public static IReadOnlyList<ConnectionCandidateView> Filter(
        IReadOnlyList<ConnectionCandidateView> candidates,
        IReadOnlySet<(string Source, string Target)> existing,
        IReadOnlyCollection<string> movingInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(movingInstanceIds);

        var moving = new HashSet<string>(movingInstanceIds, StringComparer.Ordinal);
        var result = new List<ConnectionCandidateView>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (existing.Contains((candidate.SourceLiteral, candidate.TargetLiteral)))
            {
                continue;
            }

            if (!BelongsTo(candidate.SourceLiteral, moving) && !BelongsTo(candidate.TargetLiteral, moving))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private static bool BelongsTo(string literal, HashSet<string> instanceIds) =>
        instanceIds.Any(id => literal.StartsWith(id + ".", StringComparison.Ordinal));
}
