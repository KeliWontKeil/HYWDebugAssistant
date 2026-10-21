namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 一条外部接入会话的**可读快照**：状态 + 最近错误 + 计量。
/// </summary>
/// <remarks>
/// <para>
/// 状态是"当前事实"，**不做事件面**：模块在自己的循环或回调里读它；读到的永远是同一份事实，
/// 不会出现"事件与快照不同步"。
/// </para>
/// <para>
/// 计数口径：收 / 发按**有效字节**计；丢弃按被丢弃单元的字节数计；计数在实例生命周期内累计，
/// 重开会话不清零。
/// </para>
/// </remarks>
/// <param name="State">会话状态。</param>
/// <param name="Suspended">实例是否处于 Disabled（暂停）；暂停时会话被关闭但"想要打开"的意图保留。</param>
/// <param name="Detail">状态说明（适配件在打开成功时给的人类可读文本，如地址；空 = 无）。</param>
/// <param name="LastError">最近一次失败的原因（空 = 没有失败）。</param>
/// <param name="ReceivedUnits">收到的数据单元数。</param>
/// <param name="ReceivedBytes">收到的有效字节数。</param>
/// <param name="SentUnits">发出的数据单元数。</param>
/// <param name="SentBytes">发出的有效字节数。</param>
/// <param name="DroppedUnits">因队列满 / 写失败 / 超长而丢弃的单元数。</param>
/// <param name="DroppedBytes">因丢弃而未到达消费方的字节数。</param>
/// <param name="ActiveConnections">当前连接数（TCP 服务端等多连接会话用；其余为 0）。</param>
/// <param name="LastReceivedAt">最后一次收到数据的时刻（从未收到 = <see langword="null"/>）。</param>
public sealed record ExternalAccessSnapshot(
    ExternalAccessState State,
    bool Suspended,
    string Detail,
    string LastError,
    long ReceivedUnits,
    long ReceivedBytes,
    long SentUnits,
    long SentBytes,
    long DroppedUnits,
    long DroppedBytes,
    int ActiveConnections,
    DateTimeOffset? LastReceivedAt)
{
    /// <summary>没有这条接入 / 尚未创建时的空快照。</summary>
    public static readonly ExternalAccessSnapshot None = new(
        ExternalAccessState.Closed,
        Suspended: false,
        Detail: string.Empty,
        LastError: string.Empty,
        ReceivedUnits: 0,
        ReceivedBytes: 0,
        SentUnits: 0,
        SentBytes: 0,
        DroppedUnits: 0,
        DroppedBytes: 0,
        ActiveConnections: 0,
        LastReceivedAt: null);
}
