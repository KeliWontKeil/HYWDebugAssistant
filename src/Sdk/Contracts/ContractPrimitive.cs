namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// 受控基础形状原语目录（契约三层能力之②）——ADR-0028 C3-5：**仅定长原语 + 结构组合**，无变长。
/// </summary>
/// <remarks>
/// 作者只能从该目录组合契约形状；加载期校验（不在目录内的成员即拒绝注册）。
/// 定长是"数据单元可扁平化、零拷贝投递可行"的前提（ADR-0029 Q4-2）。
/// </remarks>
public enum ContractPrimitive
{
    /// <summary>8 位有符号整数。</summary>
    I8 = 0,

    /// <summary>16 位有符号整数。</summary>
    I16 = 1,

    /// <summary>32 位有符号整数。</summary>
    I32 = 2,

    /// <summary>64 位有符号整数。</summary>
    I64 = 3,

    /// <summary>8 位无符号整数。</summary>
    U8 = 4,

    /// <summary>16 位无符号整数。</summary>
    U16 = 5,

    /// <summary>32 位无符号整数。</summary>
    U32 = 6,

    /// <summary>64 位无符号整数。</summary>
    U64 = 7,

    /// <summary>32 位浮点数。</summary>
    F32 = 8,

    /// <summary>64 位浮点数。</summary>
    F64 = 9,

    /// <summary>布尔。</summary>
    Bool = 10,
}
