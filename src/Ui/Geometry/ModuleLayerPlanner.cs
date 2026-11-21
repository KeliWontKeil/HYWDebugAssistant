namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **模块层规划器**：把“叠放顺序 + 重合关系”派生成模块层号。
/// </summary>
/// <remarks>
/// <para>
/// 层不是用户配置的数字，也不是全局 z-order 序号。全局序号会让任意一次抬升把模块推成不同层，
/// 于是任何两个模块都无法拼接。这里按“叠放带”派生：
/// <list type="number">
///   <item>按窗口叠放顺序自底向顶排列模块；</item>
///   <item>模块层号 = 下方所有**与它正面积重合**的模块最大层号 + 1；没有下方重合模块 = 0；</item>
///   <item>外壳固定为底层参照：与外壳正面积重合的模块从 1 层起。</item>
/// </list>
/// </para>
/// <para>
/// <b>层先于组</b>：本规划器**不看贴合关系**。两个模块即使边贴边，只要它们各自因其它重合关系落在
/// 不同层，就不会被本类合并成同层；成组由 <c>ModuleWindowsViewModel.GroupOfNode</c> 在**同层**
/// 模块之间按贴合连通分量派生。若在这里用 <see cref="SnapEngine.AreJoined"/> 并层，任意进入贴合
/// 容差的模块都会被强行抹平层差异 —— 那正是“跨层拼接”的结构来源。
/// </para>
/// <para>
/// 不变量：同层模块两两不重合；重合必然分到不同层。用户点击/拖动/抬升改变 z-order 后重新规划即可。
/// 外壳是层 0 的基础实体，不参与“同层”比较；它只用于让“压在外壳上的模块”升层。
/// </para>
/// </remarks>
public static class ModuleLayerPlanner
{
    /// <summary>按叠放顺序与重合关系计算每个模块实例的层号；外壳不返回（固定 0）。</summary>
    /// <param name="modules">可见模块实例及其物理矩形。</param>
    /// <param name="shell">外壳物理矩形（可选；作为最底实体参与重合判断）。</param>
    /// <param name="zOrder">实例 → 叠放序号（越大越靠上；缺失用 <see cref="int.MinValue"/>）。</param>
    public static IReadOnlyDictionary<string, int> Plan(
        IReadOnlyList<(string InstanceId, CanvasRect Rect)> modules,
        CanvasRect? shell,
        Func<string, int> zOrder)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(zOrder);

        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        if (modules.Count == 0)
        {
            return result;
        }

        // z-order 相同时用实例 ID 稳定排序：重叠关系必须得到确定的上/下顺序。
        var ordered = modules
            .OrderBy(module => zOrder(module.InstanceId))
            .ThenBy(module => module.InstanceId, StringComparer.Ordinal)
            .ToList();

        var resolved = new List<(CanvasRect Rect, int Layer)>(ordered.Count);

        foreach (var (instanceId, rect) in ordered)
        {
            if (!rect.IsValid)
            {
                result[instanceId] = 0;
                continue;
            }

            var layer = shell is { IsValid: true } shellRect && SnapEngine.RectsOverlap(rect, shellRect)
                ? 1
                : 0;

            foreach (var lower in resolved)
            {
                if (SnapEngine.RectsOverlap(rect, lower.Rect))
                {
                    layer = Math.Max(layer, lower.Layer + 1);
                }
            }

            result[instanceId] = layer;
            resolved.Add((rect, layer));
        }

        return result;
    }
}
