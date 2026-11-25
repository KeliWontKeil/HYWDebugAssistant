namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// 缩放时正在移动的边（位标志与 <c>GuideOverlay.ResizeEdges</c> 一致）。
/// </summary>
[Flags]
public enum ResizeEdge
{
    /// <summary>没有移动边。</summary>
    None = 0,

    /// <summary>左边。</summary>
    Left = 1,

    /// <summary>右边。</summary>
    Right = 2,

    /// <summary>上边。</summary>
    Top = 4,

    /// <summary>下边。</summary>
    Bottom = 8,
}

/// <summary>
/// **尺寸相等参照**：另一模块的实例 ID 与其矩形（已换算到缩放窗口的 DIP 口径）。
/// </summary>
/// <param name="Id">参照模块实例 ID（用于状态提示与稳定排序）。</param>
/// <param name="Rect">参照模块矩形（与被缩放窗口同一坐标系）。</param>
public readonly record struct ResizeSizeReference(string Id, CanvasRect Rect);

/// <summary>缩放吸附结果（纯几何）。</summary>
/// <param name="Rect">吸附/夹紧后的矩形。</param>
/// <param name="SnappedX">X 轴是否命中。</param>
/// <param name="SnappedY">Y 轴是否命中。</param>
/// <param name="XKind">X 轴命中的级别（null = 未命中）。</param>
/// <param name="YKind">Y 轴命中的级别（null = 未命中）。</param>
/// <param name="Guides">要显示的**边对齐**辅助线；尺寸相等不画线。</param>
/// <param name="XLine">X 轴命中的目标坐标（供下一帧迟滞使用）。</param>
/// <param name="YLine">Y 轴命中的目标坐标（供下一帧迟滞使用）。</param>
/// <param name="XSizeReferenceId">X 轴命中尺寸相等时的参照模块 ID。</param>
/// <param name="YSizeReferenceId">Y 轴命中尺寸相等时的参照模块 ID。</param>
public readonly record struct ResizeSnapResult(
    CanvasRect Rect,
    bool SnappedX,
    bool SnappedY,
    SnapKind? XKind,
    SnapKind? YKind,
    IReadOnlyList<GuideLine> Guides,
    double? XLine = null,
    double? YLine = null,
    string? XSizeReferenceId = null,
    string? YSizeReferenceId = null);

