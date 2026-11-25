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
    /// <summary>
    /// **结果宽/高等于另一模块**（仅缩放产生）——最低级；没有共享边，只有尺寸关系。
    /// </summary>
    Size = -1,

    /// <summary>边缘对齐（左/右/上/下分别对齐）——**与距离无关**（跨屏对齐用它）。</summary>
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

    /// <summary>**外壳边**（右侧拼合边）：只接受被拖模块的**低侧边**（竖线为左边缘）贴上去。</summary>
    ShellEdge,

    /// <summary>**外壳对齐边**（左/上/下）：两侧边都可用于对齐，但不产生贴合（不成组）。</summary>
    ShellAlign,
}

/// <summary>外壳参照线的角色：拼合边 vs 纯对齐边。</summary>
public enum ShellLineRole
{
    /// <summary>右侧拼合边：模块左边缘贴上去即拼上（可产生 Flush）。</summary>
    Join = 0,

    /// <summary>其它边：只做边对齐，不参与成组。</summary>
    Align = 1,
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
/// <param name="IsOwnerLow">仅外框边（<see cref="SnapLineSource.FrameEdge"/>）有意义：是否为归属对象的**低侧**边（左/上）。</param>
/// <param name="OwnerLayer">
/// 归属对象所在的模块层 = 吸上它之后的**目标层**（外壳线与无归属线为 0 = 底板）。
/// 它只用于判定"落位会不会**新增遮挡**"，不参与候选排序（排序见 <see cref="SnapKind"/>）。
/// </param>
public readonly record struct SnapLine(
    bool AlignsX,
    double Coordinate,
    double Start,
    double End,
    SnapLineSource Source,
    CanvasRect Owner,
    bool IsOwnerLow = false,
    int OwnerLayer = 0);

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
/// 与 <see cref="TilingGraph.IsShellGlued"/> 的"模块左边缘贴外壳右边缘"保持同一侧。
/// 之所以不复用矩形：零宽矩形会被 <see cref="CanvasRect.IsValid"/>（要求正尺寸）过滤掉（P25）。
/// </remarks>
public readonly record struct SnapReferenceLine(
    bool IsVertical,
    double Position,
    double Start,
    double End,
    ShellLineRole Role = ShellLineRole.Join);

/// <summary>
/// **端口锚点对**：被拖模块的某个口 ↔ 对方某个口。
/// </summary>
/// <param name="AlignsX">对齐轴是否约束 X（**由端口所在的边决定**：左/右边的口 → 约束 Y；上/下边的口 → 约束 X）。</param>
/// <param name="OtherCoordinate">对方端口在**该对齐轴**上的坐标。</param>
/// <param name="Other">对方模块矩形（用于接近范围判定与辅助线长度）。</param>
/// <param name="MovingOffset">被拖模块该端口在对齐轴上的**归一化位置**（0–1）。</param>
/// <param name="MovingIndex">组吸附时该端口所属成员在 <c>moving</c> 列表里的下标（单模块为 0）。</param>
/// <param name="OwnerLayer">对方模块所在的层（= 吸上它之后的目标层；只用于判落位新增的遮挡）。</param>
public readonly record struct PortAnchorPair(bool AlignsX, double OtherCoordinate, CanvasRect Other, double MovingOffset, int MovingIndex = 0, int OwnerLayer = 0);

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
///     **外壳 = 右侧拼合边 + 其它三条对齐边**（不是特殊通道，同样进线表）；</item>
///   <item><b>级别由「线来源 + 交叉轴条件」派生</b>：贴合 &gt; 端口锚点 &gt; 中心线 &gt; 边缘对齐；</item>
///   <item><b>同坐标候选去重</b>：两条候选位置相差 ≤1px 时只保留级别最高的 ——
///     消除"端口在 0.5 与模块中心线"这类**重复登记**（同一坐标只登记一次，级别最高的那条胜出）；</item>
///   <item><b>不做网格吸附</b>；<b>调用方决定"何时用"</b>（当前 = 拖动与缩放中实时吸附）；</item>
///   <item><b>组关系由位置派生</b>（C5-1）：只保存位置，不单独存储；组解析在 UI 层按
///     <b>贴合 + 互不遮挡</b>完成（<see cref="TilingGraph"/>），本类只提供
///     <see cref="AreJoined"/> 这个纯几何相邻谓词；</item>
/// </list>
/// </remarks>
public static partial class SnapEngine
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

