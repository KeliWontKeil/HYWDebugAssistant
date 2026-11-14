namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>吸附辅助线（拖动命中时显示，09 §8）。</summary>
/// <param name="IsVertical">true = 竖线（X 恒定）；false = 横线（Y 恒定）。</param>
/// <param name="Position">线所在坐标（竖线为 X，横线为 Y）。</param>
/// <param name="Start">线段起点（竖线为 Y，横线为 X）。</param>
/// <param name="End">线段终点。</param>
/// <remarks>
/// 三个数都是**屏幕 DIP** —— 与模型几何（<c>CanvasRect</c>）**同一单位**，
/// 因为它的输入就是模型矩形。DIP → 物理像素的换算只在窗口边界一处
/// （见 <c>ModuleWindowManager.ApplyGeometry</c>）；绘制方（`GuideOverlay`）只许做
/// "减去本窗口客户区原点（同样换成 DIP）"这一步，**不许再乘/除缩放**。
/// </remarks>
public readonly record struct GuideLine(bool IsVertical, double Position, double Start, double End);

/// <summary>
/// **吸附级别**（跨级不比较：级别高的候选永远优先，同级才比偏移大小）。
/// </summary>
/// <remarks>级别由「参照线的来源 + 交叉轴条件」派生，不另写一套判断。</remarks>
public enum SnapKind
{
    /// <summary>边缘对齐（左/右/上/下分别对齐）——最低级；**与距离无关**（跨屏对齐用它）。</summary>
    Edge = 0,

    /// <summary>**模块中心线**对齐。</summary>
    ModuleCenter = 1,

    /// <summary>**端口/网络标签锚点**对齐（"要插上了"，接近范围内才参与）。</summary>
    PortCenter = 2,

    /// <summary>**贴合**（相邻边接触）——最高级。</summary>
    Flush = 3,
}

/// <summary>参照线的来源。</summary>
public enum SnapLineSource
{
    /// <summary>其他模块的**外框边**：吸上后可能是贴合（交叉轴有重叠、且是朝外那条边），否则是边缘对齐。</summary>
    FrameEdge,

    /// <summary>其他模块的**中心线**（只用模块的中心锚点去对）。</summary>
    FrameCenter,

    /// <summary>**端口/网络标签锚点**：网络标签是外框上的一个点，对齐轴由它所在的边决定。</summary>
    PortAnchor,

    /// <summary>**外壳边**（只暴露右边缘）：只接受被拖模块的**低侧边**（竖线为左边缘）贴上去。</summary>
    ShellEdge,
}

/// <summary>
/// **吸附参照线**——一条竖线（约束 X）或横线（约束 Y）。
/// </summary>
/// <param name="AlignsX">true = 竖线，约束 X 坐标；false = 横线，约束 Y 坐标。</param>
/// <param name="Coordinate">线所在坐标（竖线为 X，横线为 Y）。</param>
/// <param name="Start">线段起点（用于"沿线重叠"判定与辅助线长度）。</param>
/// <param name="End">线段终点。</param>
/// <param name="Source">来源类别（决定候选级别）。</param>
/// <param name="Owner">归属对象矩形（外框边/中心线/端口锚点有；外壳线为默认值 = 无归属）。</param>
/// <param name="IsOwnerLow">仅 <see cref="SnapLineSource.FrameEdge"/> 有意义：是否为归属对象的**低侧**边（左/上）。</param>
public readonly record struct SnapLine(
    bool AlignsX,
    double Coordinate,
    double Start,
    double End,
    SnapLineSource Source,
    CanvasRect Owner,
    bool IsOwnerLow = false);

