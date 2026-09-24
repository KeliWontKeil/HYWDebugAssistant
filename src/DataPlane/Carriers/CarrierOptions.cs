using HYWDebugAssistant.DataPlane.Queues;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 承载参数（默认值来自应用级设置——ADR-0030；此处为 v1 过渡默认值）。
/// </summary>
public sealed record CarrierOptions
{
    /// <summary>输入队列容量（单元数）。v1 默认 1024（M1-13 实测后回写最终值）。</summary>
    public int QueueCapacity { get; init; } = 1024;

    /// <summary>满时丢弃方式（默认丢最旧保最新）。</summary>
    public DropPolicy DropPolicy { get; init; } = DropPolicy.DropOldest;

    /// <summary>停止协议的有界等待时限。v1 默认 5 s。</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>投递循环的等待粒度（无数据时的等待上限，兼顾延迟与 CPU）。</summary>
    public TimeSpan IdleWait { get; init; } = TimeSpan.FromMilliseconds(1);
}
