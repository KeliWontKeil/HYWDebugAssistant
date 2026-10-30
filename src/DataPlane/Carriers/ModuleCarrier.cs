using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>停止结果。</summary>
public enum CarrierStopResult
{
    /// <summary>已按期停止（线程已结束）。</summary>
    Stopped = 0,

    /// <summary>超过期限未停止——线程成为**孤儿**（框架放弃等待，由调用方报 Issue）。</summary>
    TimedOut = 1,

    /// <summary>已处于停止状态。</summary>
    AlreadyStopped = 2,
}

/// <summary>
/// 入队结果（**分离"满队丢弃"与"无承载"**——两者性质不同，不得混成一个 false）。
/// </summary>
public enum CarrierEnqueueResult
{
    /// <summary>已入队（可能同时按策略丢弃了某个旧单元）。</summary>
    Accepted = 0,

    /// <summary>目标口的队列**满**（best-effort 丢弃）。</summary>
    QueueFull = 1,

    /// <summary>**目标口不存在**（未声明/已移除）⇒ 该口没有队列承载。</summary>
    NoCarrier = 2,

    /// <summary>承载已停止/已释放（实例正在拆除）。</summary>
    Stopped = 3,

    /// <summary>
    /// 已入队，但**挤掉了一个更旧的单元**（丢旧策略下队满）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Accepted"/> 分开表达是**记账诚实**的要求：新单元确实进了队列（该次投递成立），
    /// 但**被挤掉的那个旧单元实际没有到达消费方** ⇒ 调用方应把它计入"队列满丢弃"，
    /// 否则「投递数」会把从未被消费的单元也算成成功（丢弃不得混算、也不得漏算）。
    /// </remarks>
    QueueFullEvicted = 4,
}

/// <summary>
/// 模块承载者：**一个 Active 实例一个专属线程 + 每 (实例, 口) 一条有界输入队列**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>承载模式</b>：模块实现自发活动（<see cref="ICarrierTarget.HasActivity"/>）时运行其活动体（可阻塞）；
///         否则由框架运行投递循环（出队 → 调用接收处理）。</item>
///   <item><b>队列粒度 = 每 (实例, 口)</b>：每个接收口**各有一条**有界 FIFO 队列，
///         容量按**口**计（<see cref="CarrierOptions.QueueCapacity"/>）。⇒ 某个口满载/慢速**不会堵住同实例的其它口**
///         （消除跨口队头阻塞）；同口内仍是严格 FIFO（M4b"同 (源,宿) 对 FIFO 强保证"由此保证）。</item>
///   <item><b>公平调度</b>：投递循环按**轮转**在非空口之间取单元（每轮每口至多一个），保证任一口都不会被饿死。</item>
///   <item><b>不阻塞发送方</b>：队列满即丢弃（丢旧/丢新）并计数。</item>
///   <item><b>异常边界</b>：活动体异常经 <see cref="ICarrierTarget.ReportActivityFailure"/> 上报，不逸出。</item>
///   <item><b>停止协议</b>：置停止信号 → **先清尾**（计 <c>DiscardedOnStop</c> 并归还池）→ 有界等待 →
///         超时则标记孤儿并**放弃队列缓冲（不归还池）**。框架**不假装能强杀线程**（.NET 无安全中止）。</item>
/// </list>
/// </remarks>
public sealed class ModuleCarrier : IDisposable
{
    /// <summary>位图提示可覆盖的最大口数（超过则退化为逐口探测）。</summary>
    private const int MaxMaskedPorts = 64;

    private readonly ICarrierTarget _target;
    private readonly PooledPayloadBufferPool _pool;
    private readonly CarrierOptions _options;
    /// <summary>
    /// 二值“有活干”信号：仅**无活动体**的实例使用（口入队 / 会话就绪 / 命令输入）。
    /// 有活动体的实例自己轮询读取面，不等待这个信号。
    /// </summary>
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly Thread _thread;

