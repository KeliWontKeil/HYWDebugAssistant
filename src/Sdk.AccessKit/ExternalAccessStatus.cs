namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// 模块给外部接入状态行的一句话：文本 + 可选严重度。
/// </summary>
/// <remarks>
/// 严重度为空时由通道按**会话状态**给（故障红、开合黄、其余随连接状态）；
/// 模块自己判定的压力（例如本模块 <c>tx</c> 口队列饱和）要带自己的严重度。
/// </remarks>
/// <param name="Text">给人看的一句话（模块自己的文案）。</param>
/// <param name="Severity">外观角色词（<c>danger</c> / <c>warning</c> …）；空 = 由通道按会话状态给。</param>
public readonly record struct ExternalAccessStatus(string Text, string? Severity = null);

/// <summary>
/// **会话侧**压力种类：通道按固定优先级询问模块"这句话怎么说"。
/// </summary>
/// <remarks>
/// <para>
/// 只包含**框架会话快照**能给的事实；口侧压力（本模块 <c>tx</c> 队列饱和、口丢弃）不在其中 ——
/// 那是模块间通信的事实，由模块在自己的状态句里表达。
/// </para>
/// <para>
/// 模块返回 <see langword="null"/> = "我不说"，通道继续往下一档回落。
/// </para>
/// </remarks>
public enum ExternalAccessPressure
{
    /// <summary>接收侧已经丢过单元（会话轴）。</summary>
    ReceiveLost = 0,

    /// <summary>发送侧已经丢过单元（写失败 / 发送积压挤旧或过期）。</summary>
    SendLost = 1,

    /// <summary>发送积压（设备写缓冲）还有待写单元，尚未丢。</summary>
    SendBacklogged = 2,

    /// <summary>接收队列饱和（深度到阈值并持续够久）。</summary>
    ReceiveSaturated = 3,
}
