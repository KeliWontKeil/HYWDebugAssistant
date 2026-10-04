using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// **端口有效边的解析**（G5 · C5-7）——用户拖动覆盖 &gt; 包内 `view.json` 声明的边。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么单独一处</b>：有效边被三个消费者用到（呈现、建网判定、UI 吸附），
///     解析规则若各写一份，迟早"一处改了另一处没改"（`implementation/01` 的"来源唯一"）；</item>
///   <item><b>没有惯例回退</b>：边的**唯一声明来源**是包内 `view.json` 的
///     <c>frame.ports[].edge</c> —— 每个数据面口都必须显式给出，缺条目或缺 edge 按**硬伤**报
///     （读完后由宿主对账，见 <c>Hosting.Instances.ModuleInstanceFactory</c>）；
///     框架**不再**替它猜"发送右 / 接收左"；</item>
///   <item><see cref="PortEdge.Auto"/> 只是**工作区里"用户从没拖过这条边"的存储哨兵**，
///     不是声明面的取值（声明面的枚举里没有它）。</item>
/// </list>
/// </remarks>
public static class PortEdgeResolution
{
    /// <summary>解析端口的**有效边**。</summary>
    /// <param name="declared">包内 `view.json` 声明的边（声明面保证是具体边）。</param>
    /// <param name="overridden">用户拖动后的覆盖边（<c>Auto</c> = 用户没拖过）。</param>
    /// <returns>
    /// 有效边。两边都没给时返回 <c>Auto</c> —— 只可能出现在"声明缺失 ⇒ 这份声明已被判硬伤、整份忽略"的
    /// 实例上（此时这个口**没有位置**，界面按"未声明"呈现，不再由习惯推断）。
    /// </returns>
    public static PortEdge Resolve(PortEdge declared, PortEdge overridden) =>
        overridden != PortEdge.Auto ? overridden : declared;
}
