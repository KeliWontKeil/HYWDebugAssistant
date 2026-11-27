using HYWDebugAssistant.Sdk.Reporting;

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
/// <param name="IsAnchorFixed">
/// 锚点是否「**固定**」—— 固定 ⇒ **不可拖动**（模块拉伸时按比例重排；`Offset` 归一化，天然如此）。
/// **与「模块窗口锁定」不是一回事**（后者是整个模块不可移动、不可缩放）⇒ 用词一律「固定」。
/// <para>
/// 这里是**已解析的有效值**（G5 · C5-7）：<c>PortGeometryView.IsAnchorFixed</c>（用户设置）优先，
/// 未设置 ⇒ 框架固定项（**锚点默认固定** = 不可拖动）；
/// 解析唯一实现 = <c>Core.Composition.PortGeometryResolution.ResolveAnchorFixed</c>）。
/// 界面只读这一个值 —— **不要再**自己去合流两个来源（那就是"同一事实两个来源"）。
/// </para>
/// </param>
/// <param name="NetworkLabel">
/// 端口**网络标签**（用户可见的唯一建网机制）；同名标签的发送口与接收口自动连成命名网络。
/// </param>
/// <param name="ContractText">
/// 端口**信息类型**的可读描述（如 <c>f32</c> · <c>{seq:u64, value:f32}</c>）。
/// 由 <c>ContractShape.Describe()</c> 渲染；null = 未知（界面显示占位符）。
/// </param>
public sealed record PortView(
    string Name,
    string Direction,
    int ContractSizeInBytes,
    bool IsConnected,
    bool IsStalled,
    double AnchorOffset = 0.5,
    bool IsAnchorFixed = false,
    string? NetworkLabel = null,
    string? ContractText = null)
{
    /// <summary>
    /// 该口的**有效挂载边**（G5 · C5-7）——来自**包内声明**（或用户在画布上拖动后的覆盖）。
    /// </summary>
    /// <remarks>
    /// 取值优先级：**用户在画布上拖动后的覆盖**（`PortGeometryView.Edge`）优先于**包内声明的默认边**
    /// （`frame.ports[].edge`）；**没有惯例回退** —— 每个数据面口都必须在声明里显式给出 edge，
    /// 缺条目 / 缺 edge 按硬伤报（未声明就没有位置，框架不替它猜）；解析见 `PortEdgeResolution`（唯一实现）。
    /// <para>
    /// <b>参与两件事</b>：① **呈现**（标记贴哪条边、横排还是竖排）；② **建网判定**（"接触且对齐"要求端口挂在
    /// **朝向接触面**的那条边上）。**不参与**兼容 / 停流 / 投递 / 计量。
    /// </para>
    /// </remarks>
    public HYWDebugAssistant.Sdk.Ports.PortEdge Edge { get; init; } = HYWDebugAssistant.Sdk.Ports.PortEdge.Left;
    /// <summary>
    /// **该口声明的类型标识**（未声明或引用不可解析 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>与 <see cref="ContractText"/> 的分工：**本字段 = 身份**（用于配对/色板映射/角色键），
    ///     <see cref="ContractText"/> = **呈现文本**（给人看）；</item>
    ///   <item>**为什么不能用呈现文本代替**：`f32` 这个文本无法区分"声明了 `RawBytes`"与"声明了别的标识但形状相同"，
    ///     而二者**不是同一种量** ⇒ 用文本会把两种角色错误合并；</item>
    ///   <item>未声明类型的旧模块：身份回落为**结构描述**（= <see cref="ContractText"/>）。</item>
    /// </list>
    /// </remarks>
    public string? TypeKey { get; init; }
}


/// <summary>
/// **角色视图**（模块的一个**功能分块**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>角色 = 模块声明的功能分块</b>：一种数据类型 + 它的输入口/输出口（通常各 1）；
///     与**网络标签/端口标记**（数据接口的抽象）**不同层级** —— 本视图不表达网络；</item>
///   <item><b>`m/n` 是框架派生量</b>（不是模块自报）：<see cref="InboundRoleCount"/> = **输入侧连接的对端角色数**、
///     <see cref="OutboundRoleCount"/> = **输出侧连接的对端角色数**；去重键 =（对端实例, 对端角色）；
///     **没有该方向的口 ⇒ <see langword="null"/>**（呈现层写 `--`）；**有口但没连 ⇒ 0**（两者含义不同）；</item>
///   <item><b>只用于呈现</b>：不参与建网、兼容、停流、投递与计量，也不进组合件。</item>
/// </list>
/// </remarks>
/// <param name="Key">角色键（= `实例ID/角色Id`，稳定、可供 UI 配对）。</param>
/// <param name="RoleId">角色标识（模块内唯一）。</param>
/// <param name="DisplayName">功能名（空 ⇒ 呈现层回退为类型短名）。</param>
/// <param name="TypeKey">类型标识（色板键，跨模块一致）。</param>
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
/// <param name="PayloadRateBytesPerSecond">
/// **该角色自身的有效载荷字节速率**（B/s）：
/// 取该角色**选定侧每个口所承载的绑定**的 <see cref="BindingDiagnosticView.PayloadByteRate"/> 之和
/// （有输出口 ⇒ 输出侧各口；纯汇分块 ⇒ 输入侧各口）；**同一绑定只计一次**。
/// <para>
/// 速率**按源计**：一条 `源 → N 宿` 绑定的有效字节只在发布时累加一次 ⇒ 多宿/多播**不会放大**。
/// 这与 <see cref="Delivered"/>（绑定级按宿）是两个口径，不可混用。
/// </para>
/// 这是**角色维度**的量（"这块功能自己在跑多快"），与端口标记上的**通路速率**（"这条网络在跑多快"）
/// 是两个维度——两者读**同一份**框架诊断，但表达的对象不同（底层同源不等于同一个维度）。
/// <para>
/// **为什么是「有效载荷」而不是「搬运」**（速率只要有效数据，截断的不应算在有效数据里面；
/// 整个数据包大小速率是底层协议，要知道的肯定是**有效速率**）⇒ 本字段取第 3 档
/// （<see cref="BindingDiagnosticView.PayloadByteRate"/>：逐单元读类型声明的有效长度字段），
/// 不是含补零/对齐填充的 <see cref="BindingDiagnosticView.BusByteRate"/>。
/// </para>
/// </param>
/// <param name="Delivered">
/// 该角色选定侧各口所承载绑定的**投递单元数**合计。
/// **绑定级按宿**：一条 `源 → N 宿` 绑定的 `Delivered` 是 N 份，故本字段会随宿数放大（与速率口径不同）。
/// </param>
/// <param name="Dropped">该角色选定侧各口所承载绑定的**传输丢弃**合计（不含停流未投递）。</param>
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
    double PayloadRateBytesPerSecond = 0,
    long Delivered = 0,
    long Dropped = 0);

