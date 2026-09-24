using System.Collections.ObjectModel;

namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// 契约**形状**描述（契约三层能力之③：组合与签名）——ADR-0028 §3.4 / C3-5。
/// </summary>
/// <remarks>
/// 形状 = 成员序列（名称 + 原语类型 + 顺序）= **签名**；兼容判定 = 接收契约 ⊇ 发送契约（形状子集，可计算）。
/// <para>
/// v1 规则：仅定长原语（<see cref="ContractPrimitive"/>）、定长字节块（1–256）、定长数组（元素为原语，长度 1–256）、
/// 结构组合（嵌套深度 ≤ 4）；**无变长**。
/// </para>
/// <para>
/// 契约**不含单位/量纲语义**（主文档 §5.4.2）——单位属模块配置/显示层。
/// </para>
/// </remarks>
public abstract record ContractShape
{
    /// <summary>形状的**定长字节数**（编译/加载期即可确定）。</summary>
    public abstract int SizeInBytes { get; }

    /// <summary>嵌套深度（顶层为 1；结构组合上限 4）。</summary>
    public abstract int Depth { get; }

    /// <summary>创建单原语形状。</summary>
    public static PrimitiveShape OfPrimitive(ContractPrimitive primitive) => new(primitive);

    /// <summary>创建定长字节块（1–256 字节）。</summary>
    public static BytesShape OfBytes(int length) => new(length);

    /// <summary>创建定长数组（元素为原语，长度 1–256）。</summary>
    public static ArrayShape OfArray(ContractPrimitive element, int length) => new(element, length);

    /// <summary>创建结构组合（字段顺序即签名顺序）。</summary>
    public static StructShape OfStruct(params (string Name, ContractShape Shape)[] fields) => new(fields);

    /// <summary>
    /// **信息类型的可读描述**【新增 · M1-16 / ADR-0039 D4】：把契约形状渲染成紧凑文本
    /// （如 <c>f32</c> · <c>u64</c> · <c>f32[8]</c> · <c>bytes[16]</c> · <c>{seq:u64, value:f32}</c>）。
    /// </summary>
    /// <remarks>
    /// 用途 = **界面表达"接口的信息类型"**（端口标记 / 拓扑诊断）。此前该事实只存在于 SDK 与运行时内部，
    /// 公开面只有字节数，界面无从显示（用户第六轮反馈："完全不知道接口的信息类型"）。
    /// 描述文本**不承载语义**（单位/量纲仍属模块配置与显示层，主文档 §5.4.2）。
    /// </remarks>
    public string Describe()
    {
        return this switch
        {
            PrimitiveShape primitive => DescribePrimitive(primitive.Primitive),
            BytesShape bytes => $"bytes[{bytes.Length}]",
            ArrayShape array => $"{DescribePrimitive(array.Element)}[{array.Length}]",
            StructShape structure => "{" + string.Join(", ", structure.Fields.Select(f => $"{f.Name}:{f.Shape.Describe()}")) + "}",
            _ => "?",
        };
    }

    /// <summary>原语的紧凑字面量（小写，与 SDK 文档一致）。</summary>
    /// <param name="primitive">原语。</param>
    public static string DescribePrimitive(ContractPrimitive primitive) => primitive switch
    {
        ContractPrimitive.I8 => "i8",
        ContractPrimitive.I16 => "i16",
        ContractPrimitive.I32 => "i32",
        ContractPrimitive.I64 => "i64",
        ContractPrimitive.U8 => "u8",
        ContractPrimitive.U16 => "u16",
        ContractPrimitive.U32 => "u32",
        ContractPrimitive.U64 => "u64",
        ContractPrimitive.F32 => "f32",
        ContractPrimitive.F64 => "f64",
        ContractPrimitive.Bool => "bool",
        _ => primitive.ToString().ToLowerInvariant(),
    };
}

/// <summary>单原语形状。</summary>
public sealed record PrimitiveShape(ContractPrimitive Primitive) : ContractShape
{
    /// <inheritdoc />
    public override int SizeInBytes => Primitive switch
    {
        ContractPrimitive.I8 or ContractPrimitive.U8 or ContractPrimitive.Bool => 1,
        ContractPrimitive.I16 or ContractPrimitive.U16 => 2,
        ContractPrimitive.I32 or ContractPrimitive.U32 or ContractPrimitive.F32 => 4,
        ContractPrimitive.I64 or ContractPrimitive.U64 or ContractPrimitive.F64 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(Primitive), Primitive, "未登记的原语"),
    };

    /// <inheritdoc />
    public override int Depth => 1;
}

/// <summary>定长字节块（1–256 字节）。</summary>
public sealed record BytesShape : ContractShape
{
    /// <summary>长度上限（v1）。</summary>
    public const int MaxLength = 256;

    /// <summary>创建字节块形状。</summary>
    public BytesShape(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);
        Length = length;
    }

    /// <summary>字节数。</summary>
    public int Length { get; }

    /// <inheritdoc />
    public override int SizeInBytes => Length;

    /// <inheritdoc />
    public override int Depth => 1;
}

/// <summary>定长数组（元素为原语，长度 1–256）。</summary>
public sealed record ArrayShape : ContractShape
{
    /// <summary>长度上限（v1）。</summary>
    public const int MaxLength = 256;

    /// <summary>创建定长数组形状。</summary>
    public ArrayShape(ContractPrimitive element, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);
        Element = element;
        Length = length;
        ElementSize = new PrimitiveShape(element).SizeInBytes;
    }

    /// <summary>元素原语类型。</summary>
    public ContractPrimitive Element { get; }

    /// <summary>元素个数。</summary>
    public int Length { get; }

    private int ElementSize { get; }

    /// <inheritdoc />
    public override int SizeInBytes => ElementSize * Length;

    /// <inheritdoc />
    public override int Depth => 2;
}

/// <summary>结构组合（字段顺序即签名顺序；嵌套深度 ≤ 4）。</summary>
public sealed record StructShape : ContractShape
{
    /// <summary>嵌套深度上限（v1）。</summary>
    public const int MaxDepth = 4;

    /// <summary>创建结构形状。</summary>
    public StructShape(params (string Name, ContractShape Shape)[] fields)
        : this((IEnumerable<(string Name, ContractShape Shape)>)fields)
    {
    }

    /// <summary>创建结构形状。</summary>
    public StructShape(IEnumerable<(string Name, ContractShape Shape)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var list = fields.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("结构形状至少需要一个字段", nameof(fields));
        }

        if (list.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != list.Count)
        {
            throw new ArgumentException("结构形状的字段名必须唯一", nameof(fields));
        }

        var depth = list.Max(f => f.Shape.Depth) + 1;
        if (depth > MaxDepth)
        {
            throw new ArgumentException($"结构嵌套深度 {depth} 超过上限 {MaxDepth}", nameof(fields));
        }

        Fields = new ReadOnlyCollection<(string Name, ContractShape Shape)>(list);
        Depth = depth;
        SizeInBytes = list.Sum(f => f.Shape.SizeInBytes);
    }

    /// <summary>字段（顺序即签名顺序）。</summary>
    public ReadOnlyCollection<(string Name, ContractShape Shape)> Fields { get; }

    /// <inheritdoc />
    public override int SizeInBytes { get; }

    /// <inheritdoc />
    public override int Depth { get; }
}
