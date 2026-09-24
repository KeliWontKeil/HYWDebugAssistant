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
/// 契约兼容判定：**接收契约 ⊇ 发送契约**（形状子集，可计算）——ADR-0008 / 主文档 §5.4.1。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Core 只算**可计算关系**：传递语义可匹配 + 形状子集；跨语义不判兼容（模块内部组帧）；</item>
///   <item><b>不自动转换</b>：不兼容即不可连 + UI 明示差异，转换由用户显式插入转换模块（§5.4.1 PQ-IF-05）；</item>
///   <item>"合法无意义组合"由方向决定（两个发送口相接 = 连接存在但无数据流），**不在这里判**。</item>
/// </list>
/// </remarks>
public static class ContractCompatibility
{
    /// <summary>判定发送口契约能否供接收口使用。</summary>
    /// <param name="sender">发送方契约形状。</param>
    /// <param name="senderSemantics">发送方传递语义。</param>
    /// <param name="receiver">接收方契约形状。</param>
    /// <param name="receiverSemantics">接收方传递语义。</param>
    public static CompatibilityResult Check(
        ContractShape sender,
        TransferSemantics senderSemantics,
        ContractShape receiver,
        TransferSemantics receiverSemantics)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(receiver);

        if (senderSemantics != receiverSemantics)
        {
            return CompatibilityResult.Incompatible(
                $"传递语义不一致（{Describe(senderSemantics)} → {Describe(receiverSemantics)}）；" +
                "跨语义不判兼容，需要时由模块内部组帧或插入转换模块");
        }

        return IsSubset(sender, receiver, out var reason)
            ? CompatibilityResult.Compatible()
            : CompatibilityResult.Incompatible(reason!);
    }

    /// <summary>形状子集判定：接收契约必须覆盖发送契约的**全部字段**（名称 + 类型 + 顺序兼容）。</summary>
    private static bool IsSubset(ContractShape sender, ContractShape receiver, out string? reason)
    {
        reason = null;

        // 单原语 / 定长字节块 / 定长数组：**类型（含原语种类）与长度都必须一致**——
        // 同为 8 字节的 u64 与 f64 位模式完全不同，算兼容会让接收方按错误类型解读（§5.4.1 "不自动转换"）
        if (sender is not StructShape || receiver is not StructShape)
        {
            var sameKind = (sender, receiver) switch
            {
                (PrimitiveShape s, PrimitiveShape r) => s.Primitive == r.Primitive,
                (BytesShape s, BytesShape r) => s.Length == r.Length,
                (ArrayShape s, ArrayShape r) => s.Element == r.Element && s.Length == r.Length,
                _ => false,
            };

            if (!sameKind)
            {
                reason = $"形状不一致（{Describe(sender)} vs {Describe(receiver)}）";
                return false;
            }

            return true;
        }

        // 结构：接收方的字段须能覆盖发送方的每个字段（按名 + 形状）
        var senderStruct = (StructShape)sender;
        var receiverStruct = (StructShape)receiver;

        foreach (var (name, senderField) in senderStruct.Fields)
        {
            var receiverField = receiverStruct.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
            if (receiverField.Shape is null)
            {
                reason = $"接收契约缺少字段 '{name}'";
                return false;
            }

            if (!IsSubset(senderField, receiverField.Shape, out var fieldReason))
            {
                reason = $"字段 '{name}' 不兼容：{fieldReason}";
                return false;
            }
        }

        return true;
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
