using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

namespace HYWDebugAssistant.Ui.ModuleSurface.Layout;

/// <summary>
/// **块轴事实** —— 一个节点在纵向排布里只回答两件事：**吃不吃余量**、**最少要多高**。
/// </summary>
/// <remarks>
/// <para>
/// 它是**声明面 + 控件目录**两处的合成结果，合成规则只写在这里（<see cref="FromNode"/>）——
/// 于是"这一种控件吃不吃余量、它的地板是多少"在整个渲染侧**只有一个出处**。
/// </para>
/// <para>
/// <b>地板只有声明一个来源</b>（<c>height.min</c>）：目录只说"吃不吃余量"，**不带高度数值** ——
/// 地板散进代码之后，作者看不出、也改不了自己的界面在窗口压小时剩多少。
/// </para>
/// <para>
/// 未知 kind（目录里没有）⇒ 自适应，**不静默当成填满**（那会让一个拼错的 kind 悄悄吃掉整窗高度）。
/// </para>
/// </remarks>
/// <param name="IsFill">是否吃容器剩余纵向余量。</param>
/// <param name="MinHeight">窗口压小时的最小高（DIP；0 = 无下限）。</param>
public readonly record struct SurfaceMetrics(bool IsFill, double MinHeight)
{
    /// <summary>非填满、无下限。</summary>
    public static readonly SurfaceMetrics None = new(false, 0);

    /// <summary>
    /// 从一个**节点自身**（不看子节点）推出块轴事实。
    /// </summary>
    /// <param name="node">声明节点。</param>
    /// <remarks>
    /// <para>
    /// 优先级：声明面**逐元素改轴** &gt; 目录档位（"这一种控件吃不吃余量"）。
    /// 声明面写了 <c>height.fill</c> ⇒ 填满；写了 <c>height.min</c> ⇒ **那就是地板**。
    /// </para>
    /// <para>
    /// <b>不看子节点是有意的</b>：容器内部的结构高度（页签条、标题行）与子项的地板**不进**外层的下限。
    /// 想让默认窗口就放得下，这些数必须由作者算进自己的 <c>height.min</c> 与 <c>frame.window</c>
    /// （见 `docs/view-declaration/排布与坐标.md`）—— 框架替作者"猜"的话，
    /// "放不下"就会变成**静默压内部**：容器里的东西被压扁一两像素，而外面看不出任何异常。
    /// </para>
    /// </remarks>
    public static SurfaceMetrics FromNode(ViewNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var descriptor = ControlCatalog.ControlCatalog.Find(node.Kind);
        var strategy = descriptor is null ? SurfaceGeometryStrategy.Adaptive : descriptor.GeometryStrategy;

        // 声明面逐元素改轴：显式写 fill 就等于"这个元素自己说要填满"。
        var fill = node.Height.Fill || strategy.IsFill;
        if (!fill)
        {
            return None;
        }

        var floor = node.Height.Min ?? 0;
        return new SurfaceMetrics(true, Math.Max(0, floor));
    }
}
