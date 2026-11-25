namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **拼图图的唯一真源**：贴合谓词、外壳单侧拼合规则、两个容差常量，以及"拼接件"的构建。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须只有一个真源</b>：层与组若各写一份贴合判定，迟早出现"层说同层、组说不同组"的分叉。
/// 所以本类**不接受调用方传容差** —— 容差只有一个值，两边不可能传不同的数。
/// </para>
/// <para>
/// <b>两种关系必须分开，不能混成一个</b>：
/// <list type="bullet">
///   <item><b>拼接件</b>（<see cref="Build"/>，供层规划）= 模块↔模块贴合闭包，件内两两不重合，**外壳不入件**。
///     一件 = 一个层号（刚体只能在一个平面上）。</item>
///   <item><b>接地整体</b>（由调用方用 <see cref="TilingAssembly.TouchesShell"/> 再合并）= 全部贴壳件 + 外壳，
///     **跨带**，用于拖动/选择/抬升。拖任一贴壳件必然带动外壳，外壳一动手全部贴壳件都得跟，
///     否则贴壳边被撕开（表现为模块之间像"弹簧"一样互相追赶）。</item>
/// </list>
/// 把外壳塞进并查集会毁掉前者（所有贴壳件被压成同一层）；不给外壳建桥会毁掉后者。
/// </para>
/// <para>
/// <b>拼接判据里没有"层"</b>：层是"谁压谁"的编号，而"能不能拼"的真正判据是"两块之间有没有遮挡"。
/// 同层 ⇒ 不重合（充分条件）成立，但不重合 ⇏ 同层（两块可以因别处的原因被抬到不同层）。
/// 用充分条件当门禁会误杀：组的一部分被抬层后，另一端悬空的模块再也拼不上。
/// </para>
/// </remarks>
public static class TilingGraph
{
    /// <summary>外壳在拼图图里的节点 id（与模块实例 id 不会冲突）。</summary>
    public const string ShellNodeId = "#shell";

    /// <summary>模块↔模块的贴合容差（px）——宽：拼接是主用途，值得给宽容差。</summary>
    public const double ModuleJoinTolerance = SnapEngine.FlushTolerance;

    /// <summary>
    /// 外壳↔模块的贴合容差（px）——**严格**：只有真的贴上（吸附产生的 0 间隙，允许 1px 取整误差）才算。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ModuleJoinTolerance"/> **刻意不同**：模块之间"差一点也算拼上"对拼装手感友好；
    /// 外壳用它会让"停在附近、没吸附"的模块在拖动外壳时被一并带走（表现为"没产生吸附却粘在壳上"）。
    /// </remarks>
    public const double ShellJoinTolerance = 1;

    /// <summary>两块模块是否**贴合**（相邻边在容差内接触、交叉轴有重叠、且**不重合**）。</summary>
    /// <param name="a">模块 A 矩形。</param>
    /// <param name="b">模块 B 矩形。</param>
    public static bool AreFlush(CanvasRect a, CanvasRect b) => SnapEngine.AreJoined(a, b, ModuleJoinTolerance);

    /// <summary>模块是否**单侧贴在**外壳右边缘上（只认右边缘；压在壳上不算）。</summary>
    /// <param name="module">模块矩形。</param>
    /// <param name="shell">外壳矩形。</param>
    public static bool IsShellGlued(CanvasRect module, CanvasRect shell)
    {
        if (!module.IsValid || !shell.IsValid)
        {
            return false;
        }

        if (SnapEngine.RectsOverlap(module, shell))
        {
            return false;   // 压在壳上不算贴壳：它会挡住壳自己的内容
        }

        var verticalOverlap = Math.Min(module.Bottom, shell.Bottom) - Math.Max(module.Top, shell.Top);
        return Math.Abs(module.Left - shell.Right) <= ShellJoinTolerance
            && verticalOverlap > ShellJoinTolerance;
    }

