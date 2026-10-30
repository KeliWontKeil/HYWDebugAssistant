using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Context;

/// <summary>
/// 数据单元接收处理（框架在**该实例的专属线程**上调用）。
/// </summary>
/// <param name="payload">数据单元的定长负载（生命周期仅限本次调用；需要留存时自行拷贝）。</param>
public delegate void ReceiveHandler(ReadOnlySpan<byte> payload);

/// <summary>输入源的持久化类别。</summary>
public enum InputPersistence
{
    /// <summary>不持久化（用户输入关闭后即丢；连续发送/文件路径等有副作用的输入用这一档）。</summary>
    Transient = 0,

    /// <summary>随组合件/工作区保存，并在加载时先恢复视图状态再激活模块。</summary>
    Persistent = 1,
}

/// <summary>输入源声明（名称 + 是否持久化）。</summary>
/// <param name="Name">输入源名（模块内唯一）。</param>
public sealed record InputDeclaration(string Name)
{
    /// <summary>持久化类别（默认 Transient；持久化需模块显式声明）。</summary>
    public InputPersistence Persistence { get; init; } = InputPersistence.Transient;
}

/// <summary>
/// 模块**声明入口**（在 <see cref="Lifecycle.IModule.OnRegistered"/> 中调用一次）。
/// </summary>
/// <remarks>对应成员统一路径的 ①声明 → ②实例化（主文档 §4.10.2）：框架据声明创建成员实例。</remarks>
public interface IModuleDeclaration
{
    /// <summary>
    /// 声明一个**数据类型**：标识 + 版本 + 尺寸（由形状定）+ 传输语义 + 计量块 + **解码器**。
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
    /// 声明一个**角色**（模块的功能分块：一种数据类型 + 其输入口/输出口）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>角色 = 模块内部数据处理功能的抽象</b>（"我有哪几块处理功能"），与**网络标签/端口标记**
    ///     （数据接口的抽象）**不是同一层级** —— 叠加层只画角色，接口仍由端口标记表达；</item>
    ///   <item>一个角色**只有一种类型**；**口通常各 1 个**（多源融合器 = 2 入 1 出，允许）；一个口**最多属于一个角色**；</item>
    ///   <item>**只呈现**：不参与建网 / 契约兼容 / 停流 / 投递 / 计量，也不进组合件；</item>
    ///   <item>声明即契约：有数据口的模块**应当**声明角色；未声明 ⇒ 叠加层不画方块。**运行期不报**（避免在自家正确模块的
    ///     Issue 中心报警）；由 SDK 自检（`ModuleContract`）给一条**建议**（`Advisories`，**不进 `Problems`**）。</item>
    /// </list>
    /// </remarks>
    /// <param name="role">角色声明。</param>
    void DeclareRole(RoleDeclaration role);

    /// <summary>
    /// 声明一条**模块内部的处理顺序**（`角色 → 角色`）——
    /// 表达「分块之间的先后」（如「融合 → 限幅」）。单分块模块**零声明**。
    /// </summary>
    /// <remarks>只呈现：不参与建网/兼容/停流/投递/计量，也不进组合件；两端角色必须已声明（否则报 Issue 并忽略该条）。</remarks>
    /// <param name="flow">内部处理关系（角色 → 角色）。</param>
    void DeclareFlow(FlowDeclaration flow);

    /// <summary>
    /// 声明一条**显示源**（界面按名字读它；框架据此建订阅缓冲）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是契约，不是界面</b>：它只回答"我有哪些可以给界面读的源"，不回答"画在哪、画成什么"。
    /// 名字重复 ⇒ 拒绝后者并报 Issue（**不静默改名**，否则界面读到的会是一个意料之外的源）。
    /// </para>
    /// <para>
    /// 未声明的名字被写入时，框架**丢弃并只提示一次**（不刷屏、也不静默吞掉）。
    /// </para>
    /// </remarks>
    /// <param name="source">显示源声明。</param>
    void DeclareDisplaySource(DisplaySourceDeclaration source);

    /// <summary>
    /// 声明一个**输入源**（用户把值提交给模块的那条通道的名字）。
    /// </summary>
    /// <remarks>
    /// 界面的输入控件提交时，框架要判断"这个名字模块登记过吗"——没登记就拒收并报 Issue，
    /// 而不是把一个谁都不认的名字写进模块的收件箱。名字重复只登记一次。
    /// </remarks>
    /// <param name="sourceName">输入源名。</param>
    void DeclareInput(string sourceName);

