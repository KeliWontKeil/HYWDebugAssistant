using System.Buffers.Binary;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Ui.Runtime;

/// <summary>
/// 订阅样本解码：按**声明的契约原语**把定长字节解成数值（§5.3 声明数据源）。
/// </summary>
/// <remarks>
/// **不猜类型**：模块未声明原语时解码失败，UI 显示"未声明原语 + 字节数"，而不是按长度推断。
/// 字节序与数据面一致（小端，见 `PayloadWriter`）。
/// </remarks>
internal static class SampleCodec
{
    /// <summary>解码一个样本。</summary>
    /// <param name="primitive">声明的契约原语（null = 未声明 → 解码失败）。</param>
    /// <param name="sample">样本字节。</param>
    /// <param name="value">解出的数值。</param>
    public static bool TryDecode(ContractPrimitive? primitive, ReadOnlySpan<byte> sample, out double value)
    {
        value = 0;

        if (primitive is not { } type)
        {
            return false;
        }

        try
        {
            value = type switch
            {
                ContractPrimitive.I8 => (sbyte)sample[0],
                ContractPrimitive.U8 => sample[0],
                ContractPrimitive.Bool => sample[0] == 0 ? 0d : 1d,
                ContractPrimitive.I16 => BinaryPrimitives.ReadInt16LittleEndian(sample),
                ContractPrimitive.U16 => BinaryPrimitives.ReadUInt16LittleEndian(sample),
                ContractPrimitive.I32 => BinaryPrimitives.ReadInt32LittleEndian(sample),
                ContractPrimitive.U32 => BinaryPrimitives.ReadUInt32LittleEndian(sample),
                ContractPrimitive.I64 => BinaryPrimitives.ReadInt64LittleEndian(sample),
                ContractPrimitive.U64 => BinaryPrimitives.ReadUInt64LittleEndian(sample),
                ContractPrimitive.F32 => BinaryPrimitives.ReadSingleLittleEndian(sample),
                ContractPrimitive.F64 => BinaryPrimitives.ReadDoubleLittleEndian(sample),
                _ => double.NaN,
            };

            return !double.IsNaN(value) || type == ContractPrimitive.F32 || type == ContractPrimitive.F64;
        }
        catch (ArgumentOutOfRangeException)
        {
            // 样本长度不足（声明与写入不一致）→ 解码失败，由 UI 显示占位
            return false;
        }
    }

    /// <summary>按声明原语的字节数（未声明时为 0）。</summary>
    public static int SizeOf(ContractPrimitive? primitive) => primitive switch
    {
        ContractPrimitive.I8 or ContractPrimitive.U8 or ContractPrimitive.Bool => 1,
        ContractPrimitive.I16 or ContractPrimitive.U16 => 2,
        ContractPrimitive.I32 or ContractPrimitive.U32 or ContractPrimitive.F32 => 4,
        ContractPrimitive.I64 or ContractPrimitive.U64 or ContractPrimitive.F64 => 8,
        _ => 0,
    };
}
