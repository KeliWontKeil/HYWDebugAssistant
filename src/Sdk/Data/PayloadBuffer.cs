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
/// <remarks>
/// <list type="bullet">
///   <item>ref struct：零分配、不跨 await/线程存活；</item>
///   <item><b>构造会校验长度</b>：数据报短于布局即抛 <see cref="ArgumentException"/>。
///     这在**多源 / 版本升级**场景下是真实风险 —— 见 <see cref="TryCreate"/> 的说明；</item>
///   <item><b>接收方的读取纪律</b>【ADR-0046 D6】：兼容只保证"你声明的字段集**覆盖**发送方"，
///     **不保证你能按自己的契约整块读**。版本升级（尾部追加字段）时到达的数据报长度 = **发送方**的契约尺寸，
///     尾部字段**根本不在**；把新版本接收口接到旧版本发送口上，必须用 <see cref="TryCreate"/> 或先判长度。</item>
/// </list>
/// </remarks>
public readonly ref struct PayloadReader
{
    private readonly ReadOnlySpan<byte> _payload;
    private readonly ContractLayout _layout;

    /// <summary>创建读取器（数据报短于布局 ⇒ **抛异常**；长度不确定时请用 <see cref="TryCreate"/>）。</summary>
    /// <param name="payload">数据报字节。</param>
    /// <param name="layout">契约布局。</param>
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

    /// <summary>
    /// **安全创建**：长度不足时返回 <see langword="false"/> 而不抛异常。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>什么时候需要它</b>：接收口的版本**高于**发送方（尾部追加升级，ADR-0046 D6 判为兼容），
    ///     或多源网络里**不同版本**的发布者投到同一个口 —— 这两种情况下到达的数据报长度可能小于本口的契约尺寸，
    ///     硬构造会抛异常；**实测后果**：异常会让该模块的**投递循环整体退出**，表现为"模块莫名不再收数"
    ///     （只留一条 Issue，且文案写的是"活动体异常"，容易误导）；</item>
    ///   <item><b>拿到 false 该怎么办</b>：**丢弃该数据报并计数/上报**（不静默、也不崩）——
    ///     它表示"发布方版本比本口旧，尾部字段不存在"；</item>
    ///   <item>若只需要**公共前缀字段**（通常如此），正确做法是判长度后用自己契约里"一定存在"的字段读取：
    ///     <c>if (PayloadReader.TryCreate(payload, Layout, out var reader)) { reader.ReadU64("seq"); ... }</c>。</item>
    /// </list>
    /// </remarks>
    /// <param name="payload">数据报字节。</param>
    /// <param name="layout">契约布局。</param>
    /// <param name="reader">创建成功时的读取器。</param>
    /// <returns>长度是否足够。</returns>
    public static bool TryCreate(ReadOnlySpan<byte> payload, ContractLayout layout, out PayloadReader reader)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (payload.Length < layout.SizeInBytes)
        {
            reader = default;
            return false;
        }

        reader = new PayloadReader(payload, layout);
        return true;
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
