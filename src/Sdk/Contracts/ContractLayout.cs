using System.Collections.ObjectModel;

namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>字段布局：名称 + 在数据单元中的**字节偏移** + 形状。</summary>
/// <param name="Name">字段名（顶层字段；非结构形状为空串）。</param>
/// <param name="Offset">相对数据单元起点的字节偏移。</param>
/// <param name="Shape">字段形状。</param>
public readonly record struct FieldLayout(string Name, int Offset, ContractShape Shape);

/// <summary>
/// 契约**布局**（字段偏移表）：由 <see cref="ContractShape"/> 计算得出。
/// </summary>
/// <remarks>
/// <para>这是"数据单元可扁平化"的关键：定长形状在**加载/声明期**即可算出每字段的固定偏移，
/// 运行期读写只做偏移寻址，无反射、无分配（ADR-0029 Q4-2）。</para>
/// <para><b>v1 范围</b>：只提供**顶层字段**偏移；嵌套结构的字段访问由作者按整体字节块自行解析
/// （嵌套访问器的加载期生成属 R 项优化）。</para>
/// </remarks>
public sealed class ContractLayout
{
    private readonly Dictionary<string, FieldLayout> _fieldsByName;

    private ContractLayout(ContractShape shape, IReadOnlyList<FieldLayout> fields, int sizeInBytes)
    {
        Shape = shape;
        Fields = fields;
        SizeInBytes = sizeInBytes;
        _fieldsByName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    /// <summary>原始形状。</summary>
    public ContractShape Shape { get; }

    /// <summary>数据单元的定长字节数。</summary>
    public int SizeInBytes { get; }

    /// <summary>顶层字段（顺序即签名顺序）。</summary>
    public IReadOnlyList<FieldLayout> Fields { get; }

    /// <summary>由形状计算布局。</summary>
    public static ContractLayout FromShape(ContractShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (shape is StructShape structure)
        {
            var fields = new List<FieldLayout>(structure.Fields.Count);
            var offset = 0;
            foreach (var (name, fieldShape) in structure.Fields)
            {
                fields.Add(new FieldLayout(name, offset, fieldShape));
                offset += fieldShape.SizeInBytes;
            }

            return new ContractLayout(shape, new ReadOnlyCollection<FieldLayout>(fields), offset);
        }

        // 非结构形状：整体作为一个匿名字段（偏移 0）
        return new ContractLayout(shape, [new FieldLayout(string.Empty, 0, shape)], shape.SizeInBytes);
    }

    /// <summary>按字段名查偏移（O(1)）。</summary>
    public bool TryGetField(string name, out FieldLayout field)
    {
        if (name is not null && _fieldsByName.TryGetValue(name, out var found))
        {
            field = found;
            return true;
        }

        field = default;
        return false;
    }

    /// <summary>按字段名取偏移；不存在即抛出（用于作者代码中的**契约错误**，应在开发期暴露）。</summary>
    public FieldLayout GetField(string name) =>
        TryGetField(name, out var field)
            ? field
            : throw new ArgumentException($"契约中没有名为 '{name}' 的字段", nameof(name));
}