/// <summary>
/// **角色间数据流视图**（一条线 = 一次「角色 → 角色」的流动）。
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
/// **不要在界面上显示网络名**：网络标签是**接口层**概念，与角色不是同一层级。
/// </param>
public sealed record RoleFlowView(
    string LocalRoleKey,
    bool LocalIsSend,
    string PeerInstanceId,
    string PeerRoleKey,
    int PortPairCount,
    IReadOnlyList<string> NetworkKeys);

/// <summary>
/// **模块内部处理顺序视图**（`角色 → 角色`）。
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
/// <param name="ViewType">
/// 模块的**视图工厂类型**（<c>[ModuleDefinition(ViewFactory = …)]</c>）；<see langword="null"/> = 无自绘界面。
/// 界面形态由模块自己的 XAML 决定，框架只负责挂载与按帧刷新。
/// </param>
/// <param name="Window">
/// 模块**代码声明**的窗口尺寸（<c>[ModuleDefinition]</c>；<c>0</c> = 未声明，由框架地板兜底）。
/// </param>
public sealed record ModuleInstanceView(
    string InstanceId,
    string ModuleId,
    string DisplayName,
    string State,
    bool HasView,
    IReadOnlyList<PortView> Ports,
    bool IsPlaceholder = false,
    Type? ViewType = null,
    ModuleWindowView? Window = null,
    long WallpaperRevision = 0);

/// <summary>
/// 模块窗口的**声明尺寸**（来自模块的代码声明；<c>0</c> = 未声明，由框架给缺省/地板）。
/// </summary>
/// <param name="Width">默认宽（DIP）。</param>
/// <param name="Height">默认高（DIP）。</param>
/// <param name="MinWidth">最小宽（DIP；0 = 框架地板）。</param>
/// <param name="MinHeight">最小高（DIP；0 = 框架地板）。</param>
public sealed record ModuleWindowView(double Width, double Height, double MinWidth, double MinHeight);

/// <summary>
/// 某个元素**运行时被设定的外观**（经 <c>IRuntimeQuery.GetAppearance</c> 取得）。
/// </summary>
/// <param name="SourceName">元素的数据源名（= 视图声明里该元素的绑定名）。</param>
/// <param name="Outline">边框色（<c>0xAARRGGBB</c>）；<see langword="null"/> = 没设 ⇒ 按主题。</param>
/// <param name="Text">文字色（<c>0xAARRGGBB</c>）；<see langword="null"/> = 没设 ⇒ 按声明 / 主题。</param>
/// <param name="TextSize">文字大小（DIP）；<see langword="null"/> = 没设 ⇒ 按声明 / 主题那一档。</param>
/// <param name="ReadOnly">运行期只读开关；<see langword="null"/> = 没设 ⇒ 按声明那一档。</param>
/// <param name="OutlineRole">边框的**角色词**（相对色）；<see langword="null"/> = 没设。与 <paramref name="Outline"/> 互斥。</param>
/// <param name="TextRole">文字的**角色词**（相对色）；<see langword="null"/> = 没设。与 <paramref name="Text"/> 互斥。</param>
/// <param name="Revision">动作复原信号（每次 <c>ResetInteraction</c> 自增；控件据此把用户刚做的交互复原）。</param>
/// <remarks>
/// <para>
/// <b>它不含语义</b>：这里只有颜色、字号、只读与"复原信号"，**没有**"成功 / 失败"这种判断 ——
/// 它们与"何时变"都是模块的 C# 逻辑（框架不认识"红 = 失败"）。
/// </para>
/// <para>
/// **只呈现**：不参与建网/兼容/停流/投递/计量，**不进组合件**（纯运行期状态）。
/// 生命周期 = 模块自己设、自己清（设 <see langword="null"/> 即回到主题）。
/// </para>
/// </remarks>
public sealed record AppearanceView(
    string SourceName,
    uint? Outline,
    uint? Text,
    double? TextSize = null,
    long Revision = 0,
    bool? ReadOnly = null,
    string? OutlineRole = null,
    string? TextRole = null,
    bool? Enabled = null);

