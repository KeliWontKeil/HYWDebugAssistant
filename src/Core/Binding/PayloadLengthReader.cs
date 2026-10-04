using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Core.Binding;

/// <summary>
/// **有效长度字段的读取器**（速率只计有效数据）：把模块声明的
/// "契约里哪个字段写着**这一个单元真正有多少业务字节**"解析成 **偏移 + 原语**（加载期一次），
/// 运行期只做偏移寻址 + 小端读取（零反射、零分配）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：定长契约里的**变长载荷**（典型 <c>{ len: u16, data: bytes[1472] }</c>，
///     尾部补零）用一个静态尺寸表达不出"这一个单元真正有多少业务字节" ⇒ 有效载荷字节速率只能退化成
///     搬迁字节速率。声明字段名之后，框架逐单元读它 ⇒ 第 3 档速率是**真实**有效速率；</item>
///   <item><b>字段名而不是委托</b>：`MeteringBlock` 上的有效长度字段是**可选字符串声明**
///     —— 框架只做**机械读取**（按 <see cref="ContractLayout"/> 的顶层偏移表），
///     与"框架只持机械镜像、不理解语义"的既有纪律一致；</item>
///   <item><b>解析不出来 ⇒ null</b>（而不是 0）：调用方回落到静态尺寸，**绝不**把"声明写错了"
///     静默表现成"有效速率恒为 0"（那种静默归零比误报危险得多）。声明错误由
///     <c>Sdk.Testing.ModuleContract</c> 的契约自检在加载期明确报出。</item>
/// </list>
/// </remarks>
/// <param name="Offset">有效长度字段相对数据单元起点的字节偏移。</param>
/// <param name="Primitive">该字段的整型原语（决定读几个字节）。</param>
public readonly record struct PayloadLengthReader(int Offset, ContractPrimitive Primitive)
{
    /// <summary>该原语的字节数。</summary>
    private int SizeInBytes => PrimitiveSampleDecoder.SizeOf(Primitive);

    /// <summary>
    /// 解析"有效长度字段"声明；**任何一处对不上都返回 <see langword="null"/>**（含未声明）。
    /// </summary>
    /// <param name="layout">数据单元的契约布局（顶层字段偏移表）。</param>
    /// <param name="fieldName">声明的字段名（可空 = 未声明）。</param>
    /// <returns>读取器；不可用时为 <see langword="null"/>。</returns>
    public static PayloadLengthReader? Resolve(ContractLayout layout, string? fieldName)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (string.IsNullOrWhiteSpace(fieldName)
            || !layout.TryGetField(fieldName, out var field)
            || field.Shape is not PrimitiveShape primitive
            || !IsInteger(primitive.Primitive))
        {
            return null;
        }

        return new PayloadLengthReader(field.Offset, primitive.Primitive);
    }

    /// <summary>该原语是否是"可以当字节数用"的整型（浮点/字节块/结构都不行）。</summary>
    /// <param name="primitive">原语。</param>
    public static bool IsInteger(ContractPrimitive primitive) => primitive
        is ContractPrimitive.I8 or ContractPrimitive.U8
        or ContractPrimitive.I16 or ContractPrimitive.U16
        or ContractPrimitive.I32 or ContractPrimitive.U32
        or ContractPrimitive.I64 or ContractPrimitive.U64;

    /// <summary>
    /// 从数据单元里读出"有效字节数"。
    /// </summary>
    /// <param name="unit">数据单元字节。</param>
    /// <param name="value">读出的字节数（**负值夹成 0**；超过 <see cref="int.MaxValue"/> 夹到上限）。</param>
    /// <returns>是否读成功（字节不够 = 失败，不抛）。</returns>
    /// <remarks>
    /// 这里**只做读取与夹取**，"不得超过契约尺寸"由调用方（<see cref="Binding.PayloadBytesOfUnit"/>）再夹一次
    /// —— 上限的语义属于绑定（它才知道契约尺寸）。
    /// </remarks>
    public bool TryRead(ReadOnlySpan<byte> unit, out int value)
    {
        value = 0;

        var size = SizeInBytes;
        if (size == 0 || Offset < 0 || unit.Length < Offset + size)
        {
            return false;
        }

        if (!PrimitiveSampleDecoder.TryDecode(Primitive, unit[Offset..], out var decoded))
        {
            return false;
        }

        value = decoded switch
        {
            < 0 => 0,
            > int.MaxValue => int.MaxValue,
            _ => (int)decoded,
        };

        return true;
    }
}
