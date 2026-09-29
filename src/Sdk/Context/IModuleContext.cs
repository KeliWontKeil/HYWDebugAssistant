using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Context;

/// <summary>
/// 数据单元接收处理（框架在**该实例的专属线程**上调用）。
/// </summary>
/// <param name="payload">数据单元的定长负载（生命周期仅限本次调用；需要留存时自行拷贝）。</param>
public delegate void ReceiveHandler(ReadOnlySpan<byte> payload);

/// <summary>
/// 模块**声明入口**（在 <see cref="Lifecycle.IModule.OnRegistered"/> 中调用一次）。
/// </summary>
/// <remarks>对应成员统一路径的 ①声明 → ②实例化（主文档 §4.10.2）：框架据声明创建成员实例。</remarks>
public interface IModuleDeclaration
{
    /// <summary>
    /// 声明一个**数据类型**【ADR-0046 D2】：标识 + 版本 + 尺寸（由形状定）+ 传输语义 + 计量块 + **解码器**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>类型是**契约的身份**：口用 <see cref="PortDeclaration.Type"/> 引用它，兼容判定据此走三层判据；</item>
    ///   <item>解码器随类型注册：**呈现期解码由模块做，框架只回调**（D1 ③）；不提供则不显示数值，不猜；</item>
    ///   <item>不声明类型的模块**照常工作**（回退结构判定）——本声明是兼容扩展，不是必填项。</item>
    /// </list>
    /// </remarks>
    void DeclareDataType(DataTypeDeclaration type);

    /// <summary>声明一个口成员（方向两态；口名模块内唯一）。</summary>
    void DeclarePort(PortDeclaration port);

    /// <summary>
    /// 声明一个**角色**（模块的功能分块：一种数据类型 + 其输入口/输出口）【ADR-0052】。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>角色 = 模块内部数据处理功能的抽象</b>（"我有哪几块处理功能"），与**网络标签/端口标记**
    ///     （数据接口的抽象）**不是同一层级** —— 叠加层只画角色，接口仍由端口标记表达；</item>
    ///   <item>一个角色**只有一种类型**；**口通常各 1 个**（多源融合器 = 2 入 1 出，允许）；一个口**最多属于一个角色**；</item>
    ///   <item>**只呈现**：不参与建网 / 契约兼容 / 停流 / 投递 / 计量，也不进组合件（ADR-0048 R9）；</item>
    ///   <item>声明即契约：有数据口的模块**应当**声明角色；未声明 ⇒ 叠加层不画方块。**运行期不报**（避免在自家正确模块的
    ///     Issue 中心报警）；由 SDK 自检（`ModuleContract`）给一条**建议**（`Advisories`，**不进 `Problems`**）—— 见 B5 裁定。</item>
    /// </list>
    /// </remarks>
    /// <param name="role">角色声明。</param>
    void DeclareRole(RoleDeclaration role);

    /// <summary>
    /// 声明一条**模块内部的处理顺序**（`角色 → 角色`）【ADR-0052 · 修订 ADR-0049 D13】——
    /// 表达"分块之间的先后"（如"融合 → 限幅"）。单分块模块**零声明**。
    /// </summary>
    /// <remarks>只呈现：不参与建网/兼容/停流/投递/计量，也不进组合件；两端角色必须已声明（否则报 Issue 并忽略该条）。</remarks>
    /// <param name="flow">内部处理关系（角色 → 角色）。</param>
    void DeclareFlow(FlowDeclaration flow);

    /// <summary>声明视图（预置元素组合；不声明 = 无 UI 模块，合法）。</summary>
    void DeclareView(ViewDeclaration view);
}

/// <summary>
/// 口的**存取与投递**入口（成员统一路径的 ③存取·连接）。
/// </summary>
/// <remarks>
/// 投递为**直接绑定**（不经 Core 中转，P2/E1）；队列满时**绝不阻塞发送方**——按口声明的丢弃方式丢弃并计数。
/// </remarks>
public interface IPortAccess
{
    /// <summary>发送一个数据单元（发送口）。返回 <see langword="false"/> 表示已丢弃（队列满或口停流）。</summary>
    bool TrySend(string portName, ReadOnlySpan<byte> payload);

    /// <summary>注册接收处理（接收口）——**被动接收模式**：框架在数据到达时于该实例线程上调用。</summary>
    /// <remarks>
    /// 被动模式适用于**不实现** <see cref="Lifecycle.IActivitySource"/> 的模块（由框架驱动投递循环）。
    /// 实现了自发活动的模块（活动体可阻塞）应改用**主动接收**（<see cref="TryReceive"/>），
    /// 在自己的活动体内取数据——否则框架无法在其阻塞期间插入投递。
    /// </remarks>
    void SetReceiveHandler(string portName, ReceiveHandler handler);

