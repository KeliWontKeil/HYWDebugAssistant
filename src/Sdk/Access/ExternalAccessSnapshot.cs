using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 一条外部接入会话的**可读快照**：状态 + 端点 + 最近失败 + 计量 + 丢弃分账。
/// </summary>
/// <remarks>
/// <para>
/// 状态是"当前事实"，**不做事件面**：模块在自己的循环或回调里读它；读到的永远是同一份事实，
/// 不会出现"事件与快照不同步"。
/// </para>
/// <para>
/// 计数口径：收 / 发按**有效字节**计；丢弃按被丢弃单元的字节数计；计数在实例生命周期内累计，
/// 重开会话不清零——**唯一的归零路径是清空计数**。
/// </para>
/// <para>
/// <b>丢弃的不变量</b>（三个合计各自闭合，任何一处漏记都会让它不成立）：
/// </para>
/// <list type="bullet">
///   <item><c>DroppedUnits == RejectedUnits + SendFailedUnits + ReceiveDroppedUnits + 发送积压丢弃</c>
///     （字节同构：<c>DroppedBytes == RejectedBytes + SendFailedBytes + ReceiveDroppedBytes + 发送积压字节</c>；\n    ///     发送积压丢弃 = 挤旧 + 过期 + 停止清空）；</item>
///   <item><c>ReceiveDroppedUnits == ReceiveQueueDroppedUnits + AdapterDroppedUnits + OversizedDroppedUnits + ExpiredUnits</c>
///     （字节同构，由各原因的字节字段给出）；</item>
///   <item>"收侧"的丢弃**不含**停止清尾：拆除时队列里剩下的单元不是运行期丢包，
///     它记在队列自己的账本上，不进本快照。</item>
/// </list>
/// <para>
/// 分原因字段在**没有来源上报时读 0**（未接线的来源不参与不变量，直到它开始上报）。
/// </para>
/// </remarks>
/// <param name="State">会话状态。</param>
/// <param name="Suspended">实例是否处于 Disabled（暂停）；暂停时会话被关闭但"想要打开"的意图保留。</param>
/// <param name="Endpoint">当前端点（打开 / 连接中时由适配件给出；空 = 暂无）。</param>
/// <param name="Failure">最近一次失败的结构化事实（空 = 没有失败）。</param>
/// <param name="ReceivedUnits">收到的数据单元数。</param>
/// <param name="ReceivedBytes">收到的有效字节数。</param>
/// <param name="SentUnits">发出的数据单元数。</param>
/// <param name="SentBytes">发出的有效字节数。</param>
/// <param name="DroppedUnits">丢弃合计：被拒（未打开）+ 写失败 + 收侧丢弃；见类型说明的不变量。</param>
/// <param name="DroppedBytes">丢弃合计的字节数（与 <paramref name="DroppedUnits"/> 同构）。</param>
/// <param name="RejectedUnits">
/// 会话未打开时被拒绝的发送单元数（会话级累计；同时计入 <paramref name="DroppedUnits"/>，
/// 只有清空计数会归零）。
/// </param>
/// <param name="ActiveConnections">当前连接数（TCP 服务端等多连接会话用；其余为 0）。</param>
/// <param name="LastReceivedAt">最后一次收到数据的时刻（从未收到 = <see langword="null"/>）。</param>
public sealed record ExternalAccessSnapshot(
    ExternalAccessState State,
    bool Suspended,
    ExternalAccessEndpoint? Endpoint,
    ExternalAccessFailure? Failure,
    long ReceivedUnits,
    long ReceivedBytes,
    long SentUnits,
    long SentBytes,
    long DroppedUnits,
    long DroppedBytes,
    long RejectedUnits,
    int ActiveConnections,
    DateTimeOffset? LastReceivedAt)
{
    /// <summary>没有这条接入 / 尚未创建时的空快照。</summary>
    public static readonly ExternalAccessSnapshot None = new(
        ExternalAccessState.Closed,
        Suspended: false,
        Endpoint: null,
        Failure: null,
        ReceivedUnits: 0,
        ReceivedBytes: 0,
        SentUnits: 0,
        SentBytes: 0,
        DroppedUnits: 0,
        DroppedBytes: 0,
        RejectedUnits: 0,
        ActiveConnections: 0,
        LastReceivedAt: null);

    /// <summary>被拒（会话未打开）的**字节数**（与 <see cref="RejectedUnits"/> 同构）。</summary>
    public long RejectedBytes { get; init; }

    /// <summary>**写失败**的单元数（适配件返回失败或抛出；字节没有离开进程）。</summary>
    public long SendFailedUnits { get; init; }

    /// <summary>**写失败**的字节数。</summary>
    public long SendFailedBytes { get; init; }

    /// <summary>
    /// **收侧丢弃合计**（= 接入队列满 + 适配件溢出 + 超长 + 过期；见类型说明的不变量）。
    /// </summary>
    public long ReceiveDroppedUnits { get; init; }

    /// <summary>**收侧丢弃合计**的字节数（原始字节，不是业务有效字节）。</summary>
    public long ReceiveDroppedBytes { get; init; }

    /// <summary>收侧丢弃中**接入队列满**造成的单元数（挤旧 + 拒新）。</summary>
    public long ReceiveQueueDroppedUnits { get; init; }

    /// <summary>收侧丢弃中**适配件溢出**的单元数（适配件自己的暂存满 / 数据报被截断）。</summary>
    public long AdapterDroppedUnits { get; init; }

    /// <summary>收侧丢弃中**超长单元**的单元数（目标缓冲或声明上限装不下，绝不截断）。</summary>
    public long OversizedDroppedUnits { get; init; }

    /// <summary>收侧丢弃中**过期**的单元数（单元在队列里停留超过允许的积压时长）。</summary>
    public long ExpiredUnits { get; init; }

    /// <summary>发送积压**挤旧**的单元数（设备写长期跟不上报价时发生）。</summary>
    public long SendBacklogEvictedUnits { get; init; }

    /// <summary>发送积压挤旧的有效字节数。</summary>
    public long SendBacklogEvictedBytes { get; init; }

    /// <summary>发送积压中**过期**丢弃的单元数。</summary>
    public long SendBacklogExpiredUnits { get; init; }

    /// <summary>发送积压过期的有效字节数。</summary>
    public long SendBacklogExpiredBytes { get; init; }

    /// <summary>关闭 / 故障清空发送积压时丢弃的单元数。</summary>
    public long SendBacklogDiscardedUnits { get; init; }

    /// <summary>关闭 / 故障清空发送积压时丢弃的有效字节数。</summary>
    public long SendBacklogDiscardedBytes { get; init; }

    /// <summary>发送积压当前深度（单元数）。</summary>
    public int SendBacklogDepth { get; init; }

    /// <summary>发送积压当前字节数。</summary>
    public int SendBacklogBytes { get; init; }

    /// <summary>发送积压容量（字节）。</summary>
    public int SendBacklogCapacityBytes { get; init; }

    /// <summary>发送积压历史峰值（字节）。</summary>
    public int SendBacklogPeakBytes { get; init; }

    /// <summary>发送积压是否饱和（深度到阈值并持续够久）。</summary>
    public bool SendBacklogSaturated { get; init; }

    /// <summary>发送积压已饱和多久。</summary>
    public TimeSpan SendBacklogSaturatedFor { get; init; }

    /// <summary>接收队列当前深度（单元数）。</summary>
    public int ReceiveQueueDepth { get; init; }

    /// <summary>接收队列容量（单元数；按会话计）。</summary>
    public int ReceiveQueueCapacity { get; init; }

    /// <summary>接收队列当前是否处于饱和（深度到达阈值并持续超过保持时长）。</summary>
    public bool ReceiveSaturated { get; init; }

    /// <summary>已持续饱和的时长（未饱和为 <see cref="TimeSpan.Zero"/>）。</summary>
    public TimeSpan ReceiveSaturatedFor { get; init; }

    /// <summary>
    /// 最近一次丢弃的原因（从未丢弃 = <see langword="null"/>）。
    /// </summary>
    /// <remarks>它回答"最近这一下为什么丢"，**不回答**"丢了多少"——数量在各分原因字段里。</remarks>
    public DropReason? LastDropReason { get; init; }

    /// <summary>最近一次丢弃发生的时刻（从未丢弃 = <see langword="null"/>）。</summary>
    public DateTimeOffset? LastDropAt { get; init; }
}
