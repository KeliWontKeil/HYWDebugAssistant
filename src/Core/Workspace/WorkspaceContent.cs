using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk;

namespace HYWDebugAssistant.Core.Workspace;

/// <summary>
/// 抽象工作区的内容：**实例 + 网络 + 布局**（主文档 §9.1：无容器、无多开、无生命周期）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>本类型是"当前现场"的唯一内容持有者，**只由管理线程访问**（变更入口串行化，§6.2 无全局锁）；</item>
///   <item>持久化不在这里：现场物化为**组合件**由用户在保存时触发（§9.2，M1-8）；</item>
///   <item>布局数据（Layout）见 <see cref="Layout"/>（M1-8 加入：位置 + 端口几何）；</item>
///   <item>**占位实例**（<see cref="Placeholders"/>）：加载组合件时定义缺失/版本不兼容的降级态——保留声明但不活动（§9.3）。</item>
/// </list>
/// </remarks>
public sealed class WorkspaceContent
{
    private readonly Dictionary<string, ModuleInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlaceholderInstance> _placeholders = new(StringComparer.Ordinal);
    private readonly List<Network> _networks = [];
    private readonly Dictionary<string, int> _addOrder = new(StringComparer.Ordinal);
    private int _addCounter;

    /// <summary>当前全部实例（**不含占位实例**）。</summary>
    public IReadOnlyCollection<ModuleInstance> Instances => _instances.Values;

    /// <summary>当前全部占位实例（模块定义缺失/版本不兼容；通常为空）。</summary>
    public IReadOnlyCollection<PlaceholderInstance> Placeholders => _placeholders.Values;

    /// <summary>当前全部网络（含停流的）。</summary>
    public IReadOnlyList<Network> Networks => _networks;

    /// <summary>布局数据（位置 / 尺寸 / 视图开关 / 端口几何）。</summary>
    public WorkspaceLayout Layout { get; } = new();

    /// <summary>实例数（不含占位）。</summary>
    public int InstanceCount => _instances.Count;

    /// <summary>占位实例数。</summary>
    public int PlaceholderCount => _placeholders.Count;

    /// <summary>查找实例。</summary>
    public bool TryGetInstance(string instanceId, out ModuleInstance? instance) =>
        _instances.TryGetValue(instanceId, out instance);

    /// <summary>查找占位实例。</summary>
    public bool TryGetPlaceholder(string instanceId, out PlaceholderInstance? placeholder) =>
        _placeholders.TryGetValue(instanceId, out placeholder);

    /// <summary>该 ID 是否已被占用（实例或占位）。</summary>
    public bool IsInstanceIdTaken(string instanceId) =>
        _instances.ContainsKey(instanceId) || _placeholders.ContainsKey(instanceId);

    /// <summary>按会话内 ID（Defintion 标识）列出实例。</summary>
    public IReadOnlyList<ModuleInstance> FindByModuleId(ModuleId moduleId) =>
        [.. _instances.Values.Where(i => i.Definition.Id == moduleId)];

    /// <summary>
    /// 实例的**加入顺序**（R1：快速建网的主动方 = 最新加入的实例）。
    /// </summary>
    public int GetAddOrder(string instanceId) =>
        _addOrder.TryGetValue(instanceId, out var order) ? order : -1;

    /// <summary>加入实例（由 ChangeSet 提交阶段调用）。</summary>
    internal bool AddInstance(ModuleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (!_instances.TryAdd(instance.InstanceId, instance))
        {
            return false;
        }

        _addOrder[instance.InstanceId] = ++_addCounter;
        return true;
    }

    /// <summary>移除实例（由 ChangeSet 提交阶段调用；网络的级联在应用器里完成）。</summary>
    internal bool RemoveInstance(string instanceId) => _instances.Remove(instanceId) && RemoveAddOrder(instanceId);

    /// <summary>加入占位实例（加载组合件时定义缺失/版本不兼容）。</summary>
    internal bool AddPlaceholder(PlaceholderInstance placeholder)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        return _placeholders.TryAdd(placeholder.InstanceId, placeholder);
    }

    /// <summary>移除占位实例。</summary>
    internal bool RemovePlaceholder(string instanceId) => _placeholders.Remove(instanceId);

    /// <summary>加入网络。</summary>
    internal void AddNetwork(Network network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _networks.Add(network);
    }

    /// <summary>按网络名移除网络。</summary>
    internal bool RemoveNetwork(string networkName)
    {
        ArgumentNullException.ThrowIfNull(networkName);

        var index = _networks.FindIndex(n => string.Equals(n.Name, networkName, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _networks.RemoveAt(index);
        return true;
    }

    /// <summary>按对象引用移除网络（匿名网络无名字，用引用标识）。</summary>
    internal bool RemoveNetwork(Network network) => _networks.Remove(network);

    /// <summary>按对象引用替换网络（成员重锚用；**绑定快照要求网络不可变**，故换新对象）。</summary>
    internal bool ReplaceNetwork(Network existing, Network replacement)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);

        var index = _networks.IndexOf(existing);
        if (index < 0)
        {
            return false;
        }

        _networks[index] = replacement;
        return true;
    }


    private bool RemoveAddOrder(string instanceId) => _addOrder.Remove(instanceId);
}
