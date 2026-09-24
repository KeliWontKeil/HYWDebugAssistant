using System.Collections.Concurrent;
using HYWDebugAssistant.Sdk;

namespace HYWDebugAssistant.Hosting.Registry;

/// <summary>
/// 模块注册表（Module Registry）：Definition 的全局登记服务——基线 §4.2/§4.9（归属 Core）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>版本维度</b>：同 ID 的多个版本**共存注册**（ADR-0020 §11.2）；键为完整三元组字面量。</item>
///   <item><b>重复注册</b>：同 ID 同版本重复 → 拒绝（返回原因），**不覆盖**。</item>
///   <item><b>移除</b>：注销注册项；已实例化的在用实例**不强制卸载**（进入缺失态由上层处理）。</item>
/// </list>
/// 线程安全（管理线程注册/查询）；不持锁进入用户代码。
/// </remarks>
public sealed class ModuleRegistry
{
    private readonly ConcurrentDictionary<string, ModuleDefinition> _definitions = new(StringComparer.Ordinal);

    /// <summary>已注册的全部定义（快照）。</summary>
    public IReadOnlyCollection<ModuleDefinition> Definitions => [.. _definitions.Values];

    /// <summary>注册一个模块定义；同 ID 同版本已存在时拒绝。</summary>
    /// <param name="definition">模块定义。</param>
    /// <param name="failure">失败原因（成功时为 <see langword="null"/>）。</param>
    public bool TryRegister(ModuleDefinition definition, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!_definitions.TryAdd(definition.Id.ToString(), definition))
        {
            failure = $"模块已注册（同 ID 同版本）：{definition.Id}";
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>按标识解析定义（精确版本匹配）。</summary>
    public bool TryResolve(ModuleId id, out ModuleDefinition? definition) =>
        _definitions.TryGetValue(id.ToString(), out definition);

    /// <summary>按作者 + 模块名列出**全部已注册版本**（多版本共存）。</summary>
    public IReadOnlyList<ModuleDefinition> ResolveVersions(string author, string name) =>
        [.. _definitions.Values
            .Where(d => string.Equals(d.Id.Author, author, StringComparison.Ordinal)
                     && string.Equals(d.Id.Name, name, StringComparison.Ordinal))
            .OrderBy(d => d.Id.Version, StringComparer.Ordinal)];

    /// <summary>注销注册项（已实例化的在用实例不受影响，由上层置为缺失态）。</summary>
    public bool TryUnregister(ModuleId id) => _definitions.TryRemove(id.ToString(), out _);
}
