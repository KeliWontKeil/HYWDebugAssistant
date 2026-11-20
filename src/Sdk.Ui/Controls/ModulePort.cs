using Avalonia;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Ui.Geometry;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块视图里的一个端口声明**（XAML 可写）：名称、方向、类型标识、默认边/偏移/可拖动性。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只声明，不画</b>：运行时标记由框架窗口绘制，预览外壳只按它画占位标记；</item>
///   <item><b>形状/语义不在这里</b>：由 <see cref="TypeId"/> 指向的数据类型决定（同一标识 = 同一类型）；</item>
///   <item>它同时是 <c>PortSpecReader</c> 读取的源（包内 `views/*.axaml`），两边字段名必须一致。</item>
/// </list>
/// </remarks>
public sealed class ModulePort : AvaloniaObject
{
    /// <summary>口名（模块内唯一；XAML 里用 `{x:Static}` 指向模块常量）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>方向。</summary>
    public PortDirection Direction { get; set; }

    /// <summary>数据类型标识（必须是 `OnRegistered` 里已声明的类型）。</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>默认挂载边（`Auto` = 发送右、接收左）。</summary>
    public PortEdge Edge { get; set; } = PortEdge.Auto;

    /// <summary>默认沿边归一化偏移（0–1）。</summary>
    public double Offset { get; set; } = 0.5;

    /// <summary>默认是否可拖动（false = 框架固定项；用户仍可全局解锁）。</summary>
    public bool AnchorDraggable { get; set; }

    /// <summary>可选传递语义覆盖（空 = 取数据类型声明的语义）。</summary>
    public TransferSemantics? Semantics { get; set; }

    /// <summary>是否发送口（预览标记的方向色/箭头用）。</summary>
    public bool IsSend => Direction == PortDirection.Send;

    /// <summary>解析后的挂载边（`Auto` → 发送右 / 接收左）。</summary>
    public PortEdge ResolvedEdge => Edge == PortEdge.Auto ? PortEdgeGeometry.DefaultFor(Direction) : Edge;

    /// <summary>
    /// 预览排布用的边：就是 <see cref="Edge"/>（放不下时 <see cref="ResolveEdge"/> 已把它改过去）。
    /// </summary>
    public PortSide Side => PortMarkerLayout.SideOf(ResolvedEdge);

    /// <summary>
    /// 按预览窗口尺寸把**放不下的边直接改掉**（创建预览外壳时调用一次；设计器内改尺寸不重解析）。
    /// </summary>
    /// <param name="width">预览窗口宽。</param>
    /// <param name="height">预览窗口高。</param>
    public void ResolveEdge(double width, double height)
    {
        Edge = PortMarkerLayout.FitEdge(ResolvedEdge, Direction, width, height);
    }

    /// <summary>竖排（左右边）还是横排（上下边）。</summary>
    public bool IsVertical => Side is PortSide.Left or PortSide.Right;

    /// <summary>预览标记宽（与运行时同一份规格）。</summary>
    public double MarkerWidth => IsVertical ? PortMarkerMetrics.Width : PortMarkerMetrics.Length;

    /// <summary>预览标记高（与运行时同一份规格）。</summary>
    public double MarkerHeight => IsVertical ? PortMarkerMetrics.Length : PortMarkerMetrics.Width;

    /// <summary>转成加载链使用的声明规格（预览/快照不需要，Hosting 读取 XAML 源时用另一条解析器）。</summary>
    public PortDeclarationSpec ToSpec() => new(
        Name,
        Direction,
        TypeId,
        Edge,
        Offset,
        AnchorDraggable,
        Semantics);
}