/// <summary>网络视图。</summary>
/// <param name="Name">网络名（匿名网络为空串）。</param>
/// <param name="IsAnonymous">是否匿名（直连）。</param>
/// <param name="IsStalled">
/// 是否停流（**全部源未锚定**，加载降级态）。**部分源未锚定不算停流**。
/// </param>
/// <param name="Source">
/// **主源**（`实例ID.口名` = <see cref="Sources"/> 的第一个；**停流时为首个未锚定的声明源**）。
/// <b>只是兼容访问器：多源场景的成员判定/展示一律用 <see cref="Sources"/>，不得用它。</b>
/// </param>
/// <param name="Targets">在场（已锚定）的宿成员列表。</param>
/// <param name="UnresolvedMembers">
/// **悬空的宿成员**（加载重锚失败的口，<c>实例ID.口名</c>）：保留声明 + 不建立绑定，其余部分照常运行（§9.3）。
/// </param>
public sealed record NetworkView(
    string Name,
    bool IsAnonymous,
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
    /// 键含**全部声明的源**（网络身份 = 声明的源集合 + 宿集合；两条都停流的网络
    /// 不得因"在场源为空"而撞键）；逐源的绑定/诊断键另见
    /// <see cref="BindingDiagnosticView.Key"/>（= `单源 → 宿子集`）。
    /// </remarks>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// **全部声明的源成员**（`实例.口名`；**在场的在前、悬空的后**，各组内有序）
    /// **多源是一等组合**：同名标签的多个发送端同属一条网络。
    /// </summary>
    /// <remarks>
    /// 单源网络为 1 个元素；位置参数 `Source` = 主源（= 本列表第一个）保留兼容。
    /// **成员判定必须用本列表**（不能用位置参数 `Source`：多源时它只代表第一个源）。
    /// **在场与否**：<see cref="IsStalled"/> 回答"还有没有活着的源"，逐源归属看
    /// <see cref="BindingDiagnosticView.Key"/>（每个在场的源一条诊断）；悬空的源见
    /// <see cref="UnresolvedSources"/>。
    /// </remarks>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// **悬空的源成员**（加载重锚失败、声明保留但未建立绑定的源；`实例.口名`）。
    /// </summary>
    /// <remarks>
    /// <see cref="Sources"/> 已包含它们（在场在前、悬空在后）；本属性只用于把它们与“在场的源”区分呈现
    /// （例如拓扑诊断的“悬空”行）。未锚定的源不参与绑定，故其指标恒为 0。
    /// </remarks>
    public IReadOnlyList<string> UnresolvedSources { get; init; } = [];
}

/// <summary>
/// 模块视图布局（**纯数值**：位置与尺寸，随组合件保存）。
/// </summary>
/// <remarks>
/// <para>
/// <b>单位口径 = DIP（设备无关像素），四个数一律如此</b>。这条不是审美，是被两处硬约束逼出来的：
/// <list type="number">
///   <item><b>接触判定必须同单位</b>：<c>DockRule.TryGetContact</c> 把 <c>X + Width</c>、
///     <c>Y + Height</c> 写在同一个表达式里比较像素相邻 —— 两者不同单位就判不出来。
///     而它跑在 <c>Core</c> 里（<c>ModuleRuntime.Networking</c> 直接拿布局构造候选），
///     <b>拿不到屏幕缩放</b>，没有地方做换算。</item>
///   <item><b>尺寸必须是可移植的</b>：模型若存物理像素，同一份组合件在 150% 的屏上就会
///     以 1.5 倍的物理尺寸开窗（窗口尺寸是 DIP）⇒ <b>右边框长出屏幕被遮住</b>。
///     存 DIP 则跨屏后内容不伸缩，物理尺寸随屏自适应 —— 那是期望行为。</item>
/// </list>
/// </para>
/// <para>
/// <b>DIP ↔ 物理像素的换算只发生在窗口边界</b>（<c>ModuleWindowManager</c>：落位时
/// <c>DIP × 该窗口 RenderScaling</c> 取整成 <c>PixelPoint</c>；读窗口尺寸时直接用 DIP）。
/// 其余位置一律不许再乘除缩放。
/// </para>
/// <para>
/// "是否拼为一组"由位置派生（C5-1）：只保存位置，组关系在加载/拖动后由几何重新推导，**不单独存储**。
/// </para>
/// </remarks>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="X">画布坐标 X（**DIP**）。</param>
/// <param name="Y">画布坐标 Y（**DIP**）。</param>
/// <param name="Width">视图宽度（**DIP**）。</param>
/// <param name="Height">视图高度（**DIP**）。</param>
/// <param name="IsViewVisible">模块视图是否开启（关 = 仍在运行，仅隐藏表现层）。</param>
public sealed record ModuleLayoutView(
    string InstanceId,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsViewVisible = true)
{
    /// <summary>
    /// **落位时该窗口所在屏的缩放**（位置的物理投影 = X/Y × Scale；尺寸仍是 DIP）。
    /// 旧组合件没有该字段 ⇒ 1；运行期由窗口真实的 `ResolvedScaling` 刷新。
    /// </summary>
    public double Scale { get; init; } = 1;
}

