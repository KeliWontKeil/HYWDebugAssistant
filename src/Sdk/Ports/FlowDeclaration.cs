namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// 模块**内部处理顺序**的声明（`角色 → 角色`）【ADR-0052 · 修订 ADR-0049 D13】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它是什么</b>：表达模块内部**分块之间的先后**（如"融合 → 限幅 → 输出"）。
///     **角色内部的 in→out 不需要声明**（角色本身就含输入/输出）；</item>
///   <item><b>与网络无关</b>：与任何网络/实例/拓扑无关 —— 同一模块的多个实例画法相同；</item>
///   <item><b>只呈现</b>：仅用于叠加层展示（细实线、中性色、不流动）。**不参与**建网、契约兼容判定、停流、
///     投递与计量，**也不进组合件**；</item>
///   <item><b>零声明就是不画</b>：单分块模块（绝大多数参考模块）**无需声明**，框架**不猜**内部顺序。</item>
/// </list>
/// </remarks>
/// <param name="FromRoleId">起点**角色标识**（必须是本模块已声明的角色）。</param>
/// <param name="ToRoleId">终点**角色标识**（同上）。</param>
/// <param name="Label">可选注记（v1 不绘制）。</param>
public sealed record FlowDeclaration(string FromRoleId, string ToRoleId, string? Label = null)
{
    /// <inheritdoc />
    public override string ToString() => $"{FromRoleId}→{ToRoleId}";
}