/// <summary>
/// **外部参照线**（调用方用来表达"只暴露一条边的对象"，如外壳的右边缘）。
/// </summary>
/// <param name="IsVertical">true = 竖线（X 恒定）。</param>
/// <param name="Position">线的坐标。</param>
/// <param name="Start">线段起点（用于"沿线重叠"判定与辅助线长度）。</param>
/// <param name="End">线段终点。</param>
/// <remarks>
/// 它**不是**一条特殊通道：由 <see cref="SnapEngine.LineOf"/> 转成
/// <see cref="SnapLineSource.ShellEdge"/> 的 <see cref="SnapLine"/>，与模块外框线进同一张表。
/// 外壳是**单侧锚点**：只接受被拖模块的低侧边（竖线为左边缘），
/// 与 `AreNodesJoined` 的"模块左边缘贴外壳右边缘"保持同一侧。
/// 之所以不复用矩形：零宽矩形会被 <see cref="CanvasRect.IsValid"/>（要求正尺寸）过滤掉（P25）。
/// </remarks>
public readonly record struct SnapReferenceLine(bool IsVertical, double Position, double Start, double End);

/// <summary>
/// **端口锚点对**：被拖模块的某个口 ↔ 对方某个口。
/// </summary>
/// <param name="AlignsX">对齐轴是否约束 X（**由端口所在的边决定**：左/右边的口 → 约束 Y；上/下边的口 → 约束 X）。</param>
/// <param name="OtherCoordinate">对方端口在**该对齐轴**上的坐标。</param>
/// <param name="Other">对方模块矩形（用于接近范围判定与辅助线长度）。</param>
/// <param name="MovingOffset">被拖模块该端口在对齐轴上的**归一化位置**（0–1）。</param>
/// <param name="MovingIndex">组吸附时该端口所属成员在 <c>moving</c> 列表里的下标（单模块为 0）。</param>
public readonly record struct PortAnchorPair(bool AlignsX, double OtherCoordinate, CanvasRect Other, double MovingOffset, int MovingIndex = 0);

/// <summary>吸附结果（**纯函数**：只由"原始位置 + 参照线"决定，不含跨帧状态）。</summary>
/// <param name="X">命中后的 X（未命中则等于输入）。</param>
/// <param name="Y">命中后的 Y（未命中则等于输入）。</param>
/// <param name="SnappedX">X 是否命中。</param>
/// <param name="SnappedY">Y 是否命中。</param>
/// <param name="XKind">X 轴命中的级别（null = 未命中）。</param>
/// <param name="YKind">Y 轴命中的级别（null = 未命中）。</param>
/// <param name="Guides">要显示的辅助线（"会吸到哪里"的预览）。</param>
/// <param name="XLine">X 轴命中的参照线坐标（未命中为 null，供下一帧迟滞使用）。</param>
/// <param name="YLine">Y 轴命中的参照线坐标（未命中为 null）。</param>
public readonly record struct SnapResult(
    double X,
    double Y,
    bool SnappedX,
    bool SnappedY,
    SnapKind? XKind,
    SnapKind? YKind,
    IReadOnlyList<GuideLine> Guides,
    double? XLine = null,
    double? YLine = null);

/// <summary>上一帧的吸附记忆：只在越过释放距离后才允许换线/松手，消除阈值附近的来回跳。</summary>
/// <param name="XLine">上一帧 X 轴参照线坐标（null = 无）。</param>
/// <param name="YLine">上一帧 Y 轴参照线坐标（null = 无）。</param>
public readonly record struct SnapMemory(double? XLine, double? YLine);

/// <summary>
/// **几何吸附**（候选统一为「参照线」模型）——
/// 纯几何、**纯函数**，不依赖任何框架对象，可独立测试。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>唯一判据</b>：一切吸附 = 把**模块外框的锚点**（左/中心/右 · 上/中心/下）吸到**某条参照线**上；</item>
///   <item><b>参照线只有两个来源</b>（<see cref="SnapLineSource"/>）：其他模块的外框（4 边 + 中心线）与端口锚点；
///     **外壳 = 只暴露一条边的对象**（不是特殊通道，同样进线表）；</item>
///   <item><b>级别由「线来源 + 交叉轴条件」派生</b>：贴合 &gt; 端口锚点 &gt; 中心线 &gt; 边缘对齐；</item>
///   <item><b>同坐标候选去重</b>：两条候选位置相差 ≤1px 时只保留级别最高的 ——
///     消除"端口在 0.5 与模块中心线"这类**重复登记**（同一坐标只登记一次，级别最高的那条胜出）；</item>
///   <item><b>不做网格吸附</b>；<b>调用方决定"何时用"</b>（当前 = 拖动中实时吸附）；</item>
///   <item><b>组关系由位置派生</b>（C5-1）：只保存位置，不单独存储（见 <see cref="FindGroups"/>）。</item>
/// </list>
/// </remarks>
public static class SnapEngine
{
    /// <summary>**对齐类**候选的容差（px）——窄：对齐只是"看着整齐"，不该抢用户的手感。</summary>
    public const double DefaultTolerance = 4;

