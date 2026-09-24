using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Data;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>停止结果。</summary>
public enum CarrierStopResult
{
    /// <summary>已按期停止（线程已结束）。</summary>
    Stopped = 0,

    /// <summary>超过期限未停止——线程成为**孤儿**（框架放弃等待，由调用方报 Issue）。</summary>
    TimedOut = 1,

    /// <summary>此前已停止。</summary>
    AlreadyStopped = 2,
}

/// <summary>
/// 模块承载者：**一个 Active 实例一个专属线程 + 一个有界输入队列**（ADR-0029 Q4-1）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>承载模式</b>：模块实现自发活动（<see cref="ICarrierTarget.HasActivity"/>）时运行其活动体（可阻塞）；
///         否则由框架运行投递循环（出队 → 调用接收处理）。</item>
///   <item><b>不阻塞发送方</b>：队列满即丢弃（丢旧/丢新）并计数。</item>
///   <item><b>异常边界</b>：活动体异常经 <see cref="ICarrierTarget.ReportActivityFailure"/> 上报，不逸出。</item>
///   <item><b>停止协议</b>：置停止信号 → 有界等待 → 超时则标记孤儿并**放弃队列缓冲（不归还池）**。
///         框架**不假装能强杀线程**（.NET 无安全中止）。</item>
/// </list>
/// </remarks>
public sealed class ModuleCarrier : IDisposable
{
    private readonly ICarrierTarget _target;
    private readonly PooledPayloadBufferPool _pool;
    private readonly CarrierOptions _options;
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly Thread _thread;

    private volatile bool _stopRequested;
    private volatile bool _stopped;
    private volatile bool _orphaned;
    private bool _disposed;

    /// <summary>创建承载者（构造后需调用 <see cref="Start"/>）。</summary>
    public ModuleCarrier(ICarrierTarget target, PooledPayloadBufferPool pool, CarrierOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(pool);

        _target = target;
        _pool = pool;
        _options = options ?? new CarrierOptions();
        Queue = new InboundBuffer(_options.QueueCapacity, _options.DropPolicy);
        Diagnostics = new CarrierDiagnostics();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"hyw-carrier-{target.InstanceId}",
        };
    }

    /// <summary>实例 ID。</summary>
    public string InstanceId => _target.InstanceId;

    /// <summary>输入队列（每实例一个）。</summary>
    public InboundBuffer Queue { get; }

    /// <summary>L4 诊断计数。</summary>
    public CarrierDiagnostics Diagnostics { get; }

    /// <summary>是否已成为孤儿（停止超时未退出）。</summary>
    public bool IsOrphaned => _orphaned;

    /// <summary>专属线程是否仍在运行。</summary>
    public bool IsRunning => _thread.IsAlive;

    /// <summary>启动专属线程（进入 Active）。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_thread.IsAlive)
        {
            throw new InvalidOperationException($"承载者已启动：{InstanceId}");
        }

        _thread.Start();
    }

    /// <summary>投递一个数据单元到目标口（生产者线程调用；**不阻塞**）。</summary>
    /// <returns>是否入队成功（丢新策略下满队时返回 <see langword="false"/>）。</returns>
    public bool TryEnqueue(string targetPort, PayloadBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPort);
        ArgumentNullException.ThrowIfNull(buffer);

        if (_stopRequested || _disposed)
        {
            _pool.Return(buffer);
            Diagnostics.RecordDropped();
            return false;
        }

        var accepted = Queue.TryEnqueue(new InboundUnit(buffer, targetPort), out var dropped);
        if (dropped is not null)
        {
            _pool.Return(dropped);
            Diagnostics.RecordDropped();
        }

        Diagnostics.RecordEnqueued();

        if (accepted)
        {
            SignalDataAvailable();
        }

        return accepted;
    }

    /// <summary>
    /// 停止协议：置停止信号 → 有界等待 → 超时则标记孤儿并放弃队列缓冲。
    /// </summary>
    /// <remarks>
    /// 超时路径的资源处置（ADR-0029 §4.1）：队列/缓冲**标记弃用且不归还池**（孤儿线程可能仍会写它们），
    /// 仅由 GC 回收——保证"孤儿线程的后续写不会破坏框架或其他模块"。
    /// </remarks>
    public CarrierStopResult Stop(TimeSpan timeout)
    {
        if (_stopped)
        {
            return CarrierStopResult.AlreadyStopped;
        }

        _stopRequested = true;
        _target.RequestStop();          // 通知模块活动体（否则其可能永远无法感知停止）
        SignalDataAvailable();

        if (_thread.Join(timeout))
        {
            _stopped = true;
            DrainQueue(returnToPool: true);
            return CarrierStopResult.Stopped;
        }

        _orphaned = true;
        DrainQueue(returnToPool: false);
        return CarrierStopResult.TimedOut;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_stopped)
        {
            _ = Stop(_options.StopTimeout);
        }

        _dataAvailable.Dispose();
    }

    private void Run()
    {
        try
        {
            if (_target.HasActivity)
            {
                // 模块自控活动（可阻塞、可循环）；必须响应 StopSignal
                _target.RunActivity();
            }
            else
            {
                RunDeliveryLoop();
            }
        }
        catch (Exception ex)
        {
            // 异常边界：转 Issue（由宿主侧适配器上报），**不逸出**、不影响其他实例（AC-2）
            _target.ReportActivityFailure(ex);
        }
        finally
        {
            _stopped = true;
        }
    }

    private void RunDeliveryLoop()
    {
        while (!_stopRequested)
        {
            if (Queue.TryDequeue(out var unit))
            {
                Deliver(unit);
                continue;
            }

            // 混合等待：短等待（稳态下近乎立即返回）+ 信号唤醒，兼顾延迟与 CPU
            _dataAvailable.Wait(_options.IdleWait);
        }
    }

    private void Deliver(InboundUnit unit)
    {
        try
        {
            if (_target.TryGetReceiveHandler(unit.TargetPort, out var handler))
            {
                handler(unit.Buffer.Span);
                Diagnostics.RecordDelivered();
            }
            else
            {
                // 目标口没有接收处理（口未声明/未注册）→ 丢弃并计数
                Diagnostics.RecordDropped();
            }
        }
        finally
        {
            // 所有权在消费方：回调返回后归还池（文档：回调内不得持有缓冲）
            _pool.Return(unit.Buffer);
        }
    }

    private void DrainQueue(bool returnToPool)
    {
        Queue.Drain(buffer =>
        {
            if (returnToPool)
            {
                _pool.Return(buffer);
            }
            else
            {
                _pool.Abandon(buffer);
            }
        });
    }

    private void SignalDataAvailable()
    {
        try
        {
            _dataAvailable.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有未消费的信号：无需重复唤醒
        }
    }
}