    /// <summary>
    /// 声明一个**输入源**及其持久化类别；默认实现保持旧模块的 Transient 行为。
    /// </summary>
    /// <param name="input">输入声明。</param>
    void DeclareInput(InputDeclaration input) => DeclareInput(input.Name);

    /// <summary>
    /// 声明一条**命令**（按钮点一下要触发的那件事）。
    /// </summary>
    /// <remarks>
    /// 未登记的命令名由框架拒收并报 Issue；<paramref name="title"/> 只用于**诊断文案**
    /// （"执行「打开」失败"这种一句话），不参与任何判定。
    /// </remarks>
    /// <param name="name">命令名（模块内唯一）。</param>
    /// <param name="title">给人看的命令名（空 = 用命令名本身）。</param>
    void DeclareCommand(string name, string title);

    /// <summary>
    /// 声明一条**外部接入**：模块内唯一的名字 + 适配件（懂某个外部标准的实现件）。
    /// </summary>
    /// <remarks>
    /// 与数据类型 / 口 / 角色同级：声明期一次性、运行期不重读。框架据此创建会话、登记适配件的参数类型
    /// （配置表单与持久化自动复用），并在实例状态/用户请求/参数变更时执行打开、关闭、重开。
    /// <para>
    /// <b>默认空实现</b>：只为一件事——已有测试替身 / 自定义实现不因新增成员而编译失败。真实框架与
    /// <c>Sdk.Testing</c> 的测试上下文都会覆盖它。
    /// </para>
    /// </remarks>
    /// <param name="access">接入声明。</param>
    void DeclareExternalAccess(ExternalAccessDeclaration access)
    {
    }
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
    /// 输入队列是**每模块实例一个**，因此主动接收返回「任意一个单元 + 其目标口名」；
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

    /// <summary>
    /// 读本实例某个**接收口**的队列流动状态（深度 / 峰值 / 丢弃账本 / 饱和）。
    /// </summary>
    /// <param name="portName">接收口名。</param>
    /// <param name="state">流动状态（无队列时为 <see cref="PortFlowState.None"/>）。</param>
    /// <returns>该口有队列时为 <see langword="true"/>；**没有队列**时为 <see langword="false"/>。</returns>
    /// <remarks>
    /// <para>
    /// <b>只对本实例的接收口有效</b>：别的实例、发送口、未声明的名字一律返回 <see langword="false"/>
    /// ——流量事实不能跨实例借用（P12）。
    /// </para>
    /// <para>
    /// 它给的是**当前事实**（不是事件）：模块在自己的循环里读，问的是"我这个口现在堵不堵、丢了多少"。
    /// </para>
    /// </remarks>
    bool TryGetFlowState(string portName, out PortFlowState state)
    {
        state = PortFlowState.None;
        return false;
    }

    /// <summary>
    /// 上报一次**模块自己造成的**丢弃（业务上不适用、内部缓冲已满、解析失败等）。
    /// </summary>
    /// <param name="portName">接收口名。</param>
    /// <param name="reason">丢弃原因；**只接受 <see cref="DropReason.ModuleInternal"/>**。</param>
    /// <param name="units">丢弃的单元数。</param>
    /// <param name="bytes">丢弃的字节数（= 契约尺寸 × 单元数）。</param>
    /// <remarks>
    /// <para>
    /// <b>只接受 <see cref="DropReason.ModuleInternal"/>，其它原因由框架静默忽略</b>：
    /// 传输与会话的事实归框架（队列满、无承载、承载已停、写失败这些它自己看得见），
    /// 模块不得伪造——否则诊断上会出现一件框架从未观察到的事。
    /// </para>
    /// <para>默认空实现：既有模块与测试替身不会因为新增成员而编译失败。</para>
    /// </remarks>
    void ReportLoss(string portName, DropReason reason, long units, long bytes)
    {
    }
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
/// 停止信号：模块的长时间活动应周期性检查该信号并尽快退出。
/// </summary>
public interface IStopSignal
{
    /// <summary>是否已请求停止。</summary>
    bool IsStopRequested { get; }

    /// <summary>停止取消令牌（等待类操作可直接链接它）。</summary>
    CancellationToken StoppingToken => CancellationToken.None;

