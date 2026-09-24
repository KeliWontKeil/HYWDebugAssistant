namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>吸附辅助线（拖动命中时显示，09 §8）。</summary>
/// <param name="IsVertical">true = 竖线（X 恒定）；false = 横线（Y 恒定）。</param>
/// <param name="Position">线所在坐标（竖线为 X，横线为 Y）。</param>
/// <param name="Start">线段起点（竖线为 Y，横线为 X）。</param>
/// <param name="End">线段终点。</param>
public readonly record struct GuideLine(bool IsVertical, double Position, double Start, double End);

/// <summary>吸附结果。</summary>
/// <param name="X">吸附后的 X（未命中则等于输入）。</param>
/// <param name="Y">吸附后的 Y（未命中则等于输入）。</param>
/// <param name="SnappedX">X 是否命中。</param>
/// <param name="SnappedY">Y 是否命中。</param>
/// <param name="Guides">要显示的辅助线。</param>
public readonly record struct SnapResult(
    double X,
    double Y,
    bool SnappedX,
    bool SnappedY,
    IReadOnlyList<GuideLine> Guides);

/// <summary>
/// **几何吸附**（Q5-1 / C5-2 / C5-1）——纯几何，不依赖任何框架对象，可独立测试。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不做网格吸附</b>（用户裁决）：吸附只发生在**模块之间**与**画布边缘/参考线**；</item>
///   <item><b>贴合优先于对齐</b>：边缘相邻（贴合）优先，其次才是左边对齐/右边对齐等"参考对齐"；</item>
///   <item><b>端口对齐优先（C5-2）</b>：X 方向发生贴合时，Y 方向优先让两模块的端口对齐（"插上"的手感）；
///     有网络标签时不强制端口对齐（由标签块承担对齐职责）——该分支由调用方按是否显示标签决定；</item>
///   <item><b>组关系由位置派生</b>（C5-1）：只保存位置，"是否拼为一组"由贴合谓词 + 连通分量算出，**不单独存储**。</item>
/// </list>
/// </remarks>
public static class SnapEngine
{
    /// <summary>吸附容差默认值（px；09 §5 与 C5-1 约束 4 的具体数值属 Gate C，M1 用 8）。</summary>
    public const double DefaultTolerance = 8;

    /// <summary>辅助线在两端各外伸的长度（视觉取值，Gate C 微调）。</summary>
    private const double GuideOvershoot = 12;

    /// <summary>吸附候选（内部）：偏移 + 是否贴合 + 该候选对应的辅助线坐标与对照区间。</summary>
    private readonly record struct Candidate(double Delta, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd);

