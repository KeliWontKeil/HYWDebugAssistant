namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// 口视图（只读 DTO）——公开面**只出现契约类型与自有 DTO**，不出现框架内部类型（AR-16）。
/// </summary>
/// <param name="Name">口名。</param>
/// <param name="Direction">方向（<c>Send</c> / <c>Receive</c>）。</param>
/// <param name="ContractSizeInBytes">契约定长尺寸。</param>
/// <param name="IsConnected">是否已加入网络。</param>
/// <param name="IsStalled">是否停流（合法状态）。</param>
/// <param name="AnchorOffset">端口在所属边缘上的归一化位置（0–1；**端口几何**，随组合件保存）。</param>
/// <param name="IsAnchorLocked">端口位置是否被锁定（锁定 = 模块拉伸时按比例重排，不可拖动）。</param>
/// <param name="NetworkLabel">
/// 端口**网络标签**（ADR-0034：用户可见的唯一建网机制）；同名标签的发送口与接收口自动连成命名网络。
/// </param>
/// <param name="ContractText">
/// 端口**信息类型**的可读描述【新增 · M1-16 / ADR-0039 D4】（如 <c>f32</c> · <c>{seq:u64, value:f32}</c>）。
/// 由 <c>ContractShape.Describe()</c> 渲染；null = 未知（界面显示占位符）。
/// </param>
public sealed record PortView(
    string Name,
    string Direction,
    int ContractSizeInBytes,
    bool IsConnected,
    bool IsStalled,
    double AnchorOffset = 0.5,
    bool IsAnchorLocked = false,
    string? NetworkLabel = null,
    string? ContractText = null)
{
    /// <summary>
    /// **该口声明的类型身份**（`标识@版本`；未声明或引用不可解析 ⇒ <see langword="null"/>）【ADR-0048 R2】。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>与 <see cref="ContractText"/> 的分工：**本字段 = 身份**（用于配对/色板映射/角色键），
    ///     <see cref="ContractText"/> = **呈现文本**（给人看）；</item>
    ///   <item>**为什么不能用呈现文本代替**：`f32` 这个文本无法区分"声明了 `hyw/f32@1`"与"声明了别的标识但形状相同"，
    ///     而 ADR-0051 明确二者**不是同一种量** ⇒ 用文本会把两种角色错误合并；</item>
    ///   <item>未声明类型的旧模块：身份回落为**结构描述**（= <see cref="ContractText"/>）。</item>
    /// </list>
    /// </remarks>
    public string? TypeKey { get; init; }
}


