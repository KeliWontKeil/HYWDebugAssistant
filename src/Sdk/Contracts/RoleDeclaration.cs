namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// **角色声明**：模块的**一个功能分块**（"我有哪几块处理功能"）【ADR-0052】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>是什么</b>：角色是模块**内部数据处理功能的抽象** —— 一个分块 = **一种数据类型** + 它的
///     **输入口**与**输出口**（通常各 1 个；允许更多，例如多源融合器 = 2 入 1 出）。
///     它与**网络标签/端口标记**（数据接口的抽象）**不是同一层级**：叠加层只表达角色，接口仍由端口标记表达；</item>
///   <item><b>为什么声明而不是派生</b>："这块功能把两路合成一路"是**设计信息**，框架**推不出来**
///     （推出来的只是"方向 × 类型"的机械组合，会把融合器画成"一收一发两块"）；</item>
///   <item><b>仍然只呈现</b>：角色与它所带的计数**不参与**建网、契约兼容、停流、投递与计量，**也不进组合件**
///     （ADR-0048 R9 的红线不放宽）；</item>
///   <item><b>声明即契约</b>（ADR-0052 裁定：**不做旧模块回退**）：本框架处于开发早期，
///     有数据口的模块**应当**声明角色；没声明 ⇒ 叠加层不画方块，并由自检/运行期 Issue 提示作者补齐；</item>
///   <item><b>名字是呈现文本</b>：<see cref="DisplayName"/> 与视图元素标题同级，框架**不理解**它
///     （主文档 §6.2：不引入业务分类；框架**不内置**任何角色类型枚举）。</item>
/// </list>
/// </remarks>
/// <param name="Id">角色标识（**模块内唯一**、稳定；用于配对与呈现键）。</param>
/// <param name="Type">该分块处理的**数据类型引用**（必须在**本模块**已声明；一个角色只有一种类型）。</param>
/// <param name="Inputs">**输入口名**（可为空 = 纯源分块；口必须是本模块的接收口）。</param>
/// <param name="Outputs">**输出口名**（可为空 = 纯汇分块；口必须是本模块的发送口）。</param>
/// <param name="DisplayName">功能名（给人看，如"融合"；空 ⇒ 呈现层回退为类型短名）。</param>
public sealed record RoleDeclaration(
    string Id,
    DataTypeRef Type,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    string? DisplayName = null)
{
    /// <summary>**纯汇**分块（只有输入口）——如显示器、记录器。</summary>
    /// <param name="id">角色标识。</param>
    /// <param name="type">数据类型引用。</param>
    /// <param name="inputPort">输入口名。</param>
    /// <param name="displayName">功能名。</param>
    public static RoleDeclaration Input(string id, DataTypeRef type, string inputPort, string? displayName = null) =>
        new(id, type, [inputPort], [], displayName);

    /// <summary>**纯源**分块（只有输出口）——如数据发生器。</summary>
    /// <param name="id">角色标识。</param>
    /// <param name="type">数据类型引用。</param>
    /// <param name="outputPort">输出口名。</param>
    /// <param name="displayName">功能名。</param>
    public static RoleDeclaration Output(string id, DataTypeRef type, string outputPort, string? displayName = null) =>
        new(id, type, [], [outputPort], displayName);

    /// <summary>**处理**分块（一进一出）——最常见形态。</summary>
    /// <param name="id">角色标识。</param>
    /// <param name="type">数据类型引用。</param>
    /// <param name="inputPort">输入口名。</param>
    /// <param name="outputPort">输出口名。</param>
    /// <param name="displayName">功能名。</param>
    public static RoleDeclaration Both(
        string id,
        DataTypeRef type,
        string inputPort,
        string outputPort,
        string? displayName = null) =>
        new(id, type, [inputPort], [outputPort], displayName);

    /// <summary>输入口数与输出口数之和（自检用于"是否建议拆分"）。</summary>
    public int PortCount => Inputs.Count + Outputs.Count;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id}（{Type}；in=[{string.Join(",", Inputs)}] out=[{string.Join(",", Outputs)}]）";
}