    /// <summary>
    /// 把节点表切成**拼接件**（贴合闭包 + 件内两两不重合）。返回顺序 = 叠放键升序。
    /// </summary>
    /// <param name="nodes">可见模块节点（无效矩形会被忽略）。</param>
    /// <param name="shell">外壳矩形（只用于算 <see cref="TilingAssembly.TouchesShell"/>，不参与并件）。</param>
    /// <param name="zOrder">实例 → 叠放序号（越大越靠上；缺失用 <see cref="int.MinValue"/>）。</param>
    /// <remarks>
    /// <b>贪心的确定性</b>：按 (z 升序, id 序数) 逐个并入——被压住的那块先入件，所以冲突时"底板那边"胜出；
    /// 与候选件的合并顺序也按件内最小处理序号，结果与输入顺序、与 LINQ 迭代顺序无关。
    /// 唯一会受 z 序变化影响的是"链式传递重合"（a 贴 b、b 贴 c、但 a 与 c 重合）这种病态几何：
    /// 抬高其中一块可能改变归属——两种归属都是合法的非重合件，取舍只有观感差异。
    /// </remarks>
    public static IReadOnlyList<TilingAssembly> Build(
        IReadOnlyList<TilingNode> nodes,
        CanvasRect? shell,
        Func<string, int> zOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(zOrder);

        var valid = new List<TilingNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Rect.IsValid)
            {
                valid.Add(node);
            }
        }

        if (valid.Count == 0)
        {
            return [];
        }

        var ordered = valid
            .OrderBy(node => zOrder(node.Id))
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var zs = new int[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            zs[i] = zOrder(ordered[i].Id);
        }

        // 并查集：parent 用"处理序号"，first 记件内最小处理序号（合并顺序与叠放键的确定依据），
        // pieceTopZ 记件内最大叠放序号（决定"归属哪一件"时上层优先）。
        var parent = new int[ordered.Count];
        var first = new int[ordered.Count];
        var pieceTopZ = new int[ordered.Count];
        var members = new List<int>[ordered.Count];

        for (var i = 0; i < ordered.Count; i++)
        {
            parent[i] = i;
            first[i] = i;
            pieceTopZ[i] = zs[i];
            members[i] = [i];
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            // 与 x 贴合、且已入件的那些"件"（同一件只登记一次）。
            var candidates = new List<int>();
            for (var j = 0; j < i; j++)
            {
                if (!AreFlush(ordered[i].Rect, ordered[j].Rect))
                {
                    continue;
                }

                var root = Find(parent, j);
                if (!candidates.Contains(root))
                {
                    candidates.Add(root);
                }
            }

            // **上层优先**：同时贴得上两件时归叠放位置更高的那件（平局回落到件内最小序号，保证确定性）。
            // 典型场景是两块几何完全重合的组给出的贴合边一模一样，此时"归谁"直接决定新模块的层号。
            candidates.Sort((a, b) =>
            {
                var byTop = pieceTopZ[b].CompareTo(pieceTopZ[a]);
                return byTop != 0 ? byTop : first[a].CompareTo(first[b]);
            });

            foreach (var candidate in candidates)
            {
                var target = Find(parent, i);
                var root = Find(parent, candidate);

                if (root == target)
                {
                    continue;
                }

                // **真刚体不能自己压自己**：任一交叉对正面积重合 ⇒ 这两件不能拼成一件。
                // 这一条就是"压住组内某块的模块永远进不了这一件"，也是"层号差多少都能拼"的保底：
                // 只要两块互不遮挡，层号差多少都可以拼。
                if (Overlaps(members[target], members[root], ordered))
                {
                    continue;
                }

                parent[root] = target;
                first[target] = Math.Min(first[target], first[root]);
                pieceTopZ[target] = Math.Max(pieceTopZ[target], pieceTopZ[root]);
                members[target].AddRange(members[root]);
                members[root] = [];
            }
        }

        var buckets = new Dictionary<int, List<int>>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var root = Find(parent, i);
            if (!buckets.TryGetValue(root, out var bucket))
            {
                bucket = [];
                buckets[root] = bucket;
            }

            bucket.Add(i);
        }

        var assemblies = new List<TilingAssembly>(buckets.Count);
        foreach (var bucket in buckets.Values)
        {
            bucket.Sort();

            var assemblyMembers = new List<TilingNode>(bucket.Count);
            var topZ = int.MinValue;
            var touchesShell = false;

            foreach (var index in bucket)
            {
                var node = ordered[index];
                assemblyMembers.Add(node);
                topZ = Math.Max(topZ, zs[index]);

                if (shell is { IsValid: true } shellRect && IsShellGlued(node.Rect, shellRect))
                {
                    touchesShell = true;
                }
            }

            assemblies.Add(new TilingAssembly(assemblyMembers, topZ, touchesShell));
        }

        assemblies.Sort((left, right) =>
        {
            var byZ = left.TopZ.CompareTo(right.TopZ);
            return byZ != 0 ? byZ : string.CompareOrdinal(left.Members[0].Id, right.Members[0].Id);
        });

        return assemblies;
    }

    /// <summary>并查集查找（带路径折半；用静态局部函数避免为几行代码引入一个类型）。</summary>
    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    /// <summary>两组成员之间是否存在**正面积重合**的对（刚体不能自压）。</summary>
    private static bool Overlaps(List<int> left, List<int> right, List<TilingNode> ordered)
    {
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                if (SnapEngine.RectsOverlap(ordered[a].Rect, ordered[b].Rect))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