    /// <summary>主动取一个待处理数据单元——**主动接收模式**（非阻塞；无数据时返回 <see langword="false"/>）。</summary>
    /// <remarks>
    /// 输入队列是**每模块实例一个**（ADR-0029 §4.2），因此主动接收返回"任意一个单元 + 其目标口名"；
    /// 只关心单一接收口的模块自行比对 <paramref name="sourcePort"/> 即可。
    /// 取到的缓冲**必须**经 <see cref="ReleaseBuffer"/> 归还。
    /// </remarks>
    /// <param name="buffer">取到的数据单元。</param>
    /// <param name="sourcePort">该单元的目标口名。</param>
    bool TryReceive([NotNullWhen(true)] out PayloadBuffer? buffer, [NotNullWhen(true)] out string? sourcePort);

    /// <summary>归还主动接收取得的数据单元（归还后**不得**再访问其内容）。</summary>
    void ReleaseBuffer(PayloadBuffer buffer);

    /// <summary>
    /// 写入**订阅缓冲**（供 UI 按刷新节拍读取——Q4-4/C3-6）。
    /// </summary>
    /// <param name="dataSourceName">在视图声明中登记的数据源名。</param>
    /// <param name="value">数据（单值 = 最新值槽；序列 = 环形缓冲，满则丢旧）。</param>
    void Publish(string dataSourceName, ReadOnlySpan<byte> value);
}

/// <summary>配置读写（读写的是该实例的 Configuration，随组合件保存）。</summary>
public interface IConfigurationAccess
{
    /// <summary>读取当前配置（不可变快照）。</summary>
    T Read<T>()
        where T : class, new();

    /// <summary>写回配置：触发框架的配置变更通知路径（<see cref="Lifecycle.IModule.OnConfigurationChanged"/>）。</summary>
    void Write<T>(T configuration)
        where T : class, new();
}

/// <summary>
/// 停止信号：模块的长时间活动应周期性检查该信号并尽快退出（ADR-0029 §4.1 停止协议）。
/// </summary>
public interface IStopSignal
{
    /// <summary>是否已请求停止。</summary>
    bool IsStopRequested { get; }

    /// <summary>阻塞等待停止信号（可在活动体的空闲等待中使用）。</summary>
    /// <param name="timeout">最长等待时间。</param>
    /// <returns>已请求停止时为 <see langword="true"/>。</returns>
    bool Wait(TimeSpan timeout);
}

/// <summary>
/// **人机操作收件箱**【新增 · M1-16 / ADR-0040 D1-D3】：
/// 框架把"用户点按钮 / 填输入框 / 拨开关"投递到这里，**模块在自身线程上消费**
/// （与数据投递同一线程纪律：框架不跨线程直接调模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁消费</b>：实现 <see cref="Lifecycle.IActivitySource"/> 的模块在其活动体内轮询
///     （典型写法：<c>while (!stop) { while (inbox.TryDequeueCommand(out var cmd)) ...; }</c>）；</item>
///   <item><b>有界</b>：容量有限，满时**丢最旧**并计数（不阻塞 UI、不无限增长）；</item>
///   <item><b>语义</b>：命令 = "做一次"，输入 = "一个值"；两者都**不保证执行完成**（模块自行决定一次性/连续行为）。</item>
/// </list>
/// </remarks>
public interface IModuleInbox
{
    /// <summary>取一个待执行命令（非阻塞；无命令时返回 <see langword="false"/>）。</summary>
    /// <param name="commandName">命令名（视图声明中登记）。</param>
    bool TryDequeueCommand([NotNullWhen(true)] out string? commandName);

    /// <summary>取一个待写入的输入值（非阻塞）。</summary>
    /// <param name="source">输入元素绑定的数据源名。</param>
    /// <param name="text">用户输入的文本（模块自行解析；非法值应上报 Issue）。</param>
    bool TryDequeueInput([NotNullWhen(true)] out string? source, [NotNullWhen(true)] out string? text);
}

