using System.Buffers.Binary;

namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// **原语样本解码器（参考实现）**：按声明的契约原语把定长字节解成数值。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>这是「参考实现」，不是框架内建解码</b>：呈现期解码**应由模块提供**
///         （<see cref="DataTypeDeclaration.Decoder"/>），框架只回调。本类存在的意义有二：
///         ① 给模块一个**可以直接拿去用**的实现（<c>Decoder = PrimitiveSampleDecoder.For(f32)</c>）；
///         ② 给**尚未声明类型的旧模块**一个过渡回退（回退路径与"结构回退"同一纪律：只为兼容，不作为机制）；</item>
///   <item><b>不猜类型</b>：调用方必须给出原语；给不出就**解码失败**，界面显示"未声明原语 + 字节数"；</item>
///   <item>字节序与数据面一致（**小端**，见 <c>PayloadWriter</c>）。</item>
/// </list>
/// </remarks>
public static class PrimitiveSampleDecoder
{
    /// <summary>解码一个样本。</summary>
    /// <param name="primitive">声明的契约原语。</param>
    /// <param name="sample">样本字节。</param>
    /// <param name="value">解出的数值。</param>
    /// <returns>是否解码成功（长度不足 = 失败，不抛）。</returns>
    public static bool TryDecode(ContractPrimitive primitive, ReadOnlySpan<byte> sample, out double value)
    {
        value = 0;

        if (sample.Length < SizeOf(primitive))
        {
            return false;
        }

        switch (primitive)
        {
            case ContractPrimitive.I8:
                value = (sbyte)sample[0];
                return true;
            case ContractPrimitive.U8:
                value = sample[0];
                return true;
            case ContractPrimitive.Bool:
                value = sample[0] == 0 ? 0d : 1d;
                return true;
            case ContractPrimitive.I16:
                value = BinaryPrimitives.ReadInt16LittleEndian(sample);
                return true;
            case ContractPrimitive.U16:
                value = BinaryPrimitives.ReadUInt16LittleEndian(sample);
                return true;
            case ContractPrimitive.I32:
                value = BinaryPrimitives.ReadInt32LittleEndian(sample);
                return true;
            case ContractPrimitive.U32:
                value = BinaryPrimitives.ReadUInt32LittleEndian(sample);
                return true;
            case ContractPrimitive.I64:
                value = BinaryPrimitives.ReadInt64LittleEndian(sample);
                return true;
            case ContractPrimitive.U64:
                value = BinaryPrimitives.ReadUInt64LittleEndian(sample);
                return true;
            case ContractPrimitive.F32:
                value = BinaryPrimitives.ReadSingleLittleEndian(sample);
                return true;
            case ContractPrimitive.F64:
                value = BinaryPrimitives.ReadDoubleLittleEndian(sample);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 取一个**可直接注册**的解码器（模块在声明类型时写 <c>Decoder = PrimitiveSampleDecoder.For(ContractPrimitive.F32)</c>）。
    /// </summary>
    /// <param name="primitive">契约原语。</param>
    /// <returns>解码器委托。</returns>
    public static SampleDecoder For(ContractPrimitive primitive)
    {
        var size = SizeOf(primitive);

        return (ReadOnlySpan<byte> sample, out double value) =>
        {
            if (sample.Length < size)
            {
                value = 0;
                return false;
            }

            return TryDecode(primitive, sample, out value);
        };
    }

    /// <summary>按声明原语的字节数（未登记的原语为 0）。</summary>
    /// <param name="primitive">契约原语。</param>
    public static int SizeOf(ContractPrimitive primitive) => primitive switch
    {
        ContractPrimitive.I8 or ContractPrimitive.U8 or ContractPrimitive.Bool => 1,
        ContractPrimitive.I16 or ContractPrimitive.U16 => 2,
        ContractPrimitive.I32 or ContractPrimitive.U32 or ContractPrimitive.F32 => 4,
        ContractPrimitive.I64 or ContractPrimitive.U64 or ContractPrimitive.F64 => 8,
        _ => 0,
    };
}
