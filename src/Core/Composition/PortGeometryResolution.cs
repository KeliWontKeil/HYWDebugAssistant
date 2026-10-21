using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// **端口几何的最终解析**——用户覆盖 &gt; 模块代码里声明的默认位置（**没有惯例回退**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么单独一处</b>：与 <see cref="PortEdgeResolution"/> 同理 —— 端口几何有三个消费者
///     （呈现、建网判定、UI 吸附），规则各写一份迟早"一处改了另一处没改"；</item>
///   <item><b>边的优先级</b>：用户拖动（<c>stored.Edge</c> ≠ <c>Auto</c>）&gt;
///     <see cref="PortDeclaration.Edge"/>；声明默认是 <see cref="PortEdge.Auto"/>（= 沿用数据流惯例）；</item>
///   <item><b>偏移</b>：用户工作区里有这个口的记录 ⇒ 用记录里的值，否则用声明的 <see cref="PortDeclaration.AnchorOffset"/>；</item>
///   <item><b>可拖动性</b>：用户显式设过三态值则以用户为准，否则看 <see cref="PortDeclaration.AnchorDraggable"/>
///     （默认固定）。</item>
/// </list>
/// </remarks>
internal static class PortGeometryResolution
{
    /// <summary>解析端口的**有效几何**（边、偏移、标签）。</summary>
    /// <param name="declaration">该口的代码级声明（默认边 / 默认偏移的唯一来源）。</param>
    /// <param name="stored">工作区里存的几何（<c>Auto</c> = 用户没拖过这条边）。</param>
    /// <param name="userOverrode">工作区里是否**有这个口的记录**（true ⇒ 偏移以记录为准）。</param>
    public static PortGeometryView Resolve(
        PortDeclaration declaration,
        PortGeometryView stored,
        bool userOverrode)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var edge = PortEdgeResolution.Resolve(DeclaredEdge(declaration), stored.Edge);
        var offset = userOverrode ? stored.Offset : declaration.AnchorOffset;

        return stored with { Offset = offset, Edge = edge };
    }

    /// <summary>
    /// 声明里的边；<see cref="PortEdge.Auto"/> ⇒ **数据流惯例**（发送在右、接收在左）。
    /// </summary>
    /// <param name="declaration">口声明。</param>
    /// <remarks>
    /// <c>Auto</c> 是"我没特别要求"的**声明取值**（不是"没声明"）：模块只关心"我这个口是进还是出"时，
    /// 不必逐个去写边，于是新模块的默认形态就是"发送在右、接收在左"——
    /// 那正是上下/左右叠放时最容易连上的一档。要特例就写具体边。
    /// </remarks>
    private static PortEdge DeclaredEdge(PortDeclaration declaration) => declaration.Edge switch
    {
        PortEdge.Auto => declaration.Direction == PortDirection.Send ? PortEdge.Right : PortEdge.Left,
        var edge => edge,
    };

    /// <summary>解析锚点**是否固定**（不可拖动）。</summary>
    /// <param name="overridden">用户设置过的三态值（非空 = 用户说了算）。</param>
    /// <param name="declaredDraggable">模块声明的默认可拖（<see cref="PortDeclaration.AnchorDraggable"/>）。</param>
    public static bool ResolveAnchorFixed(bool? overridden, bool declaredDraggable) =>
        overridden ?? !declaredDraggable;
}