/// <summary>端口几何（用户在边缘上调整过的位置；未调整时为默认值）。</summary>
/// <param name="PortName">口名。</param>
/// <param name="Offset">在所属边缘上的归一化位置（0–1）。</param>
/// <param name="IsAnchorFixed">
/// **用户对锚点「固定」的覆盖**（G5 · C5-7）：固定 ⇒ **不可拖动**（模块拉伸时锚点按比例重排 —— `Offset` 是归一化的，天然如此）。
/// **与「模块窗口锁定」不是一回事**：后者是整个模块不可移动、不可缩放。
/// 用词一律「固定」，不用「锁定」。
/// <para>
/// <b>三态</b>（与 <see cref="Edge"/> 的 <c>Auto</c> 同一手法 —— 只是布尔没有哨兵值，故用 <see langword="null"/>）：
/// <list type="bullet">
///   <item><see langword="null"/> = **用户没设置过** ⇒ 框架固定项（**锚点默认固定** = 不可拖动）；**缺省不写**；</item>
///   <item>具体值 = **用户设过**（拖动 / 全局「固定 / 解锁端口锚点」快捷键）⇒ 覆盖缺省，随组合件保存。</item>
/// </list>
/// 少了"未设置"这一态，全局快捷键就无法解锁"包内声明为固定"的口（要么快捷键说谎，要么包内声明失效）——
/// 两种坏结果都真实存在，故必须三态。
/// </para>
/// </param>
/// <param name="Label">
/// **端口网络标签**（用户可见的唯一建网机制）——
/// 同名标签的发送口与接收口**自动连成一条命名网络**；空 = 未命名（不参与命名网络）。
/// </param>
/// <param name="Edge">
/// **用户在画布上拖动后的挂载边覆盖**（G5 · C5-7）：<see cref="HYWDebugAssistant.Sdk.Ports.PortEdge.Auto"/> = **无覆盖**
/// ⇒ 该口的边由**模块声明**决定（见 `PortView.Edge` 的有效值）。
/// **刻意与 `Offset` 分开**：`Auto` 表示"用户没拖过边"，从而让"模块声明的默认边"继续生效。
/// </param>
public sealed record PortGeometryView(
    string PortName,
    double Offset = 0.5,
    bool? IsAnchorFixed = null,
    string? Label = null,
    HYWDebugAssistant.Sdk.Ports.PortEdge Edge = HYWDebugAssistant.Sdk.Ports.PortEdge.Auto);

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
public readonly record struct SubscriptionSourceView(
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
    /// **模块提供的样本解码器**：呈现期解码由模块做，框架**只回调**。
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
    public string? DataType { get; init; }
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
    /// **停流未投递**次数 —— 上游挂起/网络停流期间的发送：
    /// **合法状态，不是故障**，因此**不得**与"传输丢弃"混算。
    /// </summary>
    public long DropStalled { get; init; }

    /// <summary>**队列满**导致未被投递的次数（best-effort 丢弃；= 挤旧 + 拒新）。</summary>
    public long DropQueueFull { get; init; }

    /// <summary>队列满时**挤掉最旧**的次数（丢旧策略：本次投递成立，被挤的那个没送到）。</summary>
    public long DropQueueFullEvicted { get; init; }

    /// <summary>队列满时**拒绝新到**的次数（丢新策略：本次被拒）。</summary>
    public long DropQueueFullRejected { get; init; }

    /// <summary>**承载已停止**（实例正在拆除）导致未被投递的次数（传输丢弃之一）。</summary>
    public long DropCarrierStopped { get; init; }

    /// <summary>**无承载**（目标线程/队列不存在）导致未被投递的次数。</summary>
    public long DropNoCarrier { get; init; }

    /// <summary>**载荷非法**（长度小于契约尺寸）导致未被投递的次数。</summary>
    public long DropInvalidPayload { get; init; }

    /// <summary>
    /// **传输丢弃**合计 = 队列满 + 无承载 + 载荷非法。
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
    /// **单元速率**（数据报 / 秒；**按源计**）——由框架的速率计给出，UI **不得**自行差分估算。
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
/// **每 (实例, 口) 队列的深度与吞吐**（可观测性 L4；框架速率计的「水位」）。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">接收口名。</param>
/// <param name="Depth">当前队列深度（单元数）。</param>
/// <param name="Capacity">容量（单元数；**按口计**）。</param>
/// <param name="Enqueued">该口累计入队数（= **按宿计的投递次数**）。</param>
/// <param name="Dropped">该口累计满队丢弃数（= 挤旧 + 拒新；见 <see cref="DroppedEvictedUnits"/> / <see cref="DroppedRejectedUnits"/>）。</param>
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

    /// <summary>观察到的最大深度（单元数；不随饱和段重置）。</summary>
    public int PeakDepth { get; init; }

    /// <summary>满队**挤掉最旧**的累计单元数（`Dropped` 的一部分）。</summary>
    public long DroppedEvictedUnits { get; init; }

    /// <summary>满队**拒绝新到**的累计单元数（`Dropped` 的另一部分）。</summary>
    public long DroppedRejectedUnits { get; init; }

    /// <summary>
    /// 满队丢弃的**契约尺寸字节数**（= 契约尺寸 × 被挤/被拒单元数）。
    /// </summary>
    /// <remarks>
    /// 它是**承载口径**，**不得**与会话轴的有效字节相加：两者量的不是同一种东西
    /// （那边是业务数据字节，这边是队列里定长一份的尺寸）。
    /// </remarks>
    public long DroppedBytes { get; init; }

    /// <summary>**停止清尾**的累计单元数（拆除时队列里剩下的，不属于运行期丢包）。</summary>
    public long DiscardedOnStopUnits { get; init; }

    /// <summary>**模块上报的内部丢弃**累计单元数（模块自己的口径，框架只登记）。</summary>
    public long ModuleInternalDroppedUnits { get; init; }

    /// <summary>**过期**（在队列里停留超过最大积压年龄）的累计单元数。</summary>
    public long ExpiredUnits { get; init; }

    /// <summary>**本次饱和段内**的丢弃单元数（离开饱和段即归零）。</summary>
    public long DroppedWhileSaturatedUnits { get; init; }

    /// <summary>当前是否处于饱和（深度到达阈值并持续超过保持时长）。</summary>
    public bool Saturated { get; init; }

    /// <summary>已持续饱和的时长（未饱和为 <see cref="TimeSpan.Zero"/>）。</summary>
    public TimeSpan SaturatedFor { get; init; }

    /// <summary>最近一次丢弃的原因（从未丢弃为 <see langword="null"/>）。</summary>
    public DropReason? LastDropReason { get; init; }

    /// <summary>距最近一次丢弃的时长（从未丢弃为 <see langword="null"/>）。</summary>
    public TimeSpan? SinceLastDrop { get; init; }

    /// <summary>队列水位占比（0–1；容量为 0 时为 0）。</summary>
    public double Fill => Capacity > 0 ? Math.Clamp((double)Depth / Capacity, 0, 1) : 0;
}

