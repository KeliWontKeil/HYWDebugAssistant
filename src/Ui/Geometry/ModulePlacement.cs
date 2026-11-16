namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>新模块的一次落位结果。</summary>
/// <param name="X">左上角 X（DIP）。</param>
/// <param name="Y">左上角 Y（DIP）。</param>
/// <param name="Stacked">是否是因为排满而堆叠（重合 ⇒ 由层规划自动升层）。</param>
public readonly record struct ModulePlacementResult(double X, double Y, bool Stacked);

/// <summary>
/// **新模块落位**：先在外壳右侧找空位，排满后在工作区内堆叠。
/// </summary>
/// <remarks>
/// 纯几何、无状态：层号不在这里保存；堆叠造成的重合由 `ModuleLayerPlanner` 按叠放顺序派生为更高层。
/// 所有输入输出都是 DIP（与模块模型同一单位）。
/// </remarks>
public static class ModulePlacement
{
    /// <summary>没有外壳时的起始偏移。</summary>
    public const double Origin = 48;

    /// <summary>模块之间的呼吸间距。</summary>
    public const double Gap = 12;

    /// <summary>堆叠时每一级的错位步长。</summary>
    public const double StackStep = 16;

    private const int MaxColumns = 16;
    private const int MaxRows = 64;
    private const int StackCycle = 8;

    /// <summary>
    /// **选出形位工作区**：与外壳相交面积最大的那块屏；外壳不可用或都不相交时退回第一块。
    /// </summary>
    /// <param name="shell">外壳矩形（DIP；可空）。</param>
    /// <param name="workAreas">全部屏幕工作区（DIP）。</param>
    /// <returns>工作区；没有任何有效工作区 ⇒ <see langword="null"/>。</returns>
    public static CanvasRect? ChooseWorkArea(CanvasRect? shell, IReadOnlyList<CanvasRect> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);

        CanvasRect? best = null;

