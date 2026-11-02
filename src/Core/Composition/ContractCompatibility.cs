using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>兼容判定结果。</summary>
/// <param name="IsCompatible">是否兼容（可建立数据流）。</param>
/// <param name="Reason">不兼容原因（面向用户的描述）。</param>
public readonly record struct CompatibilityResult(bool IsCompatible, string? Reason)
{
    /// <summary>兼容。</summary>
    public static CompatibilityResult Compatible() => new(true, null);

    /// <summary>不兼容。</summary>
    public static CompatibilityResult Incompatible(string reason) => new(false, reason);
}

/// <summary>
/// 契约兼容判定——主文档 §5.4.1；**直连要求"形状完全一致"**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>规则（一句话）</b>：**传递语义一致 + 形状结构完全一致**（字段名 / 原语 / 数组与字节块长度 /
///     字段顺序 / 字段数量 / 因此尺寸必然相同），**且**在双方都声明类型时 **标识也相同**；</item>
///   <item><b>双方都声明类型</b>：
///     ① 同标识 + 形状一致 → 兼容；<br/>
///     ② **不同标识 → 不兼容**（框架无资格判定二者是否同一种量；**没有"强制连接"这条放行路径**）；</item>
///   <item><b>任一方未声明类型</b>（旧模块形态）：只按**形状完全一致**判定；</item>
///   <item><b>为什么把子集判定也取消</b>：子集判定允许"接收方字段更多"，于是**到达的数据报可能短于本口契约**，
///     接收方按自己的契约读就会抛异常（实测：会让该模块的投递循环整体退出）。要求形状一致后，
///     "同一宿口混入不同尺寸的源"**从源头不可能**，"兼容"与"能安全读取"重新合为一件事；</item>
///   <item><b>不自动转换</b>：不兼容即不可连 + 明示差异，转换由用户显式插入转换模块（PQ-IF-05 不变）；</item>
///   <item>"合法无意义组合"由方向决定（两个发送口相接 = 连接存在但无数据流），**不在这里判**。</item>
/// </list>
/// <para>
/// <b>只有一个重载</b>：不再提供"只比形状"的入口——那会让调用方在不知情时用上更弱的规则
/// （历史上 `DockRule` 就走了那条，表现为"贴合能连、打标签不能连"）。调用方拿不到类型标识时传
/// <see langword="null"/>，规则自动回退为形状比较，但**入口仍然只有一个**。
/// </para>
/// </remarks>
public static class ContractCompatibility
{
    /// <summary>判定发送口契约能否供接收口使用（**唯一入口**：类型标识 + 形状；任一方标识为 null 时只比形状）。</summary>
    /// <param name="sender">发送方契约形状。</param>
    /// <param name="senderSemantics">发送方传递语义。</param>
    /// <param name="senderTypeId">发送方**数据类型标识**（未声明 = <see langword="null"/>）。</param>
    /// <param name="receiver">接收方契约形状。</param>
    /// <param name="receiverSemantics">接收方传递语义。</param>
    /// <param name="receiverTypeId">接收方数据类型标识（未声明 = <see langword="null"/>）。</param>
    public static CompatibilityResult Check(
        ContractShape sender,
        TransferSemantics senderSemantics,
        string? senderTypeId,
        ContractShape receiver,
        TransferSemantics receiverSemantics,
        string? receiverTypeId)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(receiver);

        if (senderSemantics != receiverSemantics)
        {
            return CompatibilityResult.Incompatible(CoreText.Get(
                "core.compat.semanticsMismatch",
                Describe(senderSemantics),
                Describe(receiverSemantics)));
        }

        // **形状必须完全一致**——直连的唯一结构条件（尺寸一致是它的推论）
        if (!ContractShape.AreSame(sender, receiver))
        {
            return CompatibilityResult.Incompatible(CoreText.Get(
                "core.compat.shapeMismatch",
                Describe(sender),
                sender.SizeInBytes,
                Describe(receiver),
                receiver.SizeInBytes));
        }

        // 任一方没声明类型 ⇒ 旧模块形态：形状已一致，直接兼容
        if (senderTypeId is null || receiverTypeId is null)
        {
            return CompatibilityResult.Compatible();
        }

        if (!string.Equals(senderTypeId, receiverTypeId, StringComparison.Ordinal))
        {
            // **不同标识 = 不是同一种量**：没有"强制连接"这条放行路径（它曾是"应用层看着能连、
            // 底层按逐对过滤"的来源）。要连就把两侧声明成同一类型，或插入转换模块。
            return CompatibilityResult.Incompatible(CoreText.Get(
                "core.compat.typeIdMismatch", senderTypeId, receiverTypeId));
        }

        return CompatibilityResult.Compatible();
    }

    private static string Describe(ContractShape shape) => shape switch
    {
        PrimitiveShape p => p.Primitive.ToString(),
        BytesShape b => $"bytes[{b.Length}]",
        ArrayShape a => $"{a.Element}[{a.Length}]",
        StructShape s => "{" + string.Join(", ", s.Fields.Select(f => $"{f.Name}: {Describe(f.Shape)}")) + "}",
        _ => shape.GetType().Name,
    };

    private static string Describe(TransferSemantics semantics) => semantics switch
    {
        TransferSemantics.Stream => CoreText.Get("core.compat.semantics.stream"),
        TransferSemantics.Message => CoreText.Get("core.compat.semantics.message"),
        TransferSemantics.Event => CoreText.Get("core.compat.semantics.event"),
        _ => semantics.ToString(),
    };
}
