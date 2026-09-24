using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Data;

/// <summary>
/// 数据单元缓冲（**只读视图**）：框架侧负责池化与归属，模块侧只读取其内容。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>生命周期仅限本次回调/接收：需要留存时必须自行拷贝；</item>
///   <item>回调返回后缓冲可能立即被归还池并复用（**不要持有**）；</item>
///   <item>主动接收（<c>TryReceive</c>）拿到的缓冲须显式归还（<c>ReleaseBuffer</c>）。</item>
/// </list>
/// </remarks>
public abstract class PayloadBuffer
{
    /// <summary>数据单元字节数（= 契约布局的定长尺寸）。</summary>
    public abstract int Length { get; }

    /// <summary>契约布局（该数据单元的形状与字段偏移）。</summary>
    public abstract ContractLayout Layout { get; }

    /// <summary>只读字节视图。</summary>
    public abstract ReadOnlySpan<byte> Span { get; }
}

/// <summary>
/// 数据单元**写入器**（按契约布局寻址写字段）。
/// </summary>
/// <remarks>ref struct：只持有 <see cref="Span{T}"/>，零分配、不可跨 await/线程存活。</remarks>
public readonly ref struct PayloadWriter
{
    private readonly Span<byte> _payload;
    private readonly ContractLayout _layout;

    /// <summary>创建写入器。</summary>
    /// <param name="payload">目标缓冲（长度须 ≥ 布局尺寸）。</param>
    /// <param name="layout">契约布局。</param>
    public PayloadWriter(Span<byte> payload, ContractLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (payload.Length < layout.SizeInBytes)
        {
            throw new ArgumentException(
                $"目标缓冲过小：需要 {layout.SizeInBytes} 字节，实际 {payload.Length} 字节", nameof(payload));
        }

        _payload = payload[..layout.SizeInBytes];
        _layout = layout;
    }

    /// <summary>清空为默认值（可选；便于调试时区分"未写入"）。</summary>
    public void Clear() => _payload.Clear();

    /// <summary>写 8 位无符号整数。</summary>
    public void WriteU8(string field, byte value) => _payload[_layout.GetField(field).Offset] = value;

    /// <summary>写 8 位有符号整数。</summary>
    public void WriteI8(string field, sbyte value) => _payload[_layout.GetField(field).Offset] = unchecked((byte)value);

    /// <summary>写 16 位无符号整数（小端）。</summary>
    public void WriteU16(string field, ushort value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(_payload[_layout.GetField(field).Offset..], value);

    /// <summary>写 32 位无符号整数（小端）。</summary>
    public void WriteU32(string field, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_payload[_layout.GetField(field).Offset..], value);

    /// <summary>写 64 位无符号整数（小端）。</summary>
    public void WriteU64(string field, ulong value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(_payload[_layout.GetField(field).Offset..], value);

    /// <summary>写 32 位浮点数（小端）。</summary>
    public void WriteF32(string field, float value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(_payload[_layout.GetField(field).Offset..], value);

    /// <summary>写 64 位浮点数（小端）。</summary>
    public void WriteF64(string field, double value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(_payload[_layout.GetField(field).Offset..], value);

    /// <summary>写布尔（1 字节）。</summary>
    public void WriteBool(string field, bool value) => _payload[_layout.GetField(field).Offset] = value ? (byte)1 : (byte)0;

    /// <summary>写定长字节块。</summary>
    public void WriteBytes(string field, ReadOnlySpan<byte> value)
    {
        var layout = _layout.GetField(field);
        var target = _payload.Slice(layout.Offset, layout.Shape.SizeInBytes);
        value[..Math.Min(value.Length, target.Length)].CopyTo(target);
    }
}

/// <summary>
/// 数据单元**读取器**（按契约布局寻址读字段）。
/// </summary>
/// <remarks>ref struct：零分配、不跨 await/线程存活。</remarks>
public readonly ref struct PayloadReader
{
    private readonly ReadOnlySpan<byte> _payload;
    private readonly ContractLayout _layout;

    /// <summary>创建读取器。</summary>
    public PayloadReader(ReadOnlySpan<byte> payload, ContractLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (payload.Length < layout.SizeInBytes)
        {
            throw new ArgumentException(
                $"数据单元过小：布局需要 {layout.SizeInBytes} 字节，实际 {payload.Length} 字节", nameof(payload));
        }

        _payload = payload[..layout.SizeInBytes];
        _layout = layout;
    }

    /// <summary>读 8 位无符号整数。</summary>
    public byte ReadU8(string field) => _payload[_layout.GetField(field).Offset];

    /// <summary>读 8 位有符号整数。</summary>
    public sbyte ReadI8(string field) => unchecked((sbyte)_payload[_layout.GetField(field).Offset]);

    /// <summary>读 16 位无符号整数（小端）。</summary>
    public ushort ReadU16(string field) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_payload[_layout.GetField(field).Offset..]);

    /// <summary>读 32 位无符号整数（小端）。</summary>
    public uint ReadU32(string field) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_payload[_layout.GetField(field).Offset..]);

    /// <summary>读 64 位无符号整数（小端）。</summary>
    public ulong ReadU64(string field) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(_payload[_layout.GetField(field).Offset..]);

    /// <summary>读 32 位浮点数（小端）。</summary>
    public float ReadF32(string field) =>
        System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(_payload[_layout.GetField(field).Offset..]);

    /// <summary>读 64 位浮点数（小端）。</summary>
    public double ReadF64(string field) =>
        System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(_payload[_layout.GetField(field).Offset..]);

    /// <summary>读布尔。</summary>
    public bool ReadBool(string field) => _payload[_layout.GetField(field).Offset] != 0;

    /// <summary>读取定长字节块。</summary>
    public ReadOnlySpan<byte> ReadBytes(string field)
    {
        var layout = _layout.GetField(field);
        return _payload.Slice(layout.Offset, layout.Shape.SizeInBytes);
    }
}