/// <summary>
/// **缩放吸附**：只移动指定边，复用拖动同一套参照线与优先级。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>候选来源：其它模块的外框边、外壳边、其它模块的宽/高；中心线与端口锚点不参与；</item>
///   <item>优先级：贴合 &gt; 边缘对齐 &gt; 尺寸相等（尺寸相等是 <see cref="SnapKind.Size"/>，最低级）；</item>
///   <item>未移动的边固定；所有候选必须先通过最小/最大尺寸约束，不合法直接丢弃；</item>
///   <item>最终矩形若与障碍**新增**正面积重合，整次回落原始矩形，不返回辅助线；</item>
///   <item>尺寸相等不画“共享边”虚线，由调用方给状态提示；只有边对齐/贴合写 <see cref="Guides"/>。</item>
/// </list>
/// </remarks>
public static partial class SnapEngine
{
    /// <summary>
    /// 把缩放中的**移动边**吸附到给定参照边/尺寸。
    /// </summary>
    /// <param name="rect">指针推出来的原始矩形（已按指针位移换算，未吸附）。</param>
    /// <param name="edges">正在移动的边。</param>
    /// <param name="lines">其它模块与外壳的参照线（已按缩放窗口口径换算）。</param>
    /// <param name="sizeReferences">其它模块的尺寸参照。</param>
    /// <param name="minWidth">最小宽（DIP）。</param>
    /// <param name="minHeight">最小高（DIP）。</param>
    /// <param name="maxWidth">最大宽（DIP；null = 无上限）。</param>
    /// <param name="maxHeight">最大高（DIP；null = 无上限）。</param>
    /// <param name="tolerance">边对齐容差（px）。</param>
    /// <param name="flushTolerance">贴合/尺寸相等容差（px）。</param>
    /// <param name="obstacles">
    /// 其它模块与外壳的障碍（带层号）；缩放**不看层号**：任何新增重合都回落原始矩形（见下方说明）。
    /// </param>
    /// <param name="memory">上一帧的吸附记忆。</param>
    /// <returns>吸附后的矩形、命中级别、辅助线与尺寸参照。</returns>
    /// <exception cref="ArgumentException"><paramref name="edges"/> 同时移动一对相对边。</exception>
    public static ResizeSnapResult SnapResize(
        CanvasRect rect,
        ResizeEdge edges,
        IReadOnlyList<SnapLine> lines,
        IReadOnlyList<ResizeSizeReference> sizeReferences,
        double minWidth,
        double minHeight,
        double? maxWidth = null,
        double? maxHeight = null,
        double tolerance = DefaultTolerance,
        double flushTolerance = FlushTolerance,
        IReadOnlyList<SnapObstacle>? obstacles = null,
        SnapMemory memory = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(sizeReferences);

        if ((edges & ResizeEdge.Left) != 0 && (edges & ResizeEdge.Right) != 0)
        {
            throw new ArgumentException("Cannot move both horizontal edges at once.", nameof(edges));
        }

        if ((edges & ResizeEdge.Top) != 0 && (edges & ResizeEdge.Bottom) != 0)
        {
            throw new ArgumentException("Cannot move both vertical edges at once.", nameof(edges));
        }

        // 最大小于最小时以最小为准：约束永远不能让矩形变成负尺寸。
        if (maxWidth is { } widthLimit && widthLimit < minWidth)
        {
            maxWidth = null;
        }

        if (maxHeight is { } heightLimit && heightLimit < minHeight)
        {
            maxHeight = null;
        }

        var raw = ClampResizeRect(rect, edges, minWidth, minHeight, maxWidth, maxHeight);

        if (!raw.IsValid || edges == ResizeEdge.None)
        {
            return new ResizeSnapResult(raw, false, false, null, null, []);
        }

        var xs = new List<Candidate>();
        var ys = new List<Candidate>();

        var movesLeft = (edges & ResizeEdge.Left) != 0;
        var movesRight = (edges & ResizeEdge.Right) != 0;
        var movesTop = (edges & ResizeEdge.Top) != 0;
        var movesBottom = (edges & ResizeEdge.Bottom) != 0;

        foreach (var line in lines)
        {
            if (line.AlignsX)
            {
                if (movesLeft)
                {
                    CollectResizeLine(raw, line, lowSide: true, raw.Left, minWidth, minHeight, maxWidth, maxHeight, xs);
                }

                if (movesRight)
                {
                    CollectResizeLine(raw, line, lowSide: false, raw.Right, minWidth, minHeight, maxWidth, maxHeight, xs);
                }
            }
            else
            {
                if (movesTop)
                {
                    CollectResizeLine(raw, line, lowSide: true, raw.Top, minWidth, minHeight, maxWidth, maxHeight, ys);
                }

                if (movesBottom)
                {
                    CollectResizeLine(raw, line, lowSide: false, raw.Bottom, minWidth, minHeight, maxWidth, maxHeight, ys);
                }
            }
        }

        foreach (var reference in sizeReferences)
        {
            if (!reference.Rect.IsValid)
            {
                continue;
            }

            if (movesRight)
            {
                CollectSizeCandidate(raw, reference, target: raw.Left + reference.Rect.Width, alignsX: true, ResizeEdge.Right, raw.Right, minWidth, minHeight, maxWidth, maxHeight, xs);
            }

            if (movesLeft)
            {
                CollectSizeCandidate(raw, reference, target: raw.Right - reference.Rect.Width, alignsX: true, ResizeEdge.Left, raw.Left, minWidth, minHeight, maxWidth, maxHeight, xs);
            }

            if (movesBottom)
            {
                CollectSizeCandidate(raw, reference, target: raw.Top + reference.Rect.Height, alignsX: false, ResizeEdge.Bottom, raw.Bottom, minWidth, minHeight, maxWidth, maxHeight, ys);
            }

            if (movesTop)
            {
                CollectSizeCandidate(raw, reference, target: raw.Bottom - reference.Rect.Height, alignsX: false, ResizeEdge.Top, raw.Top, minWidth, minHeight, maxWidth, maxHeight, ys);
            }
        }

        var orderedXs = Deduplicate(StableOrder(xs));
        var orderedYs = Deduplicate(StableOrder(ys));
        var chosenX = Choose(orderedXs, tolerance, flushTolerance, memory.XLine);
        var chosenY = Choose(orderedYs, tolerance, flushTolerance, memory.YLine);

        var final = raw;
        if (chosenX is { } x)
        {
            // 一次缩放只移动一条水平边；用 edges 决定是哪一条，不能靠 delta 的正负
            //（向左扩大/向右缩小时 delta 会变号）。
            final = WithEdge(final, movesLeft ? ResizeEdge.Left : ResizeEdge.Right, x.LineCoordinate);
        }

        if (chosenY is { } y)
        {
            final = WithEdge(final, movesTop ? ResizeEdge.Top : ResizeEdge.Bottom, y.LineCoordinate);
        }

        if (!final.IsValid)
        {
            return new ResizeSnapResult(raw, false, false, null, null, []);
        }

        // **缩放不允许新增任何遮挡**（不分层号）：缩放不改变"这块在哪个平面"，涨到压住别人、
        // 或涨到自己刚被放在下面的更低层模块上都不可接受，所以这里保持"任何重合都回落"。
        // 手势起点就已重合的障碍不参与 —— 否则"压在主窗口上"的模块一缩放就整次失灵。
        if (obstacles is { Count: > 0 }
            && obstacles.Any(obstacle => !obstacle.AlreadyOverlaps && RectsOverlap(final, obstacle.Rect)))
        {
            return new ResizeSnapResult(raw, false, false, null, null, []);
        }

        var guides = new List<GuideLine>(2);

        if (chosenX is { } gx && gx.Kind != SnapKind.Size)
        {
            guides.Add(new GuideLine(
                IsVertical: true,
                Position: gx.LineCoordinate,
                Start: Math.Min(final.Top, gx.LineStart) - GuideOvershoot,
                End: Math.Max(final.Bottom, gx.LineEnd) + GuideOvershoot));
        }

        if (chosenY is { } gy && gy.Kind != SnapKind.Size)
        {
            guides.Add(new GuideLine(
                IsVertical: false,
                Position: gy.LineCoordinate,
                Start: Math.Min(final.Left, gy.LineStart) - GuideOvershoot,
                End: Math.Max(final.Right, gy.LineEnd) + GuideOvershoot));
        }

        return new ResizeSnapResult(
            final,
            chosenX is not null,
            chosenY is not null,
            chosenX?.Kind,
            chosenY?.Kind,
            guides,
            chosenX?.LineCoordinate,
            chosenY?.LineCoordinate,
            chosenX is { Kind: SnapKind.Size } ? chosenX.Value.ReferenceId : null,
            chosenY is { Kind: SnapKind.Size } ? chosenY.Value.ReferenceId : null);
    }