/// <summary>
/// **输入处理结果反馈**【新增 · G3 / 用户 2026-09 裁决】：模块把"某次输入的处理结果"回报到**对应的输入元素**上，
/// 框架据此把它渲染成**失败态**（红框）—— 模块**不持 UI**、视图由框架渲染，故这件事**本质只能由框架做**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：`SubmitInput` 返回的 `CommandResult` **只反映"是否投递入队"**，
///     不反映模块的处理结果（转换失败、HEX 非法……都发生在**模块内部**，此时投递是成功的）⇒
///     没有本通道时，"处理失败"在界面上**无处可见**；</item>
///   <item><b>粒度</b>：按**输入元素**（`ViewElement.DataSource` 的名字）定位；**只做输入** ——
///     命令侧的同类反馈留待需要时再只增；</item>
///   <item><b>生命周期（"下一次提交即刷新"）</b>：框架在**投递入队时清除**该 (实例, 源名) 的旧结果 ⇒
///     **不回报 = 无失败态**（模块可以默认什么都不做，零影响）；想让失败态消失，**下一次提交**即可；</item>
///   <item><b>纪律</b>：<see cref="Report"/> **不抛**（非法源名 / 内部失败只记诊断计数）；回报的是**呈现文本**，
///     框架**不理解**它的语义（与 `Issue` / 业务状态同一口径）。</item>
/// </list>
/// </remarks>
public interface IInputFeedbackReporter
{
    /// <summary>回报一次输入的处理结果。</summary>
    /// <param name="sourceName">输入元素的数据源名（= 视图声明里该输入元素的 <c>DataSource</c>）。</param>
    /// <param name="succeeded">是否处理成功（false ⇒ 该输入渲染失败态）。</param>
    /// <param name="message">可选原因（展示在该输入元素旁；null = 留白）。</param>
    void Report(string sourceName, bool succeeded, string? message = null);
}

/// <summary>
/// 模块上下文：模块**唯一**的框架访问面（不持有框架对象引用）——ADR-0028 §3.8。
/// </summary>
/// <remarks>
/// 方向纪律：模块 → 框架为"声明 / 上报 / 请求"；框架 → 模块为"生命周期钩子 + 投递回调"。
/// 模块**不得**反向查询其他模块是否存在、不得持有其他模块实例（P12）。
/// </remarks>
public interface IModuleContext
{
    /// <summary>本实例的模块标识。</summary>
    ModuleId ModuleId { get; }

    /// <summary>声明入口（仅在 <c>OnRegistered</c> 期间有效）。</summary>
    IModuleDeclaration Declaration { get; }

    /// <summary>口的存取与投递。</summary>
    IPortAccess Ports { get; }

    /// <summary>配置读写。</summary>
    IConfigurationAccess Configuration { get; }

    /// <summary>Issue 上报（可呈现的问题；**业务状态不入 Issue**）。</summary>
    IIssueReporter Issues { get; }

    /// <summary>诊断日志（经框架汇集，可观测性 L3）。</summary>
    IModuleLogger Log { get; }

    /// <summary>业务状态上报（框架只登记/呈现、**不解释语义**）。</summary>
    IBusinessStateReporter State { get; }

    /// <summary>**输入处理结果反馈**（G3：让"处理失败"在对应输入元素上可见）。</summary>
    IInputFeedbackReporter InputFeedback { get; }

    /// <summary>停止信号（长期活动必须响应）。</summary>
    IStopSignal StopSignal { get; }

    /// <summary>**人机操作收件箱**（命令 / 输入；模块在自身线程消费 —— ADR-0040）。</summary>
    IModuleInbox Inbox { get; }

    /// <summary>
    /// **显示内容的请求面**（模块 → 框架的"请求"方向）：目前只有"**清空某个显示源**"
    /// 〔2026-09 · 为 `hyw/data-monitor` 的「清空」而只增〕。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ViewState"/> 的分界：那边是**只读**（模块改不了用户的选中页与编辑内容）；
    /// 这里能动的是**模块自己发布出去的显示内容**（如 A5 文本日志的缓冲）。语义见 <see cref="IViewDisplayAccess"/>。
    /// </remarks>
    IViewDisplayAccess Display { get; }

    /// <summary>
    /// **视图状态回读**（只读快照）：当前选中页 / 输入框当前文本 / 显示区保留的文本
    /// 〔2026-09 用户裁定 · `decisions.md` 7.28〕。
    /// </summary>
    /// <remarks>
    /// <b>方向纪律的增补</b>：既有纪律是"模块 → 框架 = 声明/上报/请求；框架 → 模块 = 钩子 + 投递回调"；
    /// 本属性是**框架 → 模块的只读事实**（不是回调、不反向操作 UI）—— 语义见 <see cref="IViewStateReader"/>。
    /// <b>只增成员</b>：模块实现者无需改动（属性由框架提供），既有模块零影响。
    /// </remarks>
    IViewStateReader ViewState { get; }
}
