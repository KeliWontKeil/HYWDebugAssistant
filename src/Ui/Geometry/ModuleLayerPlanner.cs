namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **模块层规划器**：把“叠放顺序 + 重合关系”派生成模块层号。
/// </summary>
/// <remarks>
/// <para>
/// 层不是用户配置的数字，也不是全局 z-order 序号。全局序号会让任意一次抬升把模块推成不同层，
/// 于是任何两个模块都无法拼接。这里按“叠放带”派生：
/// <list type="number">
///   <item>每个当前拼接组（或孤立模块）是一个实体；</item>
///   <item>实体按最上层窗口的 z-order 自底向顶排序；</item>
///   <item>实体层号 = 下方所有**与它重合**的实体最大层号 + 1；没有下方重合实体 = 0。</item>
/// </list>
/// </para>
/// <para>
/// 不变量：同层实体两两不重合；重合必然分到不同层。用户点击/拖动/抬升改变 z-order 后重新规划即可。
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

        var parent = new int[modules.Count];
        for (var index = 0; index < parent.Length; index++)
        {
            parent[index] = index;
        }

        for (var left = 0; left < modules.Count; left++)
        {
            for (var right = left + 1; right < modules.Count; right++)
            {
                if (SnapEngine.AreJoined(modules[left].Rect, modules[right].Rect))
                {
                    Union(parent, left, right);
                }
            }
        }

        var buckets = new Dictionary<int, List<int>>();
        for (var index = 0; index < modules.Count; index++)
        {
            var root = Find(parent, index);
            if (!buckets.TryGetValue(root, out var bucket))
            {
                bucket = [];
                buckets[root] = bucket;
            }

            bucket.Add(index);
        }

        var entities = new List<Entity>(buckets.Count + (shell is { IsValid: true } ? 1 : 0));

        foreach (var bucket in buckets.Values)
        {
            var rects = new List<CanvasRect>(bucket.Count);
            var topOrder = int.MinValue;
            var stableId = string.Empty;

            foreach (var index in bucket)
            {
                rects.Add(modules[index].Rect);
                topOrder = Math.Max(topOrder, zOrder(modules[index].InstanceId));

                if (stableId.Length == 0
                    || string.CompareOrdinal(modules[index].InstanceId, stableId) < 0)
                {
                    stableId = modules[index].InstanceId;
                }
            }

            entities.Add(new Entity(rects, topOrder, stableId, bucket, isShell: false));
        }

        if (shell is { IsValid: true } shellRect)
        {
            entities.Add(new Entity([shellRect], int.MinValue, string.Empty, [], isShell: true));
        }

        // 外壳固定在最底；其余按 z-order 自底向顶，稳定序用最小实例 ID。
        var ordered = entities
            .OrderBy(entity => entity.IsShell ? int.MinValue : entity.TopOrder)
            .ThenBy(entity => entity.StableId, StringComparer.Ordinal)
            .ToList();

        var resolved = new List<Entity>(ordered.Count);

        foreach (var entity in ordered)
        {
            var layer = 0;

            foreach (var lower in resolved)
            {
                if (Overlaps(entity, lower))
                {
                    layer = Math.Max(layer, lower.Layer + 1);
                }
            }

            entity.Layer = layer;
            resolved.Add(entity);

            if (entity.IsShell)
            {
                continue;
            }

            foreach (var index in entity.MemberIndexes)
            {
                result[modules[index].InstanceId] = layer;
            }
        }

        return result;
    }

    private static bool Overlaps(Entity left, Entity right)
    {
        foreach (var a in left.Rects)
        {
            foreach (var b in right.Rects)
            {
                if (SnapEngine.RectsOverlap(a, b))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot != rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
    }

    private sealed class Entity
    {
        public Entity(
            IReadOnlyList<CanvasRect> rects,
            int topOrder,
            string stableId,
            IReadOnlyList<int> memberIndexes,
            bool isShell)
        {
            Rects = rects;
            TopOrder = topOrder;
            StableId = stableId;
            MemberIndexes = memberIndexes;
            IsShell = isShell;
        }

        public IReadOnlyList<CanvasRect> Rects { get; }

        public int TopOrder { get; }

        public string StableId { get; }

        public IReadOnlyList<int> MemberIndexes { get; }

        public bool IsShell { get; }

        public int Layer { get; set; }
    }
}