    /// <summary>
    /// 把 <paramref name="moving"/> 吸附到 <paramref name="others"/> 的边缘/参考线；返回吸附后的位置与辅助线。
    /// </summary>
    /// <param name="moving">被拖动模块的矩形（当前位置）。</param>
    /// <param name="others">参与吸附的其他模块矩形（**不含**同组成员）。</param>
    /// <param name="tolerance">容差（px）。</param>
    /// <param name="portAlignment">
    /// 端口对齐候选（可选）：<c>(moving 端口 Y, other 端口 Y)</c> 对；
    /// 仅在 X 方向发生**贴合**时启用（C5-2"无网络标签时端口对齐优先"）。
    /// </param>
    public static SnapResult Snap(
        CanvasRect moving,
        IReadOnlyList<CanvasRect> others,
        double tolerance = DefaultTolerance,
        IReadOnlyList<(double MovingY, double OtherY)>? portAlignment = null,
        IReadOnlyList<(bool IsVertical, double Position)>? referenceLines = null)
    {
        ArgumentNullException.ThrowIfNull(others);

        var bestX = FindBest(moving, others, XCandidates, tolerance);
        var bestY = FindBest(moving, others, YCandidates, tolerance);

        // **单边参照线**【M1-16 / ADR-0038 D5】：外壳只提供"右边缘"一条参照 —— 线不是矩形，
        // 用零宽矩形表达会被 `IsValid`（要求正尺寸）过滤掉，故在此单独评估。
        foreach (var (isVertical, position) in referenceLines ?? [])
        {
            if (isVertical)
            {
                bestX = Better(bestX, LineCandidate(position - moving.Left, position, moving.Top, moving.Bottom), tolerance);
                bestX = Better(bestX, LineCandidate(position - moving.Right, position, moving.Top, moving.Bottom), tolerance);
            }
            else
            {
                bestY = Better(bestY, LineCandidate(position - moving.Top, position, moving.Left, moving.Right), tolerance);
                bestY = Better(bestY, LineCandidate(position - moving.Bottom, position, moving.Left, moving.Right), tolerance);
            }
        }

        var x = moving.X + bestX.Delta;
        var y = moving.Y + bestY.Delta;
        var snappedX = bestX.Found;
        var snappedY = bestY.Found;

        // 端口对齐优先：X 已贴合时，Y 优先选"端口对齐"的偏移（比"上边对齐"更符合拼接手感）
        if (snappedX && bestX.IsFlush && portAlignment is { Count: > 0 })
        {
            var best = double.NaN;
            foreach (var (movingY, otherY) in portAlignment)
            {
                var delta = otherY - movingY;
                if (Math.Abs(delta) <= tolerance && (double.IsNaN(best) || Math.Abs(delta) < Math.Abs(best)))
                {
                    best = delta;
                }
            }

            if (!double.IsNaN(best))
            {
                y = moving.Y + best;
                snappedY = true;
            }
        }

        var snappedRect = moving.At(x, y);
        var guides = new List<GuideLine>(2);

        if (snappedX)
        {
            guides.Add(new GuideLine(
                IsVertical: true,
                Position: bestX.GuidePosition,
                Start: Math.Min(snappedRect.Top, bestX.OtherStart) - GuideOvershoot,
                End: Math.Max(snappedRect.Bottom, bestX.OtherEnd) + GuideOvershoot));
        }

        if (snappedY)
        {
            guides.Add(new GuideLine(
                IsVertical: false,
                Position: bestY.GuidePosition,
                Start: Math.Min(snappedRect.Left, bestY.OtherStart) - GuideOvershoot,
                End: Math.Max(snappedRect.Right, bestY.OtherEnd) + GuideOvershoot));
        }

        return new SnapResult(x, y, snappedX, snappedY, guides);
    }