    /// <summary>吸附候选（内部）：级别 + 偏移 + 参照线坐标 + 该线的区间 + **目标层**。</summary>
    /// <param name="TargetLayer">吸上这条线之后的模块层（= 参照物所在层）：判定"会不会压住目标层"用。</param>
    private readonly record struct Candidate(
        SnapKind Kind,
        double Delta,
        double LineCoordinate,
        bool AlignsX,
        double LineStart,
        double LineEnd,
        int TargetLayer = 0,
        string? ReferenceId = null);

    /// <summary>
    /// 由**其他模块外框**生成参照线（4 条边 + 2 条中心线，两个轴各一批）。
    /// </summary>
    /// <param name="other">其他模块矩形。</param>
    /// <param name="ownerLayer">该模块所在的层（写进每条线的 <see cref="SnapLine.OwnerLayer"/>）。</param>
    /// <returns>该模块提供的全部参照线。</returns>
    /// <remarks>
    /// <b>一个模块只提供一套线</b>：无论它与被拖模块差几层，4 条边都同时可用于贴合与对齐。
    /// 层不参与"谁能跟谁对齐"——层只回答"落位会不会新增遮挡，以及这个遮挡看不看得见"，
    /// 由调用方通过 <see cref="SnapLine.OwnerLayer"/> 与 <see cref="SnapObstacle"/> 一起判。
    /// 若按层剪裁候选（例如只给跨层模块留"能拼上的边"），同一件事就有两套判据，
    /// 结果必然是"能贴合却不能对齐"——两个判据总会在某处给出不同答案。
    /// </remarks>
    public static IEnumerable<SnapLine> LinesOf(CanvasRect other, int ownerLayer = 0)
    {
        if (!other.IsValid)
        {
            return [];
        }

        SnapLine[] lines =
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

        if (ownerLayer == 0)
        {
            return lines;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index] with { OwnerLayer = ownerLayer };
        }