    /// <summary>接收口名 → 队列下标（构造后**不变**：口集合在实例激活时已定）。</summary>
    private readonly Dictionary<string, int> _portIndex;

    /// <summary>**每口一条**队列（下标与 <see cref="PortNames"/> 一致）。</summary>
    private readonly InboundBuffer[] _queues;

    /// <summary>"该口有数据"的**提示位图**（仅优化：跳过空口探测；正确性由信号量保证，见 <see cref="TryDequeueAny"/>）。</summary>
    private readonly bool _useReadyMask;
    private long _readyMask;

    /// <summary>轮转游标（公平调度的起点）。</summary>
    private int _cursor;

    /// <summary>投递时间片：一次唤醒内层最多连续投递多久（构造时把非正值夹到 1 ms）。</summary>
    private readonly TimeSpan _deliverySlice;

    /// <summary>最大积压年龄（<see cref="Stopwatch"/> 计时刻度；0 = 不限）。</summary>
    private long _maxBacklogAgeTicks;

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

        PortNames = [.. target.ReceivePortNames];
        _portIndex = new Dictionary<string, int>(PortNames.Count, StringComparer.Ordinal);
        _queues = new InboundBuffer[PortNames.Count];

        for (var index = 0; index < PortNames.Count; index++)
        {
            _portIndex[PortNames[index]] = index;
            _queues[index] = new InboundBuffer(
                _options.QueueCapacity,
                _options.DropPolicy,
                _options.HintDepth,
                _options.HintHold);
        }

        // 口数 ≤ 64 才用位图提示；更多口退化为逐口探测（正确性不变，只是多几次空探测）
        _useReadyMask = PortNames.Count <= MaxMaskedPorts;

        // 时间片必须为正：0 会让内层每个单元都回外层（多一次循环开销），负值无意义
        _deliverySlice = _options.DeliverySlice > TimeSpan.Zero
            ? _options.DeliverySlice
            : TimeSpan.FromMilliseconds(1);
        _maxBacklogAgeTicks = InboundBuffer.ToTicks(_options.MaxBacklogAge);

