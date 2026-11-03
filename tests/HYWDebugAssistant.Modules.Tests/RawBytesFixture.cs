using System.Buffers.Binary;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 测试侧对 <c>RawBytes</c> 约定的组帧工具（与各模块自带的实现同形）。
/// </summary>
/// <remarks>
/// 这里刻意留一份独立实现：适配件用例验证的是"原始字节怎么进出"，
/// 不借用任何模块的组帧代码，免得用例的判据跟着某个模块的实现走。
/// </remarks>
internal static class RawBytesFixture
{
    /// <summary>单单元字节区容量（与四个外部接入模块一致）。</summary>
    public const int Capacity = 1472;

    /// <summary>定长载荷尺寸（<c>2 + capacity</c>）。</summary>
    /// <param name="capacity">字节区容量。</param>
    public static int FrameSize(int capacity) => sizeof(ushort) + capacity;

    /// <summary>把一段有效内容写成定长载荷（内容超长或缓冲不足 ⇒ 抛）。</summary>
    /// <param name="content">有效内容。</param>
    /// <param name="frame">目标缓冲。</param>
    /// <param name="capacity">字节区容量。</param>
    public static void WriteFrame(ReadOnlySpan<byte> content, Span<byte> frame, int capacity)
    {
        if (content.Length > capacity)
        {
            throw new ArgumentException($"内容长度 {content.Length} 超过字节区容量 {capacity}", nameof(content));
        }

        var size = FrameSize(capacity);
        if (frame.Length < size)
        {
            throw new ArgumentException($"目标缓冲过小：需要 {size} 字节，实际 {frame.Length} 字节", nameof(frame));
        }

        var target = frame[..size];
        target.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)content.Length);
        content.CopyTo(target[sizeof(ushort)..]);
    }

    /// <summary>从定长载荷里按 <c>len</c> 取有效内容。</summary>
    /// <param name="payload">到达的载荷。</param>
    /// <param name="capacity">字节区容量。</param>
    /// <param name="content">有效内容。</param>
    /// <returns>载荷是否可读。</returns>
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
