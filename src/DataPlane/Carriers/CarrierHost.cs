using System.Collections.Concurrent;
using HYWDebugAssistant.DataPlane.Buffers;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 数据面宿主：创建 / 启动 / 停止 / 回收承载者（**每个 Active 实例一个专属线程**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>归属</b>：L3 数据面持有承载者；实例的存在性与状态由 L2/L4 决定（C1-2）。</item>
///   <item><b>线程数前提</b>：按活跃实例计（数十量级）；`Disabled` 的实例不占线程。</item>
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

    /// <summary>承载参数（修改**只影响新建承载者**：既有队列保持创建时容量——`04b` §6）。</summary>
    public CarrierOptions Options { get; private set; }

    /// <summary>更新承载参数（由应用级设置驱动；即时对后续操作生效）。</summary>
    public void UpdateOptions(CarrierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    /// <summary>当前全部承载者（快照）。</summary>
    public IReadOnlyCollection<ModuleCarrier> Carriers => [.. _carriers.Values];

    /// <summary>启动一个实例的承载（创建队列与专属线程）。</summary>
    /// <param name="target">承载目标。</param>
    /// <param name="carrier">成功时返回承载者。</param>
    /// <param name="failure">失败原因。</param>
    public bool TryActivate(ICarrierTarget target, out ModuleCarrier? carrier, out string? failure)
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
            created.Start();
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

    /// <summary>停止全部承载（应用退出时调用）。</summary>
    /// <returns>超时（成为孤儿）的实例 ID 列表，供调用方转 Issue。</returns>
    public IReadOnlyList<string> StopAll()
    {
        var orphans = new List<string>();

        foreach (var instanceId in _carriers.Keys.ToArray())
        {
            if (TryDeactivate(instanceId, out var result) && result == CarrierStopResult.TimedOut)
            {
                orphans.Add(instanceId);
            }
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