    /// <summary>阻塞等待停止信号（可在活动体的空闲等待中使用）。</summary>
    /// <param name="timeout">最长等待时间。</param>
    /// <returns>已请求停止时为 <see langword="true"/>。</returns>
    bool Wait(TimeSpan timeout);
}

/// <summary>
/// **人机操作收件箱**：
/// 框架把「用户点按钮 / 填输入框 / 拨开关」投递到这里，**模块在自身线程上消费**
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
/// **元素外观的设定面**：模块把"界面上某个元素现在是什么颜色"告诉框架。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：`SubmitInput` 返回的 `CommandResult` **只反映"是否投递入队"**，
///     不反映模块的处理结果（转换失败、HEX 非法……都发生在**模块内部**，此时投递是成功的）⇒
///     没有本通道时，模块**够不到**界面，也就无法让"这一格现在不对"在屏幕上可见；</item>
///   <item><b>它是基础外观能力，不含语义</b>：框架**不认识**"红 = 失败"这类约定 ——
///     颜色由模块给，什么时候给、给什么色，全归模块的 C# 逻辑。
///     若把"成功/失败"做成参数，就等于框架替模块规定了"失败必须长这样"，那正是要避免的限定；</item>
///   <item><b>粒度</b>：按**数据源名**定位（源名是模块唯一知道的键；元素 ID 不存在）。
///     同一源名被两个元素引用 ⇒ 两个元素同色；</item>
///   <item><b>可回退</b>：传 <see langword="null"/> ⇒ 该键位回到声明 / 主题给的值；</item>
///   <item><b>纪律</b>：各方法**不抛**（非法源名只记诊断计数），与 `Issue` / 业务状态同一口径。</item>
/// </list>
/// </remarks>
public interface IViewAppearanceAccess
{
    /// <summary>设某个元素的**边框色**（ARGB —— **绝对色**，深浅主题与用户改色都不影响它）。</summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="argb">颜色（<c>0xAARRGGBB</c>）；<see langword="null"/> ⇒ 回到主题给的边框。</param>
    /// <remarks>
    /// 与 <see cref="SetOutlineRole"/> 是**同一键位的两种写法**（后写的那次生效，两者互斥）：
    /// 这条给"我就要这个色"（刻意固定）；那条给"我要配色表里的某一支"（随主题与用户改色跟随）。
    /// </remarks>
    void SetOutlineColor(string sourceName, uint? argb);

    /// <summary>
    /// 设某个元素的**边框色**（**角色词** —— 相对色：随深浅主题与用户改色自动跟随）。
    /// </summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="role">
    /// 角色词（见 <see cref="Views.ViewInkRoles"/>，如 <c>danger</c> / <c>muted</c>）；
    /// <see langword="null"/> ⇒ 回到主题给的边框。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>为什么要有它</b>：用 <see cref="SetOutlineColor"/> 传一个**写死的红**，用户在设置里改了
    /// <c>SemanticError</c>、或换了深浅主题，那一圈红纹丝不动。
    /// 模块想说的其实是"这一格现在不对"（语义），而不是"我要那一支红"（色值）；
    /// 角色词让它只说出前半句，配色表怎么长由用户定。
    /// </para>
    /// <para>
    /// <b>仍然不含业务语义</b>：框架不认识"失败"，它只知道"这一格要 <c>danger</c> 这一支色" ——
    /// 该不该标、什么时候标，仍是模块的判断。
    /// </para>
    /// <para>
    /// <b>纪律</b>：与同族一样**不抛**；没登记过的词视同没设（界面回落到声明 / 主题那一档）。
    /// </para>
    /// </remarks>
    void SetOutlineRole(string sourceName, string? role);

    /// <summary>设某个元素的**文字色**（ARGB —— **绝对色**，刻意固定）。</summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="argb">颜色（<c>0xAARRGGBB</c>）；<see langword="null"/> ⇒ 回到声明 / 主题给的颜色。</param>
    void SetTextColor(string sourceName, uint? argb);

    /// <summary>设某个元素的**文字色**（**角色词** —— 相对色，随主题与用户改色跟随）。</summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="role">角色词（见 <see cref="Views.ViewInkRoles"/>）；<see langword="null"/> ⇒ 回到声明 / 主题。</param>
    /// <remarks>与 <see cref="SetOutlineRole"/> 同一条口径：它是同一键位的另一种写法，两者互斥。</remarks>
    void SetTextRole(string sourceName, string? role);