    /// <summary>
    /// **贴合谓词**（C5-1）：两模块的边缘在容差内贴合即视为拼接。
    /// </summary>
    /// <remarks>
    /// 竖直边贴合（左右相邻）要求**竖直方向有重叠**，水平边贴合同理——否则"隔着老远但边缘数值巧合"会被误判。
    /// </remarks>
    public static bool AreJoined(CanvasRect a, CanvasRect b, double tolerance = DefaultTolerance)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return false;
        }

        var verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        var horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);

        var flushLeftRight = Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance;
        var flushTopBottom = Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance;

        return (flushLeftRight && verticalOverlap > tolerance)
            || (flushTopBottom && horizontalOverlap > tolerance);
    }

    /// <summary>
    /// **拼接组**（C5-1）：贴合关系的**连通分量**（并查集）；组不是容器，不含语义、不影响运行。
    /// </summary>
    /// <param name="modules">(实例 ID, 矩形) 列表。</param>
    /// <param name="tolerance">容差（px）。</param>
    /// <returns>每个分量的实例 ID 列表（按输入顺序，仅含 ≥2 个成员的分量）。</returns>
    public static IReadOnlyList<IReadOnlyList<string>> FindGroups(
        IReadOnlyList<(string Id, CanvasRect Rect)> modules,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var parent = new int[modules.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        for (var i = 0; i < modules.Count; i++)
        {
            for (var j = i + 1; j < modules.Count; j++)
            {
                if (AreJoined(modules[i].Rect, modules[j].Rect, tolerance))
                {
                    Union(parent, i, j);
                }
            }
        }

        // 按输入顺序分组（顺序稳定 → 结果可断言）
        var order = new List<int>();
        var buckets = new Dictionary<int, List<string>>();
        for (var i = 0; i < modules.Count; i++)
        {
            var root = Find(parent, i);
            if (!buckets.TryGetValue(root, out var bucket))
            {
                bucket = [];
                buckets[root] = bucket;
                order.Add(root);
            }

            bucket.Add(modules[i].Id);
        }

        var groups = new List<IReadOnlyList<string>>();
        foreach (var root in order)
        {
            if (buckets[root].Count > 1)
            {
                groups.Add(buckets[root]);
            }
        }

        return groups;
    }

    /// <summary>取某实例所在的拼接组（含自身；单独一个模块时返回只含自身的列表）。</summary>
    public static IReadOnlyList<string> GroupOf(
        string instanceId,
        IReadOnlyList<(string Id, CanvasRect Rect)> modules,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var group in FindGroups(modules, tolerance))
        {
            if (group.Contains(instanceId, StringComparer.Ordinal))
            {
                return group;
            }
        }

        return [instanceId];
    }

    private static (double Delta, bool Found, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd)
        FindBest(
            CanvasRect moving,
            IReadOnlyList<CanvasRect> others,
            Func<CanvasRect, CanvasRect, IEnumerable<Candidate>> candidates,
            double tolerance)
    {
        var found = false;
        var best = default(Candidate);
        var bestMagnitude = double.MaxValue;

        foreach (var other in others)
        {
            if (!other.IsValid)
            {
                continue;
            }

            foreach (var candidate in candidates(moving, other))
            {
                var magnitude = Math.Abs(candidate.Delta);
                if (magnitude > tolerance)
                {
                    continue;
                }

                // 贴合优先；同为贴合/对齐时取偏移更小者
                var better = !found
                    || (candidate.IsFlush && !best.IsFlush)
                    || (candidate.IsFlush == best.IsFlush && magnitude < bestMagnitude);

                if (better)
                {
                    found = true;
                    best = candidate;
                    bestMagnitude = magnitude;
                }
            }
        }

        return (best.Delta, found, best.IsFlush, best.GuidePosition, best.OtherStart, best.OtherEnd);
    }

    /// <summary>构造一条**参照线**候选（贴合语义：把模块的边吸到该线上）。</summary>
    private static (double Delta, bool Found, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd)
        LineCandidate(double delta, double position, double start, double end) =>
        (delta, true, true, position, start, end);

    /// <summary>按"贴合优先 → 偏移更小"的同一规则，在两条候选之间取更好的一条。</summary>
    private static (double Delta, bool Found, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd)
        Better(
            (double Delta, bool Found, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd) current,
            (double Delta, bool Found, bool IsFlush, double GuidePosition, double OtherStart, double OtherEnd) candidate,
            double tolerance)
    {
        if (Math.Abs(candidate.Delta) > tolerance)
        {
            return current;
        }

        if (!current.Found
            || (candidate.IsFlush && !current.IsFlush)
            || (candidate.IsFlush == current.IsFlush && Math.Abs(candidate.Delta) < Math.Abs(current.Delta)))
        {
            return candidate;
        }

        return current;
    }

    private static IEnumerable<Candidate> XCandidates(CanvasRect moving, CanvasRect other)    {
        // 贴合（相邻）：moving 在左 → 右边缘贴 other 左边缘；moving 在右 → 左边缘贴 other 右边缘
        yield return new Candidate(other.Left - moving.Right, true, other.Left, other.Top, other.Bottom);
        yield return new Candidate(other.Right - moving.Left, true, other.Right, other.Top, other.Bottom);

        // 参考对齐：左边对齐 / 右边对齐 / **中心对齐**（用户要求：目前缺少中心对齐）
        yield return new Candidate(other.Left - moving.Left, false, other.Left, other.Top, other.Bottom);
        yield return new Candidate(other.Right - moving.Right, false, other.Right, other.Top, other.Bottom);

        var centerX = other.Left + (other.Width / 2);
        yield return new Candidate(centerX - (moving.Left + (moving.Width / 2)), false, centerX, other.Top, other.Bottom);
    }

    private static IEnumerable<Candidate> YCandidates(CanvasRect moving, CanvasRect other)
    {
        yield return new Candidate(other.Top - moving.Bottom, true, other.Top, other.Left, other.Right);
        yield return new Candidate(other.Bottom - moving.Top, true, other.Bottom, other.Left, other.Right);
        yield return new Candidate(other.Top - moving.Top, false, other.Top, other.Left, other.Right);
        yield return new Candidate(other.Bottom - moving.Bottom, false, other.Bottom, other.Left, other.Right);

        var centerY = other.Top + (other.Height / 2);
        yield return new Candidate(centerY - (moving.Top + (moving.Height / 2)), false, centerY, other.Left, other.Right);
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
}