    /// <summary>**贴合（接触拼接）类**候选的容差（px）——宽：拼接是主用途，值得给宽容差。</summary>
    public const double FlushTolerance = 8;

    /// <summary>端口锚点对齐的**接近范围**（px）：两模块在交叉轴上离得比这更远时，端口对齐不参与。</summary>
    public const double PortAlignReach = 24;

    /// <summary>判定"沿线重叠/交叉轴重叠"的最小值（px）。</summary>
    public const double MinOverlap = 4;

    /// <summary>两条候选位置相差不超过该值时视为"同坐标"，只保留级别最高的。</summary>
    private const double DuplicateEpsilon = 1;

    /// <summary>辅助线在两端各外伸的长度（视觉取值，Gate C 微调）。</summary>
    private const double GuideOvershoot = 12;

    /// <summary>吸附候选（内部）：级别 + 偏移 + 参照线坐标 + 该线的区间。</summary>
    private readonly record struct Candidate(
        SnapKind Kind,
        double Delta,
        double LineCoordinate,
        bool AlignsX,
        double LineStart,
        double LineEnd);

    /// <summary>
    /// 由**其他模块外框**生成参照线（4 条边 + 1 条中心线，两个轴各一批）。
    /// </summary>
    /// <param name="other">其他模块矩形。</param>
    /// <returns>该模块提供的全部参照线。</returns>
    public static IEnumerable<SnapLine> LinesOf(CanvasRect other)
    {
        if (!other.IsValid)
        {
            return [];
        }

        return
        [
            // 竖线（约束 X）：左右边 + 中心线
            new SnapLine(true, other.Left, other.Top, other.Bottom, SnapLineSource.FrameEdge, other, IsOwnerLow: true),
            new SnapLine(true, other.Right, other.Top, other.Bottom, SnapLineSource.FrameEdge, other, IsOwnerLow: false),
            new SnapLine(true, other.CenterX, other.Top, other.Bottom, SnapLineSource.FrameCenter, other),

            // 横线（约束 Y）：上下边 + 中心线
            new SnapLine(false, other.Top, other.Left, other.Right, SnapLineSource.FrameEdge, other, IsOwnerLow: true),
            new SnapLine(false, other.Bottom, other.Left, other.Right, SnapLineSource.FrameEdge, other, IsOwnerLow: false),
            new SnapLine(false, other.CenterY, other.Left, other.Right, SnapLineSource.FrameCenter, other),
        ];
    }

    /// <summary>由**外壳参照线**生成（外壳只暴露一条边）。</summary>
    /// <param name="line">外壳边（竖线或横线 + 线段范围）。</param>
    /// <returns>一条 <see cref="SnapLineSource.ShellEdge"/> 参照线。</returns>
    public static SnapLine LineOf(SnapReferenceLine line) =>
        new(line.IsVertical, line.Position, line.Start, line.End, SnapLineSource.ShellEdge, default);

