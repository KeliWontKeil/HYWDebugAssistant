using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Binding;

/// <summary>
/// 绑定的诊断计数（可观测性 L4）：投递数 / 丢弃数 / 最后数据时间。
/// </summary>
/// <remarks>
/// 这些数据只在框架侧用于诊断呈现，**不进数据面**（主文档 §5.4.2）。
/// 丢弃**按原因分类**（`DroppedByReason`）：`停流未投递` 与 `传输丢弃` 不得混算；
/// `Dropped` 保留为**总数**（向后兼容，等于各原因之和）。
/// <para>
/// 原因词表是契约层的 <see cref="DropReason"/>（与队列轴、会话轴共用一套），
/// 本类型不再自带一份枚举——两条轴各写一套词表，读出来的数就再也对不上账。
/// </para>
/// </remarks>
public sealed class BindingDiagnostics
{
    private static readonly int ReasonCount = Enum.GetValues<DropReason>().Length;

    private readonly long[] _droppedByReason = new long[ReasonCount];
    private long _published;
    private long _payloadBytes;
    private long _delivered;
    private long _dropped;
    private long _lastDataTicks = -1;
    private long _lastSequence;

    /// <summary>
    /// **该源发布的数据报数**（**每个数据报一次**——与按宿计的 <see cref="Delivered"/> 区分）。
    /// </summary>
    /// <remarks>
    /// "**单元速率（按源计）**"的分母即本计数（**多播不会把它放大**）；
    /// **多源网络薄旁路头的序号来源**（序号按源递增，1 起）。
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
    /// <param name="payloadBytes">
    /// 本次数据报的**有效字节数**（业务数据；由 <see cref="Binding.PayloadBytesOfUnit"/> 给出）——
    /// 第 3 档速率（有效载荷字节速率）的分子即本累计值（速率只计有效数据）。
    /// </param>
    /// <remarks>
    /// 在宿循环**之前**调用，故它不受多播放大、也不因某个宿满队而漏计（与"按源计"的口径一致）。
    /// </remarks>
    public void RecordPublished(long payloadBytes)
    {
        Interlocked.Increment(ref _published);
        Interlocked.Add(ref _payloadBytes, payloadBytes);
    }

    /// <summary>
    /// **累计有效载荷字节**（业务数据字节之和；按源计）——有效载荷字节速率的分子。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Published"/> **同一处**累加（同一次发布）：两者相除必须是"每单元平均有效字节"，
    /// 分开两处写就会出现"分子分母不是同一批数据报"的经典缺陷。
    /// </remarks>
    public long PayloadBytes => Interlocked.Read(ref _payloadBytes);

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
///   <item><b>契约</b>：以源口的契约为准（目标契约已在建网时校验为"形状 + 类型身份一致"）；</item>
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
    /// 创建绑定（**逐源**）：多源网络为**每个源各建一条绑定**，
    /// 绑定携带**它自己的源**与**该源可用的宿子集**（逐 (源,宿) 对兼容过滤的结果）。
    /// </summary>
    /// <param name="network">对应的网络声明。</param>
    /// <param name="source">本绑定负责的**源成员**。</param>
    /// <param name="targets">该源可投递的**宿成员子集**（已逐对校验兼容）。</param>
    /// <param name="layout">该源口的契约布局。</param>
    /// <param name="metering">
    /// 源口**类型声明的计量块**（未声明类型 ⇒ <see langword="null"/> = 用缺省：有效载荷 = 契约尺寸、
    /// 每单元 1 样本 1 通道、无标称速率）。
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

        _staticPayloadBytes = Math.Clamp(Metering.PayloadSizeInBytes ?? layout.SizeInBytes, 0, layout.SizeInBytes);
        _payloadLength = PayloadLengthReader.Resolve(layout, Metering.PayloadLengthField);
    }

    /// <summary>未声明"有效长度字段"时的**静态**每单元有效字节数（= `PayloadSizeInBytes` ?? 契约尺寸）。</summary>
    private readonly int _staticPayloadBytes;

    /// <summary>有效长度字段的读取器（**加载期解析一次**：偏移 + 原语；未声明 / 不可用 ⇒ null）。</summary>
    private readonly PayloadLengthReader? _payloadLength;

    /// <summary>对应的网络声明。</summary>
    public Network Network { get; }

    /// <summary>本绑定负责的**源成员**（多源网络下每个源一条绑定）。</summary>
    public NetworkMember Source { get; }

    /// <summary>本绑定可投递的**宿成员子集**（逐 (源,宿) 对兼容过滤后）。</summary>
    public IReadOnlyList<NetworkMember> Targets { get; }

    /// <summary>数据单元的契约布局。</summary>
    public ContractLayout Layout { get; }

    /// <summary>
    /// **计量块**（来自源口类型的声明；未声明类型 ⇒ 缺省值）。
    /// </summary>
    /// <remarks>
    /// 框架据它**机械**给出四档速率（零解析）：有效载荷字节速率 · 样本 / 通道样本速率；
    /// 缺省语义：`PayloadSizeInBytes` 空 ⇒ 取 <see cref="Layout"/> 的契约尺寸；
    /// 声明了 `PayloadLengthField` ⇒ **逐单元**读那个字段（变长载荷的真实有效字节）。
    /// </remarks>
    public MeteringBlock Metering { get; }

    /// <summary>诊断计数。</summary>
    public BindingDiagnostics Diagnostics { get; } = new();

    /// <summary>
    /// **一个数据单元的"有效字节数"**（有效载荷字节速率的分子；速率只计有效数据）。
    /// </summary>
    /// <param name="unit">数据单元字节（长度已由调用方校验 ≥ 契约尺寸）。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>声明了 **<see cref="MeteringBlock.PayloadLengthField"/>** ⇒ 读该字段（整型、小端）、
    ///     夹到 <c>[0, 契约尺寸]</c> ⇒ 变长载荷（如 <c>{len:u16,data:bytes[N]}</c>）拿到**真实**业务字节；
    ///     读不出来（声明了形状里却没有 / 原语不对 / 字节不够）⇒ **回落到静态值**，不放任它静默变 0
    ///     （声明错误由 <c>ModuleContract</c> 的契约自检在加载期报出来）；</item>
    ///   <item>未声明该字段 ⇒ 静态值 = `PayloadSizeInBytes`（缺省 = 契约尺寸）⇒ **旧模块行为不变**。</item>
    /// </list>
    /// </remarks>
    public int PayloadBytesOfUnit(ReadOnlySpan<byte> unit)
    {
        if (_payloadLength is { } reader && reader.TryRead(unit, out var declared))
        {
            return Math.Clamp(declared, 0, Layout.SizeInBytes);
        }

        return _staticPayloadBytes;
    }

    /// <summary>停止投递（上游 Disabled 或停流时；绑定保留）。</summary>
    public bool IsSuspended { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Network} [{Layout.SizeInBytes}B]";
}
