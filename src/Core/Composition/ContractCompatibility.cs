using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>兼容判定结果。</summary>
/// <param name="IsCompatible">是否兼容（可建立数据流）。</param>
/// <param name="Reason">不兼容原因（面向用户的描述）。</param>
public readonly record struct CompatibilityResult(bool IsCompatible, string? Reason)
{
    /// <summary>
    /// **附加说明**（兼容但有话要说，如"用户显式确认强连"）——调用方应把它呈现给用户。
    /// </summary>
    public string? Note { get; init; }

    /// <summary>兼容。</summary>
    public static CompatibilityResult Compatible() => new(true, null);

    /// <summary>兼容（但带一条必须让用户看见的说明）。</summary>
    /// <param name="note">说明文本。</param>
    public static CompatibilityResult CompatibleWithNote(string note) => new(true, null) { Note = note };

    /// <summary>不兼容。</summary>
    public static CompatibilityResult Incompatible(string reason) => new(false, reason);
}

/// <summary>
/// 契约兼容判定——主文档 §5.4.1；**直连要求"形状完全一致"**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>规则（一句话）</b>：**传递语义一致 + 形状结构完全一致**（字段名 / 原语 / 数组与字节块长度 /
///     字段顺序 / 字段数量 / 因此尺寸必然相同），**且**在双方都声明类型时 **标识与版本也相同**；</item>
///   <item><b>双方都声明类型</b>：
///     ① 同标识 + 同版本 + 形状一致 → 兼容；<br/>
///     ② **同标识 + 不同版本 → 不兼容**（版本升级属**破坏性**变更，不再有"尾部追加 = 兼容"）；<br/>
///     ③ **不同标识 → 框架无资格判定二者是否同一种量** ⇒ 默认不兼容；用户**显式确认**后可强连
///     （**此时仍要求形状完全一致**——否则字节根本不是一回事）；</item>
///   <item><b>任一方未声明类型</b>（旧模块形态）：同样只按**形状完全一致**判定（不再有"接收 ⊇ 发送"的子集判定）；</item>
///   <item><b>为什么把子集判定也取消</b>：子集判定允许"接收方字段更多"，于是**到达的数据报可能短于本口契约**，
///     接收方按自己的契约读就会抛异常（实测：会让该模块的投递循环整体退出）。要求形状一致后，
///     "同一宿口混入不同尺寸的源"**从源头不可能**，"兼容"与"能安全读取"重新合为一件事；</item>
///   <item><b>不自动转换</b>：不兼容即不可连 + 明示差异，转换由用户显式插入转换模块（PQ-IF-05 不变）；</item>
///   <item>"合法无意义组合"由方向决定（两个发送口相接 = 连接存在但无数据流），**不在这里判**。</item>
/// </list>
/// </remarks>
public static class ContractCompatibility
{
    /// <summary>判定发送口契约能否供接收口使用（**未声明类型的旧模块路径**：只比形状）。</summary>
    /// <param name="sender">发送方契约形状。</param>
    /// <param name="senderSemantics">发送方传递语义。</param>
    /// <param name="receiver">接收方契约形状。</param>
    /// <param name="receiverSemantics">接收方传递语义。</param>
    public static CompatibilityResult Check(
        ContractShape sender,
        TransferSemantics senderSemantics,
        ContractShape receiver,
        TransferSemantics receiverSemantics) =>
        Check(sender, senderSemantics, senderType: null, receiver, receiverSemantics, receiverType: null);

    /// <summary>判定发送口契约能否供接收口使用（**类型身份 + 形状**；任一方类型为 null 时只比形状）。</summary>
    /// <param name="sender">发送方契约形状。</param>
    /// <param name="senderSemantics">发送方传递语义。</param>
    /// <param name="senderType">发送方**数据类型引用**（未声明 = <see langword="null"/>）。</param>
    /// <param name="receiver">接收方契约形状。</param>
    /// <param name="receiverSemantics">接收方传递语义。</param>
    /// <param name="receiverType">接收方数据类型引用（未声明 = <see langword="null"/>）。</param>
    /// <param name="allowUnverified">
    /// 用户是否已**显式确认**允许"不同标识"的强连（默认 <see langword="false"/> = 不兼容）。
    /// </param>
    public static CompatibilityResult Check(
        ContractShape sender,
        TransferSemantics senderSemantics,
        DataTypeRef? senderType,
        ContractShape receiver,
        TransferSemantics receiverSemantics,
        DataTypeRef? receiverType,
        bool allowUnverified = false)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(receiver);

        if (senderSemantics != receiverSemantics)
        {
            return CompatibilityResult.Incompatible(
                $"传递语义不一致（{Describe(senderSemantics)} → {Describe(receiverSemantics)}）；" +
                "跨语义不判兼容，需要时由模块内部组帧或插入转换模块");
        }

        // **形状必须完全一致**——直连的唯一结构条件（尺寸一致是它的推论）
        var sameShape = ContractShape.AreSame(sender, receiver);

        if (!sameShape)
        {
            return CompatibilityResult.Incompatible(
                $"形状不一致（发送方 {Describe(sender)} = {sender.SizeInBytes}B → 接收方 {Describe(receiver)} = {receiver.SizeInBytes}B）："
                + "**直连要求形状完全一致**（字段名 / 类型 / 顺序 / 数量全同）——需要不同形状时请插入转换模块");
        }

        // 任一方没声明类型 ⇒ 旧模块形态：形状已一致，直接兼容
        if (senderType is not { } from || receiverType is not { } to)
        {
            return CompatibilityResult.Compatible();
        }

        if (!string.Equals(from.TypeId, to.TypeId, StringComparison.Ordinal))
        {
            return allowUnverified
                ? CompatibilityResult.CompatibleWithNote(
                    $"**用户已显式确认强连**：数据类型标识不同（{from} → {to}），框架不保证二者是同一种量")
                : CompatibilityResult.Incompatible(
                    $"数据类型标识不同（{from} → {to}）：**框架无资格判定二者是否同一种量**（形状相同也不自动认定）" +
                    "——默认不兼容；确需连接请**显式确认强连**");
        }

        if (from.Version != to.Version)
        {
            // 版本升级属**破坏性**变更：不再有"尾部追加 = 兼容"
            return CompatibilityResult.Incompatible(
                $"数据类型版本不同（{from} → {to}）：**不同版本不视为同一种数据报**（版本升级按破坏性变更处理）" +
                "——请把两侧升到同一版本，或插入转换模块");
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
        TransferSemantics.Stream => "流",
        TransferSemantics.Message => "消息",
        TransferSemantics.Event => "事件",
        _ => semantics.ToString(),
    };
}
