using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;

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
    private readonly Dictionary<DataTypeRef, DataTypeDeclaration> _dataTypes;

    internal ModuleInstance(
        string instanceId,
        ModuleDefinition definition,
        IModule module,
        IReadOnlyList<PortInstance> ports,
        IReadOnlyList<DataTypeDeclaration> dataTypes,
        IReadOnlyList<Sdk.Contracts.RoleDeclaration> roles,
        IReadOnlyList<Sdk.Ports.FlowDeclaration> flows,
        IReadOnlyList<Sdk.Views.DisplaySourceDeclaration> displaySources,
        IReadOnlyList<string> inputs,
        IReadOnlyCollection<string> persistentInputs,
        IReadOnlyList<(string Name, string Title)> commands,
        IReadOnlyList<ExternalAccessDeclaration> externalAccesses,
        Context.ModuleContext context)
    {
        InstanceId = instanceId;
        Definition = definition;
        Module = module;
        Ports = ports;
        DataTypes = dataTypes;
        Roles = roles;
        Flows = flows;
        DisplaySources = displaySources;
        Inputs = inputs;
        PersistentInputs = persistentInputs;
        Commands = commands;
        ExternalAccesses = externalAccesses;
        Context = context;

        // 机械镜像：按（标识, 版本）索引，供口/数据源的引用解析（同键重复时以**先声明者**为准，重复已由工厂报 Issue）
        _dataTypes = new Dictionary<DataTypeRef, DataTypeDeclaration>(dataTypes.Count);

        foreach (var type in dataTypes)
        {
            _ = _dataTypes.TryAdd(type.Reference, type);
        }
    }

    /// <summary>实例 ID（工作现场内唯一、稳定）。</summary>
    public string InstanceId { get; }

    /// <summary>所引用的模块定义（Definition ≠ Instance）。</summary>
    public ModuleDefinition Definition { get; }

    /// <summary>模块对象（实现 <see cref="IModule"/>）。</summary>
    public IModule Module { get; }

    /// <summary>口实例表（成员实例表的一部分）。</summary>
    public IReadOnlyList<PortInstance> Ports { get; }

    /// <summary>
    /// 该实例**声明的数据类型**（按声明顺序）：框架只持**机械镜像**，不理解语义。
    /// </summary>
    public IReadOnlyList<DataTypeDeclaration> DataTypes { get; }

    /// <summary>
    /// 该实例**声明的角色**（功能分块：一种数据类型 + 其输入口/输出口）。
    /// </summary>
    /// <remarks>
    /// **只用于呈现**（叠加层画功能块与 `m/n`）：不参与建网/兼容/停流/投递/计量，**不进组合件**。
    /// 已由工厂校验：口存在且方向相符、一个口最多属于一个角色、类型可在本模块解析、Id 唯一。
    /// </remarks>
    public IReadOnlyList<Sdk.Contracts.RoleDeclaration> Roles { get; }

    /// <summary>
    /// 该实例声明的**内部处理顺序**（`角色 → 角色`，已校验两端角色存在）。
    /// </summary>
    /// <remarks>**只用于呈现**（叠加层画模块内部的细实线）：不参与任何判定，**不进组合件**。零声明 ⇒ 不画。</remarks>
    public IReadOnlyList<Sdk.Ports.FlowDeclaration> Flows { get; }

    /// <summary>
    /// 该实例声明的**显示源**（框架据此建订阅缓冲；界面按名字读）。
    /// </summary>
    /// <remarks>
    /// 未在此登记的名字被写入时丢弃并只提示一次 —— 于是"声明漏了一条"表现为
    /// **一条明确的提示**，而不是"界面永远空白"。
    /// </remarks>
    public IReadOnlyList<Sdk.Views.DisplaySourceDeclaration> DisplaySources { get; }

    /// <summary>该实例声明的**输入源名**（提交闸门：未登记的名字拒收并报 Issue）。</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>声明为持久化的输入源名（随组合件/工作区保存）。</summary>
    public IReadOnlyCollection<string> PersistentInputs { get; }

    /// <summary>该实例声明的**命令**（名字 + 诊断用标题；触发闸门据此判定）。</summary>
    public IReadOnlyList<(string Name, string Title)> Commands { get; }

    /// <summary>
    /// 该实例声明的**外部接入**（已由工厂校验：名字唯一、适配件与参数类型可实例化）。
    /// </summary>
    /// <remarks>框架据此创建会话；模块只通过 <c>IModuleContext.Access</c> 发请求与读写。</remarks>
    public IReadOnlyList<ExternalAccessDeclaration> ExternalAccesses { get; }

    /// <summary>
    /// 本实例的**视图工厂类型**（模块自带视图）；<see langword="null"/> = 无自绘界面（窗口只显示身份与状态）。
    /// </summary>
    public Type? ViewType => Definition.ViewFactory;


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

    /// <summary>按（标识, 版本）查找已声明的数据类型（未声明 = <see langword="null"/> ⇒ 兼容判定回退结构比较）。</summary>
    /// <param name="reference">类型引用。</param>
    public DataTypeDeclaration? FindDataType(DataTypeRef reference) =>
        _dataTypes.TryGetValue(reference, out var type) ? type : null;

    /// <inheritdoc />
    public override string ToString() => $"{InstanceId}@{Definition.Id}";
}