    /// <summary>原始边先夹进允许区间；未移动的边保持不动。</summary>
    private static CanvasRect ClampResizeRect(
        CanvasRect rect,
        ResizeEdge edges,
        double minWidth,
        double minHeight,
        double? maxWidth,
        double? maxHeight)
    {
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;

        if ((edges & ResizeEdge.Left) != 0)
        {
            left = Clamp(left, right - maxWidth, right - minWidth);
        }

        if ((edges & ResizeEdge.Right) != 0)
        {
            right = Clamp(right, left + minWidth, left + maxWidth);
        }

        if ((edges & ResizeEdge.Top) != 0)
        {
            top = Clamp(top, bottom - maxHeight, bottom - minHeight);
        }

        if ((edges & ResizeEdge.Bottom) != 0)
        {
            bottom = Clamp(bottom, top + minHeight, top + maxHeight);
        }

        return new CanvasRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>把目标坐标换成候选；边对齐/贴合与外壳规则都在这里。</summary>
    private static void CollectResizeLine(
        CanvasRect moving,
        SnapLine line,
        bool lowSide,
        double currentEdge,
        double minWidth,
        double minHeight,
        double? maxWidth,
        double? maxHeight,
        List<Candidate> target)
    {
        SnapKind kind;

        switch (line.Source)
        {
            case SnapLineSource.FrameEdge:
            {
                // 低侧边对上对方高侧边（或反之）才是接触；否则只是同侧边对齐。
                var contact = lowSide != line.IsOwnerLow;
                var overlap = CrossOverlap(moving, line);
                kind = contact && overlap > MinOverlap ? SnapKind.Flush : SnapKind.Edge;
                break;
            }

            case SnapLineSource.ShellEdge:
            {
                // 外壳右边缘只接受移动边的低侧（与拖动/成组判据同侧）。
                if (!lowSide)
                {
                    return;
                }

                var overlap = LineOverlap(moving, line);
                kind = overlap > MinOverlap ? SnapKind.Flush : SnapKind.Edge;
                break;
            }

            case SnapLineSource.ShellAlign:
                kind = SnapKind.Edge;
                break;

            default:
                // 中心线与端口锚点不参与缩放。
                return;
        }

        var candidate = WithEdge(moving, lowSide ? (line.AlignsX ? ResizeEdge.Left : ResizeEdge.Top) : (line.AlignsX ? ResizeEdge.Right : ResizeEdge.Bottom), line.Coordinate);
        if (!Fits(candidate, minWidth, minHeight, maxWidth, maxHeight))
        {
            return;
        }

        target.Add(new Candidate(
            kind,
            line.Coordinate - currentEdge,
            line.Coordinate,
            line.AlignsX,
            line.Start,
            line.End));
    }

    /// <summary>尺寸相等候选：目标边 = 固定边 ± 参照模块宽/高。</summary>
    private static void CollectSizeCandidate(
        CanvasRect moving,
        ResizeSizeReference reference,
        double target,
        bool alignsX,
        ResizeEdge edge,
        double currentEdge,
        double minWidth,
        double minHeight,
        double? maxWidth,
        double? maxHeight,
        List<Candidate> targetList)
    {
        var candidate = WithEdge(moving, edge, target);
        if (!Fits(candidate, minWidth, minHeight, maxWidth, maxHeight))
        {
            return;
        }

        targetList.Add(new Candidate(
            SnapKind.Size,
            target - currentEdge,
            target,
            alignsX,
            0,
            0,
            ReferenceId: reference.Id));
    }

    /// <summary>把某一条边设到目标坐标，其余边不变。</summary>
    private static CanvasRect WithEdge(CanvasRect rect, ResizeEdge edge, double target) => edge switch
    {
        ResizeEdge.Left => new CanvasRect(target, rect.Top, rect.Right - target, rect.Height),
        ResizeEdge.Right => new CanvasRect(rect.Left, rect.Top, target - rect.Left, rect.Height),
        ResizeEdge.Top => new CanvasRect(rect.Left, target, rect.Width, rect.Bottom - target),
        ResizeEdge.Bottom => new CanvasRect(rect.Left, rect.Top, rect.Width, target - rect.Top),
        _ => rect,
    };

    private static bool Fits(
        CanvasRect rect,
        double minWidth,
        double minHeight,
        double? maxWidth,
        double? maxHeight) =>
        rect.Width >= minWidth - 0.01
        && rect.Height >= minHeight - 0.01
        && (maxWidth is not { } widthLimit || rect.Width <= widthLimit + 0.01)
        && (maxHeight is not { } heightLimit || rect.Height <= heightLimit + 0.01);

    private static double Clamp(double value, double? min, double? max)
    {
        var result = min is { } lower ? Math.Max(value, lower) : value;
        return max is { } upper ? Math.Min(result, upper) : result;
    }

    /// <summary>稳定排序：级别 → 距离 → 坐标 → 参照 ID；再交给共享去重/选择。</summary>
    private static List<Candidate> StableOrder(List<Candidate> candidates) =>
        candidates
            .OrderByDescending(candidate => candidate.Kind)
            .ThenBy(candidate => Math.Abs(candidate.Delta))
            .ThenBy(candidate => candidate.LineCoordinate)
            .ThenBy(candidate => candidate.ReferenceId ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 选一个候选（缩放不做逐候选分层校验：它的障碍判定是"整矩形一旦新增重合就回落"，见 <see cref="SnapResize"/>）。
    /// </summary>
    private static Candidate? Choose(
        List<Candidate> candidates,
        double tolerance,
        double flushTolerance,
        double? rememberedLine) =>
        PreferMemory(
            Best(candidates, tolerance, flushTolerance, static _ => true),
            candidates,
            rememberedLine,
            Math.Max(tolerance, flushTolerance) + 2,
            static _ => true);
}
