using System.Buffers.Binary;

namespace HYWDebugAssistant.Modules.Udp;

/// <summary>
/// 本模块对 <c>RawBytes</c> 约定的组帧实现：<c>{ len: u16, data: bytes[1472] }</c>。
/// </summary>
/// <remarks>
/// <para>
/// 组帧属于**模块间通信**：数据单元的布局、有效长度与补零都由这个模块自己定义与解读，
/// 外部接入面只交原始字节块。四个外部接入模块各带一份这样的实现 —— 约定相同是**约定**，
/// 不是共享代码。
/// </para>
/// <para>
/// <b>不变式</b>：投递载荷永远写满 <see cref="FrameSize"/> 字节（先清零再写有效内容）；
/// <c>len</c> 是有效字节数，<c>0x00</c> 是合法数据、不是终止符。
/// </para>
/// </remarks>
internal static class RawBytesCodec
{
    /// <summary>单单元字节区容量（有效内容上限）。</summary>
    public const int Capacity = 1472;

    /// <summary>定长载荷尺寸（<c>2 + Capacity</c>）。</summary>
    public const int FrameSize = sizeof(ushort) + Capacity;

    /// <summary>把一段有效内容写成定长载荷。</summary>
    /// <param name="content">有效内容（长度 ≤ <see cref="Capacity"/>）。</param>
    /// <param name="frame">目标缓冲（长度须 ≥ <see cref="FrameSize"/>）。</param>
    /// <returns>是否写成（内容超长或缓冲不足 ⇒ <see langword="false"/>，不抛）。</returns>
    public static bool TryWriteFrame(ReadOnlySpan<byte> content, Span<byte> frame)
    {
        if (content.Length > Capacity || frame.Length < FrameSize)
        {
            return false;
        }

        var target = frame[..FrameSize];
        target.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)content.Length);
        content.CopyTo(target[sizeof(ushort)..]);
        return true;
    }

    /// <summary>从定长载荷里按 <c>len</c> 取有效内容（禁止 NUL 截断）。</summary>
    /// <param name="frame">到达的载荷。</param>
    /// <param name="content">有效内容。</param>
    /// <returns>载荷是否可读（短于 <see cref="FrameSize"/> 或 <c>len</c> 越界 ⇒ <see langword="false"/>）。</returns>
    public static bool TryReadContent(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> content)
    {
        content = default;

        if (frame.Length < FrameSize)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        if (length > Capacity)
        {
            return false;
        }

        content = frame.Slice(sizeof(ushort), length);
        return true;
    }
}
