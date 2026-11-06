using System.Collections.Concurrent;
using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Buffers;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 数据面宿主：创建 / 启动 / 停止 / 回收承载者（**每个 Active 实例一个专属线程**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>归属</b>：L3 数据面持有承载者；实例的存在性与状态由 L2/L4 决定（C1-2）。</item>
///   <item><b>线程数前提</b>：按活跃实例计（数十量级）；`Disabled` 的实例保留线程与资源，线程暂停、不调用 `OnStep`。</item>
///   <item><b>停止超时</b>：返回结果供调用方（L4）转 Issue——宿主自身不依赖上报口（保持 L3 只依赖 L1/L0）。</item>
/// </list>
/// </remarks>
public sealed class CarrierHost : IDisposable
{
    private readonly ConcurrentDictionary<string, ModuleCarrier> _carriers = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>创建数据面宿主。</summary>
    public CarrierHost(PooledPayloadBufferPool? buffers = null, CarrierOptions? options = null)
    {
        Buffers = buffers ?? new PooledPayloadBufferPool();
        Options = options ?? new CarrierOptions();
    }

    /// <summary>数据单元缓冲池（投递方借缓冲用）。</summary>
    public PooledPayloadBufferPool Buffers { get; }

    /// <summary>
    /// 承载参数（容量与满时策略**只影响新建承载者**：既有队列保持创建时容量；饱和判据不受此限，见
    /// <see cref="UpdateOptions"/>）。
    /// </summary>
    public CarrierOptions Options { get; private set; }

    /// <summary>
    /// 更新承载参数（由应用级设置驱动）：保存参数，并把**可即时改变**的部分下发给既有承载。
    /// </summary>
    /// <remarks>
    /// <b>容量与满时策略不在这里传播</b>：队列在创建时就把它们取走，改设置只影响**新建**队列
    /// （运行中给一条正在用的队列换底，会让"已经积压多少"这件事失去意义）。
    /// 能即时改的是饱和判据（深度阈值 + 保持时长）与积压上限。
    /// </remarks>
    /// <param name="options">新参数（同时保存下来供此后新建的承载使用）。</param>
    public void UpdateOptions(CarrierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;

        foreach (var carrier in _carriers.Values)
        {
            // 激活过程中会短暂占位（见 TryActivate），占位项没有队列可下发
            carrier?.UpdateFlowPolicy(options.HintDepth, options.HintHold, options.MaxBacklogAge);
            carrier?.UpdateStatusPolicy(
                options.StatusBacklogEnterDepth,
                options.StatusBacklogExitDepth,
                options.StatusHold,
                options.StatusDropHold);
        }
    }

    /// <summary>当前全部承载者（快照）。</summary>
    public IReadOnlyCollection<ModuleCarrier> Carriers => [.. _carriers.Values];

    /// <summary>启动一个实例的承载（创建队列与专属线程）。</summary>
    /// <param name="target">承载目标。</param>
    /// <param name="carrier">成功时返回承载者。</param>
    /// <param name="failure">失败原因。</param>
    /// <param name="startPaused">是否先停在暂停门（等生命周期钩子跑完再恢复）。</param>
    public bool TryActivate(
        ICarrierTarget target,
        out ModuleCarrier? carrier,
        out string? failure,
        bool startPaused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        carrier = null;
        failure = null;

        if (!_carriers.TryAdd(target.InstanceId, null!))
        {
            failure = DataPlaneText.Get("dataplane.carrier.alreadyExists", target.InstanceId);
            return false;
        }

        var created = new ModuleCarrier(target, Buffers, Options);
        _carriers[target.InstanceId] = created;

        try
        {
            created.Start(startPaused);
        }
        catch (ThreadStateException ex)
        {
            _carriers.TryRemove(target.InstanceId, out _);
            failure = DataPlaneText.Get("dataplane.carrier.startFailed", ex.Message);
            return false;
        }

        carrier = created;
        return true;
    }

    /// <summary>停止并回收一个实例的承载。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="result">停止结果（超时＝孤儿，由调用方报 Issue）。</param>
    public bool TryDeactivate(string instanceId, out CarrierStopResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        result = CarrierStopResult.AlreadyStopped;

        if (!_carriers.TryRemove(instanceId, out var carrier) || carrier is null)
        {
            return false;
        }

        result = carrier.Stop(Options.StopTimeout);
        carrier.Dispose();
        return true;
    }

    /// <summary>查找承载者。</summary>
    public bool TryGet(string instanceId, out ModuleCarrier? carrier) =>
        _carriers.TryGetValue(instanceId, out carrier) && carrier is not null;

    /// <summary>
    /// 停止全部承载（应用退出时调用）：**先全部叫停，再在一个共享总时限里逐个等**。
    /// </summary>
    /// <returns>超时（成为孤儿）的实例 ID 列表，供调用方转 Issue。</returns>
    /// <remarks>
    /// 逐个 <c>Stop</c> 会让总等待变成"实例数 × 单项超时"——界面在退出时被拖住的时间与实例数成正比。
    /// 两阶段后总等待只取决于最慢的那一个（且不超过总时限），线程数与停止语义不变。
    /// </remarks>
    public IReadOnlyList<string> StopAll()
    {
        var entries = _carriers.ToArray();
        var orphans = new List<string>();

        foreach (var entry in entries)
        {
            entry.Value?.BeginStop();
        }

        var deadline = Stopwatch.GetTimestamp()
            + (long)(Options.StopTimeout.TotalSeconds * Stopwatch.Frequency);

        foreach (var entry in entries)
        {
            if (entry.Value is not { } carrier)
            {
                _ = _carriers.TryRemove(entry.Key, out _);
                continue;
            }

            var remainingTicks = deadline - Stopwatch.GetTimestamp();
            var remaining = remainingTicks > 0
                ? TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency)
                : TimeSpan.Zero;

            if (carrier.CompleteStop(remaining) == CarrierStopResult.TimedOut)
            {
                orphans.Add(entry.Key);
            }

            _ = _carriers.TryRemove(entry.Key, out _);
            carrier.Dispose();
        }

        return orphans;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = StopAll();
    }
}