/// <summary>
/// **一个端口的统一指标**（可观测性 L4）——经 <see cref="IRuntimeQuery.GetPortMetrics"/> 取得。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么统一宿口取数口径（关系到整个数据来源的一致性）</b>：
/// 按「两次字典查找 + 猜键」取数会出错 —— 先按<b>网络身份键</b>（`源集合 → 宿集合`）查、查不到再拿
/// <b>口字面量</b>去查<b>逐源诊断表</b>（键 = `单源 → 宿子集`，是<b>源</b>键）。宿口永远匹配不上后一张表
/// ⇒ <b>宿口的投递/丢弃/速率静默为 0</b>（多源网络、加载期有未锚定源、宿被逐对兼容过滤时必现；
/// 1:1 单源单宿时两个键恰好相等，所以一直看不出来）。
/// </para>
/// <para><b>口径定义（唯一，必须照此实现）</b>：</para>
/// <list type="bullet">
///   <item>一个端口的指标 = <b>该端口参与的"流"上的指标之和</b>；</item>
///   <item>"流" = 网络里的一条 **(源成员 → 宿成员) 绑定**，指标取该绑定的**逐源诊断**
///     （<see cref="BindingDiagnosticView"/>，键 = `源 → 宿子集`）；</item>
///   <item><b>发送口</b> = 所有以"该 (实例, 口)"为**源**的绑定；</item>
///   <item><b>接收口</b> = 所有以"该 (实例, 口)"为**宿**的绑定 —— <b>宿口按网络把该网络的各源诊断加起来</b>，
///     而不是拿宿口字面量去查"源"键的表；</item>
///   <item>该端口**不在任何网络上 ⇒ 本视图全部为 0**（"未入网"由界面自行判定，本视图不管文案）。</item>
/// </list>
/// <para>
/// <b>绑定级口径的已知性质（读之前必须知道）</b>：框架的投递计数是**每条绑定一份**
/// （<c>ModuleRuntime</c> 在"逐宿循环"里对每个宿各记一次 ⇒ 一条含 N 个宿的绑定，其
/// <c>Delivered</c> 是**按宿计的 N 倍**）。因此：
/// <list type="bullet">
///   <item>一条 `源 → 多宿` 绑定的**每个宿口都会读到同一份计数**（含投给兄弟宿的那些）；
///     它回答的是「**该口参与的流上走了多少**」，**不是**「落到该口队列多少」；</item>
///   <item>想要"严格落到本口队列"的数，那是**队列口径**（<see cref="PortQueueDepthView.Enqueued"/>）；
///     **不得**拿它来充当本视图的 <see cref="Delivered"/> / <see cref="BytesPerSecond"/>——
///     那会让端口标记与角色方块（<see cref="RoleView.PayloadRateBytesPerSecond"/>）分叉，正是本次要消除的问题；</item>
///   <item>单源单宿（最常见）时两者数值相同，看不出差别。</item>
/// </list>
/// </para>
/// <para>
/// <b>速率 vs 计数（多宿/多播）</b>：<see cref="UnitRateHz"/> / <see cref="PayloadBytesPerSecond"/> 等
/// 速率的分子按**源**累计（一次发布只算一次）⇒ **多宿不会放大**；而
/// <see cref="Delivered"/> / <see cref="BytesPerSecond"/> 是绑定级**按宿**计数（一条绑定 N 个宿即 N 份）。
/// 界面显示的速率只取前者；宿口在 1:N / N:M 下的 <see cref="Delivered"/> 含同源其他宿的份额，
/// 这是文档化的**流口径**，不是"落到本口队列"的口径。
/// </para>
/// <para>
/// <b>速率与角色方块同一口径</b>：界面显示的那一个速率 = <see cref="PayloadBytesPerSecond"/>
/// （逐源诊断 <see cref="BindingDiagnosticView.PayloadByteRate"/> 之和），与
/// <see cref="RoleView.PayloadRateBytesPerSecond"/> 读的是<b>同一批</b>框架速率计（<c>_rateMeters</c>），
/// 只是汇总范围不同（端口维度 / 角色维度）。<b>不得</b>为端口另起"每口队列"的第二套速率
/// （那会让同一根线的两侧显示不同的数）。
/// <para>
/// <see cref="BytesPerSecond"/>（搬运口径）**保留为 API 事实**，但**界面不再显示它**——
/// 界面只要有效数据（截断的不应算在有效数据里面）。
/// </para>
/// </para>
/// <para>
/// <b>三类丢弃不混算</b>：<see cref="Dropped"/> 只装
/// **传输丢弃**（队列满 + 无承载 + 载荷非法，= <see cref="BindingDiagnosticView.DropTransport"/>）；
/// **停流未投递**是合法状态，单独放 <see cref="DropStalled"/>，**不并进** <see cref="Dropped"/>；
/// **模块内部丢弃**（样本覆盖/丢弃）是模块自己的口径，不经网络诊断，故不在本视图内。
/// </para>
/// <para>界面侧<b>只格式化</b>本视图的字段，不得再自行差分估算或另行相加（`07` §"叠加层与视觉基线"）。</para>
/// </remarks>
/// <param name="Delivered">
/// 该端口参与的**全部流**的累计投递单元数之和（= 各条绑定的
/// <see cref="BindingDiagnosticView.Delivered"/> 之和；**绑定级「按宿计」**——
/// 一条含 N 个宿的绑定，它的 N 个宿口读到**同一份**计数，见 Remarks 的"已知性质"）。
/// </param>
/// <param name="Dropped">
/// 该端口参与的**全部流**的**传输丢弃**合计（队列满 + 无承载 + 载荷非法；**不含停流未投递**）。
/// </param>
/// <param name="BytesPerSecond">
/// **搬运字节速率**（字节 / 秒；与角色方块**同一口径**）= 各条流 `Delivered × 该源契约尺寸` 的速率之和。
/// </param>
public sealed record PortMetricsView(long Delivered, long Dropped, double BytesPerSecond)
{
    /// <summary>
    /// **停流未投递**次数 —— 上游挂起 / 网络停流期间的发送：
    /// **合法状态，不是故障**，因此**不得**与 <see cref="Dropped"/>（传输丢弃）混算，故单独给。
    /// </summary>
    public long DropStalled { get; init; }

