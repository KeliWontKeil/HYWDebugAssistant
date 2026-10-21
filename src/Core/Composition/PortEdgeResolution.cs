using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// **端口有效边的解析**（G5 · C5-7）——用户拖动覆盖 &gt; 模块代码声明的边。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么单独一处</b>：有效边被三个消费者用到（呈现、建网判定、UI 吸附），
///     解析规则若各写一份，迟早"一处改了另一处没改"（`implementation/01` 的"来源唯一"）；</item>
///   <item><b>声明来源</b>：<see cref="PortDeclaration.Edge"/>；<c>Auto</c> = 沿用数据流惯例
///     （发送在右、接收在左），由 <see cref="PortGeometryResolution"/> 在解析时落成具体边；</item>
///   <item><see cref="PortEdge.Auto"/> 同时是**工作区里"用户从没拖过这条边"的存储哨兵**。</item>
/// </list>
/// </remarks>
public static class PortEdgeResolution
{
    /// <summary>解析端口的**有效边**。</summary>
    /// <param name="declared">声明给出的边（调用方已把 <c>Auto</c> 按惯例落成具体边）。</param>
    /// <param name="overridden">用户拖动后的覆盖边（<c>Auto</c> = 用户没拖过）。</param>
    /// <returns>有效边。</returns>
    public static PortEdge Resolve(PortEdge declared, PortEdge overridden) =>
        overridden != PortEdge.Auto ? overridden : declared;
}