    /// <summary>设某个元素的**文字大小**（DIP）。</summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="dips">字号（DIP）；<see langword="null"/> ⇒ 回到声明 / 主题给的那一档。</param>
    /// <remarks>
    /// 与颜色同档：**基础外观能力，不含语义**。"这条提示该用小一号字"是模块自己的排版判断，
    /// 框架只负责把它画出来 —— 所以它和 <see cref="SetTextColor"/> 一样收的是**值**，不是"重要/次要"这类档位名
    /// （档位名会把"什么算次要"写成框架的规矩）。
    /// </remarks>
    void SetTextSize(string sourceName, double? dips);

    /// <summary>
    /// **把某一格现在设成只读 / 可编辑**（运行期开关）。
    /// </summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <param name="isReadOnly">是否只读。</param>
    /// <remarks>
    /// <para>
    /// 与声明里的 <c>readonly</c> 分工：那个是**这一格一贯只读**（静态、由作者定）；
    /// 这个是**它现在暂时不让改**（运行期、由模块按业务定）。例：自动发送进行中把「间隔」锁住，
    /// 免得中途改值让发送节奏变形。
    /// </para>
    /// <para>
    /// 仍然是**基础能力、不含语义**：框架不认识"为什么锁"，它只会把这一格画成只读的样子
    /// （灰化、不被光标强占、点进去也改不了字）—— 要不要锁、锁多久，是模块的判断。
    /// </para>
    /// <para><b>纪律</b>：与同族一样**不抛**（非法源名只记诊断计数）；传 <see langword="false"/> ⇒ 回到可编辑。</para>
    /// </remarks>
    void SetReadOnly(string sourceName, bool isReadOnly);

    /// <summary>
    /// **把某一格现在设成可用 / 不可用**（运行期开关）。
    /// </summary>
    /// <param name="sourceName">该元素绑定的源名（显示源 / 输入源 / 命令 / 配置字段）。</param>
    /// <param name="isEnabled">是否可用。</param>
    /// <remarks>
    /// <para>
    /// 与 <see cref="SetReadOnly"/> 的分工：只读解决“能看不能改”；本方法解决“现在整格不可操作”
    /// （例如会话打开后不允许再点“打开”，或参数在运行中不允许改）。
    /// </para>
    /// <para><b>纪律</b>：与同族一样不抛；非法源名只记诊断计数。</para>
    /// </remarks>
    void SetEnabled(string sourceName, bool isEnabled);

    /// <summary>
    /// **把这一格上用户刚做的那次交互复原**（勾选框拨回、滑块滑回）。
    /// </summary>
    /// <param name="sourceName">该元素绑定的数据源名。</param>
    /// <remarks>
    /// <para>
    /// <b>用在"动作不成立"的场合</b>：用户勾了「连续发送」，但模块判定发不出去（没内容 / 没设间隔）
    /// ⇒ 模块说"把这一格复原"，界面就把它拨回未选中 —— 用户看到的语义是
    /// **"这个操作没成立，它自己退回去了"**，而不是"我勾了但它没反应"。
    /// </para>
    /// <para>
    /// <b>它是基础能力，不含语义</b>：名字刻意不叫 "ReportFailed"、参数里**没有"成功/失败"** ——
    /// 框架不认识业务上的成败，它只会"把这一格复原"；要不要复原、什么时候复原，是模块的判断。
    /// 与 <see cref="SetOutlineColor"/> 配合用：**复原**是"把动作撤掉"，**标色**是"告诉用户为什么"。
    /// </para>
    /// <para><b>纪律</b>：**不抛**（非法源名只记诊断计数）。</para>
    /// </remarks>
    void ResetInteraction(string sourceName);
}

/// <summary>
/// 模块上下文：模块**唯一**的框架访问面（不持有框架对象引用）。
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

    /// <summary>
    /// **外部接入的存取面**（双向数据 + 打开/关闭请求 + 状态/计量快照）。
    /// </summary>
    /// <remarks>
    /// 未接线时回落到 <see cref="ExternalAccessDefaults.Empty"/>：请求被忽略、读无数据、写失败，
    /// **不抛异常**（"框架还没接线"不该让模块崩）。
    /// </remarks>
    IExternalAccess Access => ExternalAccessDefaults.Empty;

