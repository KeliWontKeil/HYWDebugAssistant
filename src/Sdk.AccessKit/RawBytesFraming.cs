using System.Buffers.Binary;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// **应用标准 `RawBytes` 的组帧 / 拆帧**：`{ len: u16, data: bytes[capacity] }`。
/// </summary>
/// <remarks>
/// <para>
/// 外部接入只交**原始字节块**；把字节变成应用标准数据单元（以及反过来）是模块侧的事。
/// 四个外部模块共用这一份口径：`len` = **有效字节数**，`data` 尾部补零只是定长填充，
/// `0x00` 是合法数据、不是终止符。
/// </para>
/// <para>
/// 口投递要求**定长**载荷，因此发送时永远写满 <c>2 + capacity</c> 字节（先清零再写有效内容）。
/// </para>
/// </remarks>
public static class RawBytesFraming
{
    /// <summary>数据类型标识（与既有四个基础模块逐字相同）。</summary>
    public const string TypeId = "RawBytes";


    /// <summary>默认单单元字节区容量（与既有 <c>RawBytes</c> 契约一致）。</summary>
    public const int Capacity = 1472;

    /// <summary>构造 <c>RawBytes</c> 的契约形状。</summary>
    /// <param name="capacity">字节区容量。</param>
    public static ContractShape Shape(int capacity) => ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(capacity)));

    /// <summary>定长载荷尺寸（<c>2 + capacity</c>）。</summary>
    /// <param name="capacity">字节区容量。</param>
    public static int FrameSize(int capacity) => sizeof(ushort) + capacity;

    /// <summary>把一段有效内容写成定长载荷。</summary>
    /// <param name="content">有效内容（长度 ≤ capacity）。</param>
    /// <param name="frame">目标缓冲（长度 ≥ <see cref="FrameSize"/>）。</param>
    /// <param name="capacity">字节区容量。</param>
    public static void WriteFrame(ReadOnlySpan<byte> content, Span<byte> frame, int capacity)
    {
        if (content.Length > capacity)
        {
            throw new ArgumentException(
                $"内容长度 {content.Length} 超过 RawBytes 字节区容量 {capacity}",
                nameof(content));
        }

        var size = FrameSize(capacity);
        if (frame.Length < size)
        {
            throw new ArgumentException(
                $"目标缓冲过小：需要 {size} 字节，实际 {frame.Length} 字节",
                nameof(frame));
        }

        var target = frame[..size];
        target.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)content.Length);
        content.CopyTo(target[sizeof(ushort)..]);
    }

    /// <summary>从定长载荷里按 <c>len</c> 取有效内容（禁止 NUL 截断）。</summary>
    /// <param name="payload">到达的载荷。</param>
    /// <param name="capacity">字节区容量。</param>
    /// <param name="content">有效内容。</param>
    /// <returns>载荷是否可读（短于 2 字节或 <c>len</c> 越界 ⇒ <see langword="false"/>）。</returns>
    public static bool TryReadContent(ReadOnlySpan<byte> payload, int capacity, out ReadOnlySpan<byte> content)
    {
        content = default;

        if (payload.Length < sizeof(ushort))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        var available = Math.Min(capacity, payload.Length - sizeof(ushort));

        if (length > available)
        {
            return false;
        }

        content = payload.Slice(sizeof(ushort), length);
        return true;
    }
}