    /// <summary>
    /// 把单个矩形吸附到给定参照线；这是 <see cref="SnapGroup"/> 的单成员快捷入口。
    /// </summary>
    /// <param name="moving">被拖动模块的矩形（**原始位置**：指针推出来的位置）。</param>
    /// <param name="lines">参照线（由 <see cref="LinesOf"/> / <see cref="LineOf"/> 生成；**不含**同组成员）。</param>
    /// <param name="tolerance">对齐类容差（px）；贴合类另有 <paramref name="flushTolerance"/>。</param>
    /// <param name="portAnchors">端口锚点对（可选）。</param>
    /// <param name="flushTolerance">贴合类容差（px）。</param>
    /// <param name="portReach">端口锚点的接近范围（px）。</param>
    /// <param name="memory">上一帧的吸附记忆。</param>
    public static SnapResult Snap(
        CanvasRect moving,
        IReadOnlyList<SnapLine> lines,
        double tolerance = DefaultTolerance,
        IReadOnlyList<PortAnchorPair>? portAnchors = null,
        double flushTolerance = FlushTolerance,
        double portReach = PortAlignReach,
        SnapMemory memory = default) =>
        SnapGroup([moving], 0, lines, tolerance, portAnchors, flushTolerance, portReach, obstacles: null, memory);

    /// <summary>
    /// 把一组**刚体平移**的矩形吸附到给定参照线：候选来自每个成员的边/中心/端口，整组共用同一个位移。
    /// </summary>
    /// <param name="moving">组内成员的矩形（**原始位置**：已按指针位移整体推过的位置）。</param>
    /// <param name="anchorIndex">返回的 <see cref="SnapResult.X"/> / <see cref="SnapResult.Y"/> 以哪个成员为基准。</param>
    /// <param name="lines">参照线（**不含**组内成员）。</param>
    /// <param name="tolerance">对齐类容差（px）。</param>
    /// <param name="portAnchors">端口锚点对（<see cref="PortAnchorPair.MovingIndex"/> 指向成员）。</param>
    /// <param name="flushTolerance">贴合类容差（px）。</param>
    /// <param name="portReach">端口锚点的接近范围（px）。</param>
    /// <param name="obstacles">
    /// 组外障碍矩形；若选中的平移会让任一成员与障碍重合，则本帧不吸附（返回原始位置）。
    /// 这样吸附不会主动把模块/组叠到另一个模块上。
    /// </param>
    /// <param name="memory">上一帧的吸附记忆。</param>
    /// <exception cref="ArgumentException"><paramref name="moving"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="anchorIndex"/> 越界。</exception>
    public static SnapResult SnapGroup(
        IReadOnlyList<CanvasRect> moving,
        int anchorIndex,
        IReadOnlyList<SnapLine> lines,
        double tolerance = DefaultTolerance,
        IReadOnlyList<PortAnchorPair>? portAnchors = null,
        double flushTolerance = FlushTolerance,
        double portReach = PortAlignReach,
        IReadOnlyList<CanvasRect>? obstacles = null,
        SnapMemory memory = default)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(lines);

        if (moving.Count == 0)
        {
            throw new ArgumentException("At least one moving rectangle is required.", nameof(moving));
        }

        if ((uint)anchorIndex >= (uint)moving.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        }

        var xs = new List<Candidate>(Math.Max(lines.Count, 1) * moving.Count);
        var ys = new List<Candidate>(Math.Max(lines.Count, 1) * moving.Count);

        foreach (var rect in moving)
        {
            foreach (var line in lines)
            {
                CollectLine(rect, line, xs, ys);
            }
        }

        foreach (var pair in portAnchors ?? [])
        {
            if ((uint)pair.MovingIndex >= (uint)moving.Count)
            {
                continue;
            }

            CollectPortAnchor(moving[pair.MovingIndex], pair, xs, ys, portReach);
        }

        var dedupedX = Deduplicate(xs);
        var dedupedY = Deduplicate(ys);
        var release = Math.Max(tolerance, flushTolerance) + 2;

        var chosenX = PreferMemory(Best(dedupedX, tolerance, flushTolerance), dedupedX, memory.XLine, release);
        var chosenY = PreferMemory(Best(dedupedY, tolerance, flushTolerance), dedupedY, memory.YLine, release);

        var deltaX = chosenX?.Delta ?? 0;
        var deltaY = chosenY?.Delta ?? 0;

