using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// **端口几何的最终解析**——用户覆盖 &gt; 包内 `view.json` 声明的默认位置（**没有惯例回退**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么单独一处</b>：与 <see cref="PortEdgeResolution"/> 同理 —— 端口几何有三个消费者
///     （呈现、建网判定、UI 吸附），规则各写一份迟早"一处改了另一处没改"；</item>
///   <item><b>边的优先级</b>：用户拖动（<c>stored.Edge</c> ≠ <c>Auto</c>）&gt; 声明里的
///     <c>ports[].edge</c>；**声明是唯一来源** —— 每个数据面口都必须在声明里显式给出 edge
///     （缺条目 / 缺 edge = 硬伤，整份声明忽略），框架不再按"发送右 / 接收左"推断；</item>
///   <item><b>偏移</b>：用户工作区里有这个口的记录 ⇒ 用记录里的值，否则用声明里的 <c>offset</c>；</item>
///   <item><b>标签与可拖动性</b>：两者都是**框架固定项** —— 标签文案由数据面口名给，锚点默认固定
///     （用户显式设过三态值则以用户为准）。</item>
/// </list>
/// </remarks>
internal static class PortGeometryResolution
{
    /// <summary>解析端口的**有效几何**（边、偏移、标签）。</summary>
    /// <param name="frame">包内 `view.json` 的框架级声明（没有 ⇒ 全走缺省；此时带口的数据面已是硬伤）。</param>
    /// <param name="portName">口名。</param>
    /// <param name="stored">工作区里存的几何（<c>Auto</c> = 用户没拖过这条边）。</param>
    /// <param name="userOverrode">工作区里是否**有这个口的记录**（true ⇒ 偏移以记录为准）。</param>
    public static PortGeometryView Resolve(
        ViewFrame? frame,
        string portName,
        PortGeometryView stored,
        bool userOverrode)
    {
        var declared = Declared(frame, portName);

        var edge = PortEdgeResolution.Resolve(declared?.Edge ?? PortEdge.Auto, stored.Edge);
        var offset = userOverrode ? stored.Offset : declared?.Offset ?? stored.Offset;

        return stored with { Offset = offset, Edge = edge };
    }

    /// <summary>解析锚点**是否固定**（不可拖动）——锚点固定是框架固定项，只有用户显式设过才让位。</summary>
    /// <param name="overridden">用户设置过的三态值（非空 = 用户说了算）。</param>
    public static bool ResolveAnchorFixed(bool? overridden) => overridden ?? true;

    private static ViewNetworkLabel? Declared(ViewFrame? frame, string portName) =>
        frame?.Labels.FirstOrDefault(
            label => string.Equals(label.Name, portName, StringComparison.Ordinal));
}