    /// <summary>**队列满**导致未被投递的次数（传输丢弃之一；= 挤旧 + 拒新）。</summary>
    public long DropQueueFull { get; init; }

    /// <summary>队列满时**挤掉最旧**的次数（丢旧策略：本次投递成立，被挤的那个没送到）。</summary>
    public long DropQueueFullEvicted { get; init; }

    /// <summary>队列满时**拒绝新到**的次数（丢新策略：本次被拒）。</summary>
    public long DropQueueFullRejected { get; init; }

    /// <summary>**承载已停止**（实例正在拆除）导致未被投递的次数（传输丢弃之一）。</summary>
    public long DropCarrierStopped { get; init; }

    /// <summary>**无承载**（目标线程 / 队列不存在）导致未被投递的次数（传输丢弃之一）。</summary>
    public long DropNoCarrier { get; init; }

    /// <summary>**载荷非法**（长度小于契约尺寸）导致未被投递的次数（传输丢弃之一）。</summary>
    public long DropInvalidPayload { get; init; }

    /// <summary>
    /// **累计搬运字节**（= 各条流 `Delivered × 该源契约尺寸` 之和）——与 <see cref="BytesPerSecond"/>
    /// **同一口径的累计量**。给它是为了让界面**只格式化**（旧写法是界面自己拿"投递数 × 契约尺寸"乘一遍）。
    /// </summary>
    public long BytesDelivered { get; init; }

    /// <summary>**投递单元速率**（单元 / 秒；按宿计）——与 <see cref="BytesPerSecond"/> 同源于逐源诊断。</summary>
    public double UnitRateHz { get; init; }

    /// <summary>
    /// **累计有效载荷字节**（= 各条流**逐单元读有效长度字段**累加的有效字节之和；**按源计**）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PayloadBytesPerSecond"/> **同一口径的累计量**（界面只要有效数据）
    /// （界面显示的"流量约 X"取它，不再取含补零的 <see cref="BytesDelivered"/>）。
    /// </remarks>
    public long PayloadBytesDelivered { get; init; }

    /// <summary>
    /// **有效载荷字节速率**（字节 / 秒；**按源计**）——界面（网络标签 / 角色方块）显示的那一个速率。
    /// </summary>
    /// <remarks>
    /// = 逐源诊断 <see cref="BindingDiagnosticView.PayloadByteRate"/> 之和
    /// （逐单元读类型声明的有效长度字段；未声明 ⇒ 静态 `PayloadSizeInBytes` ?? 契约尺寸）。
    /// 与 <see cref="BytesPerSecond"/>（搬运字节，含补零/对齐）**是两个口径**：后者保留为 API 事实，
    /// **界面不再显示它**。
    /// </remarks>
    public double PayloadBytesPerSecond { get; init; }

    /// <summary>该端口参与的流里**最新的一次投递时刻**（无数据为 <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDataTime { get; init; }

