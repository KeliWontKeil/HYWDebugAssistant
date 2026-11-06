namespace HYWDebugAssistant.Sdk.Tuning;

/// <summary>
/// **通信与内存机制的默认值唯一事实源**。
/// </summary>
/// <remarks>
/// <para>
/// 这些值同时被应用设置（<c>FlowSettings</c>）、数据面承载参数（<c>CarrierOptions</c>）、
/// 队列压力计与适配件默认值引用。任何默认值只允许在这里出现一次；
/// 设置页只负责展示与修改，不得再抄一份数字。
/// </para>
/// <para>
/// 本类只放**用户可调的机制参数**。协议长度（如 <c>BytesShape.MaxLength</c>）、
/// 单次泵送上限、发送积压单元数护栏等属于正确性/安全常量，不放进设置。
/// </para>
/// </remarks>
public static class FlowDefaults
{
    /// <summary>每口接收队列容量（单元数）。</summary>
    public const int QueueCapacity = 1024;

    /// <summary>缓冲满时默认策略（<c>DropOldest</c> / <c>DropNewest</c>）。</summary>
    public const string DropPolicy = "DropOldest";

    /// <summary>停止/移除时等待模块线程退出的默认上限（秒）。</summary>
    public const double StopTimeoutSeconds = 5;

    /// <summary>饱和判定默认深度（单元数）。</summary>
    public const int HintDepth = 8;

    /// <summary>饱和判定默认持续时间（毫秒）。</summary>
    public const int HintHoldMs = 500;

    /// <summary>最大滞留时间默认值（毫秒；0 = 不限）。</summary>
    public const int MaxBacklogAgeMs = 0;

    /// <summary>单次外部写默认超时（毫秒）。</summary>
    public const int ExternalWriteTimeoutMs = 1000;

    /// <summary>探活默认节拍（毫秒）。</summary>
    public const int LivenessIntervalMs = 250;

    /// <summary>会话发送积压默认容量（字节）。</summary>
    public const int SendBacklogBytes = 256 * 1024;

    /// <summary>适配件字节暂存默认容量（字节）。</summary>
    public const int AdapterBufferBytes = 64 * 1024;

    /// <summary>适配件关闭默认等待（毫秒）。</summary>
    public const int AccessCloseTimeoutMs = 1000;

    /// <summary>设备丢失默认判定次数。</summary>
    public const int DeviceLostMisses = 2;

    /// <summary>积压显示默认进入深度（单元数）。</summary>
    public const int StatusBacklogEnterDepth = 2;

    /// <summary>积压显示默认退出深度（单元数）。</summary>
    public const int StatusBacklogExitDepth = 1;

    /// <summary>积压显示默认滞回时间（毫秒）。</summary>
    public const int StatusHoldMs = 200;

    /// <summary>丢弃显示默认保持时间（毫秒）。</summary>
    public const int StatusDropHoldMs = 300;

    /// <summary>队列容量下限。</summary>
    public const int QueueCapacityMin = 1;

    /// <summary>队列容量上限。</summary>
    public const int QueueCapacityMax = 65536;

    /// <summary>停止等待下限（秒）。</summary>
    public const double StopTimeoutSecondsMin = 0.1;

    /// <summary>停止等待上限（秒）。</summary>
    public const double StopTimeoutSecondsMax = 300;

    /// <summary>饱和判定深度下限。</summary>
    public const int HintDepthMin = 1;

    /// <summary>饱和判定深度上限。</summary>
    public const int HintDepthMax = 65536;

    /// <summary>饱和判定持续下限（毫秒）。</summary>
    public const int HintHoldMsMin = 50;

    /// <summary>饱和判定持续上限（毫秒）。</summary>
    public const int HintHoldMsMax = 10000;

    /// <summary>最大滞留时间下限（毫秒）。</summary>
    public const int MaxBacklogAgeMsMin = 0;

    /// <summary>最大滞留时间上限（毫秒）。</summary>
    public const int MaxBacklogAgeMsMax = 60000;

    /// <summary>外部写超时下限（毫秒）。</summary>
    public const int ExternalWriteTimeoutMsMin = 50;

    /// <summary>外部写超时上限（毫秒）。</summary>
    public const int ExternalWriteTimeoutMsMax = 60000;

    /// <summary>发送积压容量下限（字节）。</summary>
    public const int SendBacklogBytesMin = 8192;

    /// <summary>发送积压容量上限（字节）。</summary>
    public const int SendBacklogBytesMax = 4 * 1024 * 1024;

    /// <summary>探活节拍下限（毫秒）。</summary>
    public const int LivenessIntervalMsMin = 50;

    /// <summary>探活节拍上限（毫秒）。</summary>
    public const int LivenessIntervalMsMax = 5000;

    /// <summary>适配件暂存下限（字节）。</summary>
    public const int AdapterBufferBytesMin = 4096;

    /// <summary>适配件暂存上限（字节）。</summary>
    public const int AdapterBufferBytesMax = 1024 * 1024;

    /// <summary>关闭等待下限（毫秒）。</summary>
    public const int AccessCloseTimeoutMsMin = 100;

    /// <summary>关闭等待上限（毫秒）。</summary>
    public const int AccessCloseTimeoutMsMax = 10000;

    /// <summary>设备丢失判定次数下限。</summary>
    public const int DeviceLostMissesMin = 1;

    /// <summary>设备丢失判定次数上限。</summary>
    public const int DeviceLostMissesMax = 10;

    /// <summary>积压显示进入深度下限。</summary>
    public const int StatusBacklogEnterDepthMin = 1;

    /// <summary>积压显示进入深度上限。</summary>
    public const int StatusBacklogEnterDepthMax = 65536;

    /// <summary>积压显示退出深度下限。</summary>
    public const int StatusBacklogExitDepthMin = 0;

    /// <summary>积压显示退出深度上限。</summary>
    public const int StatusBacklogExitDepthMax = 65535;

    /// <summary>状态滞回时间下限（毫秒）。</summary>
    public const int StatusHoldMsMin = 0;

    /// <summary>状态滞回时间上限（毫秒）。</summary>
    public const int StatusHoldMsMax = 10000;

    /// <summary>丢弃显示保持时间下限（毫秒）。</summary>
    public const int StatusDropHoldMsMin = 0;

    /// <summary>丢弃显示保持时间上限（毫秒）。</summary>
    public const int StatusDropHoldMsMax = 10000;
}