/// <summary>
/// **角色视图**（模块的一个**功能分块**）【ADR-0052】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>角色 = 模块声明的功能分块</b>：一种数据类型 + 它的输入口/输出口（通常各 1）；
///     与**网络标签/端口标记**（数据接口的抽象）**不同层级** —— 本视图不表达网络；</item>
///   <item><b>`m/n` 是框架派生量</b>（不是模块自报）：<see cref="InboundRoleCount"/> = **输入侧连接的对端角色数**、
///     <see cref="OutboundRoleCount"/> = **输出侧连接的对端角色数**；去重键 =（对端实例, 对端角色）；
///     **没有该方向的口 ⇒ <see langword="null"/>**（呈现层写 `--`）；**有口但没连 ⇒ 0**（两者含义不同）；</item>
///   <item><b>只用于呈现</b>：不参与建网、兼容、停流、投递与计量，也不进组合件（ADR-0048 R9）。</item>
/// </list>
/// </remarks>
/// <param name="Key">角色键（= `实例ID/角色Id`，稳定、可供 UI 配对）。</param>
/// <param name="RoleId">角色标识（模块内唯一）。</param>
/// <param name="DisplayName">功能名（空 ⇒ 呈现层回退为类型短名）。</param>
/// <param name="TypeKey">类型身份（`标识@v版本`；色板键，跨模块一致）。</param>
/// <param name="TypeText">类型呈现文本（`{seq:u64, value:f32}` 之类，给人看）。</param>
/// <param name="Inputs">输入口名（可为空 = 纯源分块）。</param>
/// <param name="Outputs">输出口名（可为空 = 纯汇分块）。</param>
/// <param name="InboundRoleCount">输入侧连接的**对端角色数**（`m`；无输入口 ⇒ null）。</param>
/// <param name="OutboundRoleCount">输出侧连接的**对端角色数**（`n`；无输出口 ⇒ null）。</param>
/// <param name="SemanticText">
/// 该类型的**语义标签**（模块声明的可读注记，多个用 ` / ` 连接；未声明 ⇒ 空串）。
/// **只呈现**：框架不解释语义，只把它交给界面显示。
/// </param>
/// <param name="NominalRateHz">该类型的**标称速率**（Hz；未声明 ⇒ null）——有了它才能看"掉速"。</param>
/// <param name="RateBytesPerSecond">
/// **该角色自身的搬运字节速率**（B/s）【ADR-0053 · 用户第三十四轮】：取该角色**产出侧**所承载网络的
/// `BusByteRate` 之和（有输出口 ⇒ 输出网络；纯汇分块 ⇒ 输入网络）。这是**角色维度**的量
/// （"这块功能自己在跑多快"），与端口标记上的**通路速率**（"这条网络在跑多快"）是两个维度——
/// 两者读**同一份**框架诊断，但表达的对象不同（用户裁定：不能因为底层同源就当成一个维度）。
/// </param>
/// <param name="Delivered">该角色产出侧承载网络的**投递单元数**合计。</param>
/// <param name="Dropped">该角色产出侧承载网络的**传输丢弃**合计（不含停流未投递）。</param>
public sealed record RoleView(
    string Key,
    string RoleId,
    string DisplayName,
    string TypeKey,
    string TypeText,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    int? InboundRoleCount,
    int? OutboundRoleCount,
    string SemanticText = "",
    double? NominalRateHz = null,
    double RateBytesPerSecond = 0,
    long Delivered = 0,
    long Dropped = 0);

/// <summary>
/// **角色间数据流视图**（一条线 = 一次"角色 → 角色"的流动）【ADR-0052】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>由**网络成员关系**派生：本角色某个输出口与对端某个输入口之间存在投递 ⇒ 一条流；
///     **多源网络**下每个源是独立对端 ⇒ 各自一条；同（本端角色, 对端实例, 对端角色）**聚合**为一条
///     并给出口对数 <see cref="PortPairCount"/>（`>1` 时呈现层可标注，但**不再画网络名** —— 接口层信息不进叠加层）；</item>
///   <item>只读、只用于呈现；线的几何由 UI 决定（两端锚点 = 各自角色方块的输出/输入侧边）。</item>
/// </list>
/// </remarks>
/// <param name="LocalRoleKey">本端角色键。</param>
/// <param name="LocalIsSend">本端是**发送侧**（数据从本角色的输出侧流出）。</param>
/// <param name="PeerInstanceId">对端实例 ID。</param>
/// <param name="PeerRoleKey">对端角色键。</param>
/// <param name="PortPairCount">聚合的口对数（该角色对之间实际建立投递的口对个数）。</param>
/// <param name="NetworkKeys">
/// 承载这些连接的网络身份键（去重、有序）。**只用于取指标**（速率/投递/丢弃按网络查诊断）——
/// **不要在界面上显示网络名**：网络标签是**接口层**概念，与角色不是同一层级（ADR-0052）。
/// </param>
public sealed record RoleFlowView(
    string LocalRoleKey,
    bool LocalIsSend,
    string PeerInstanceId,
    string PeerRoleKey,
    int PortPairCount,
    IReadOnlyList<string> NetworkKeys);

/// <summary>
/// **模块内部处理顺序视图**（`角色 → 角色`）【ADR-0052】。
/// </summary>
/// <remarks>来源 = 模块**声明**（`DeclareFlow`）；**零声明 ⇒ 空**（框架不猜）。只呈现，不参与任何判定，不进组合件。</remarks>
/// <param name="FromRoleKey">起点角色键。</param>
/// <param name="ToRoleKey">终点角色键。</param>
/// <param name="Label">可选注记（v1 不绘制）。</param>
public sealed record RoleOrderView(string FromRoleKey, string ToRoleKey, string? Label);

