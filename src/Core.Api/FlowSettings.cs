using HYWDebugAssistant.Sdk.Tuning;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **通信（数据面缓冲与流控）机制参数**：队列容量、满时策略、停止等待、饱和判据、积压上限、外部写超时、探活节拍、适配件暂存 / 关闭等待、设备丢失判定、状态显示滞回。
/// </summary>
/// <remarks>
/// <para>
/// 这一块回答"框架怎么搬运数据"，是**应用级**事实，与模块实例参数（随组合件保存）严格分开：
/// 改这里影响的是所有模块的数据通路，而不是某一个实例。
/// </para>
/// <para>
/// <b>生效时机分两类</b>——界面必须能分辨，否则用户改了容量却没看到变化，会以为设置没生效：
/// </para>
/// <list type="bullet">
///   <item><b>对新建的对象生效</b>（<see cref="QueueCapacity"/> / <see cref="DropPolicy"/> /
///     <see cref="ExternalWriteTimeoutMs"/>）：队列与会话在创建时把值取走，既有队列保持自己的容量；</item>
///   <item><b>立即生效</b>（其余各项）：改完下一拍就作用于**既有**队列与会话。</item>
/// </list>
/// <para>
/// 这里只声明**默认值与语义**（值的唯一来源），不做归一化与校验——那是设置服务的职责。
/// </para>
/// </remarks>
public sealed record FlowSettings
{
    /// <summary>
    /// **每个接收口一条队列的容量上限**（单元数；范围 1–65536，默认 1024）。**对新建队列生效**。
    /// </summary>
    /// <remarks>
    /// 容量按**口**计（不是按实例）：一个口满载不占用其它口的缓冲额度。
    /// 每口最大积压字节数 ≈ 容量 × 该口契约尺寸 —— 调大它买的是"更抗突发"，代价是内存与"迟到的数据更多"。
    /// </remarks>
    public int QueueCapacity { get; init; } = FlowDefaults.QueueCapacity;

    /// <summary>
    /// **缓冲满时丢哪一头**：<c>DropOldest</c>（默认，丢最旧保最新）/ <c>DropNewest</c>（丢最新保积压）。
    /// **对新建队列生效**。
    /// </summary>
    /// <remarks>
    /// 实时监视通常要"最新的那个值"，故默认丢旧；要让一段已积压的数据完整送出去，才选丢新。
    /// </remarks>
    public string DropPolicy { get; init; } = FlowDefaults.DropPolicy;

    /// <summary>
    /// **停止 / 移除时等待模块线程退出的上限**（秒；范围 0.1–300，默认 5）。**立即生效**。
    /// </summary>
    /// <remarks>
    /// 超时即判"线程已成为孤儿"：框架放弃等待、该实例的队列与缓冲不再归还池，并记一条 Issue。
    /// 它同时是**拆除动作的最长占用时间** ⇒ 调大会让"移除模块"变慢。
    /// </remarks>
    public double StopTimeoutSeconds { get; init; } = FlowDefaults.StopTimeoutSeconds;

    /// <summary>
    /// **饱和判定深度**（单元数；范围 1–65536，默认 8）。**立即生效**。
    /// </summary>
    /// <remarks>
    /// 队列深度到达该值并持续 <see cref="HintHoldMs"/> 之后，才把该口判为"跟不上"。
    /// 大于队列容量时按容量算（否则队列永远到不了阈值，判据会静默失效）。
    /// </remarks>
    public int HintDepth { get; init; } = FlowDefaults.HintDepth;

    /// <summary>
    /// **饱和判定持续时长**（毫秒；范围 50–10000，默认 500）。**立即生效**。
    /// </summary>
    /// <remarks>
    /// 迟滞的时间维度：越小越敏感（一次突发尖峰就报），越大越不抖（只报持续拥塞）。
    /// </remarks>
    public int HintHoldMs { get; init; } = FlowDefaults.HintHoldMs;

    /// <summary>
    /// **最大滞留时间**（毫秒；范围 0–60000，默认 0 = 不限）。**立即生效**。
    /// </summary>
    /// <remarks>
    /// 大于 0 时，在队列里等待超过该时长的单元在出队时被丢弃并计入"过期"：
    /// 迟到的实时数据投给消费方只会让界面显示一个已经过期的值。
    /// </remarks>
    public int MaxBacklogAgeMs { get; init; } = FlowDefaults.MaxBacklogAgeMs;

