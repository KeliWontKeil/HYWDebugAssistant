using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>波形帧的模块间约定（本模块自己的声明配方；不引用其它模块/Sdk 共享类型）。</summary>
internal static class WaveformFrameContract
{
    /// <summary>
    /// 类型标识 —— **PascalCase 短名**（不带作者前缀、不带版本）；必须与另一模块逐字相同。
    /// </summary>
    public const string TypeId = "WaveformFrame";
    public const int MaxChannels = 64;
    public const int SizeInBytes = 280;
    public const int HeaderSize = 24;

    public static ContractShape Shape { get; } = ContractShape.OfStruct(
        ("channelCount", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("payloadBytes", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("groupId", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("rateHz", ContractShape.OfPrimitive(ContractPrimitive.F32)),
        ("validMask", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("samples", ContractShape.OfArray(ContractPrimitive.F32, MaxChannels)));

    public static MeteringBlock Metering { get; } = new()
    {
        PayloadLengthField = "payloadBytes",
        SamplesPerUnit = 1,
        ChannelCount = 1,
    };
}