        if (obstacles is { Count: > 0 })
        {
            foreach (var rect in moving)
            {
                var translated = rect.OffsetBy(deltaX, deltaY);
                if (obstacles.Any(obstacle => RectsOverlap(translated, obstacle)))
                {
                    var raw = moving[anchorIndex];
                    return new SnapResult(raw.X, raw.Y, false, false, null, null, []);
                }
            }
        }

        var anchor = moving[anchorIndex];
        var x = anchor.X + deltaX;
        var y = anchor.Y + deltaY;
        var snapped = anchor.At(x, y);

        var guides = new List<GuideLine>(2);
        if (chosenX is { } gx)
        {
            guides.Add(new GuideLine(
                IsVertical: true,
                Position: gx.LineCoordinate,
                Start: Math.Min(snapped.Top, gx.LineStart) - GuideOvershoot,
                End: Math.Max(snapped.Bottom, gx.LineEnd) + GuideOvershoot));
        }

        if (chosenY is { } gy)
        {
            guides.Add(new GuideLine(
                IsVertical: false,
                Position: gy.LineCoordinate,
                Start: Math.Min(snapped.Left, gy.LineStart) - GuideOvershoot,
                End: Math.Max(snapped.Right, gy.LineEnd) + GuideOvershoot));
        }

        return new SnapResult(
            x,
            y,
            chosenX is not null,
            chosenY is not null,
            chosenX?.Kind,
            chosenY?.Kind,
            guides,
            chosenX?.LineCoordinate,
            chosenY?.LineCoordinate);
    }

    /// <summary>两个矩形是否存在**正面积**重合（边缘恰好相接不算）。</summary>
    /// <param name="a">矩形 A。</param>
    /// <param name="b">矩形 B。</param>
    /// <param name="epsilon">取整噪声容差（px）；默认 <see cref="OverlapEpsilon"/>。</param>
    public static bool RectsOverlap(CanvasRect a, CanvasRect b, double epsilon = OverlapEpsilon)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return false;
        }

        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > epsilon && height > epsilon;
    }

    /// <summary>取整噪声容差：小于它的“重叠”视为同一条边。</summary>
    public const double OverlapEpsilon = 0.5;

    /// <summary>
    /// 吸附迟滞：上一帧的线仍在释放距离内时优先保持它，避免指针停在阈值附近时"吸/不吸"来回跳。
    /// </summary>
    private static Candidate? PreferMemory(Candidate? chosen, List<Candidate> candidates, double? line, double release)
    {
        if (line is not { } rememberedLine)
        {
            return chosen;
        }

        if (chosen is { } current && Math.Abs(current.LineCoordinate - rememberedLine) <= 0.5)
        {
            return current;
        }

        Candidate? remembered = null;
        var bestDistance = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.LineCoordinate - rememberedLine) > 0.5)
            {
                continue;
            }

            var distance = Math.Abs(candidate.Delta);
            if (distance <= release && distance < bestDistance)
            {
                remembered = candidate;
                bestDistance = distance;
            }
        }

        return remembered ?? chosen;
    }

    /// <summary>
    /// **贴合谓词**（C5-1）：两模块的边缘在容差内贴合即视为拼接。
    /// </summary>
    /// <remarks>竖直边贴合（左右相邻）要求**竖直方向有重叠**，水平边贴合同理。</remarks>
    /// <param name="a">模块 A 矩形。</param>
    /// <param name="b">模块 B 矩形。</param>
    /// <param name="tolerance">容差（px）——默认取贴合容差。</param>
    public static bool AreJoined(CanvasRect a, CanvasRect b, double tolerance = FlushTolerance)
    {
        if (!a.IsValid || !b.IsValid)
        {
            return false;
        }

        // 重合不是贴合：窄模块/部分重叠时，反向外边也可能落在容差内。
        if (RectsOverlap(a, b))
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

    /// <summary>**拼接组**（C5-1）：贴合关系的连通分量（并查集）。</summary>
    /// <param name="modules">(实例 ID, 矩形) 列表。</param>
    /// <param name="tolerance">拼接容差（px）。</param>
    /// <returns>每个分量的实例 ID 列表（按输入顺序，仅含 ≥2 个成员的分量）。</returns>
    public static IReadOnlyList<IReadOnlyList<string>> FindGroups(
        IReadOnlyList<(string Id, CanvasRect Rect)> modules,
        double tolerance = FlushTolerance)
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

    /// <summary>取某实例所在的拼接组（含自身）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="modules">(实例 ID, 矩形) 列表。</param>
    /// <param name="tolerance">拼接容差（px）。</param>
    public static IReadOnlyList<string> GroupOf(
        string instanceId,
        IReadOnlyList<(string Id, CanvasRect Rect)> modules,
        double tolerance = FlushTolerance)
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

    // ---------------------------------------------------------------- 候选收集

    /// <summary>
    /// 由一条参照线生成候选：**允许哪些锚点由线的来源决定**（这是"线语义"的核心）。
    /// </summary>
    private static void CollectLine(CanvasRect moving, SnapLine line, List<Candidate> xs, List<Candidate> ys)
    {
        var size = line.AlignsX ? moving.Width : moving.Height;
        var origin = line.AlignsX ? moving.Left : moving.Top;
        var target = line.AlignsX ? xs : ys;

        switch (line.Source)
        {
            case SnapLineSource.FrameCenter:
                // 中心线：只用模块的中心锚点去对
                Add(0.5, SnapKind.ModuleCenter, line, origin, size, target);
                break;

            case SnapLineSource.FrameEdge:
            {
                // 外框边：模块的**低侧边(锚点 0)** 与 **高侧边(锚点 1)** 都可以对上去。
                // 其中"朝外那条边落在对方外框边上" = 两个模块相接 → 贴合（还要求交叉轴有重叠）。
                var overlap = CrossOverlap(moving, line);
                Add(0, FlushOrEdge(0, line, overlap), line, origin, size, target);
                Add(1, FlushOrEdge(1, line, overlap), line, origin, size, target);
                break;
            }

            case SnapLineSource.ShellEdge:
            {
                // 外壳是一侧锚点：只有被拖模块的**低侧边（竖线的左边缘 / 横线的上边缘）**能贴上去。
                // 另一侧若也参与，模块会以右边缘吸到外壳右边缘 —— 视觉上压在壳上，却不符合
                // `AreNodesJoined` 的"模块左边缘贴外壳右边缘"成组判据。
                var overlap = LineOverlap(moving, line);
                var kind = overlap > MinOverlap ? SnapKind.Flush : SnapKind.Edge;
                Add(0, kind, line, origin, size, target);
                break;
            }

            case SnapLineSource.PortAnchor:
            default:
                break;   // 端口锚点走 CollectPortAnchor（需要"被拖模块的哪个口"）
        }
    }

    /// <summary>端口锚点候选：只用**被拖模块自己的那个口**去对，且两模块要接近。</summary>
    private static void CollectPortAnchor(CanvasRect moving, PortAnchorPair pair, List<Candidate> xs, List<Candidate> ys, double reach)
    {
        if (!pair.Other.IsValid)
        {
            return;
        }

        var crossGap = pair.AlignsX
            ? Gap(moving.Top, moving.Bottom, pair.Other.Top, pair.Other.Bottom)
            : Gap(moving.Left, moving.Right, pair.Other.Left, pair.Other.Right);

        if (crossGap > reach)
        {
            return;
        }

        var size = pair.AlignsX ? moving.Width : moving.Height;
        var origin = pair.AlignsX ? moving.Left : moving.Top;
        var delta = pair.OtherCoordinate - (origin + (size * pair.MovingOffset));
        var line = pair.AlignsX
            ? new SnapLine(true, pair.OtherCoordinate, pair.Other.Top, pair.Other.Bottom, SnapLineSource.PortAnchor, pair.Other)
            : new SnapLine(false, pair.OtherCoordinate, pair.Other.Left, pair.Other.Right, SnapLineSource.PortAnchor, pair.Other);

        (pair.AlignsX ? xs : ys).Add(new Candidate(SnapKind.PortCenter, delta, line.Coordinate, line.AlignsX, line.Start, line.End));
    }

    /// <summary>把"锚点 → 线的坐标"换算成候选并加入该轴。</summary>
    private static void Add(double anchor, SnapKind kind, SnapLine line, double origin, double size, List<Candidate> target)
    {
        var position = line.Coordinate - (size * anchor);
        target.Add(new Candidate(kind, position - origin, line.Coordinate, line.AlignsX, line.Start, line.End));
    }

    /// <summary>外框边候选的级别：**朝外那条边**对上（= 相接）且交叉轴有重叠 → 贴合，否则边缘对齐。</summary>
    /// <param name="anchor">被拖模块的锚点（0 = 低侧边，1 = 高侧边）。</param>
    /// <param name="line">参照线（须为 <see cref="SnapLineSource.FrameEdge"/>）。</param>
    /// <param name="crossOverlap">两模块在交叉轴上的重叠长度。</param>
    private static SnapKind FlushOrEdge(double anchor, SnapLine line, double crossOverlap)
    {
        // 线是对方的低侧边（左/上）时，"我的高侧边落上去"才是相接；线是高侧边（右/下）时反过来。
        var contact = (anchor == 1) == line.IsOwnerLow;
        return contact && crossOverlap > MinOverlap ? SnapKind.Flush : SnapKind.Edge;
    }

    /// <summary>模块与线归属对象在**交叉轴**上的重叠长度。</summary>
    private static double CrossOverlap(CanvasRect moving, SnapLine line) => line.AlignsX
        ? Math.Min(moving.Bottom, line.Owner.Bottom) - Math.Max(moving.Top, line.Owner.Top)
        : Math.Min(moving.Right, line.Owner.Right) - Math.Max(moving.Left, line.Owner.Left);

    /// <summary>模块沿**线方向**与线段的重叠长度（外壳线用）。</summary>
    private static double LineOverlap(CanvasRect moving, SnapLine line) => line.AlignsX
        ? Math.Min(moving.Bottom, line.End) - Math.Max(moving.Top, line.Start)
        : Math.Min(moving.Right, line.End) - Math.Max(moving.Left, line.Start);

    /// <summary>同坐标去重：位置相差 ≤1px 的候选只留级别最高的。</summary>
    private static List<Candidate> Deduplicate(List<Candidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates;
        }

        var ordered = candidates
            .OrderByDescending(c => c.Kind)
            .ThenBy(c => Math.Abs(c.Delta))
            .ToList();

        var kept = new List<Candidate>(ordered.Count);
        foreach (var candidate in ordered)
        {
            var duplicate = false;

            foreach (var existing in kept)
            {
                // 同一轴上的候选可以只比 Delta（origin 相同 ⇒ Delta 差 = 位置差）
                if (Math.Abs(existing.Delta - candidate.Delta) <= DuplicateEpsilon)
                {
                    duplicate = true;   // kept 里的级别都不低于它（已按级别降序）
                    break;
                }
            }

            if (!duplicate)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>单轴选最好的候选：**级别高者优先，同级取偏移更小者**（各自按自己的容差判是否够近）。</summary>
    private static Candidate? Best(List<Candidate> candidates, double alignTolerance, double flushTolerance)
    {
        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            var band = candidate.Kind == SnapKind.Flush ? flushTolerance : alignTolerance;
            if (Math.Abs(candidate.Delta) > band)
            {
                continue;
            }

            if (best is not { } current
                || candidate.Kind > current.Kind
                || (candidate.Kind == current.Kind && Math.Abs(candidate.Delta) < Math.Abs(current.Delta)))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>两区间之间的间隙（0 = 重叠或相接）。</summary>
    private static double Gap(double aStart, double aEnd, double bStart, double bEnd) =>
        Math.Max(0, Math.Max(aStart - bEnd, bStart - aEnd));

    // ---------------------------------------------------------------- 并查集

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
