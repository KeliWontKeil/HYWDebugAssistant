using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// **端口有效边的解析**（G5 · C5-7）——用户覆盖 &gt; 模块声明的默认边 &gt; 数据流惯例。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么单独一处</b>：有效边被三个消费者用到（呈现、建网判定、UI 吸附），
///     解析规则若各写一份，迟早"一处改了另一处没改"（`implementation/01` 的"来源唯一"）；</item>
///   <item>惯例 = **发送口在右、接收口在左**（老模块零改动、行为不变）。</item>
/// </list>
/// </remarks>
public static class PortEdgeResolution
{
    /// <summary>解析端口的**有效边**（永远是具体边，不会是 <c>Auto</c>）。</summary>
    /// <param name="declared">模块声明的默认边（<c>Auto</c> = 未声明）。</param>
    /// <param name="overridden">用户拖动后的覆盖边（<c>Auto</c> = 未覆盖）。</param>
    /// <param name="isSend">是否发送口（决定惯例回退）。</param>
    /// <returns>有效边。</returns>
    public static PortEdge Resolve(PortEdge declared, PortEdge overridden, bool isSend)
    {
        if (overridden != PortEdge.Auto)
        {
            return overridden;
        }

        if (declared != PortEdge.Auto)
        {
            return declared;
        }

        return isSend ? PortEdge.Right : PortEdge.Left;
    }
}
