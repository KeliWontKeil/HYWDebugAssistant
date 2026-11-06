using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// **一个接收口的队列流动状态**（深度 / 峰值 / 丢弃账本 / 饱和压力）——模块与诊断读同一份事实。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是"状态"而不是事件</b>：饱和与丢弃都是持续存在的事实，读到的永远是同一份当前值，
/// 不会出现"事件与快照不同步"（与外部接入快照同一手法）。
/// </para>
/// <para>
/// <b>字节口径</b>：<see cref="DroppedBytes"/> 是**契约尺寸 × 被丢弃单元数**（单元在队列里就是定长一份），
/// 它**不得**与外部接入会话的"有效字节"相加——两者量的不是同一种东西（那边是业务数据字节，
/// 这边是承载缓冲的尺寸）。合并相加会得到一个既不是搬运量也不是有效数据量的数。
/// </para>
/// <para>
/// <b>饱和</b>：深度到达提示阈值并**持续**超过保持时长才算饱和（判据与迟滞由框架的队列压力算法统一给出，
/// 模块不得自行按深度估算），因此它表达的是"这个口已经堵了一阵"，而不是"刚刚有过一次尖峰"。
/// </para>
/// </remarks>
/// <param name="Depth">当前队列深度（单元数）。</param>
/// <param name="Capacity">队列容量（单元数；按口计）。</param>
/// <param name="PeakDepth">观察到的最大深度（单元数）。</param>
/// <param name="Enqueued">累计入队数（含此后被丢弃的）。</param>
/// <param name="DroppedUnits">累计丢弃单元数（**该口账本上全部原因之和**）。</param>
/// <param name="DroppedBytes">累计丢弃字节数（= 契约尺寸 × 单元数；口径见类型说明）。</param>
/// <param name="DroppedWhileSaturatedUnits">**本次饱和段内**的丢弃单元数（离开饱和段即归零）。</param>
/// <param name="Saturated">当前是否处于饱和（已持续超过保持时长）。</param>
/// <param name="SaturatedFor">已持续饱和的时长（未饱和时为 <see cref="TimeSpan.Zero"/>）。</param>
/// <param name="LastDropReason">最近一次丢弃的原因（从未丢弃时为 <see langword="null"/>）。</param>
/// <param name="SinceLastDrop">距最近一次丢弃的时长（从未丢弃时为 <see langword="null"/>）。</param>
/// <param name="DropActive">当前丢弃 episode 是否仍在进行；成功出队/清空后为 false。</param>
/// <param name="BacklogStatusActive">状态行是否应显示积压（带小滞回，不等于“深度 &gt; 0”）。</param>
/// <param name="DropStatusActive">状态行是否应显示丢弃（带恢复保持，不要求原始丢弃仍在进行）。</param>
public readonly record struct PortFlowState(
    int Depth,
    int Capacity,
    int PeakDepth,
    long Enqueued,
    long DroppedUnits,
    long DroppedBytes,
    long DroppedWhileSaturatedUnits,
    bool Saturated,
    TimeSpan SaturatedFor,
    DropReason? LastDropReason,
    TimeSpan? SinceLastDrop,
    bool DropActive = false,
    bool BacklogStatusActive = false,
    bool DropStatusActive = false)
{
    /// <summary>队列水位占比（0–1；容量为 0 时为 0）。</summary>
    public double Fill => Capacity > 0 ? Math.Clamp((double)Depth / Capacity, 0, 1) : 0;

    /// <summary>"没有队列"的空状态（深度、容量、计数全为 0，从未丢弃）。</summary>
    public static PortFlowState None => default;
}
