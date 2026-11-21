namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>新模块的一次落位结果。</summary>
/// <param name="X">左上角 X（DIP）。</param>
/// <param name="Y">左上角 Y（DIP）。</param>
/// <param name="Stacked">是否进入了几何兜底（正常分层铺开不会置位）。</param>
/// <param name="Sheet">命中的放置层（0 起；运行期概念，不持久化）。</param>
public readonly record struct ModulePlacementResult(double X, double Y, bool Stacked, int Sheet = 0);

/// <summary>
/// **带放置层的占用矩形**：同一层内不能重叠，跨层可以重叠。
/// </summary>
/// <param name="Rect">占用矩形（DIP）。</param>
/// <param name="Sheet">放置层号（运行期；外壳用负数，不参与分层）。</param>
public readonly record struct PlacementObstacle(CanvasRect Rect, int Sheet);

/// <summary>
/// **新模块落位**：在屏幕中央的自动落位放置区内按列铺开，排满后在其中错位堆叠。
/// </summary>
/// <remarks>
/// 纯几何、无状态：层号不在这里保存；堆叠造成的重合由 `ModuleLayerPlanner` 按叠放顺序派生为更高层。
/// 所有输入输出都是 DIP（与模块模型同一单位）。放置区只服务自动落位，不约束拖动/缩放/吸附/层级。
/// </remarks>
public static class ModulePlacement
{
    /// <summary>没有外壳时的起始偏移。</summary>
    public const double Origin = 48;

    /// <summary>模块之间的呼吸间距。</summary>
    public const double Gap = 12;

    /// <summary>堆叠时每一级的错位步长。</summary>
    public const double StackStep = 16;

    /// <summary>自动落位区域占工作区的比例（正常范围 60%–80%）。</summary>
    public const double PlacementAreaRatio = 0.75;

    /// <summary>自动落位区域右侧/上侧/下侧与工作区的最小余量比例。</summary>
    public const double PlacementMarginRatio = 0.10;

    /// <summary>每一层放置网格相对下一层的坐标偏移（DIP）。</summary>
    public const double LayerOffset = 16;

    private const int MaxScanColumns = 256;
    private const int MaxScanRows = 256;
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
    /// 计算**自动落位专用放置区**：正常以工作区中央约 75% 为范围，左边界贴外壳右侧；
    /// 外壳太靠右或模块太大时向左扩展/收缩，始终尽量留在工作区内。
    /// </summary>
    /// <param name="shell">外壳矩形（DIP；可空）。</param>
    /// <param name="workArea">目标工作区（DIP；可空）。</param>
    /// <param name="width">候选模块宽（DIP）。</param>
    /// <param name="height">候选模块高（DIP）。</param>
    /// <returns>放置区；没有工作区 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// 这里只服务自动落位。拖动、缩放、吸附、对齐、拼接和层级都不读取这个区域。
    /// </remarks>
    public static CanvasRect? PlacementRegion(
        CanvasRect? shell,
        CanvasRect? workArea,
        double width,
        double height)
    {
        if (workArea is not { IsValid: true } work)
        {
            return null;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var margin = Math.Min(work.Width, work.Height) * PlacementMarginRatio;
        var targetWidth = Math.Max(width, work.Width * PlacementAreaRatio);
        var targetHeight = Math.Max(height, work.Height * PlacementAreaRatio);
        var maxWidthWithMargins = Math.Max(1, work.Width - (margin * 2));
        var maxHeightWithMargins = Math.Max(1, work.Height - (margin * 2));

        var regionWidth = maxWidthWithMargins >= width
            ? Math.Min(targetWidth, maxWidthWithMargins)
            : Math.Min(work.Width, width);
        var regionHeight = maxHeightWithMargins >= height
            ? Math.Min(targetHeight, maxHeightWithMargins)
            : Math.Min(work.Height, height);

        double left;

        if (shell is { IsValid: true } shellRect)
        {
            var preferredLeft = shellRect.Right + Gap;
            var availableRight = work.Right - margin - preferredLeft;

            if (availableRight >= width)
            {
                // 正常：左边界贴外壳右侧；宽度按右侧余量收缩。
                regionWidth = Math.Min(regionWidth, availableRight);
                left = Math.Clamp(preferredLeft, work.Left, Math.Max(work.Left, work.Right - margin - regionWidth));
            }
            else
            {
                // 右侧余量放不下：先尝试把右余量让给模块；仍放不下才向左让位。
                var availableWithoutMargin = work.Right - preferredLeft;
                if (availableWithoutMargin >= width)
                {
                    regionWidth = Math.Min(regionWidth, availableWithoutMargin);
                    left = preferredLeft;
                }
                else
                {
                    regionWidth = Math.Min(work.Width, width);
                    left = Math.Max(work.Left, work.Right - margin - regionWidth);
                }
            }
        }
        else
        {
            left = work.CenterX - (regionWidth / 2);
            left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - regionWidth));
        }

