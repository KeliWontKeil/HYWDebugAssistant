using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Modules.RawToWaveform.Codec;

/// <summary>
/// 波形帧契约。字段名、顺序、原语与内置「波形发生器 / 波形显示器」逐字段一致：
/// <c>{ channelCount:U16, payloadBytes:U16, groupId:U32, sequence:U32, rateHz:F32, validMask:U64, samples:F32[64] }</c>。
/// </summary>
/// <remarks>
/// 跨模块同名即同一类型；形状必须完全一致，否则建网时拒绝连接。
/// 修改本文件前先确认内置模块的同名契约没有变化。
/// </remarks>
internal static class WaveformContract
{
    /// <summary>类型标识。</summary>
    public const string TypeId = "WaveformFrame";

    /// <summary>字段：通道数。</summary>
    public const string ChannelCountField = "channelCount";

    /// <summary>字段：有效载荷字节数（= 通道数 × 4）。</summary>
    public const string PayloadBytesField = "payloadBytes";

    /// <summary>字段：通道组编号。</summary>
    public const string GroupIdField = "groupId";

    /// <summary>字段：帧序号。</summary>
    public const string SequenceField = "sequence";

    /// <summary>字段：声明速率（Hz）。</summary>
    public const string RateHzField = "rateHz";

    /// <summary>字段：通道有效位掩码。</summary>
    public const string ValidMaskField = "validMask";

    /// <summary>字段：样本数组（定长 64）。</summary>
    public const string SamplesField = "samples";

    /// <summary>单帧最大通道数。</summary>
    public const int MaxChannels = 64;

    /// <summary>契约形状。</summary>
    public static readonly ContractShape Shape = ContractShape.OfStruct(
        (ChannelCountField, ContractShape.OfPrimitive(ContractPrimitive.U16)),
        (PayloadBytesField, ContractShape.OfPrimitive(ContractPrimitive.U16)),
        (GroupIdField, ContractShape.OfPrimitive(ContractPrimitive.U32)),
        (SequenceField, ContractShape.OfPrimitive(ContractPrimitive.U32)),
        (RateHzField, ContractShape.OfPrimitive(ContractPrimitive.F32)),
        (ValidMaskField, ContractShape.OfPrimitive(ContractPrimitive.U64)),
        (SamplesField, ContractShape.OfArray(ContractPrimitive.F32, MaxChannels)));

    /// <summary>契约布局。</summary>
    public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

    /// <summary>单帧字节数。</summary>
    public static readonly int SizeInBytes = Shape.SizeInBytes;

    /// <summary>样本数组相对偏移。</summary>
    public static readonly int SamplesOffset = Layout.GetField(SamplesField).Offset;

    /// <summary>
    /// 传递语义：内置波形显示器与波形发生器都声明 <c>Stream</c>。
    /// 语义参与兼容判定，跨语义不判兼容——写成 <c>Message</c> 会与内置模块连不上。
    /// </summary>
    public static readonly TransferSemantics Semantics = TransferSemantics.Stream;

    /// <summary>计量块：有效长度字段是 payloadBytes。</summary>
    public static MeteringBlock Metering => new() { PayloadLengthField = PayloadBytesField };
}

/// <summary>
/// 原始字节契约：<c>{ len:U16, data:bytes[1472] }</c>，语义 <c>Stream</c>。
/// </summary>
internal static class RawBytesContract
{
    /// <summary>类型标识。</summary>
    public const string TypeId = "RawBytes";

    /// <summary>字段：有效字节数。</summary>
    public const string LengthField = "len";

    /// <summary>字段：数据块。</summary>
    public const string DataField = "data";

    /// <summary>单单元最大有效载荷。</summary>
    public const int MaxPayload = 1472;

    /// <summary>契约形状。</summary>
    public static readonly ContractShape Shape = ContractShape.OfStruct(
        (LengthField, ContractShape.OfPrimitive(ContractPrimitive.U16)),
        (DataField, ContractShape.OfBytes(MaxPayload)));

    /// <summary>契约布局。</summary>
    public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

    /// <summary>单单元字节数。</summary>
    public static readonly int SizeInBytes = Shape.SizeInBytes;

    /// <summary>数据块相对偏移。</summary>
    public static readonly int DataOffset = Layout.GetField(DataField).Offset;

    /// <summary>计量块：有效长度字段是 len。</summary>
    public static MeteringBlock Metering => new() { PayloadLengthField = LengthField };
}