/// <summary>框架状态名（公开面字面量，供 UI 比对而不必硬编码字符串）。</summary>
public static class RuntimeStateNames
{
    /// <summary>占位实例状态名（模块定义缺失 / 版本不兼容：**保留声明但不活动**，§9.3）。</summary>
    public const string Placeholder = "Missing";
}

/// <summary>模块实例视图。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="ModuleId">模块标识字面量（作者/模块名/版本）。</param>
/// <param name="DisplayName">目录展示名。</param>
/// <param name="State">框架状态（<c>Registered</c>/<c>Active</c>/<c>Disabled</c>/<c>Removed</c>；占位实例为 <see cref="RuntimeStateNames.Placeholder"/>）。</param>
/// <param name="HasView">是否声明了视图（false = 无 UI 模块）。</param>
/// <param name="Ports">口视图。</param>
/// <param name="IsPlaceholder">是否占位实例（模块定义缺失/版本不兼容：无活动、只保留配置与网络成员声明，§9.3）。</param>
/// <param name="View">视图声明（<see langword="null"/> = 无 UI 模块；框架按声明渲染，模块不提供 View 类型）。</param>
public sealed record ModuleInstanceView(
    string InstanceId,
    string ModuleId,
    string DisplayName,
    string State,
    bool HasView,
    IReadOnlyList<PortView> Ports,
    bool IsPlaceholder = false,
    HYWDebugAssistant.Sdk.Views.ViewDeclaration? View = null);

