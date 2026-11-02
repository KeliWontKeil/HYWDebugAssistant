namespace HYWDebugAssistant.Sdk;

/// <summary>
/// 模块标识：**作者命名空间 / 模块名** 两段。
/// </summary>
/// <remarks>
/// 字面规则（唯一来源：本类型）：
/// <list type="bullet">
///   <item>两段均为 <c>[a-z0-9]</c> 与 <c>-</c> 组成，不能为空、不能以 <c>-</c> 开头或结尾；</item>
///   <item>字面量为两段以 <c>/</c> 连接：<c>hyw/serial-port</c>。</item>
/// </list>
/// <para>
/// **标识不含版本**：模块的"新旧"由包内容决定（同目录重建 / 重新打包），同 ID 只有一个注册项；
/// 版本只属于产品构建（见 docs/architecture/VERSIONING.md）。
/// </para>
/// </remarks>
public readonly record struct ModuleId
{
    private ModuleId(string author, string name)
    {
        Author = author;
        Name = name;
    }

    /// <summary>作者命名空间（如 <c>hyw</c>）。</summary>
    public string Author { get; }

    /// <summary>模块名（如 <c>serial-port</c>）。</summary>
    public string Name { get; }

    /// <summary>字面量：<c>作者/模块名</c>。</summary>
    public override string ToString() => $"{Author}/{Name}";

    /// <summary>创建模块标识；任一段不合法即抛出（加载期校验用 <see cref="TryParse"/>）。</summary>
    /// <param name="author">作者命名空间。</param>
    /// <param name="name">模块名。</param>
    public static ModuleId Create(string author, string name)
    {
        if (!TryParse($"{author}/{name}", out var id))
        {
            throw new ArgumentException($"非法的模块标识：{author}/{name}", nameof(author));
        }

        return id;
    }

    /// <summary>解析字面量；失败时返回 <see langword="false"/>（**不猜测、不纠正**）。</summary>
    /// <param name="text">待解析字面量。</param>
    /// <param name="id">解析结果。</param>
    public static bool TryParse(string? text, out ModuleId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IsValidSegment(parts[0]) || !IsValidSegment(parts[1]))
        {
            return false;
        }

        id = new ModuleId(parts[0], parts[1]);
        return true;
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment[0] == '-' || segment[^1] == '-')
        {
            return false;
        }

        return segment.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }
}
