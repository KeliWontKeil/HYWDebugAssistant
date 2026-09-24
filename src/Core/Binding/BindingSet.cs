using HYWDebugAssistant.Core.Composition;

namespace HYWDebugAssistant.Core.Binding;

/// <summary>
/// 绑定集：**不可变快照**，投递路径读快照、管理路径原子替换（ADR-0029 §4.2）。
/// </summary>
/// <remarks>
/// 为什么用不可变快照：投递发生在模块线程、增删发生在管理线程；快照替换让**投递无锁**且始终看到一致视图。
/// 绑定数量规模小、重建不频繁，复制成本可忽略。
/// </remarks>
public sealed class BindingSet
{
    /// <summary>空集。</summary>
    public static readonly BindingSet Empty = new([]);

    private readonly Binding[] _bindings;

    private BindingSet(Binding[] bindings) => _bindings = bindings;

    /// <summary>当前全部绑定（只读快照）。</summary>
    public IReadOnlyList<Binding> Bindings => _bindings;

    /// <summary>按网络名查找绑定。</summary>
    public bool TryGet(string networkName, out Binding? binding)
    {
        ArgumentNullException.ThrowIfNull(networkName);

        foreach (var candidate in _bindings)
        {
            if (string.Equals(candidate.Network.Name, networkName, StringComparison.Ordinal))
            {
                binding = candidate;
                return true;
            }
        }

        binding = null;
        return false;
    }

    /// <summary>新增一个绑定，返回新快照。</summary>
    public BindingSet With(Binding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new BindingSet([.. _bindings, binding]);
    }

    /// <summary>移除指定网络对应的绑定，返回新快照（幂等）。</summary>
    public BindingSet Without(string networkName)
    {
        ArgumentNullException.ThrowIfNull(networkName);

        var kept = _bindings
            .Where(b => !string.Equals(b.Network.Name, networkName, StringComparison.Ordinal))
            .ToArray();

        return kept.Length == _bindings.Length ? this : new BindingSet(kept);
    }

    /// <summary>
    /// 按**对象引用**移除绑定（成员重锚用）：匿名网络没有唯一名，只能按引用定位
    /// （按名移除会误伤其他匿名网络）。
    /// </summary>
    public BindingSet Without(Network network)
    {
        ArgumentNullException.ThrowIfNull(network);

        var kept = _bindings.Where(b => !ReferenceEquals(b.Network, network)).ToArray();
        return kept.Length == _bindings.Length ? this : new BindingSet(kept);
    }

    /// <summary>移除与某成员相关的全部绑定（删除实例时级联用），返回新快照与受影响的网络名。</summary>
    public BindingSet WithoutMember(string instanceId, out IReadOnlyList<string> removedNetworks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var removed = new List<string>();
        var kept = new List<Binding>(_bindings.Length);

        foreach (var binding in _bindings)
        {
            var involved = binding.Network.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal));
            if (involved)
            {
                removed.Add(binding.Network.Name);
            }
            else
            {
                kept.Add(binding);
            }
        }

        removedNetworks = removed;
        return removed.Count == 0 ? this : new BindingSet([.. kept]);
    }
}
