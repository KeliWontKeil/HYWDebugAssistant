using System.Collections.Concurrent;
using HYWDebugAssistant.Sdk;

namespace HYWDebugAssistant.Hosting.Registry;

/// <summary>
/// 模块注册表（Module Registry）：Definition 的全局登记服务——基线 §4.2/§4.9（归属 Core）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一个 ID 一个定义</b>：标识不含版本，键就是两段字面量。</item>
///   <item><b>重复注册</b>：同 ID 重复 → 拒绝（返回原因），**不覆盖**。</item>
///   <item><b>移除</b>：注销注册项；已实例化的在用实例**不强制卸载**（进入缺失态由上层处理）。</item>
/// </list>
/// 线程安全（管理线程注册/查询）；不持锁进入用户代码。
/// </remarks>
public sealed class ModuleRegistry
{
    private readonly ConcurrentDictionary<string, ModuleDefinition> _definitions = new(StringComparer.Ordinal);

    /// <summary>已注册的全部定义（快照）。</summary>
    public IReadOnlyCollection<ModuleDefinition> Definitions => [.. _definitions.Values];

    /// <summary>注册一个模块定义；同 ID 已存在时拒绝。</summary>
    /// <param name="definition">模块定义。</param>
    /// <param name="failure">失败原因（成功时为 <see langword="null"/>）。</param>
    public bool TryRegister(ModuleDefinition definition, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!_definitions.TryAdd(definition.Id.ToString(), definition))
        {
            failure = HostingText.Get("hosting.registry.alreadyRegistered", definition.Id);
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>按标识解析定义。</summary>
    public bool TryResolve(ModuleId id, out ModuleDefinition? definition) =>
        _definitions.TryGetValue(id.ToString(), out definition);

    /// <summary>注销注册项（已实例化的在用实例不受影响，由上层置为缺失态）。</summary>
    public bool TryUnregister(ModuleId id) => _definitions.TryRemove(id.ToString(), out _);
}
