using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;

namespace HYWDebugAssistant.Ui.ModuleSurface.Hosting;

/// <summary>
/// 表面树上的一个节点：**一条声明 + 它的孩子**。
/// </summary>
/// <remarks>
/// <para>
/// 它是"声明树"与"视觉树"之间唯一的中介：排布面板只从这里读
/// <see cref="Node"/>（几何与排布键）与 <see cref="Metrics"/>（块轴事实），
/// 编排层只在这里挂已经造好的视觉。
/// </para>
/// <para>
/// **它不持有视觉**：视觉由编排层放进自己的表里（<see cref="ModuleSurfaceHost"/>）——
/// 这样"重建视觉"与"重建节点"是两件事，切页时可以只换视觉而不动节点。
/// </para>
/// </remarks>
internal sealed class SurfaceItem : ISurfaceLayoutItem
{
    private readonly List<SurfaceItem> _children = [];

    /// <summary>造一个节点。</summary>
    /// <param name="node">它的声明节点。</param>
    /// <param name="isExpanded">展开态（只有可折叠容器会为 <see langword="false"/>；折叠着不占高、也不向父层要高度）。</param>
    internal SurfaceItem(ViewNode node, bool isExpanded = true)
    {
        ArgumentNullException.ThrowIfNull(node);
        Node = node;
        IsExpanded = isExpanded;
        Metrics = SurfaceMetrics.FromNode(node);
    }

    /// <inheritdoc />
    public ViewNode Node { get; }

    /// <inheritdoc />
    public SurfaceMetrics Metrics { get; }

    /// <inheritdoc />
    public bool IsExpanded { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<ISurfaceLayoutItem> Items => _children;

    /// <summary>按声明顺序追加一个孩子。</summary>
    /// <param name="child">孩子节点。</param>
    internal void Add(SurfaceItem child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    /// <summary>
    /// 就地改写展开态。
    /// </summary>
    /// <param name="expanded">新的展开态。</param>
    /// <remarks>
    /// 折叠态的**唯一消费者是排布引擎**（"折叠着不占高、也不向父层要高度"），而引擎读的就是
    /// <see cref="IsExpanded"/> ⇒ 就地改写即可，不必重建节点、更不必重建视觉。
    /// </remarks>
    internal void SetExpanded(bool expanded) => IsExpanded = expanded;
}
