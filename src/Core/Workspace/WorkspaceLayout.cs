using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Core.Workspace;

/// <summary>
/// 布局数据（Layout）：各模块视图的**位置/尺寸/视图开关**与**端口几何**——主文档 §9.2、C5-1、C5-10。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只保存位置</b>：拼接组由位置派生，**不作为独立信息存储**（C5-1）；</item>
///   <item><b>归属</b>：端口几何与模块参数同层（同属实例级、随组合件保存，C5-10）；</item>
///   <item><b>加载可忽略</b>：布局缺失不影响运行（§9.2"加载时可忽略布局"）；</item>
///   <item><b>线程</b>：只由管理线程（变更入口）访问；投递路径**不读布局**。</item>
///   <item><b>数值类型</b>：布局是纯数值，公开面 DTO（<see cref="ModuleLayoutView"/>）即存储形态——
///     不引入零价值的映射层。</item>
/// </list>
/// </remarks>
public sealed class WorkspaceLayout
{
    private readonly Dictionary<string, ModuleLayoutView> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, PortGeometryView>> _ports = new(StringComparer.Ordinal);

    /// <summary>已摆放的模块视图（未摆放的实例不在其中）。</summary>
    public IReadOnlyList<ModuleLayoutView> Modules => [.. _modules.Values];

    /// <summary>已摆放的模块视图数。</summary>
    public int Count => _modules.Count;

    /// <summary>取某实例的布局（未摆放时为 <see langword="false"/>；UI 自行取默认位）。</summary>
    public bool TryGetModule(string instanceId, out ModuleLayoutView layout) =>
        _modules.TryGetValue(instanceId, out layout!);

    /// <summary>设置某实例的布局（覆盖）。</summary>
    internal void SetModule(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _modules[layout.InstanceId] = layout;
    }

    /// <summary>取某实例的全部端口几何（未调整过的端口不在其中）。</summary>
    public IReadOnlyList<PortGeometryView> GetPorts(string instanceId) =>
        _ports.TryGetValue(instanceId, out var ports) ? [.. ports.Values] : [];

    /// <summary>取某端口的几何（未调整过时返回默认值：居中、未锁定）。</summary>
    public PortGeometryView GetPort(string instanceId, string portName) =>
        _ports.TryGetValue(instanceId, out var ports) && ports.TryGetValue(portName, out var geometry)
            ? geometry
            : new PortGeometryView(portName);

    /// <summary>
    /// 取某端口的几何，并告知**用户是否调整过它**。
    /// </summary>
    /// <remarks>
    /// 用途：包内 `view.json` 声明的 `ports[].default.offset` / `label` 只是"**用户没动过时的缺省**"，
    /// 所以消费侧必须能区分"有记录"与"没记录"——<see cref="GetPort"/> 把两者都归成缺省值，区分不开。
    /// </remarks>
    /// <param name="instanceId">实例 id。</param>
    /// <param name="portName">口名。</param>
    /// <param name="geometry">取到的几何（没有记录时为缺省值，不是 <see langword="null"/>）。</param>
    public bool TryGetPort(string instanceId, string portName, out PortGeometryView geometry)
    {
        if (_ports.TryGetValue(instanceId, out var ports) && ports.TryGetValue(portName, out var stored))
        {
            geometry = stored;
            return true;
        }

        geometry = new PortGeometryView(portName);
        return false;
    }

    /// <summary>设置某端口的几何。</summary>
    internal void SetPort(string instanceId, PortGeometryView geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (!_ports.TryGetValue(instanceId, out var ports))
        {
            ports = new Dictionary<string, PortGeometryView>(StringComparer.Ordinal);
            _ports[instanceId] = ports;
        }

        ports[geometry.PortName] = geometry;
    }

    /// <summary>移除实例的布局与端口几何（实例移除时级联清理）。</summary>
    internal bool RemoveInstance(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        _ports.Remove(instanceId);
        return _modules.Remove(instanceId);
    }

    /// <summary>清空（测试与"新现场"用）。</summary>
    internal void Clear()
    {
        _modules.Clear();
        _ports.Clear();
    }
}
