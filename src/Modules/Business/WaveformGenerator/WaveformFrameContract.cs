using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>波形帧的模块间约定（本模块自己的声明配方；不引用其它模块/Sdk 共享类型）。</summary>
internal static class WaveformFrameContract
{
    /// <summary>类型标识。</summary>
    public const string TypeId = "hyw/waveform-frame";

    /// <summary>单帧最大通道数。</summary>
    public const int MaxChannels = 64;

    /// <summary>单帧字节数。</summary>
    public const int SizeInBytes = 280;

    /// <summary>头部字节数（样本区偏移）。</summary>
    public const int HeaderSize = 24;

    /// <summary>契约形状。</summary>
    public static ContractShape Shape { get; } = ContractShape.OfStruct(
        ("channelCount", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("payloadBytes", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("groupId", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("rateHz", ContractShape.OfPrimitive(ContractPrimitive.F32)),
        ("validMask", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("samples", ContractShape.OfArray(ContractPrimitive.F32, MaxChannels)));

    /// <summary>计量块。</summary>
    public static MeteringBlock Metering { get; } = new()
    {
        PayloadLengthField = "payloadBytes",
        SamplesPerUnit = 1,
        ChannelCount = 1,
    };
}
