namespace HYWDebugAssistant.Sdk;

/// <summary>
/// 模块标识：**结构化三元组**（作者命名空间 / 模块名 / 版本）。
/// </summary>
/// <remarks>
/// 字面规则（唯一来源：本类型）：
/// <list type="bullet">
///   <item>三段均为 <c>[a-z0-9]</c> 与 <c>-</c> 组成，不能为空、不能以 <c>-</c> 开头或结尾；</item>
///   <item>版本为 SemVer 三段式 <c>主.次.补丁</c>（如 <c>1.0.0</c>）；</item>
///   <item>字面量为三段以 <c>/</c> 连接：<c>hyw/serial-port/1.0.0</c>。</item>
/// </list>
/// 版本是**独立维度**：同 ID 的多个版本共存注册（模块包生命周期，docs/architecture/03 §3.7.1）。
/// </remarks>
public readonly record struct ModuleId
{
    private ModuleId(string author, string name, string version)
    {
        Author = author;
        Name = name;
        Version = version;
    }

    /// <summary>作者命名空间（如 <c>hyw</c>）。</summary>
    public string Author { get; }

    /// <summary>模块名（如 <c>serial-port</c>）。</summary>
    public string Name { get; }

    /// <summary>版本（SemVer，如 <c>1.0.0</c>）。</summary>
    public string Version { get; }

    /// <summary>字面量：<c>作者/模块名/版本</c>。</summary>
    public override string ToString() => $"{Author}/{Name}/{Version}";

    /// <summary>创建模块标识；任一段不合法即抛出（加载期校验用 <see cref="TryParse"/>）。</summary>
    public static ModuleId Create(string author, string name, string version)
    {
        if (!TryParse($"{author}/{name}/{version}", out var id))
        {
            throw new ArgumentException($"非法的模块标识：{author}/{name}/{version}", nameof(author));
        }

        return id;
    }

    /// <summary>解析字面量；失败时返回 <see langword="false"/>（**不猜测、不纠正**）。</summary>
    public static bool TryParse(string? text, out ModuleId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!IsValidSegment(parts[0]) || !IsValidSegment(parts[1]) || !IsValidVersion(parts[2]))
        {
            return false;
        }

        id = new ModuleId(parts[0], parts[1], parts[2]);
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

    private static bool IsValidVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3
            && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit));
    }
}
