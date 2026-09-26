using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Core.Binding;

/// <summary>
/// **未投递原因**（ADR-0046 D8 / ADR-0050 Q5：丢弃必须分类，不得混算）。
/// </summary>
public enum DropReason
{
    /// <summary>**停流未投递**：上游挂起（`Disabled`）或网络停流 —— **合法状态，不是故障**。</summary>
    Stalled = 0,

    /// <summary>**队列满**：目标实例输入队列已满（best-effort 丢弃，ADR-0050 D1）。</summary>
    QueueFull = 1,

    /// <summary>**无承载**：目标的承载（线程/队列）不存在或未就绪。</summary>
    NoCarrier = 2,

    /// <summary>**载荷非法**：载荷长度小于契约尺寸（长度校验失败）。</summary>
    InvalidPayload = 3,
}

/// <summary>
/// 绑定的诊断计数（可观测性 L4）：投递数 / 丢弃数 / 最后数据时间。
/// </summary>
/// <remarks>
/// 这些数据只在框架侧用于诊断呈现，**不进数据面**（主文档 §5.4.2）。
/// **【ADR-0050 Q5】** 丢弃**按原因分类**（`DroppedByReason`）：`停流未投递` 与 `传输丢弃` 不得混算；
/// `Dropped` 保留为**总数**（向后兼容，等于各原因之和）。
/// </remarks>
public sealed class BindingDiagnostics
{
    private static readonly int ReasonCount = Enum.GetValues<DropReason>().Length;

    private readonly long[] _droppedByReason = new long[ReasonCount];
    private long _published;
    private long _delivered;
    private long _dropped;
    private long _lastDataTicks = -1;
    private long _lastSequence;

    /// <summary>
    /// **该源发布的数据报数**（**每个数据报一次**——与按宿计的 <see cref="Delivered"/> 区分）。
    /// </summary>
    /// <remarks>
    /// 【ADR-0046 D8】"**单元速率（按源计）**"的分母即本计数（**多播不会把它放大**）；
    /// 【ADR-0050 Q6(c)】**多源网络薄旁路头的序号来源**（序号按源递增，1 起）。
    /// </remarks>
    public long Published => Interlocked.Read(ref _published);

    /// <summary>累计投递数（成功写入目标队列的次数）。</summary>
    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>累计丢弃数（**总数**；等于各 <see cref="DroppedByReason"/> 之和）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>最后投递序号。</summary>
    public long LastSequence => Interlocked.Read(ref _lastSequence);

    /// <summary>按原因分类的丢计数（索引 = <see cref="DropReason"/>）。</summary>
    /// <param name="reason">未投递原因。</param>
    /// <returns>该原因的累计次数。</returns>
    public long DroppedByReason(DropReason reason) => Interlocked.Read(ref _droppedByReason[(int)reason]);

    /// <summary>最后数据时间（无数据时为 <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDataTime
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDataTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 记录一次**发布**：该源产出了一个**合法载荷的数据报**并进入投递阶段（**每个数据报一次**）。
    /// </summary>
    /// <remarks>在宿循环**之前**调用，故它不受多播放大、也不因某个宿满队而漏计。</remarks>
    public void RecordPublished() => Interlocked.Increment(ref _published);

    /// <summary>记录一次成功投递。</summary>
    public void RecordDelivered()
    {
        Interlocked.Increment(ref _delivered);
        Interlocked.Increment(ref _lastSequence);
        Interlocked.Exchange(ref _lastDataTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>记录一次丢弃（不分原因；保留给"原因未知"的旧路径）。</summary>
    public void RecordDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>**按原因**记录一次未投递（同时计入总数）。</summary>
    /// <param name="reason">未投递原因。</param>
    public void RecordDropped(DropReason reason)
    {
        Interlocked.Increment(ref _droppedByReason[(int)reason]);
        Interlocked.Increment(ref _dropped);
    }
}

/// <summary>
/// 绑定：**网络在运行时的投影**（主文档 §6.1：Composition ≠ Runtime Binding）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>数据直通</b>：绑定把单元直接写入目标实例的输入队列，Core **不经手数据**（§7.2/P2/E1）；</item>
///   <item><b>1:N 多播</b>：目标列表逐个写入，无转发对象；</item>
///   <item><b>契约</b>：以源口的契约为准（目标契约已在建网时校验为"接收 ⊇ 发送"）；</item>
///   <item><b>生命周期</b>：绑定随网络存在；网络删除即绑定销毁（`Disabled` 时保留但停流，见数据面队列）。</item>
/// </list>
/// </remarks>
public sealed class Binding
{
    /// <summary>创建绑定（单源：源 = 网络的**主源**、宿 = 网络的宿集合）。</summary>
    /// <param name="network">对应的网络声明。</param>
    /// <param name="layout">源口的契约布局（数据单元的定长形状）。</param>
    public Binding(Network network, ContractLayout layout)
        : this(network, network.Source, network.Targets, layout)
    {
    }

    /// <summary>
    /// 创建绑定（**逐源**）【ADR-0050 M2/M7】：多源网络为**每个源各建一条绑定**，
    /// 绑定携带**它自己的源**与**该源可用的宿子集**（逐 (源,宿) 对兼容过滤的结果）。
    /// </summary>
    /// <param name="network">对应的网络声明。</param>
    /// <param name="source">本绑定负责的**源成员**。</param>
    /// <param name="targets">该源可投递的**宿成员子集**（已逐对校验兼容）。</param>
    /// <param name="layout">该源口的契约布局。</param>
    /// <param name="metering">
    /// 源口**类型声明的计量块**（未声明类型 ⇒ <see langword="null"/> = 用缺省：有效载荷 = 契约尺寸、
    /// 每单元 1 样本 1 通道、无标称速率）【ADR-0046 D8】。
    /// </param>
    public Binding(
        Network network,
        NetworkMember source,
        IReadOnlyList<NetworkMember> targets,
        ContractLayout layout,
        MeteringBlock? metering = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(layout);

        Network = network;
        Source = source;
        Targets = targets;
        Layout = layout;
        Metering = metering ?? MeteringBlock.Default;
    }

    /// <summary>对应的网络声明。</summary>
    public Network Network { get; }

    /// <summary>本绑定负责的**源成员**（多源网络下每个源一条绑定）。</summary>
    public NetworkMember Source { get; }

    /// <summary>本绑定可投递的**宿成员子集**（逐 (源,宿) 对兼容过滤后）。</summary>
    public IReadOnlyList<NetworkMember> Targets { get; }

    /// <summary>数据单元的契约布局。</summary>
    public ContractLayout Layout { get; }

    /// <summary>
    /// **计量块**（来自源口类型的声明；未声明类型 ⇒ 缺省值）【ADR-0046 D8】。
    /// </summary>
    /// <remarks>
    /// 框架据它**机械**给出四档速率（零解析）：有效载荷字节速率 · 样本 / 通道样本速率；
    /// 缺省语义：`PayloadSizeInBytes` 空 ⇒ 取 <see cref="Layout"/> 的契约尺寸。
    /// </remarks>
    public MeteringBlock Metering { get; }

    /// <summary>诊断计数。</summary>
    public BindingDiagnostics Diagnostics { get; } = new();

    /// <summary>停止投递（上游 Disabled 或停流时；绑定保留）。</summary>
    public bool IsSuspended { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Network} [{Layout.SizeInBytes}B]";
}