        return lines;
    }

    /// <summary>由**外壳参照线**生成（右侧拼合边 / 其它对齐边）。</summary>
    /// <param name="line">外壳边（竖线或横线 + 线段范围 + 角色）。</param>
    /// <returns>一条 <see cref="SnapLineSource.ShellEdge"/> 或 <see cref="SnapLineSource.ShellAlign"/> 参照线。</returns>
    /// <remarks>
    /// 外壳线的 <see cref="SnapLine.OwnerLayer"/> 保持 0（外壳是层 0 的底板）：
    /// 贴壳意味着"站在地板上贴墙"，所以地板这一层必须真有空位。
    /// </remarks>
    public static SnapLine LineOf(SnapReferenceLine line) =>
        new(
            line.IsVertical,
            line.Position,
            line.Start,
            line.End,
            line.Role == ShellLineRole.Align ? SnapLineSource.ShellAlign : SnapLineSource.ShellEdge,
            default);

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
    /// 组外障碍（**带层号**，见 <see cref="SnapObstacle"/>）：每条候选各自校验，
    /// 只有"落位会**新增遮挡**且这个遮挡看得见"的那一条被剔除，其余候选照常参与，另一轴也不受牵连。
    /// </param>
    /// <param name="memory">上一帧的吸附记忆。</param>
    /// <exception cref="ArgumentException"><paramref name="moving"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="anchorIndex"/> 越界。</exception>
    /// <remarks>
    /// <b>为什么障碍要按层判、而且逐候选判</b>：
    /// <list type="bullet">
    ///   <item>压在**更低层**的模块上是合法的（层就是为叠放引入的），把它当障碍会让密集布局里
    ///     "目标位置背后有块东西"就再也拼不上；</item>
    ///   <item>整帧一票否决会让一条坏候选连累另一轴合法的吸附，且不换下一个候选——用户看到的是"完全吸不上"。</item>
    /// </list>
    /// <para>
    /// <b>判据为什么是充要的</b>：设参照件 R 在层 k（件内恒同层）。落位后"真的拼上"要求
    /// 与 R 件任何成员都不重合；而本判据已排除"与层号 ≥ k 的模块重合"⇒ 必然满足。
    /// 同时新模块的层 = 下层重叠件的最大层 + 1 ≤ k，又 R 在层 k 说明 R 压着层 k−1 的东西 ⇒ 层 ≥ k，
    /// 故**层恰为 k**：整件不会被顶上去，参照件也不会被连累抬层。
    /// 即：判据等价于"**该层有空间容纳它，才拼到这一层**"。
    /// </para>
    /// <para>
    /// <b>判据只管"新增"的遮挡</b>：手势开始时就已经重合的实体（<see cref="SnapObstacle.AlreadyOverlaps"/>）
    /// 不参与判定。否则"压在主窗口上"或"两块叠在一起"这种平凡情形会让每条候选都被判掉，
    /// 表现为**完全吸不上**——遮挡没有变多，不该被拦。
    /// </para>
    /// </remarks>
    public static SnapResult SnapGroup(
        IReadOnlyList<CanvasRect> moving,
        int anchorIndex,
        IReadOnlyList<SnapLine> lines,
        double tolerance = DefaultTolerance,
        IReadOnlyList<PortAnchorPair>? portAnchors = null,
        double flushTolerance = FlushTolerance,
        double portReach = PortAlignReach,
        IReadOnlyList<SnapObstacle>? obstacles = null,
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

        bool AllowsOnX(Candidate candidate) => AllowsDelta(moving, candidate.Delta, 0, candidate.TargetLayer, obstacles);
        bool AllowsOnY(Candidate candidate) => AllowsDelta(moving, 0, candidate.Delta, candidate.TargetLayer, obstacles);

        var chosenX = PreferMemory(
            Best(dedupedX, tolerance, flushTolerance, AllowsOnX),
            dedupedX,
            memory.XLine,
            release,
            AllowsOnX);

        var chosenY = PreferMemory(
            Best(dedupedY, tolerance, flushTolerance, AllowsOnY),
            dedupedY,
            memory.YLine,
            release,
            AllowsOnY);

        // 两轴各自合法不代表**同时**合法（两轴都要吸时才可能）：按"双轴 → 只 X → 只 Y → 不吸附"退化。
        // 双轴同时成立时，约束取两者中更严的那一层（层号大者）。
        if (chosenX is { } bothX && chosenY is { } bothY
            && !AllowsDelta(moving, bothX.Delta, bothY.Delta, Math.Max(bothX.TargetLayer, bothY.TargetLayer), obstacles))
        {
            if (AllowsOnX(bothX))
            {
                chosenY = null;
            }
            else if (AllowsOnY(bothY))
            {
                chosenX = null;
            }
            else
            {
                chosenX = null;
                chosenY = null;
            }
        }

        var deltaX = chosenX?.Delta ?? 0;
        var deltaY = chosenY?.Delta ?? 0;

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

    /// <summary>
    /// 该位移是否**允许**（不新增一个看得见的遮挡）。
    /// </summary>
    /// <param name="moving">组内成员矩形（原始位置）。</param>
    /// <param name="deltaX">X 位移。</param>
    /// <param name="deltaY">Y 位移。</param>
    /// <param name="targetLayer">候选的目标层（参照物所在层）。</param>
    /// <param name="obstacles">带层号的障碍；null = 不做该判定（单矩形入口与测试）。</param>
    /// <remarks>
    /// 判据见 <see cref="SnapObstacle.Blocks"/>：**目标层及以上**的模块都算挡住。
    /// 之所以不能放宽到"任意层都挡"，是因为压在更低层上完全合法，会把密集布局里的拼接全部误杀。
    /// 起点已重合的障碍（<see cref="SnapObstacle.AlreadyOverlaps"/>）不参与：遮挡没有变多就不该拦。
    /// </remarks>
    private static bool AllowsDelta(
        IReadOnlyList<CanvasRect> moving,
        double deltaX,
        double deltaY,
        int targetLayer,
        IReadOnlyList<SnapObstacle>? obstacles)
    {
        if (obstacles is not { Count: > 0 })
        {
            return true;
        }

        foreach (var rect in moving)
        {
            var translated = rect.OffsetBy(deltaX, deltaY);

            foreach (var obstacle in obstacles)
            {
                if (obstacle.Blocks(targetLayer) && RectsOverlap(translated, obstacle.Rect))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>取整噪声容差：小于它的“重叠”视为同一条边。</summary>
    public const double OverlapEpsilon = 0.5;

    /// <summary>
    /// 吸附迟滞：上一帧的线仍在释放距离内时优先保持它，避免指针停在阈值附近时"吸/不吸"来回跳。
    /// </summary>
    /// <param name="chosen">本帧几何上最优的候选（可能被记忆替换）。</param>
    /// <param name="candidates">同轴全部候选。</param>
    /// <param name="line">上一帧的参照线坐标。</param>
    /// <param name="release">释放距离（px）。</param>
    /// <param name="allowed">该候选是否可用（障碍判定；被记忆选中的候选同样要过这一关）。</param>
    private static Candidate? PreferMemory(
        Candidate? chosen,
        List<Candidate> candidates,
        double? line,
        double release,
        Func<Candidate, bool> allowed)
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
            if (Math.Abs(candidate.LineCoordinate - rememberedLine) > 0.5 || !allowed(candidate))
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
    /// **贴合谓词**（C5-1）：两模块的边缘在容差内贴合即视为几何相邻。
    /// </summary>
    /// <remarks>
    /// 这是**纯几何**谓词，**不含层**：两个模块可以几何贴合但位于不同层（层号不同并不妨碍拼接）。
    /// 竖直边贴合（左右相邻）要求**竖直方向有重叠**，水平边贴合同理。
    /// 成组不能直接用它：还需叠加"**互不遮挡**"（见 <see cref="TilingGraph"/>）——
    /// 一个**压住**对方某块的模块贴得再近也不许拼，否则第二层的模块会拼到第一层的组上。
    /// </remarks>
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
                // 外壳右侧是拼合边：只有被拖模块的**低侧边（竖线的左边缘 / 横线的上边缘）**能贴上去。
                // 另一侧若也参与，模块会以右边缘吸到外壳右边缘 —— 视觉上压在壳上，却不符合
                // 贴壳判据（只有"模块左边缘贴外壳右边缘"才算拼上）。
                var overlap = LineOverlap(moving, line);
                var kind = overlap > MinOverlap ? SnapKind.Flush : SnapKind.Edge;
                Add(0, kind, line, origin, size, target);
                break;
            }

            case SnapLineSource.ShellAlign:
            {
                // 外壳的其它边只是对齐参照：两侧边都可以吸齐，但永远不产生贴合（不对齐成组判据）。
                Add(0, SnapKind.Edge, line, origin, size, target);
                Add(1, SnapKind.Edge, line, origin, size, target);
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

        (pair.AlignsX ? xs : ys).Add(new Candidate(SnapKind.PortCenter, delta, line.Coordinate, line.AlignsX, line.Start, line.End, pair.OwnerLayer));
    }

    /// <summary>把"锚点 → 线的坐标"换算成候选并加入该轴。</summary>
    private static void Add(double anchor, SnapKind kind, SnapLine line, double origin, double size, List<Candidate> target)
    {
        var position = line.Coordinate - (size * anchor);
        target.Add(new Candidate(kind, position - origin, line.Coordinate, line.AlignsX, line.Start, line.End, line.OwnerLayer));
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

    /// <summary>
    /// 同坐标去重：位置相差 ≤1px 的候选只留级别最高的；**级别与距离都相同时留层号更大的**。
    /// </summary>
    /// <remarks>
    /// 层号参与排序是为了"上层优先"：两个完全重合、大小相同的组给出的线坐标一模一样，
    /// 新模块的几何判据对两者同时成立 —— 此时应归到**上面**那一层（高层的件在上面，抬它不抬别人）。
    /// </remarks>
    private static List<Candidate> Deduplicate(List<Candidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates;
        }

        var ordered = candidates
            .OrderByDescending(c => c.Kind)
            .ThenBy(c => Math.Abs(c.Delta))
            .ThenByDescending(c => c.TargetLayer)
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

    /// <summary>
    /// 单轴选最好的候选：**级别高者优先，同级取偏移更小者**（各自按自己的容差判是否够近）；
    /// 级别与偏移都相同时取**层号更大**者（上层优先）。不合格的候选在 <paramref name="allowed"/> 里被跳过。
    /// </summary>
    private static Candidate? Best(
        List<Candidate> candidates,
        double alignTolerance,
        double flushTolerance,
        Func<Candidate, bool> allowed)
    {
        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            var band = candidate.Kind is SnapKind.Flush or SnapKind.Size ? flushTolerance : alignTolerance;
            if (Math.Abs(candidate.Delta) > band || !allowed(candidate))
            {
                continue;
            }

            if (best is not { } current || IsBetter(candidate, current))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>候选比较：级别优先，其次偏移更小，最后**层号更大**（上层优先）。</summary>
    private static bool IsBetter(Candidate candidate, Candidate current)
    {
        if (candidate.Kind != current.Kind)
        {
            return candidate.Kind > current.Kind;
        }

        var distance = Math.Abs(candidate.Delta);
        var currentDistance = Math.Abs(current.Delta);

        if (Math.Abs(distance - currentDistance) > DuplicateEpsilon)
        {
            return distance < currentDistance;
        }

        return candidate.TargetLayer > current.TargetLayer;
    }

    /// <summary>两区间之间的间隙（0 = 重叠或相接）。</summary>
    private static double Gap(double aStart, double aEnd, double bStart, double bEnd) =>
        Math.Max(0, Math.Max(aStart - bEnd, bStart - aEnd));
}
