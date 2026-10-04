using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Hosting.Registry;

namespace HYWDebugAssistant.Core.Changes;

/// <summary>
/// 组合操作（变更单元）——主文档 §6.2 的操作集。
/// </summary>
/// <remarks>
/// 单操作**原子**（完整应用或回滚一致）；多步复合 = 一次 <see cref="ChangeSet"/>
/// （整体校验 → 一次性应用与增量重建 → 任一步失败整体拒绝）。
/// </remarks>
public abstract record ChangeOperation;

/// <summary>加入模块实例（Add Module）。</summary>
/// <param name="InstanceId">实例 ID（工作现场内唯一）。</param>
/// <param name="Definition">模块定义。</param>
public sealed record AddModuleOperation(string InstanceId, ModuleDefinition Definition) : ChangeOperation;

/// <summary>移除模块实例（Remove Module；级联清理其连接与绑定）。</summary>
/// <param name="InstanceId">实例 ID。</param>
public sealed record RemoveModuleOperation(string InstanceId) : ChangeOperation;

/// <summary>启用 / 禁用实例（暂停或恢复数据流，保留实例、拓扑与配置）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Enabled">true = 启用，false = 禁用。</param>
public sealed record SetModuleEnabledOperation(string InstanceId, bool Enabled) : ChangeOperation;

/// <summary>建立网络（手动连接；<paramref name="Name"/> 为空表示匿名直连）。</summary>
/// <param name="Source">源成员（必须是发送口）。</param>
/// <param name="Targets">宿成员（必须是接收口；1:N 多播）。</param>
/// <param name="Name">网络名（为空 = 匿名网络）。</param>
/// <param name="AllowUnverifiedTypeMatch">
/// 用户是否已**显式确认**允许"**数据类型标识不同**"的强连：默认 <see langword="false"/> = 拒绝
/// （框架**无资格**判定不同标识是否同一种量，即使结构相同）；显式确认后放行，但**结构仍必须覆盖**，并留一条警示说明。
/// </param>
public sealed record ConnectNetworkOperation(
    NetworkMember Source,
    IReadOnlyList<NetworkMember> Targets,
    string? Name = null,
    bool AllowUnverifiedTypeMatch = false) : ChangeOperation;

/// <summary>断开网络（按名；匿名网络用 <see cref="NetworkMember"/> 定位的变体由运行时处理）。</summary>
/// <param name="NetworkName">网络名。</param>
public sealed record DisconnectNetworkOperation(string NetworkName) : ChangeOperation;

/// <summary>更新实例配置（触发模块的配置变更通知；不重建绑定）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Configuration">新的配置对象。</param>
public sealed record UpdateConfigurationOperation(string InstanceId, object Configuration) : ChangeOperation;

/// <summary>
/// **批量**更新实例配置（同一实例的多个配置类型一次写入）——无 UI 模块的配置兜底路径用；
/// 与单条版本一样经变更入口，**整体应用或整体拒绝**，且只触发**一次**配置变更通知。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Configurations">新的配置对象集合。</param>
public sealed record UpdateConfigurationsOperation(string InstanceId, IReadOnlyList<object> Configurations) : ChangeOperation;

/// <summary>更新模块视图布局（位置 / 尺寸 / 视图开关）——布局数据随组合件保存（§9.2）。</summary>
/// <param name="Layout">新的布局值（按实例 ID 覆盖）。</param>
public sealed record UpdateModuleLayoutOperation(Api.ModuleLayoutView Layout) : ChangeOperation;

/// <summary>更新端口几何（端口在边缘上的位置与锁定态；端口几何与模块参数同层，C5-10）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Geometry">新的端口几何。</param>
public sealed record UpdatePortGeometryOperation(string InstanceId, Api.PortGeometryView Geometry) : ChangeOperation;

/// <summary>
/// 设置端口的**网络标签**（用户可见的唯一建网机制）——
/// 提交时按"**同名标签即连**"重算命名网络；空标签 = 退出该网络。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">口名。</param>
/// <param name="NetworkName">网络标签（空 = 清除）。</param>
public sealed record UpdatePortNetworkOperation(string InstanceId, string PortName, string? NetworkName) : ChangeOperation;

/// <summary>
/// 投递一个**模块命令**：用户点了模块视图里的命令按钮。
/// </summary>
/// <remarks>
/// 提交阶段**只入队**（模块的人机操作收件箱），命令在**模块线程**上被执行 —— 框架不跨线程直接调模块。
/// 未知命令在 Prepare 阶段即被拒绝（不静默）。
/// </remarks>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="CommandName">命令名（视图声明中登记）。</param>
public sealed record SendCommandOperation(string InstanceId, string CommandName) : ChangeOperation;

/// <summary>
/// 写入一个**文本输入值**：用户在某输入元素上回车/失焦提交。
/// </summary>
/// <remarks>与命令同样**只入队**；模块在自己的线程上解析文本（非法值由模块上报 Issue）。</remarks>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Source">输入元素绑定的数据源名。</param>
/// <param name="Text">用户输入文本。</param>
public sealed record SubmitInputOperation(string InstanceId, string Source, string Text) : ChangeOperation;

/// <summary>
/// **还原模块配置**到模块默认值【用户第十一轮反馈 · 模块窗口的「还原」按钮】。
/// </summary>
/// <remarks>
/// 语义：**丢弃该实例已保存的配置对象**，模块下次读取时按"读即登记"重新登记默认值；
/// 随后照常走配置变更通知路径（模块在自己的线程上收到 <c>OnConfigurationChanged</c>）。
/// </remarks>
/// <param name="InstanceId">实例 ID。</param>
public sealed record ResetModuleConfigurationOperation(string InstanceId) : ChangeOperation;
