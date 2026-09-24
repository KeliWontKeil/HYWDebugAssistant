using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 模块实例：当前工作现场中实际使用的模块实体——基线 §4.5。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>实例 ID</b>：工作现场内唯一、稳定（连接锚定 <c>实例ID.口名</c>）；由 L4 在"加入模块"时分配。</item>
///   <item><b>创建者</b>：Core（用户动作触发）；<b>归属</b>：工作现场内容。</item>
///   <item><b>持久化</b>：Configuration 随组合件保存；实例本身重建（本类型不持久化）。</item>
/// </list>
/// </remarks>
public sealed class ModuleInstance
{
    internal ModuleInstance(
        string instanceId,
        ModuleDefinition definition,
        IModule module,
        IReadOnlyList<PortInstance> ports,
        ViewDeclaration? view,
        Context.ModuleContext context)
    {
        InstanceId = instanceId;
        Definition = definition;
        Module = module;
        Ports = ports;
        View = view;
        Context = context;
    }

    /// <summary>实例 ID（工作现场内唯一、稳定）。</summary>
    public string InstanceId { get; }

    /// <summary>所引用的模块定义（Definition ≠ Instance）。</summary>
    public ModuleDefinition Definition { get; }

    /// <summary>模块对象（实现 <see cref="IModule"/>）。</summary>
    public IModule Module { get; }

    /// <summary>口实例表（成员实例表的一部分）。</summary>
    public IReadOnlyList<PortInstance> Ports { get; }

    /// <summary>视图声明（<see langword="null"/> = **无 UI 模块**，合法）。</summary>
    public ViewDeclaration? View { get; }

    /// <summary>
    /// 该实例的模块上下文（框架侧使用：L4 运行时要接入真实投递面、取接收处理、置停止信号）。
    /// </summary>
    /// <remarks>模块作者不直接使用本属性——它只经 <c>IModuleContext</c> 暴露服务面。</remarks>
    public Context.ModuleContext Context { get; }

    /// <summary>活动源（模块实现 <c>IActivitySource</c> 时非空）——承载由数据面负责（M1-5）。</summary>
    public IActivitySource? ActivitySource => Module as IActivitySource;

    /// <summary>框架层状态（由生命周期控制器维护）。</summary>
    public ModuleInstanceState State { get; internal set; } = ModuleInstanceState.Registered;

    /// <summary>按名字查找口实例。</summary>
    public PortInstance? FindPort(string name) =>
        Ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => $"{InstanceId}@{Definition.Id}";
}
