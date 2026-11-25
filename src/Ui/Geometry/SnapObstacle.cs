namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **吸附障碍**：一个"吸附不许主动压上去"的矩形，外加它所在的层。
/// </summary>
/// <param name="Rect">障碍矩形（与参照线同一单位）。</param>
/// <param name="Layer">该障碍所在的模块层（层号越大越靠上）。</param>
/// <param name="AlwaysBlocks">
/// true = **任何目标层都不许重合**（外壳专用：它是底板，"拼上 = 贴边不压"对它同样成立）。
/// </param>
/// <remarks>
/// <b>为什么障碍必须带层</b>：压在**更低层**的模块上是合法的（层就是为叠放引入的），
/// 把它当障碍会让密集布局里"目标位置背后有块东西"就再也拼不上。
/// 带层之后，判据变成"会不会压住**目标层**"——见 <see cref="SnapEngine.SnapGroup"/>。
/// </remarks>
public readonly record struct SnapObstacle(CanvasRect Rect, int Layer, bool AlwaysBlocks = false)
{
    /// <summary>该障碍是否挡住在 <paramref name="targetLayer"/> 落位。</summary>
    /// <param name="targetLayer">候选的目标层（参照物所在层）。</param>
    /// <remarks>
    /// 目标层及以上都挡住：压住同层 ⇒ 不能与它共面（会把自己顶到更高层，连参照件一起抬）；
    /// 压住更高层 ⇒ 自己会被挡住看不见。只有**更低层**的模块可以被压。
    /// </remarks>
    public bool Blocks(int targetLayer) => AlwaysBlocks || Layer >= targetLayer;
}