    /// <summary>
    /// 该端口参与的**网络身份键**（去重、有序；与 <see cref="NetworkView.Key"/> 一致）。
    /// </summary>
    /// <remarks>
    /// **空 ⇒ 该端口不在任何网络上**（此时本视图其余字段全为 0）。
    /// 只作诊断/配对用，**不要**拿它当指标（指标看 <see cref="Delivered"/> 等字段）。
    /// </remarks>
    public IReadOnlyList<string> NetworkKeys { get; init; } = [];
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

/// <summary>Issue 汇总（全量计数；不受 Issue 面板的“最近 N 条”显示上限影响）。</summary>
/// <param name="Total">总条数。</param>
/// <param name="Error">错误级条数。</param>
/// <param name="Warning">警告级条数。</param>
/// <param name="Info">信息级条数。</param>
public sealed record IssueSummaryView(
    int Total,
    int Error,
    int Warning,
    int Info);

/// <summary>模块目录项（可用模块清单）。</summary>
/// <param name="ModuleId">模块标识字面量。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Capabilities">能力声明（如 <c>Network, File</c>）。</param>
/// <param name="SourceDirectory">装入该模块的来源根（内置包目录或用户配置的根）；未知为 null。</param>
/// <param name="NeedsRestart">
/// 是否「在本次进程启动后被重新构建过」：为 <see langword="true"/> 时该包**必须重启应用**才生效
/// （程序集已载入且不可卸载；框架**不实现**热重载）。
/// </param>
/// <param name="Tags">
/// 模块声明的**标签**（`ModuleDefinitionAttribute.Tags`）【新增 · M1-16 后】——界面据它做**分类**与搜索；
/// 不声明标签的模块归入"其他"。空/未知为 <see langword="null"/>。
/// </param>
/// <param name="TypeKeys">
/// 模块**声明端口**承载的数据类型标识（去重、按序）：清单预览图块外圈据此着色，
/// 使"这是什么东西"一眼可辨。取色经 <c>PacketTypePalette</c>（与端口标记、角色方块**同源**）。
/// 空/未知为 <see langword="null"/>（无端口模块、或改用 C# <c>DeclarePort</c> 的旧模块）。
/// </param>
public sealed record ModuleCatalogEntryView(
    string ModuleId,
    string DisplayName,
    string Description,
    string Capabilities,
    string? SourceDirectory = null,
    bool NeedsRestart = false,
    IReadOnlyList<string>? Tags = null,
    bool IsExternal = false,
    string? PreviewImagePath = null,
    IReadOnlyList<string>? TypeKeys = null);

/// <summary>
/// **建网候选**：发送端 → 接收端，**即将**因「接触且对齐」而连上。
/// </summary>
/// <param name="SourceLiteral">发送端口字面量（<c>实例ID.口名</c>）。</param>
/// <param name="TargetLiteral">接收端口字面量（<c>实例ID.口名</c>）。</param>
public sealed record ConnectionCandidateView(string SourceLiteral, string TargetLiteral);

/// <summary>
/// **同网络名但契约不兼容的端口对**（只读呈现；不参与建网判定）。
/// </summary>
/// <param name="InstanceId">发送端所属实例。</param>
/// <param name="PortName">发送端口名。</param>
/// <param name="PeerInstanceId">接收端所属实例。</param>
/// <param name="PeerPortName">接收端口名。</param>
/// <param name="NetworkName">两端共同的网络名（已 Trim）。</param>
/// <param name="Reason">不兼容原因（面向用户的描述）。</param>
/// <remarks>
/// 它只回答“这两个口打了同一个网络名、但框架不会把它们连起来，为什么”。
/// 判定复用建网的唯一入口（<c>ContractCompatibility.Check</c>），因此与 Issue 中心/实际网络行为一致。
/// </remarks>
public sealed record PortContractConflictView(
    string InstanceId,
    string PortName,
    string PeerInstanceId,
    string PeerPortName,
    string? NetworkName,
    string Reason);

/// <summary>
/// **拖动网络标签时的「连接对齐」建议**（G5）。
/// </summary>
/// <param name="InstanceId">本口所属实例。</param>
/// <param name="PortName">本口名。</param>
/// <param name="PeerInstanceId">对端口所属实例。</param>
/// <param name="PeerPortName">对端口名。</param>
/// <param name="Offset">
/// **建议的归一化偏移**（0–1）：把它赋给本口，两个标记中心就沿接触面对齐 ⇒ **松手即可建网**。
/// </param>
/// <remarks>
/// 用途 = UI 在**拖动网络标签**时，若该口与某个**面对面**的口已满足建网条件（只差对齐），
/// 就把标记吸到这个偏移并高亮（"边框高亮"）。
/// <para>
/// <b>判据与真正建网同一份实现</b>（几何 `DockRule.IsAligned` + 唯一接受判定）：本建议只在两者都通过时才产生，
/// 因此"吸上去" ⟺ "松手后真的连上"，预览不会骗人。
/// </para>
/// </remarks>
public sealed record AnchorDockHintView(
    string InstanceId,
    string PortName,
    string PeerInstanceId,
    string PeerPortName,
    double Offset);

/// <summary>
/// **模块来源根的扫描结果** —— 供「模块清单」弹窗做诊断：
/// "到底扫了哪些目录、每个目录发生了什么"。
/// </summary>
/// <param name="Directory">根目录路径。</param>
/// <param name="Exists">该根是否存在（不存在 = 跳过并提示）。</param>
/// <param name="PackagesFound">扫到的模块包目录数（含失败的）。</param>
/// <param name="SkippedDirectories">被跳过的子目录数（不含模块清单文件）。</param>
/// <param name="FailedPackages">真正装入失败的包数（**只有真失败**）。</param>
/// <param name="Messages">逐包结果与提示（成功/失败原因/重复/需重启等，面向用户）。</param>
/// <param name="AlreadyLoadedPackages">同包同版本、内容未变的包数。</param>
/// <param name="DuplicatePackages">同 ID 同版本但来自另一目录的包数（先到者胜）。</param>
/// <param name="ChangedPackages">同版本内容已变化、需重启的包数。</param>
public sealed record ModuleSourceView(
    string Directory,
    bool Exists,
    int PackagesFound,
    int SkippedDirectories,
    int FailedPackages,
    IReadOnlyList<string> Messages,
    int AlreadyLoadedPackages = 0,
    int DuplicatePackages = 0,
    int ChangedPackages = 0);

/// <summary>
/// **用户可见的存储位置**（只读信息）：设置 / 模块 / 组合 / 工作区 / 日志 五个功能根 + 出厂种子。
/// </summary>
/// <param name="AppRoot">程序目录。</param>
/// <param name="ModulesRoot">模块包目录（内置包；外部导入在 `external` 子目录）。</param>
/// <param name="CompositesRoot">组合文件默认目录（与 modules 同级）。</param>
/// <param name="SettingsFile">应用设置文件（设置根下）。</param>
/// <param name="WorkspaceRoot">工作区快照根（含一次性重启恢复快照）。</param>
/// <param name="LogsRoot">日志目录（预留）。</param>
/// <param name="SeedsRoot">出厂种子根（只读；只有投放器读）。</param>
public sealed record StorageLocationsView(
    string AppRoot,
    string ModulesRoot,
    string CompositesRoot,
    string SettingsFile,
    string WorkspaceRoot,
    string LogsRoot,
    string SeedsRoot);

/// <summary>
/// 默认组合目录中的**一个文件**（供组合列表显示；含只读检查结果，不实例化组合）。
/// </summary>
/// <param name="Path">完整路径。</param>
/// <param name="FileName">文件名（含扩展名）。</param>
/// <param name="LastWriteTime">最后修改时间。</param>
/// <param name="SizeBytes">文件大小（字节）。</param>
/// <param name="ModuleCount">组合内的实例数。</param>
/// <param name="MissingModules">缺失/不兼容的模块标识（去重；空 = 全部可加载）。</param>
/// <param name="ReadError">无法读取的原因（空 = 可读取）。</param>
public sealed record CompositeFileView(
    string Path,
    string FileName,
    DateTimeOffset LastWriteTime,
    long SizeBytes,
    int ModuleCount = 0,
    IReadOnlyList<string>? MissingModules = null,
    string? ReadError = null,
    string? PreviewImagePath = null,
    string? DirectoryPath = null,
    bool IsPackage = false,
    string? DisplayName = null);

/// <summary>外壳窗口在工作区快照里的绝对矩形（DIP）。</summary>
public readonly record struct WorkspacePlacementView(double X, double Y, double Width, double Height);

/// <summary>模块实例壁纸（PNG 字节 + 版本；无壁纸时查询返回 null）。</summary>
/// <param name="Revision">版本号（每次设置/清除/加载递增）。</param>
/// <param name="Png">PNG 字节。</param>
public sealed record ModuleWallpaperView(long Revision, byte[] Png);

/// <summary>模块控件的持久化图片状态（字节 + 归一化裁切；无图片时查询返回 null）。</summary>
/// <param name="Revision">版本号（每次设置/清除/加载递增）。</param>
/// <param name="Bytes">图片原始字节。</param>
/// <param name="Crop">归一化裁切。</param>
public sealed record ModuleViewImageAssetView(long Revision, byte[] Bytes, ViewImageCrop Crop);

/// <summary>组合文件导入结果（复制到默认目录后的目标路径）。</summary>
/// <param name="Succeeded">是否成功（含“已存在且复用”）。</param>
/// <param name="TargetPath">默认目录中的目标文件（失败时为空）。</param>
/// <param name="Notices">提示。</param>
/// <param name="Failures">失败原因。</param>
public readonly record struct CompositeImportResult(
    bool Succeeded,
    string? TargetPath,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Failures);

/// <summary>外层文件夹批量导入的汇总结果。</summary>
/// <param name="ModuleCount">成功导入的模块包数。</param>
/// <param name="CompositeCount">成功导入的组合包数。</param>
/// <param name="Notices">逐包成功/重扫提示。</param>
/// <param name="Failures">逐包失败原因。</param>
public readonly record struct FolderImportResult(
    int ModuleCount,
    int CompositeCount,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Failures)
{
    /// <summary>成功导入的包总数。</summary>
    public int ImportedCount => ModuleCount + CompositeCount;

    /// <summary>是否有失败项。</summary>
    public bool HasFailures => Failures.Count > 0;
}

/// <summary>组合文件重命名结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="TargetPath">新文件路径（失败时为空）。</param>
/// <param name="Failures">失败原因。</param>
public readonly record struct CompositeRenameResult(
    bool Succeeded,
    string? TargetPath,
    IReadOnlyList<string> Failures);
