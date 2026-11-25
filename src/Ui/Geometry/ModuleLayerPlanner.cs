namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **模块层规划器**：把「拼接件 + 叠放顺序 + 重合关系」派生成层号。
/// </summary>
/// <remarks>
/// <para>
/// 层不是用户配置的数字，也不是全局 z-order 序号。全局序号会让任意一次抬升把同一件刚体的两半推成不同层，
/// 于是组被拆散。这里按「叠放带」派生，并且**以拼接件为最小单位**：
/// <list type="number">
///   <item>先把模块按贴合闭包切成<see cref="TilingAssembly">拼接件</see>（件内两两不重合）；</item>
///   <item>每件的叠放键 = 成员里最大的 z 序（刚体的位置 = 它最上方成员的位置）；</item>
///   <item>按叠放键升序单趟：件层号 = 下方所有**与它正面积重合**的件最大层号 + 1；
///     没有下方重合件 = 0；与外壳重合 = 从 1 起；</item>
///   <item><b>件内所有成员写同一个层号</b>——一件刚体只能在一个平面上，否则抬层会把组拆成两半。</item>
/// </list>
/// </para>
/// <para>
/// <b>层不参与"能不能拼"</b>：拼接判据是「贴合 + 互不遮挡」，见 <see cref="TilingGraph"/>。
/// 本规划器的输出只用于「谁压谁」的建模，以及对齐/端口/尺寸参照的**带内过滤**。
/// 拿「同层」当拼接门禁，是把「不重合」的充分条件当必要条件用，必然误杀（组被抬层后悬空段再也拼不上）。
/// </para>
/// <para>
/// 不变量：同层模块两两不重合；重合必然分到不同层。外壳是层 0 的基础实体，不参与"同层"比较；
/// 它只用于让"压在外壳上的模块"升层。用户点击/拖动/抬升改变 z-order 后重新规划即可。
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

        var nodes = new List<TilingNode>(modules.Count);
        foreach (var (instanceId, rect) in modules)
        {
            if (rect.IsValid)
            {
                nodes.Add(new TilingNode(instanceId, rect));
            }
            else
            {
                result[instanceId] = 0;   // 零尺寸/无效几何：谈不上层关系，给 0 层
            }
        }

        var shellRect = shell is { IsValid: true } validShell ? validShell : (CanvasRect?)null;
        var assemblies = TilingGraph.Build(nodes, shellRect, zOrder);

        // 显式排序：本方法的不变量依赖"已处理件都在下方"，不能依赖调用方的返回顺序。
        var ordered = assemblies.OrderBy(assembly => assembly.TopZ).ToList();
        var placed = new List<(IReadOnlyList<TilingNode> Members, int Layer)>(ordered.Count);

        foreach (var assembly in ordered)
        {
            var layer = 0;

            if (shellRect is { } shellValue)
            {
                foreach (var member in assembly.Members)
                {
                    if (SnapEngine.RectsOverlap(member.Rect, shellValue))
                    {
                        layer = 1;   // 压在外壳上的模块从 1 层起（外壳是层 0 的底板）
                        break;
                    }
                }
            }

            foreach (var (lowerMembers, lowerLayer) in placed)
            {
                if (Overlaps(assembly.Members, lowerMembers))
                {
                    layer = Math.Max(layer, lowerLayer + 1);
                }
            }

            foreach (var member in assembly.Members)
            {
                result[member.Id] = layer;
            }

            placed.Add((assembly.Members, layer));
        }

        return result;
    }

    /// <summary>两件之间是否有**任一对**成员正面积重合（件的层序由它决定）。</summary>
    private static bool Overlaps(IReadOnlyList<TilingNode> left, IReadOnlyList<TilingNode> right)
    {
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                if (SnapEngine.RectsOverlap(a.Rect, b.Rect))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