    /// <summary>配置读写。</summary>
    IConfigurationAccess Configuration { get; }

    /// <summary>Issue 上报（可呈现的问题；**业务状态不入 Issue**）。</summary>
    IIssueReporter Issues { get; }

    /// <summary>诊断日志（经框架汇集，可观测性 L3）。</summary>
    IModuleLogger Log { get; }

    /// <summary>业务状态上报（框架只登记/呈现、**不解释语义**）。</summary>
    IBusinessStateReporter State { get; }

    /// <summary>
    /// **元素外观的设定面**（模块 → 框架的「请求」方向）：给某个元素设边框色 / 文字色。
    /// </summary>
    /// <remarks>
    /// 外观是**基础能力**，不含任何语义：框架不认识"红 = 失败"这类约定，颜色与它何时变
    /// 全归模块的 C# 逻辑（<see cref="IViewAppearanceAccess"/>）。
    /// </remarks>
    IViewAppearanceAccess Appearance { get; }

    /// <summary>停止信号（长期活动必须响应）。</summary>
    IStopSignal StopSignal { get; }

    /// <summary>
    /// **全局计时面**：模块的等待/周期节拍一律走它，不得自建 Timer / Thread.Sleep / DispatcherTimer。
    /// </summary>
    IModuleTiming Timing { get; }

    /// <summary>**人机操作收件箱**（命令 / 输入；模块在自身线程消费）。</summary>
    IModuleInbox Inbox { get; }

    /// <summary>
    /// **显示内容的请求面**（模块 → 框架的「请求」方向）：目前只有「**清空某个显示源**」。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ViewState"/> 的分界：那边是**只读**（模块改不了用户的选中页与编辑内容）；
    /// 这里能动的是**模块自己发布出去的显示内容**（如 A5 文本日志的缓冲）。语义见 <see cref="IViewDisplayAccess"/>。
    /// </remarks>
    IViewDisplayAccess Display { get; }

    /// <summary>
    /// **视图状态回读**（只读快照）：当前选中页 / 输入框当前文本 / 显示区保留的文本。
    /// </summary>
    /// <remarks>
    /// <b>方向纪律的增补</b>：既有纪律是"模块 → 框架 = 声明/上报/请求；框架 → 模块 = 钩子 + 投递回调"；
    /// 本属性是**框架 → 模块的只读事实**（不是回调、不反向操作 UI）—— 语义见 <see cref="IViewStateReader"/>。
    /// <b>只增成员</b>：模块实现者无需改动（属性由框架提供），既有模块零影响。
    /// </remarks>
    IViewStateReader ViewState { get; }

    /// <summary>
    /// **文件选择的请求面**（模块 → 框架的「请求」方向）：模块说"帮我弹一次系统选择器"，
    /// 选定的路径经**输入通道**投回（见 <see cref="IFileAccess"/>）。
    /// </summary>
    /// <remarks>
    /// <b>文件访问是能力，不是一种控件</b>（<see cref="ModuleCapability.File"/>）：
    /// "什么时候要用户指一个文件路径"是模块逻辑（命令处理里决定），不是界面声明能表达的事 ——
    /// 做成按钮型控件会让声明面去描述"宿主对话怎么配"（过滤串 / 预填名 / 三种模式），
    /// 而那不是界面事实；那个控件还会把路径写进它自己的输入源，造成同一个值有两个存放处。
    /// </remarks>
    IFileAccess Files { get; }

    /// <summary>
    /// **模块自有资源包的只读访问面**：按 kind + 相对路径读**本模块目录下** `resources/` 的文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与标准文本 / 图标的分工：`i18n` / `icons` 是**框架认识的两种 kind**，由 `TextCatalog` / `IconCatalog`
    /// 按拥有者读；本接口是给模块**自定义 kind**（模板、字典、脚本片段……）用的，
    /// 模块不需要改框架的 <see cref="ResourceKinds"/>，框架也不解释这些文件的内容。
    /// </para>
    /// <para>
    /// 未接线时回落到 <see cref="ResourceAccess.Empty"/>：读不到东西、但**不抛异常**。
    /// </para>
    /// </remarks>
    IResourceAccess Resources => ResourceAccess.Empty;
}