    /// <summary>
    /// **单次外部写超时**（毫秒；范围 50–60000，默认 1000）。**对新建 / 重新打开的会话生效**。
    /// </summary>
    /// <remarks>
    /// 它限制一次写最多占用模块线程多久；超时即判写失败（计入丢弃），而不是把实例线程一起卡住。
    /// </remarks>
    public int ExternalWriteTimeoutMs { get; init; } = FlowDefaults.ExternalWriteTimeoutMs;

    /// <summary>
    /// **探活节拍**（毫秒；范围 50–5000，默认 250）。**立即生效**。仅"需要确认端口还在"的接入使用。
    /// </summary>
    /// <remarks>
    /// 越小越能及时发现设备拔出，代价是更频繁的端口枚举；它不影响读写。
    /// </remarks>
    public int LivenessIntervalMs { get; init; } = FlowDefaults.LivenessIntervalMs;

    /// <summary>
    /// **会话发送积压容量**（字节；范围 8192–4194304，默认 262144）。**对新建 / 重新打开的会话生效**。
    /// </summary>
    /// <remarks>
    /// 它是"设备写缓冲"：写泵线程按此容量吸收写阻塞；满时丢最旧并计入发送积压丢失。
    /// 与口队列（模块收件箱）分层，各记各的轴。
    /// </remarks>
    public int SendBacklogBytes { get; init; } = FlowDefaults.SendBacklogBytes;

    /// <summary>
    /// **适配件字节暂存容量**（字节；范围 4096–1048576，默认 65536）。**对新建 / 重新打开的会话生效**。
    /// </summary>
    /// <remarks>
    /// 流式接入的接收暂存按字节流计：容量要能装下一次写期间回显的字节（921600 下 64 KB 约 0.6 s；115200 下约 5.6 s）。
    /// 它与会话队列容量不是一层：这里满了才会在适配件层丢最旧字节并计入"适配器溢出"。
    /// </remarks>
    public int AdapterBufferBytes { get; init; } = FlowDefaults.AdapterBufferBytes;

    /// <summary>
    /// **适配件关闭等待**（毫秒；范围 100–10000，默认 1000）。**立即生效 / 下次关闭时使用**。
    /// </summary>
    /// <remarks>
    /// 重开一个还占着端口的会话时，先等旧传输关闭的上限；调大能容忍更慢的驱动。
    /// </remarks>
    public int AccessCloseTimeoutMs { get; init; } = FlowDefaults.AccessCloseTimeoutMs;

    /// <summary>
    /// **设备丢失判定次数**（范围 1–10，默认 2）。**立即生效**。连续多少次探活都找不到端口才判"设备已断开"。
    /// </summary>
    /// <remarks>
    /// 单次端口枚举抖动不应误判拔线；但设置过大意味着拔线后要更久才在界面上说"设备断开"。
    /// </remarks>
    public int DeviceLostMisses { get; init; } = FlowDefaults.DeviceLostMisses;
    /// <summary>
    /// **积压显示进入深度**（单元数；范围 1–65536，默认 2）。仅影响状态行呈现。
    /// </summary>
    public int StatusBacklogEnterDepth { get; init; } = FlowDefaults.StatusBacklogEnterDepth;

    /// <summary>
    /// **积压显示退出深度**（单元数；范围 0–65535，默认 1；必须小于进入深度）。仅影响状态行呈现。
    /// </summary>
    public int StatusBacklogExitDepth { get; init; } = FlowDefaults.StatusBacklogExitDepth;

    /// <summary>
    /// **状态滞回时间**（毫秒；范围 0–10000，默认 200）：进入/退出积压需持续的时间。
    /// </summary>
    public int StatusHoldMs { get; init; } = FlowDefaults.StatusHoldMs;

    /// <summary>
    /// **丢弃显示保持时间**（毫秒；范围 0–10000，默认 300）：丢弃事实消失后仍保持丢弃状态的时间。
    /// </summary>
    public int StatusDropHoldMs { get; init; } = FlowDefaults.StatusDropHoldMs;
}