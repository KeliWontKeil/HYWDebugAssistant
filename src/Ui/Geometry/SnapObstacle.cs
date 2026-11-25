namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **吸附障碍**：一个"吸附不许**新增**遮挡"的矩形，外加它所在的层。
/// </summary>
/// <param name="Rect">障碍矩形（与参照线同一单位）。</param>
/// <param name="Layer">该障碍所在的模块层（层号越大越靠上）。</param>
/// <param name="AlwaysBlocks">
/// true = **任何目标层都不许新增重合**（外壳专用：它是底板，"拼上 = 贴边不压"对它同样成立）。
/// </param>
/// <param name="AlreadyOverlaps">
/// true = 手势**开始时**这块就已经与移动集合重合：遮挡不会因为本次吸附变多，因此不参与判定。
/// </param>
/// <remarks>
/// <para>
/// <b>为什么障碍必须带层</b>：压在**更低层**的模块上是合法的（层就是为叠放引入的），
/// 把它当障碍会让密集布局里"目标位置背后有块东西"就再也拼不上。
/// 带层之后，判据变成"落位会不会新增一个**看得见**的遮挡"——见 <see cref="SnapEngine.SnapGroup"/>。
/// </para>
/// <para>
/// <b>为什么还要记"起点已重合"</b>：判据问的是增量，不是绝对状态。已经叠在一起的实体
/// （典型是压在主窗口上的模块）若照常参与判定，它会让**每一条**候选都被判掉，
/// 表现为"完全吸不上、连辅助线都没有"——遮挡并没有变多，拦它就是错的。
/// 由调用方按**手势起点的几何**一次性标定，所以拖动仍是指针位置的纯函数。
/// </para>
/// </remarks>
public readonly record struct SnapObstacle(
    CanvasRect Rect,
    int Layer,
    bool AlwaysBlocks = false,
    bool AlreadyOverlaps = false)
{
    /// <summary>该障碍是否挡住在 <paramref name="targetLayer"/> 落位。</summary>
    /// <param name="targetLayer">候选的目标层（参照物所在层）。</param>
    /// <remarks>
    /// 目标层及以上都挡住：压住同层 ⇒ 不能与它共面（会把自己顶到更高层，连参照件一起抬）；
    /// 压住更高层 ⇒ 自己会被挡住看不见。只有**更低层**的模块可以被压。
    /// 起点已重合的障碍一律不挡（见 <see cref="AlreadyOverlaps"/>）。
    /// </remarks>
    public bool Blocks(int targetLayer) => !AlreadyOverlaps && (AlwaysBlocks || Layer >= targetLayer);
}