        Diagnostics = new CarrierDiagnostics();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"hyw-carrier-{target.InstanceId}",
        };
    }

    /// <summary>实例 ID。</summary>
    public string InstanceId => _target.InstanceId;

    /// <summary>接收口名（顺序 = 队列下标顺序；**实例激活后不变**）。</summary>
    public IReadOnlyList<string> PortNames { get; }

    /// <summary>**每 (实例, 口) 一条**入站队列（只读快照，下标与 <see cref="PortNames"/> 一致）。</summary>
    public IReadOnlyList<InboundBuffer> Queues => _queues;

    /// <summary>L4 诊断计数。</summary>
    public CarrierDiagnostics Diagnostics { get; }

    /// <summary>是否已成为孤儿（停止超时未退出）。</summary>
    public bool IsOrphaned => _orphaned;

    /// <summary>专属线程是否仍在运行。</summary>
    public bool IsRunning => _thread.IsAlive;

    /// <summary>
    /// 投一个"有活干"信号（命令/输入/会话就绪等辅助工作源使用）。
    /// </summary>
    /// <remarks>
    /// 与口入队投的是**同一个**信号：实例循环醒来后自己判断该处理哪些工作源。
    /// 二值信号：重复投递不会累积；已有未消费信号时静默合并。
    /// <para>
    /// **活动体实例是 no-op**：它的专属线程跑活动体，没有人等待这个信号；投了只会让二值信号长期为满。
    /// 命令/输入由活动体自己的循环从收件箱取。
    /// </para>
    /// </remarks>
    public void SignalWork() => SignalDataAvailable();

    /// <summary>启动专属线程（进入 Active）。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_thread.IsAlive)
        {
            throw new InvalidOperationException(DataPlaneText.Get("dataplane.carrier.alreadyStarted", InstanceId));
        }

        _thread.Start();
    }

    /// <summary>取某个接收口的队列（口不存在时返回 <see langword="false"/>）。</summary>
    /// <param name="targetPort">口名。</param>
    /// <param name="queue">该口的队列。</param>
    public bool TryGetQueue(string targetPort, out InboundBuffer? queue)
    {
        queue = null;

        if (targetPort is not null && _portIndex.TryGetValue(targetPort, out var index))
        {
            queue = _queues[index];
            return true;
        }

        return false;
    }

    /// <summary>
    /// 读某个接收口的队列流动状态（深度 / 峰值 / 丢弃账本 / 饱和）。
    /// </summary>
    /// <param name="targetPort">接收口名。</param>
    /// <param name="state">流动状态（口不存在时为 <see cref="PortFlowState.None"/>）。</param>
    /// <returns>该口有队列时为 <see langword="true"/>。</returns>
    public bool TryGetFlowState(string targetPort, out PortFlowState state)
    {
        if (targetPort is not null && _portIndex.TryGetValue(targetPort, out var index))
        {
            return _queues[index].TryGetFlowState(out state);
        }

        state = PortFlowState.None;
        return false;
    }

    /// <summary>
    /// 记一次**模块内部**丢弃到某个口的原因账本（口不存在时忽略）。
    /// </summary>
    /// <param name="targetPort">接收口名。</param>
    /// <param name="units">单元数。</param>
    /// <param name="bytes">字节数（= 契约尺寸 × 单元数）。</param>
    public void ReportModuleLoss(string targetPort, long units, long bytes)
    {
        if (targetPort is not null && _portIndex.TryGetValue(targetPort, out var index))
        {
            _queues[index].RecordModuleLoss(units, bytes);
        }
    }

    /// <summary>把饱和判据与积压上限下发到本实例（改阈值即重开压力段）。</summary>
    /// <param name="hintDepth">深度提示阈值（单元数）。</param>
    /// <param name="hintHold">持续多久算饱和。</param>
    /// <param name="maxBacklogAge">最大积压年龄（0 ⇒ 不限）。</param>
    public void UpdateFlowPolicy(int hintDepth, TimeSpan hintHold, TimeSpan maxBacklogAge)
    {
        Interlocked.Exchange(ref _maxBacklogAgeTicks, InboundBuffer.ToTicks(maxBacklogAge));

        foreach (var queue in _queues)
        {
            queue.UpdateFlowPolicy(hintDepth, hintHold);
        }
    }

    /// <summary>
    /// 清空本实例全部口队列，把缓冲**归还池**，并把每个被清出的单元记一笔
    /// <see cref="DropReason.DiscardedOnStop"/>（停止清尾）。
    /// </summary>
    /// <remarks>
    /// 与停止协议里的普通清空不同：这里要记账。不记账就会出现"发出去的多于收到的"，
    /// 而那差额恰恰是最需要说清的一部分（它们不是丢包，是没有消费方的残留）。
    /// </remarks>
    public void PurgeQueues()
    {
        foreach (var queue in _queues)
        {
            queue.Purge(buffer => _pool.Return(buffer));
        }
    }

    /// <summary>投递一个数据单元到目标口（生产者线程调用；**不阻塞**）。</summary>
    /// <param name="targetPort">目标接收口名。</param>
    /// <param name="buffer">数据单元缓冲（所有权随入队移交）。</param>
    /// <returns>入队结果（**满队**与**无承载**分开表达）。</returns>
    public CarrierEnqueueResult TryEnqueue(string targetPort, PayloadBuffer buffer) =>
        TryEnqueue(targetPort, buffer, header: null);

    /// <summary>
    /// 投递一个数据单元到目标口，并可选携带**薄旁路头**。
    /// </summary>
    /// <param name="targetPort">目标接收口名。</param>
    /// <param name="buffer">数据单元缓冲（所有权随入队移交）。</param>
    /// <param name="header">
    /// 薄旁路头（**仅多源网络**非空；单源网络传 <see langword="null"/> ⇒ 零附加）。**不进 payload 字节流**。
    /// </param>
    /// <returns>入队结果（**满队**与**无承载**分开表达）。</returns>
    public CarrierEnqueueResult TryEnqueue(string targetPort, PayloadBuffer buffer, DatagramHeader? header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPort);
        ArgumentNullException.ThrowIfNull(buffer);

        if (_stopRequested || _disposed)
        {
            _pool.Return(buffer);
            Diagnostics.RecordDropped();
            return CarrierEnqueueResult.Stopped;
        }

        if (!_portIndex.TryGetValue(targetPort, out var index))
        {
            // 口不存在 ⇒ **无承载**：不入队、立即归还并计数（与"满队丢弃"分开）
            _pool.Return(buffer);
            Diagnostics.RecordDropped();
            return CarrierEnqueueResult.NoCarrier;
        }

        var accepted = _queues[index].TryEnqueue(new InboundUnit(buffer, targetPort, header), out var dropped);
        if (dropped is not null)
        {
            _pool.Return(dropped);
            Diagnostics.RecordDropped();
        }

        Diagnostics.RecordEnqueued();

        if (!accepted)
        {
            return CarrierEnqueueResult.QueueFull;
        }

        MarkReady(index);
        SignalDataAvailable();

        // 丢旧策略下队满会挤掉一个旧单元：**如实告知调用方**（那个单元没有到达消费方）
        return dropped is null ? CarrierEnqueueResult.Accepted : CarrierEnqueueResult.QueueFullEvicted;
    }

    /// <summary>
    /// 从**任一非空口**取一个单元（**轮转公平**：连续调用会在非空口之间轮换）。
    /// </summary>
    /// <remarks>
    /// 主动接收路径（<c>IPortAccess.TryReceive</c>）与框架投递循环**共用**本方法：
    /// 二者互斥（有活动体 ⇒ 不跑投递循环），故游标与提示位图无并发写。
    /// </remarks>
    /// <param name="unit">取到的单元（含目标口名）。</param>
    /// <returns>是否取到。</returns>
    public bool TryDequeueAny(out InboundUnit unit)
    {
        var count = _queues.Length;

        for (var step = 0; step < count; step++)
        {
            var index = _cursor + step;
            if (index >= count)
            {
                index -= count;
            }

            if (_useReadyMask && (_readyMask & (1L << index)) == 0)
            {
                continue;
            }

            if (!_queues[index].TryDequeue(out unit))
            {
                // 位图只是**提示**：探测落空即清位（不依赖它保证正确性）
                ClearReady(index);
                continue;
            }

            _cursor = index + 1 == count ? 0 : index + 1;
            return true;
        }

        unit = default;
        return false;
    }

    /// <summary>
    /// 停止协议：置停止信号 → **先清尾** → 有界等待 → 超时则标记孤儿并放弃队列缓冲。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **先清尾**：停止后队列里的单元已经没有消费方，留着只会让"已入队"变成假账；清出时逐笔记
    /// <see cref="DropReason.DiscardedOnStop"/> 并把缓冲归还池。Join 之后还要再清一次——
    /// 与生产者赛跑的那一小段仍可能挤进单元。
    /// </para>
    /// <para>
    /// 超时路径的资源处置：队列/缓冲**标记弃用且不归还池**（孤儿线程可能仍会写它们），
    /// 仅由 GC 回收——保证"孤儿线程的后续写不会破坏框架或其他模块"。
    /// </para>
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

        // 先清尾：这一步同时把"停止要等多久"从"排空整条积压"降成"等当前这一个单元处理完"
        PurgeQueues();

        if (_thread.Join(timeout))
        {
            _stopped = true;
            PurgeQueues();              // 兜住 Join 前后挤进来的单元
            return CarrierStopResult.Stopped;
        }

        _orphaned = true;
        DrainQueues(returnToPool: false);
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
            var didWork = false;

            // 工作源 ②/③：外部接入会话 + 收件箱（没有活动体时由框架驱动）
            if (_target.HasAuxiliaryWork)
            {
                didWork |= _target.PumpAuxiliary();
            }

            // 工作源 ①：口队列。**一次最多占一个时间片**——某个口长期有货时也必须回外层，
            // 否则辅助工作源（命令 / 输入 / 会话泵 / 状态发布）会被无限期排在后面。
            var sliceStarted = Stopwatch.GetTimestamp();

            while (!_stopRequested && TryDequeueAny(out var unit))
            {
                if (IsExpired(unit))
                {
                    DropExpired(unit);
                    didWork = true;
                    continue;
                }

                Deliver(unit);
                didWork = true;

                if (Stopwatch.GetElapsedTime(sliceStarted) >= _deliverySlice)
                {
                    break;
                }
            }

            if (didWork)
            {
                // 只提示"这一拍有工作"；状态/计量的唯一事实源仍是快照。
                _target.NotifyWorkCompleted();
                continue;
            }

            // 混合等待：短等待（稳态下近乎立即返回）+ 信号唤醒，兼顾延迟与 CPU
            _dataAvailable.Wait(_options.IdleWait);
        }
    }

    /// <summary>该单元在队列里待的时间是否已超过允许的积压上限（0 = 不限 ⇒ 恒为 false）。</summary>
    /// <param name="unit">数据单元。</param>
    private bool IsExpired(InboundUnit unit)
    {
        var maxAge = Volatile.Read(ref _maxBacklogAgeTicks);

        return maxAge > 0
            && unit.EnqueuedTicks > 0
            && Stopwatch.GetTimestamp() - unit.EnqueuedTicks > maxAge;
    }

    /// <summary>丢掉一个过期单元：缓冲归还池，并在该口的原因账本上记一笔"过期"。</summary>
    /// <param name="unit">数据单元（所有权在本方法）。</param>
    private void DropExpired(InboundUnit unit)
    {
        var bytes = unit.Buffer.Length;
        _pool.Return(unit.Buffer);

        if (_portIndex.TryGetValue(unit.TargetPort, out var index))
        {
            _queues[index].RecordExpired(1, bytes);
        }
    }

    private void Deliver(InboundUnit unit)
    {
        try
        {
            if (_target.TryGetReceiveHandler(unit.TargetPort, out var handler))
            {
                handler(unit.Buffer.Span);

                // 薄旁路头的**消费者**（观测面）：记录最近序号/接收时刻 ⇒ 多源归属可精确观测（Q6(c)）
                Diagnostics.RecordDelivered(unit.Header);
            }
            else
            {
                // 目标口没有接收处理（口已声明但模块未注册处理）→ 丢弃并计数
                Diagnostics.RecordDropped();
            }
        }
        finally
        {
            // 所有权在消费方：回调返回后归还池（文档：回调内不得持有缓冲）
            _pool.Return(unit.Buffer);
        }
    }

    private void DrainQueues(bool returnToPool)
    {
        foreach (var queue in _queues)
        {
            queue.Drain(buffer =>
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

        _readyMask = 0;
    }

    private void MarkReady(int index)
    {
        if (_useReadyMask)
        {
            _ = Interlocked.Or(ref _readyMask, 1L << index);
        }
    }

    private void ClearReady(int index)
    {
        if (_useReadyMask)
        {
            _ = Interlocked.And(ref _readyMask, ~(1L << index));
        }
    }

    private void SignalDataAvailable()
    {
        // 活动体实例的专属线程在 runActivity 里，**从不 Wait 这个信号**。
        if (_target.HasActivity)
        {
            return;
        }

        // **计数信号**：每个入队/会话信号各对应一次唤醒，计数由载体逐次消费；
        // 不再用二值信号 + SemaphoreFullException 的抛出/捕获来"合并"——高频小数据下
        // 那条路径每个单元抛一次异常，是应用卡顿的一个根因。
        try
        {
            _dataAvailable.Release();
        }
        catch (SemaphoreFullException)
        {
            Diagnostics.RecordSignalOverflow();
        }
        catch (ObjectDisposedException)
        {
            // 关闭竞态：载体已释放；晚到的唤醒丢弃即可。
        }
    }
}