/// <summary>网络视图。</summary>
/// <param name="Name">网络名（匿名网络为空串）。</param>
/// <param name="IsAnonymous">是否匿名（直连）。</param>
/// <param name="IsAutoGenerated">是否由快速建网自动生成。</param>
/// <param name="IsStalled">
/// 是否停流（**全部源未锚定**，加载降级态）。**部分源未锚定不算停流**（ADR-0050 M5）。
/// </param>
/// <param name="Source">
/// **主源**（`实例ID.口名` = <see cref="Sources"/> 的第一个；**停流时为首个未锚定的声明源**）。
/// </param>
/// <param name="Targets">在场（已锚定）的宿成员列表。</param>
/// <param name="UnresolvedMembers">
/// **悬空的宿成员**（加载重锚失败的口，<c>实例ID.口名</c>）：保留声明 + 不建立绑定，其余部分照常运行（§9.3）。
/// </param>
public sealed record NetworkView(
    string Name,
    bool IsAnonymous,
    bool IsAutoGenerated,
    bool IsStalled,
    string Source,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string>? UnresolvedMembers = null)
{
    /// <summary>
    /// **网络身份键**（`源 → 宿集合`，宿已排序）——与 <see cref="BindingDiagnosticView.Key"/> **一一对应**。
    /// </summary>
    /// <remarks>
    /// **为什么必须有它**【P40】：网络名**不唯一** —— 匿名网络（"接触且对齐"自动建网）的名字是**空串**，
    /// 现场同时存在两条匿名网络时，用 <c>Name</c> 当字典键会直接抛
    /// <c>ArgumentException: An item with the same key has already been added. Key: </c>（**空键**）。
    /// 故：**凡"把网络配对/索引"的地方，一律用本键，不要用 Name**。
    /// **【ADR-0050 M5】** 键含**全部声明的源**（网络身份 = 声明的源集合 + 宿集合；两条都停流的网络
    /// 不得因"在场源为空"而撞键）；逐源的绑定/诊断键另见
    /// <see cref="BindingDiagnosticView.Key"/>（= `单源 → 宿子集`）。
    /// </remarks>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// **全部声明的源成员**（`实例.口名`；**在场的在前、悬空的后**，各组内有序）
    /// **【ADR-0050 M2】多源是一等组合**：同名标签的多个发送端同属一条网络。
    /// </summary>
    /// <remarks>
    /// 单源网络为 1 个元素；位置参数 `Source` = 主源（= 本列表第一个）保留兼容。
    /// **在场与否**：<see cref="IsStalled"/> 回答"还有没有活着的源"，逐源归属看
    /// <see cref="BindingDiagnosticView.Key"/>（每个在场的源一条诊断，ADR-0050 M7）。
    /// </remarks>
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>
/// 模块视图布局（**纯数值**：位置与尺寸，随组合件保存）。
/// </summary>
/// <remarks>
/// "是否拼为一组"由位置派生（C5-1）：只保存位置，组关系在加载/拖动后由几何重新推导，**不单独存储**。
/// </remarks>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="X">画布坐标 X（像素）。</param>
/// <param name="Y">画布坐标 Y（像素）。</param>
/// <param name="Width">视图宽度（像素）。</param>
/// <param name="Height">视图高度（像素）。</param>
/// <param name="IsViewVisible">模块视图是否开启（关 = 仍在运行，仅隐藏表现层）。</param>
public sealed record ModuleLayoutView(
    string InstanceId,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsViewVisible = true);

/// <summary>端口几何（用户在边缘上调整过的位置；未调整时为默认值）。</summary>
/// <param name="PortName">口名。</param>
/// <param name="Offset">在所属边缘上的归一化位置（0–1）。</param>
/// <param name="IsLocked">是否锁定（锁定后不随模块拉伸重排，也不可拖动）。</param>
/// <param name="Label">
/// **端口网络标签**（ADR-0034：用户可见的唯一建网机制）——
/// 同名标签的发送口与接收口**自动连成一条命名网络**；空 = 未命名（不参与命名网络）。
/// </param>
public sealed record PortGeometryView(
    string PortName,
    double Offset = 0.5,
    bool IsLocked = false,
    string? Label = null);

/// <summary>
/// 订阅数据源视图（UI 交接，Q4-4）：模块声明了什么、缓冲是什么形态、写入与覆盖计数。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="DataSourceName">数据源名。</param>
/// <param name="Kind">数据源类型（<c>Value</c>/<c>Sequence</c>/…）。</param>
/// <param name="IsSeries">是否环形缓冲（false = 最新值槽）。</param>
/// <param name="Capacity">容量（样本数）。</param>
/// <param name="SampleSizeInBytes">单样本字节数（尚未写入过为 0）。</param>
/// <param name="ValueType">单样本契约原语名（非数值为 null）。</param>
/// <param name="Written">累计写入样本数。</param>
/// <param name="Overwritten">被覆盖的旧样本数（UI 读取是否跟得上的事实指标）。</param>
/// <param name="Dropped">被丢弃的样本数（尺寸不一致等）。</param>
public sealed record SubscriptionSourceView(
    string InstanceId,
    string DataSourceName,
    HYWDebugAssistant.Sdk.Views.ViewDataSourceKind Kind,
    bool IsSeries,
    int Capacity,
    int SampleSizeInBytes,
    HYWDebugAssistant.Sdk.Contracts.ContractPrimitive? ValueType,
    long Written,
    long Overwritten,
    long Dropped)
{
    /// <summary>
    /// **模块提供的样本解码器**【ADR-0046 D1 ③】：呈现期解码由模块做，框架**只回调**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>非 <see langword="null"/> ⇒ 呈现层**必须**用它解码（框架不内建、不猜）；</item>
    ///   <item><see langword="null"/> + <see cref="ValueType"/> 非空 ⇒ **旧模块过渡回退**：
    ///     呈现层按参考实现（<c>PrimitiveSampleDecoder</c>）解码；</item>
    ///   <item>两者都空 ⇒ 不显示数值，只显示"未声明类型 + 字节数"。</item>
    /// </list>
    /// </remarks>
    public HYWDebugAssistant.Sdk.Contracts.SampleDecoder? Decoder { get; init; }

    /// <summary>该数据源引用的数据类型（未引用 = <see langword="null"/>）。</summary>
    public HYWDebugAssistant.Sdk.Contracts.DataTypeRef? DataType { get; init; }
}

/// <summary>绑定诊断视图（可观测性 L4）。</summary>
/// <param name="NetworkName">网络名（**匿名网络为空串 ⇒ 不可作唯一键**）。</param>
/// <param name="Delivered">累计投递数。</param>
/// <param name="Dropped">累计丢弃数。</param>
/// <param name="LastDataTime">最后数据时间（无数据为 null）。</param>
public sealed record BindingDiagnosticView(
    string NetworkName,
    long Delivered,
    long Dropped,
    DateTimeOffset? LastDataTime)
{
    /// <summary>
    /// **网络身份键**（与 <see cref="NetworkView.Key"/> 一致，可直接配对）。
    /// </summary>
    /// <remarks>
    /// 【P40】**不要用 <see cref="NetworkName"/> 当字典键**：匿名网络名为空串，多条匿名网络会撞键
    /// （实测崩溃：`An item with the same key has already been added. Key: `）。
    /// </remarks>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// **停流未投递**次数【ADR-0046 D8 / **ADR-0050 Q5**】—— 上游挂起/网络停流期间的发送：
    /// **合法状态，不是故障**，因此**不得**与"传输丢弃"混算。
    /// </summary>
    public long DropStalled { get; init; }

    /// <summary>**队列满**导致未被投递的次数（best-effort 丢弃）。</summary>
    public long DropQueueFull { get; init; }

    /// <summary>**无承载**（目标线程/队列不存在）导致未被投递的次数。</summary>
    public long DropNoCarrier { get; init; }

    /// <summary>**载荷非法**（长度小于契约尺寸）导致未被投递的次数。</summary>
    public long DropInvalidPayload { get; init; }

    /// <summary>
    /// **传输丢弃**合计【ADR-0046 D8 三类之一】= 队列满 + 无承载 + 载荷非法。
    /// </summary>
    /// <remarks>
    /// **三类丢弃**必须分开看：**停流未投递**（<see cref="DropStalled"/>，合法状态）·
    /// **传输丢弃**（本属性）· **模块内部丢弃**（见 <see cref="SubscriptionSourceView.Overwritten"/> /
    /// <see cref="SubscriptionSourceView.Dropped"/>——那是模块自己覆盖/丢弃样本的口径，**不并入**网络口径）。
    /// </remarks>
    public long DropTransport { get; init; }

    /// <summary>该源**发布的数据报数**（每个数据报一次；多播不放大）——"单元速率"的分子。</summary>
    public long Published { get; init; }

    /// <summary>本条连接对应的**网络身份键**（与 <see cref="NetworkView.Key"/> 一致，便于把逐源行归到同一网络）。</summary>
    public string NetworkKey { get; init; } = string.Empty;

    /// <summary>
    /// **单元速率**（数据报 / 秒；**按源计**）【ADR-0046 D8 第 1 档】——由框架的速率计给出，UI **不得**自行差分估算。
    /// </summary>
    public double UnitRateHz { get; init; }

    /// <summary>
    /// **搬运字节速率**（字节 / 秒；**按宿合计**）【第 2 档】= 投递次数 × 契约尺寸（含自描述与对齐填充）。
    /// </summary>
    /// <remarks>按**每一个宿**计的搬运字节速率见 <see cref="PortQueueDepthView.BusByteRate"/>（每口一条队列）。</remarks>
    public double BusByteRate { get; init; }

    /// <summary>
    /// **有效载荷字节速率**（字节 / 秒；**按源计**）【第 3 档】——用类型声明的 `PayloadSizeInBytes`（缺省 = 契约尺寸）。
    /// </summary>
    public double PayloadByteRate { get; init; }

    /// <summary>**样本速率**（样本 / 秒；按源计）【第 4 档】= 单元速率 × `SamplesPerUnit`。</summary>
    public double SampleRateHz { get; init; }

    /// <summary>**通道样本速率**（通道样本 / 秒；按源计）= 样本速率 × `ChannelCount`。</summary>
    public double ChannelSampleRateHz { get; init; }

    /// <summary>
    /// 类型声明的**标称速率**（Hz；未声明 = <see langword="null"/>）。
    /// </summary>
    /// <remarks>**是否掉速 = 实测（<see cref="UnitRateHz"/>）vs 标称**——不做"实际 vs 框架"的单数字对比。</remarks>
    public double? NominalRateHz { get; init; }
}

/// <summary>
/// **每 (实例, 口) 队列的深度与吞吐**（可观测性 L4；ADR-0050 Q2 + ADR-0046 D8"水位"）。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">接收口名。</param>
/// <param name="Depth">当前队列深度（单元数）。</param>
/// <param name="Capacity">容量（单元数；**按口计**）。</param>
/// <param name="Enqueued">该口累计入队数（= **按宿计的投递次数**）。</param>
/// <param name="Dropped">该口累计丢弃数（队满）。</param>
public sealed record PortQueueDepthView(
    string InstanceId,
    string PortName,
    int Depth,
    int Capacity,
    long Enqueued,
    long Dropped)
{
    /// <summary>
    /// **该口的搬运字节速率**（字节 / 秒；**按此宿计**，第 2 档）——由框架速率计给出。
    /// </summary>
    public double BusByteRate { get; init; }

    /// <summary>该口的**入队速率**（单元 / 秒；按此宿计）。</summary>
    public double UnitRateHz { get; init; }

    /// <summary>队列水位占比（0–1；容量为 0 时为 0）。</summary>
    public double Fill => Capacity > 0 ? Math.Clamp((double)Depth / Capacity, 0, 1) : 0;
}

/// <summary>Issue 视图。</summary>
/// <param name="Sequence">序号。</param>
/// <param name="ObjectPath">归属对象路径。</param>
/// <param name="Category">类别（七类之一的名字）。</param>
/// <param name="Severity">严重度。</param>
/// <param name="Message">面向用户的描述。</param>
/// <param name="Timestamp">发生时间。</param>
/// <param name="Source">来源（框架判定 / 模块报告）——Issue 面板按来源过滤用。</param>
public sealed record IssueView(
    long Sequence,
    string ObjectPath,
    string Category,
    string Severity,
    string Message,
    DateTimeOffset Timestamp,
    string Source = "Framework");

/// <summary>模块目录项（可用模块清单）。</summary>
/// <param name="ModuleId">模块标识字面量。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Capabilities">能力声明（如 <c>Network, File</c>）。</param>
/// <param name="SourceDirectory">装入该模块的来源根（内置包目录或用户配置的根）；未知为 null。</param>
/// <param name="NeedsRestart">
/// 是否"在本次进程启动后被重新构建过"（ADR-0036 D4）：为 <see langword="true"/> 时该包**必须重启应用**才生效
/// （程序集已载入且不可卸载；框架**不实现**热重载）。
/// </param>
/// <param name="Tags">
/// 模块声明的**标签**（`ModuleDefinitionAttribute.Tags`）【新增 · M1-16 后】——界面据它做**分类**与搜索；
/// 不声明标签的模块归入"其他"。空/未知为 <see langword="null"/>。
/// </param>
public sealed record ModuleCatalogEntryView(
    string ModuleId,
    string DisplayName,
    string Description,
    string Capabilities,
    string? SourceDirectory = null,
    bool NeedsRestart = false,
    IReadOnlyList<string>? Tags = null);

/// <summary>
/// **建网候选**（ADR-0037）：发送端 → 接收端，**即将**因"接触且对齐"而连上。
/// </summary>
/// <param name="SourceLiteral">发送端口字面量（<c>实例ID.口名</c>）。</param>
/// <param name="TargetLiteral">接收端口字面量（<c>实例ID.口名</c>）。</param>
public sealed record ConnectionCandidateView(string SourceLiteral, string TargetLiteral);

/// <summary>
/// **模块来源根的扫描结果**（ADR-0036 D1）——供「模块清单」弹窗做诊断：
/// "到底扫了哪些目录、每个目录发生了什么"。
/// </summary>
/// <param name="Directory">根目录路径。</param>
/// <param name="Exists">该根是否存在（不存在 = 跳过并提示）。</param>
/// <param name="PackagesFound">扫到的模块包目录数（含失败的）。</param>
/// <param name="SkippedDirectories">被跳过的子目录数（不含模块清单文件）。</param>
/// <param name="FailedPackages">装入失败的包数。</param>
/// <param name="Messages">逐包结果与提示（成功/失败原因/重复/需重启等，面向用户）。</param>
public sealed record ModuleSourceView(
    string Directory,
    bool Exists,
    int PackagesFound,
    int SkippedDirectories,
    int FailedPackages,
    IReadOnlyList<string> Messages);
