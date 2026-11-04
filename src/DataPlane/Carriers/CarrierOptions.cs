using HYWDebugAssistant.DataPlane.Queues;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 承载参数（默认值来自应用级设置；此处为 v1 过渡默认值）。
/// </summary>
public sealed record CarrierOptions
{
    /// <summary>
    /// 输入队列容量（单元数）——**按 (实例, 口) 计**，不是按实例计。
    /// </summary>
    /// <remarks>
    /// 每个接收口各有一条独立队列、各自享有本容量 ⇒ 一个口满载**不会**占用其它口的缓冲额度
    /// （这是"消除跨口队头阻塞"的关键）。v1 默认 1024（M1-13 实测后回写最终值）。
    /// </remarks>
    public int QueueCapacity { get; init; } = 1024;

    /// <summary>满时丢弃方式（默认丢最旧保最新）。</summary>
    public DropPolicy DropPolicy { get; init; } = DropPolicy.DropOldest;

    /// <summary>停止协议的有界等待时限。v1 默认 5 s。</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// **兜底唤醒间隔**：数据 / 命令 / 会话事件都由信号**立即**唤醒，这个间隔只是"万一漏了一个信号"的保险。
    /// </summary>
    /// <remarks>
    /// 它不是投递节拍，**不承担数据延迟**：入队在返回前就已经唤醒投递循环。
    /// 取值放得很宽是因为它的唯一作用是把"漏唤醒导致实例停摆"换成"一秒后自愈"；
    /// 真正的周期需求（设备探活）由 <c>NextWakeupDue</c> 单独给出，比它短就按短的等。
    /// </remarks>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// **饱和提示的深度阈值**（单元数，按口计）：队列深度到达它就开始计"堵了多久"。
    /// </summary>
    /// <remarks>
    /// 它只影响**饱和判据**，不影响丢弃（丢弃仍由容量与 <see cref="DropPolicy"/> 决定）。
    /// 默认 8：对毫秒级刷新的实时监视来说，积压到 8 个单元就已经值得在界面上说一句"这个口堵了"。
    /// </remarks>
    public int HintDepth { get; init; } = 8;

    /// <summary>
    /// **饱和保持时长**：深度到达阈值并持续这么久才算真的饱和。
    /// </summary>
    /// <remarks>
    /// 这是迟滞的时间维度：没有它，一次突发尖峰就会让界面闪一下告警（而链路其实瞬间就排空了）。
    /// 默认 500 ms。
    /// </remarks>
    public TimeSpan HintHold { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// **投递时间片**：一次唤醒最多连续投递多久，超过就让出线程去做别的工作（停止响应、辅助工作源）。
    /// </summary>
    /// <remarks>
    /// 它防的是"某个口长期有货 ⇒ 投递循环永远不回头"造成的饿死（停止信号、命令、会话都被排在后面）。
    /// 默认 4 ms：远小于人眼可感的延迟，又足够摊薄每次唤醒的固定开销。
    /// </remarks>
    public TimeSpan DeliverySlice { get; init; } = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// **最大积压年龄**：单元在队列里停留超过它就被清出（过期丢弃）。
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> = **不限**（默认）：积压多久都照原样送达。
    /// 非零时，迟到的数据对实时监视没有价值——投给消费方只会让界面显示一个已经过期的值。
    /// </remarks>
    public TimeSpan MaxBacklogAge { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// **外部接入的写超时**：一次写最多等多久，避免一个卡住的传输把实例线程拖死。
    /// </summary>
    /// <remarks>
    /// 由框架在会话打开/参数变更时下发给支持该能力的适配件；适配件自己决定落到哪个底层开关。
    /// 默认 1000 ms。
    /// </remarks>
    public TimeSpan ExternalWriteTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>**探活节拍**（仅需要确认端口仍在的接入使用；默认 250 ms）。</summary>
    public TimeSpan LivenessInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>**会话发送积压容量**（字节；设备写缓冲；默认 256 KB；对新建 / 重开的会话生效）。</summary>
    public int SendBacklogBytes { get; init; } = 256 * 1024;

    /// <summary>**适配件字节暂存容量**（按字节；默认 64 KB；对新建 / 重开的会话生效）。</summary>
    public int AdapterBufferBytes { get; init; } = 64 * 1024;

    /// <summary>**适配件关闭等待**（重开占着端口的会话时先等旧传输关闭；默认 1 s）。</summary>
    public TimeSpan AccessCloseTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>**设备丢失判定次数**（连续探活缺失多少次才判断开；默认 2）。</summary>
    public int DeviceLostMisses { get; init; } = 2;
}