        if (shell is { IsValid: true } shellRect)
        {
            var bestArea = 0.0;

            foreach (var area in workAreas)
            {
                if (!area.IsValid)
                {
                    continue;
                }

                var width = Math.Min(area.Right, shellRect.Right) - Math.Max(area.Left, shellRect.Left);
                var height = Math.Min(area.Bottom, shellRect.Bottom) - Math.Max(area.Top, shellRect.Top);
                var intersection = Math.Max(0, width) * Math.Max(0, height);

                if (intersection > bestArea)
                {
                    bestArea = intersection;
                    best = area;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        foreach (var area in workAreas)
        {
            if (area.IsValid)
            {
                return area;
            }
        }

        return null;
    }

    /// <summary>
    /// 找一个落位：优先空位；当前屏排满时在工作区内堆叠。
    /// </summary>
    /// <param name="shell">外壳矩形（DIP；可空）。</param>
    /// <param name="workArea">目标工作区（DIP；可空 = 不限制屏幕）。</param>
    /// <param name="occupied">已占用的模块矩形（DIP）。</param>
    /// <param name="width">候选宽。</param>
    /// <param name="height">候选高。</param>
    /// <param name="origin">无外壳时的起始偏移。</param>
    /// <param name="gap">模块间距。</param>
    public static ModulePlacementResult Next(
        CanvasRect? shell,
        CanvasRect? workArea,
        IReadOnlyList<CanvasRect> occupied,
        double width,
        double height,
        double origin = Origin,
        double gap = Gap)
    {
        ArgumentNullException.ThrowIfNull(occupied);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var work = workArea is { IsValid: true } area ? area : (CanvasRect?)null;
        var baseX = shell is { IsValid: true } shellRect ? shellRect.Right + gap : origin;
        var baseY = shell is { IsValid: true } shellRect2 ? shellRect2.Y : origin;

        if (work is { } bounds)
        {
            // 外壳靠近工作区下沿/右沿时，先把起点夹到还能放下的位置（仍保证候选在工作区内）。
            if (baseY + height > bounds.Bottom)
            {
                baseY = Math.Max(bounds.Top, bounds.Bottom - height);
            }

            if (baseX + width > bounds.Right)
            {
                baseX = Math.Max(bounds.Left, bounds.Right - width);
            }
        }

        var x = baseX;

        for (var column = 0; column < MaxColumns; column++)
        {
            var y = baseY;

            for (var row = 0; row < MaxRows; row++)
            {
                if (work is { } limits && (y + height > limits.Bottom || x + width > limits.Right))
                {
                    break;
                }

                var candidate = new CanvasRect(x, y, width, height);
                if (!Overlaps(candidate, occupied, gap))
                {
                    return new ModulePlacementResult(x, y, Stacked: false);
                }

                if (LowestOverlappingBottom(candidate, occupied, gap) is not { } lowest)
                {
                    return new ModulePlacementResult(x, y, Stacked: false);
                }

                // 贴住最低障碍物的下沿 + 呼吸间距：不按候选高度整块跳，避免过大空档。
                y = lowest + gap;
            }

            if (work is { } rightBound && x + width > rightBound.Right)
            {
                break;
            }

            x = ColumnRight(x, width, occupied, gap) + gap;
        }

        // 外壳右侧一列一列找不到空位时，继续扫描**整个工作区**：屏幕上别处有空位，
        // 不应该因为"外壳右侧排满"就直接堆叠。
        if (work is { } areaBounds)
        {
            var obstacles = new List<CanvasRect>(occupied.Count + 1);
            obstacles.AddRange(occupied);
            if (shell is { IsValid: true } shellObstacle)
            {
                obstacles.Add(shellObstacle);
            }

            var stepX = width + gap;
            var stepY = height + gap;
            var columns = Math.Max(1, (int)Math.Floor((areaBounds.Width + gap) / stepX));
            var rows = Math.Max(1, (int)Math.Floor((areaBounds.Height + gap) / stepY));

            for (var row = 0; row < rows; row++)
            {
                var candidateY = areaBounds.Top + (row * stepY);

                for (var column = 0; column < columns; column++)
                {
                    var candidateX = areaBounds.Left + (column * stepX);
                    var candidate = new CanvasRect(candidateX, candidateY, width, height);

                    if (!Overlaps(candidate, obstacles, gap))
                    {
                        return new ModulePlacementResult(candidateX, candidateY, Stacked: false);
                    }
                }
            }
        }

        // 排满：在工作区内按固定步长堆叠。重合会让现有层规划给出更高层。
        var stackX = baseX;
        var stackY = baseY;

        if (work is { } clampBounds)
        {
            for (var attempt = 0; attempt < StackCycle * 2; attempt++)
            {
                var offset = ((occupied.Count + attempt) % StackCycle) * StackStep;
                var candidateX = Math.Clamp(
                    baseX + offset,
                    clampBounds.Left,
                    Math.Max(clampBounds.Left, clampBounds.Right - width));
                var candidateY = Math.Clamp(
                    baseY + offset,
                    clampBounds.Top,
                    Math.Max(clampBounds.Top, clampBounds.Bottom - height));

                // 完全相同的左上角会让两个模块叠成一块，用户连点都点不到第二个；
                // 换一个错位档，夹紧后仍相同就继续试。
                if (occupied.Any(other => other.IsValid
                    && Math.Abs(other.Left - candidateX) < 0.5
                    && Math.Abs(other.Top - candidateY) < 0.5))
                {
                    continue;
                }

                stackX = candidateX;
                stackY = candidateY;
                break;
            }
        }
        else
        {
            var offset = (occupied.Count % StackCycle) * StackStep;
            stackX = baseX + offset;
            stackY = baseY + offset;
        }

        return new ModulePlacementResult(stackX, stackY, Stacked: true);
    }

    /// <summary>候选是否与任一占用矩形相交（两侧各留 <paramref name="gap"/> 的呼吸间距）。</summary>
    /// <param name="candidate">候选矩形。</param>
    /// <param name="occupied">占用矩形。</param>
    /// <param name="gap">呼吸间距。</param>
    public static bool Overlaps(CanvasRect candidate, IReadOnlyList<CanvasRect> occupied, double gap = Gap)
    {
        ArgumentNullException.ThrowIfNull(occupied);

        foreach (var other in occupied)
        {
            if (!other.IsValid)
            {
                continue;
            }

            if (candidate.Left < other.Right + gap && candidate.Right + gap > other.Left
                && candidate.Top < other.Bottom + gap && candidate.Bottom + gap > other.Top)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>候选与占用相交时，返回最低的相交下沿；不相交 = null。</summary>
    private static double? LowestOverlappingBottom(
        CanvasRect candidate,
        IReadOnlyList<CanvasRect> occupied,
        double gap)
    {
        double? lowest = null;

        foreach (var other in occupied)
        {
            if (!other.IsValid)
            {
                continue;
            }

            if (candidate.Left < other.Right + gap && candidate.Right + gap > other.Left
                && candidate.Top < other.Bottom + gap && candidate.Bottom + gap > other.Top)
            {
                lowest = lowest is { } current ? Math.Max(current, other.Bottom) : other.Bottom;
            }
        }

        return lowest;
    }

    /// <summary>当前列实际占用的右边界（含本列较宽模块）。</summary>
    private static double ColumnRight(
        double columnLeft,
        double nominalWidth,
        IReadOnlyList<CanvasRect> occupied,
        double gap)
    {
        var right = columnLeft + Math.Max(1, nominalWidth);

        foreach (var other in occupied)
        {
            if (!other.IsValid)
            {
                continue;
            }

            // 只吸收与本列横向相交的模块；右侧后续列不参与本次列宽。
            if (other.Left < right + gap && other.Right > right)
            {
                right = other.Right;
            }
        }

        return right;
    }
}
