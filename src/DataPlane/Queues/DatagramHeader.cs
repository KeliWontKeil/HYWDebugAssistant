namespace HYWDebugAssistant.DataPlane.Queues;

/// <summary>
/// **薄旁路头**：数据报的传输层旁路元数据（**不进 payload 字节流**，字节守恒不受影响）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只在多源网络上带</b>：单源网络**零附加**（本类型为 <see langword="null"/>，
///         不构造、不入队、不读取）；多源网络才付这点代价——把成本放在真正需要的地方；</item>
///   <item><b>只增不改</b>：这是对「<b>包不自描述</b>」与「<b>时序只统计不附加</b>」两条不变量的
///         **局部修订**，且修订范围**仅限多源网络**；payload 仍不自描述（头是传输层旁路，永远不进载荷）；</item>
///   <item><b>谁读它</b>：传输层与**观测面**（承载诊断记录最近序号/接收时刻 ⇒ 按源精确归属、乱序/抖动可观测）。
///         模块**不**通过它取来源（应用需要来源就自带字段）——框架不替应用层做决定；</item>
///   <item><b>未来跨进程/跨机</b>：本结构即"同一语义契约"下的一跳信封（进程内零拷贝，跨进程再序列化）。</item>
/// </list>
/// </remarks>
/// <param name="SourceInstanceId">来源实例 ID。</param>
/// <param name="SourcePortName">来源发送口名。</param>
/// <param name="Sequence">
/// **按源**的序号（1 起、每个数据报一次；与按宿计的投递数不同——多播不会把它放大）。
/// </param>
/// <param name="ReceivedTimestamp">
/// 框架**投递该数据报的时刻**：<c>Stopwatch.GetTimestamp()</c>（**单调高频计数、非墙钟**；
/// 配合 <c>Stopwatch.Frequency</c> 换算秒）。跨源比较只用"差"，不用绝对时刻。
/// </param>
public readonly record struct DatagramHeader(
    string SourceInstanceId,
    string SourcePortName,
    long Sequence,
    long ReceivedTimestamp)
{
    /// <summary>来源成员字面量（<c>实例ID.口名</c>）——诊断与日志用。</summary>
    public string Source => $"{SourceInstanceId}.{SourcePortName}";

    /// <inheritdoc />
    public override string ToString() => $"{Source}#{Sequence}";
}