        var top = work.CenterY - (regionHeight / 2);
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - regionHeight));

        return new CanvasRect(
            left,
            top,
            Math.Max(1, Math.Min(regionWidth, work.Width)),
            Math.Max(1, Math.Min(regionHeight, work.Height)));
    }

    /// <summary>
    /// 兼容入口：不带放置层的调用，全部视为第 0 层。
    /// </summary>
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

        var obstacles = new List<PlacementObstacle>(occupied.Count);
        foreach (var rect in occupied)
        {
            obstacles.Add(new PlacementObstacle(rect, Sheet: 0));
        }

        return NextLayered(shell, workArea, obstacles, width, height, origin, gap);
    }

    /// <summary>
    /// 找一个落位：逐层在放置区内按列铺开；同层避让，跨层可重叠。
    /// </summary>
    /// <param name="shell">外壳矩形（DIP；可空）。</param>
    /// <param name="workArea">目标工作区（DIP；可空 = 不限制屏幕）。</param>
    /// <param name="occupied">已占用的模块矩形及其放置层。</param>
    /// <param name="width">候选宽。</param>
    /// <param name="height">候选高。</param>
    /// <param name="origin">无外壳时的起始偏移。</param>
    /// <param name="gap">模块间距。</param>
    /// <returns>落位；<see cref="ModulePlacementResult.Sheet"/> 是命中的放置层。</returns>
    public static ModulePlacementResult NextLayered(
        CanvasRect? shell,
        CanvasRect? workArea,
        IReadOnlyList<PlacementObstacle> occupied,
        double width,
        double height,
        double origin = Origin,
        double gap = Gap)
    {
        ArgumentNullException.ThrowIfNull(occupied);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var obstacles = new List<PlacementObstacle>(occupied.Count + 1);
        obstacles.AddRange(occupied);
        if (shell is { IsValid: true } shellObstacle)
        {
            obstacles.Add(new PlacementObstacle(shellObstacle, Sheet: -1));
        }

        if (PlacementRegion(shell, workArea, width, height) is { } region)
        {
            var maxSheet = -1;
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Sheet >= 0)
                {
                    maxSheet = Math.Max(maxSheet, obstacle.Sheet);
                }
            }

            // 从已有最低层开始；每层只避让同层与外壳，跨层允许重叠。
            for (var sheet = 0; sheet <= maxSheet + 1; sheet++)
            {
                var layerObstacles = obstacles
                    .Where(obstacle => obstacle.Sheet < 0 || obstacle.Sheet == sheet)
                    .Select(obstacle => obstacle.Rect)
                    .ToList();

                var originPoint = LayerOrigin(region, sheet, width, height);
                if (TryFindSlotInRegion(region, originPoint.X, originPoint.Y, layerObstacles, width, height, gap, out var x, out var y))
                {
                    return new ModulePlacementResult(x, y, Stacked: false, Sheet: sheet);
                }

                // 新层的偏移起点被外壳等挡住时，允许退回无偏移起点再试一次。
                if (sheet > maxSheet
                    && TryFindSlotInRegion(region, region.Left, region.Top, layerObstacles, width, height, gap, out var fallbackX, out var fallbackY))
                {
                    return new ModulePlacementResult(fallbackX, fallbackY, Stacked: false, Sheet: sheet);
                }
            }

            return StackInRegion(region, maxSheet + 1, obstacles, width, height);
        }

        // 无工作区：没有“屏幕中心”可定义，保留外壳/Origin 锚点的旧式兜底。
        var fallbackX0 = shell is { IsValid: true } fallbackShell ? fallbackShell.Right + gap : origin;
        var fallbackY0 = shell is { IsValid: true } fallbackShell2 ? fallbackShell2.Y : origin;
        var fallbackRegion = new CanvasRect(
            fallbackX0,
            fallbackY0,
            width + ((MaxScanColumns - 1) * (width + gap)),
            height + ((MaxScanRows - 1) * (height + gap)));

        var fallbackObstacles = obstacles.Select(obstacle => obstacle.Rect).ToList();
        if (TryFindSlotInRegion(
            fallbackRegion,
            fallbackRegion.Left,
            fallbackRegion.Top,
            fallbackObstacles,
            width,
            height,
            gap,
            out var fallbackSlotX,
            out var fallbackSlotY))
        {
            return new ModulePlacementResult(fallbackSlotX, fallbackSlotY, Stacked: false, Sheet: 0);
        }

        return StackInRegion(fallbackRegion, 0, obstacles, width, height);
    }

    /// <summary>第 <paramref name="sheet"/> 层的网格起点（相对放置区左上角偏移）。</summary>
    private static (double X, double Y) LayerOrigin(
        CanvasRect region,
        int sheet,
        double width,
        double height)
    {
        var maxOffsetX = Math.Max(0, region.Width - width);
        var maxOffsetY = Math.Max(0, region.Height - height);
        var offsetX = maxOffsetX <= 0 ? 0 : (sheet * LayerOffset) % (maxOffsetX + 1);
        var offsetY = maxOffsetY <= 0 ? 0 : (sheet * LayerOffset) % (maxOffsetY + 1);
        return (region.Left + offsetX, region.Top + offsetY);
    }

    /// <summary>在给定区域和给定起点内按列扫描；列内自上而下，列间从左到右。</summary>
    private static bool TryFindSlotInRegion(
        CanvasRect region,
        double startX,
        double startY,
        IReadOnlyList<CanvasRect> obstacles,
        double width,
        double height,
        double gap,
        out double x,
        out double y)
    {
        x = startX;
        y = startY;

        if (region.Width < width
            || region.Height < height
            || startX + width > region.Right
            || startY + height > region.Bottom)
        {
            return false;
        }

        var stepX = width + gap;
        var stepY = height + gap;
        var maxColumns = Math.Min(
            MaxScanColumns,
            Math.Max(1, (int)Math.Ceiling(((region.Right - startX - width) / stepX) + 1)));
        var maxRows = Math.Min(
            MaxScanRows,
            Math.Max(1, (int)Math.Ceiling(((region.Bottom - startY - height) / stepY) + 1)));

        for (var column = 0; column < maxColumns; column++)
        {
            y = startY;

            for (var row = 0; row < maxRows; row++)
            {
                if (x + width > region.Right || y + height > region.Bottom)
                {
                    break;
                }

                var candidate = new CanvasRect(x, y, width, height);
                if (!Overlaps(candidate, obstacles, gap))
                {
                    return true;
                }

                if (LowestOverlappingBottom(candidate, obstacles, gap) is not { } lowest)
                {
                    return true;
                }

                // 贴住最低障碍物的下沿 + 呼吸间距：不按候选高度整块跳，避免过大空档。
                y = lowest + gap;
            }

            if (x + width > region.Right)
            {
                break;
            }

            var nextX = ColumnRight(x, width, obstacles, gap) + gap;
            if (nextX <= x)
            {
                nextX = x + stepX;
            }

            x = nextX;
        }

        return false;
    }

    /// <summary>几何上无法在任何层铺开时的最后兜底：在区域中心错位落位。</summary>
    private static ModulePlacementResult StackInRegion(
        CanvasRect region,
        int sheet,
        List<PlacementObstacle> obstacles,
        double width,
        double height)
    {
        var baseX = region.CenterX - (width / 2);
        var baseY = region.CenterY - (height / 2);
        var minX = region.Left;
        var maxX = Math.Max(minX, region.Right - width);
        var minY = region.Top;
        var maxY = Math.Max(minY, region.Bottom - height);

        for (var attempt = 0; attempt < StackCycle * 2; attempt++)
        {
            var offset = ((obstacles.Count + attempt) % StackCycle) * StackStep;
            var candidateX = Math.Clamp(baseX + offset, minX, maxX);
            var candidateY = Math.Clamp(baseY + offset, minY, maxY);

            // 完全相同的左上角会让两个模块叠成一块，用户连点都点不到第二个；
            // 换一个错位档，夹紧后仍相同就继续试。
            if (obstacles.Any(other => other.Rect.IsValid
                && Math.Abs(other.Rect.Left - candidateX) < 0.5
                && Math.Abs(other.Rect.Top - candidateY) < 0.5))
            {
                continue;
            }

            return new ModulePlacementResult(candidateX, candidateY, Stacked: true, Sheet: sheet);
        }

        return new ModulePlacementResult(
            Math.Clamp(baseX, minX, maxX),
            Math.Clamp(baseY, minY, maxY),
            Stacked: true,
            Sheet: sheet);
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
