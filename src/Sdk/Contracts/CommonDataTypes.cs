namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// **参考类型库**：`hyw/` 命名空间下的**标准数据类型**，模块可直接采用（也可自建自己的类型）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么放 SDK</b>【ADR-0046 D2】：类型身份由**模块**声明并注册，框架只持机械镜像；
///     但"大家都用的那几种量"应该有一份**标准写法**，否则每个模块各写一遍形状/解码器/计量块——
///     稍有出入就变成"同标识不同形状"的假不兼容。本类只提供**声明常量与工厂**，
///     **注册动作仍然由模块自己做**（<c>context.Declaration.DeclareDataType(...)</c>）；</item>
///   <item><b>标识即身份</b>：两个模块声明同一个 <c>(标识, 版本)</c> 就被视为**同一种量**
///     （兼容走"同标识同版本"层）；自定义量请用<b>自己的前缀</b>（如 <c>acme/temp.v2</c>）；</item>
///   <item><b>解码器是模块提供的</b>：本类里的解码器基于 <see cref="PrimitiveSampleDecoder"/>（参考实现），
///     模块把它填进自己的类型声明即可 —— 框架只回调、不内建（D1 ③）。</item>
/// </list>
/// </remarks>
public static class CommonDataTypes
{
    /// <summary>标准类型标识：模拟量样本 <c>{ seq: u64, value: f32 }</c>（12 字节）。</summary>
    public const string AnalogSampleTypeId = "hyw/analog-sample";

    /// <summary>标准类型标识：单个 f32 数值（4 字节；视图数值/曲线的样本）。</summary>
    public const string F32TypeId = "hyw/f32";

    /// <summary>标准类型标识：单个 u64 计数（8 字节；序号/计数类样本）。</summary>
    public const string U64TypeId = "hyw/u64";

    /// <summary>标准类型标识：单个 u8 枚举/标志（1 字节）。</summary>
    public const string U8TypeId = "hyw/u8";

    /// <summary><c>hyw/analog-sample@1</c> 的形状：<c>{ seq: u64, value: f32 }</c>。</summary>
    public static ContractShape AnalogSampleShape { get; } = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    /// <summary><c>hyw/analog-sample</c> 的类型引用（版本 1）。</summary>
    public static DataTypeRef AnalogSampleRef { get; } = new(AnalogSampleTypeId, 1);

    /// <summary><c>hyw/f32</c> 的类型引用（版本 1）。</summary>
    public static DataTypeRef F32Ref { get; } = new(F32TypeId, 1);

    /// <summary><c>hyw/u64</c> 的类型引用（版本 1）。</summary>
    public static DataTypeRef U64Ref { get; } = new(U64TypeId, 1);

    /// <summary><c>hyw/u8</c> 的类型引用（版本 1）。</summary>
    public static DataTypeRef U8Ref { get; } = new(U8TypeId, 1);

    /// <summary>
    /// <c>hyw/analog-sample@1</c> 的声明：解码器取 **value 字段**（偏移 8）、计量块按"每单元 1 样本 1 通道"。
    /// </summary>
    public static DataTypeDeclaration AnalogSampleV1 { get; } = new(
        AnalogSampleTypeId, 1, AnalogSampleShape)
    {
        Metering = new MeteringBlock
        {
            PayloadSizeInBytes = AnalogSampleShape.SizeInBytes,
            SamplesPerUnit = 1,
            ChannelCount = 1,
        },
        Decoder = (ReadOnlySpan<byte> sample, out double value) =>
            sample.Length >= AnalogSampleShape.SizeInBytes
                ? PrimitiveSampleDecoder.TryDecode(ContractPrimitive.F32, sample[8..], out value)
                : Fail(out value),
        SemanticTags = ["样本含序号与值", "value 为无单位实数"],
    };

    /// <summary><c>hyw/f32@1</c> 的声明（解码器 = 参考实现）。</summary>
    public static DataTypeDeclaration F32V1 { get; } = new(
        F32TypeId, 1, ContractShape.OfPrimitive(ContractPrimitive.F32))
    {
        Decoder = PrimitiveSampleDecoder.For(ContractPrimitive.F32),
    };

    /// <summary><c>hyw/u64@1</c> 的声明（解码器 = 参考实现）。</summary>
    public static DataTypeDeclaration U64V1 { get; } = new(
        U64TypeId, 1, ContractShape.OfPrimitive(ContractPrimitive.U64))
    {
        Decoder = PrimitiveSampleDecoder.For(ContractPrimitive.U64),
    };

    /// <summary><c>hyw/u8@1</c> 的声明（解码器 = 参考实现）。</summary>
    public static DataTypeDeclaration U8V1 { get; } = new(
        U8TypeId, 1, ContractShape.OfPrimitive(ContractPrimitive.U8))
    {
        Decoder = PrimitiveSampleDecoder.For(ContractPrimitive.U8),
    };

    private static bool Fail(out double value)
    {
        value = 0;
        return false;
    }
}
