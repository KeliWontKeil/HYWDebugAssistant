using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// **一条外部接入会话**：适配件的生命周期 + 每会话有界队列 + 状态/计量。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>框架持有并驱动；模块只发打开/关闭请求与读写；</item>
///   <item>状态机：<c>Closed → Opening → Open → Closing → Closed</c>，任一环节失败 → <c>Faulted</c>；</item>
///   <item><b>不自动重试</b>：<c>Faulted</c> 要等用户再次请求；</item>
///   <item>队列满/超长/写失败都计数，绝不让异常逸出到模块；</item>
///   <item><b>计数只有"清空计数"会归零</b>：关闭与重开都不清零；</item>
///   <item><b>故障即释放</b>：适配件报告故障后，由实例循环把适配件交出去一次（事件线程不释放资源）。</item>
/// </list>
/// </remarks>
public sealed class ExternalAccessSession : IExternalAccessReady
{
    /// <summary>探活的节拍间隔（毫秒）：实例循环的空闲等待在毫秒量级，这个间隔足够发现拔出。</summary>
    private const long LivenessIntervalMs = 250;

    /// <summary>还没有探过活的哨兵：保证会话刚打开的那一拍就查一次。</summary>
    private const long NoLivenessCheck = long.MinValue;

    /// <summary>单次 <c>Pump</c> 最多排空的暂存单元数（只防坏适配件永不返回空）。</summary>
    private const int MaxUnitsPerPump = 4096;

    private readonly ExternalAccessDeclaration _declaration;
    private readonly IExternalAccessAdapter _adapter;
    private readonly Type _parametersType;
    private readonly Func<ExternalAccessDeclaration, object?> _parametersProvider;
    private readonly AccessInboundBuffer _queue;
    private readonly TimeSpan _closeTimeout;
    private readonly Action? _signal;
    private readonly byte[] _readBuffer;
    private readonly byte[] _deliveryBuffer;
    private readonly Lock _gate = new();
    private readonly Queue<AccessFailure> _failures = new();

    /// <summary>单次写的超时上限（打开时下发；运行期可由设置改写，见 <see cref="UpdateWriteTimeout"/>）。</summary>
    private TimeSpan _writeTimeout;

    /// <summary>探活节拍（毫秒；运行期可由设置改写）。</summary>
    private long _livenessIntervalMs = 250;

    /// <summary>适配件可调参数（关闭等待 / 暂存容量 / 丢失判定 / 探活节拍）。</summary>
    private ExternalAccessTuning _tuning = ExternalAccessTuning.Default;

    private bool _wantedOpen;
    private bool _suspended;
    private bool _restartPending;
    private bool _pendingNotification;
    private bool _releasePending;
    private ExternalAccessState _state = ExternalAccessState.Closed;
    private ExternalAccessEndpoint? _endpoint;
    private ExternalAccessFailure? _lastFailure;
    private string? _lastQueuedFailure;
    private long _receivedUnits;
    private long _receivedBytes;
    private long _sentUnits;
    private long _sentBytes;

    /// <summary>会话未打开时被拒的发送（单元 / 字节）——会话轴的一类丢弃。</summary>
    private long _rejectedUnits;
    private long _rejectedBytes;

    /// <summary>适配件写失败（返回 false 或抛异常）的发送（单元 / 字节）。</summary>
    private long _sendFailedUnits;
    private long _sendFailedBytes;

    /// <summary>发送积压（设备写缓冲；由写泵线程消费，实例线程只入队）。</summary>
    private readonly SendBacklog _backlog;

    /// <summary>写泵线程（每打开会话一个）。</summary>
    private Thread? _writeThread;

    /// <summary>停止写泵（volatile：实例线程置位，写泵线程读）。</summary>
    private int _txStopping;

    private ExternalAccessFailure? _pendingWriteFailure;
    private string? _pendingWriteFailureMessage;

    /// <summary>适配件经回报面上报的**自身溢出**（单元 / 字节）。</summary>
    private long _adapterDroppedUnits;
    private long _adapterDroppedBytes;

    /// <summary>适配件交出的单元**超过声明上限**（单元 / 字节）——由会话在泵入前拦下。</summary>
    private long _oversizedDroppedUnits;
    private long _oversizedDroppedBytes;

    /// <summary>本会话最近一次丢弃（原因 + 时刻）；清空计数会把它一起清掉。</summary>
    private DropReason? _lastDropReason;
    private DateTimeOffset? _lastDropAt;

    private long _lastLivenessCheck = NoLivenessCheck;
    private int _connections;
    private DateTimeOffset? _lastReceivedAt;
    private ReceiveHandler? _handler;

    /// <summary>创建会话。</summary>
    /// <param name="declaration">接入声明。</param>
    /// <param name="adapter">该标准创建出的适配件（每会话独立）。</param>
    /// <param name="parametersType">该标准的参数类型（框架按 catalog 解析）。</param>
    /// <param name="parametersProvider">按声明取当前参数对象（框架配置快照）。</param>
    /// <param name="queue">该会话的有界队列。</param>
    /// <param name="closeTimeout">关闭的有界等待时限。</param>
    /// <param name="signal">"有活干"信号（实例循环被唤醒）。</param>
    /// <param name="writeTimeout">
    /// 单次写的超时上限（<see langword="null"/> ⇒ 1 秒）。只下发给**支持该能力**的适配件
    /// （见 <see cref="IExternalAccessWriteTimeout"/>）；不支持的适配件保持自己的行为。
    /// </param>
    public ExternalAccessSession(
        ExternalAccessDeclaration declaration,
        IExternalAccessAdapter adapter,
        Type parametersType,
        Func<ExternalAccessDeclaration, object?> parametersProvider,
        AccessInboundBuffer queue,
        TimeSpan closeTimeout,
        Action? signal,
        TimeSpan? writeTimeout = null,
        ExternalAccessTuning? tuning = null)
        : this(declaration, adapter, parametersType, parametersProvider, queue, closeTimeout, signal, writeTimeout, tuning, backlog: null)
    {
    }

    /// <summary>创建会话（框架内部：允许注入发送积压容量/策略）。</summary>
    /// <param name="declaration">接入声明。</param>
    /// <param name="adapter">适配件。</param>
    /// <param name="parametersType">参数类型。</param>
    /// <param name="parametersProvider">参数提供者。</param>
    /// <param name="queue">有界接收队列。</param>
    /// <param name="closeTimeout">关闭等待上限。</param>
    /// <param name="signal">实例循环唤醒信号。</param>
    /// <param name="writeTimeout">单次写超时（null ⇒ 1 秒）。</param>
    /// <param name="tuning">适配件可调参数。</param>
    /// <param name="backlog">发送积压（null ⇒ 64 KB 缺省；由宿主按设置注入）。</param>
    internal ExternalAccessSession(
        ExternalAccessDeclaration declaration,
        IExternalAccessAdapter adapter,
        Type parametersType,
        Func<ExternalAccessDeclaration, object?> parametersProvider,
        AccessInboundBuffer queue,
        TimeSpan closeTimeout,
        Action? signal,
        TimeSpan? writeTimeout,
        ExternalAccessTuning? tuning,
        SendBacklog? backlog)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(parametersType);
        ArgumentNullException.ThrowIfNull(parametersProvider);
        ArgumentNullException.ThrowIfNull(queue);

        _declaration = declaration;
        _adapter = adapter;
        _parametersType = parametersType;
        _parametersProvider = parametersProvider;
        _queue = queue;
        _closeTimeout = closeTimeout;
        _signal = signal;
        _writeTimeout = NormalizeWriteTimeout(writeTimeout);
        _tuning = NormalizeTuning(tuning);
        _livenessIntervalMs = (long)_tuning.LivenessInterval.TotalMilliseconds;
        _readBuffer = new byte[Math.Max(1, declaration.MaxUnitBytes)];
        _deliveryBuffer = new byte[Math.Max(1, declaration.MaxUnitBytes)];
        _backlog = backlog ?? new SendBacklog(64 * 1024, TimeSpan.Zero, 8, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>接入名。</summary>
    public string Name => _declaration.Name;

    /// <summary>该接入的参数类型（框架按标准标识登记；供模块骨架反射配置字段）。</summary>
    public Type ParametersType => _parametersType;

    /// <summary>是否已设接收回调（回调模式下框架负责投递；读取面不可再取）。</summary>
    public bool HasReceiveHandler
    {
        get
        {
            lock (_gate)
            {
                return _handler is not null;
            }
        }
    }

    /// <summary>请求打开（只置意图，实际执行在 <see cref="Pump"/>）。</summary>
    public void RequestOpen()
    {
        lock (_gate)
        {
            _wantedOpen = true;
            _pendingNotification = true;

            // 失败后再次请求 = 用户重试：回到 Closed，允许下一次 Pump 打开。
            if (_state == ExternalAccessState.Faulted)
            {
                _state = ExternalAccessState.Closed;
                _lastFailure = null;
            }
        }

        Signal();
    }

    /// <summary>请求关闭（只置意图，实际执行在 <see cref="Pump"/>）。</summary>
    public void RequestClose()
    {
        lock (_gate)
        {
            _wantedOpen = false;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <summary>置实例暂停（Disabled）：会话关闭，但"想要打开"的意图保留。</summary>
    /// <param name="suspended">是否暂停。</param>
    public void SetSuspended(bool suspended)
    {
        lock (_gate)
        {
            _suspended = suspended;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <summary>参数变更：下一拍关闭并用新参数重开（如果当前意图是打开）。</summary>
    public void NotifyConfigurationChanged()
    {
        lock (_gate)
        {
            _restartPending = true;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <summary>设接收回调（回调模式）。</summary>
    /// <param name="handler">处理委托。</param>
    public void SetReceiveHandler(ReceiveHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handler = handler;
        }
    }

    /// <summary>
    /// 应用挂起的开关/重启/停用，并把适配件已就绪的数据取进会话队列。
    /// </summary>
    /// <returns>这一拍是否做了工作（状态变化 / 开关 / 释放 / 取到数据）。</returns>
    public bool Pump()
    {
        var didWork = false;

        if (ApplyPendingWriteFailure())
        {
            didWork = true;
        }
        bool restart;
        bool wanted;
        bool suspended;

        lock (_gate)
        {
            if (_pendingNotification)
            {
                _pendingNotification = false;
                didWork = true;
            }

            restart = _restartPending;
            _restartPending = false;
            wanted = _wantedOpen;
            suspended = _suspended;
        }

        if (restart)
        {
            if (CloseCore())
            {
                didWork = true;
            }

            lock (_gate)
            {
                if (_state == ExternalAccessState.Faulted)
                {
                    _state = ExternalAccessState.Closed;
                    _lastFailure = null;
                }
            }

            if (wanted && !suspended && OpenCore())
            {
                didWork = true;
            }

            return didWork;
        }

        if (suspended || !wanted)
        {
            if (CloseCore())
            {
                didWork = true;
            }

            return didWork;
        }

        // 释放先于重开：故障的适配件可能还握着坏掉的句柄，必须由实例线程先交出去。
        if (ReleaseAdapterIfPending())
        {
            didWork = true;
        }

        bool isClosed;
        lock (_gate)
        {
            isClosed = _state == ExternalAccessState.Closed;
        }

        if (isClosed && OpenCore())
        {
            didWork = true;
        }

        CheckLivenessIfDue();

        if (PumpAdapter())
        {
            didWork = true;
        }

        return didWork;
    }

    /// <summary>
    /// 主动取一个数据单元（先应用挂起控制、再尝试从适配件取）。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <param name="length">实际字节数。</param>
    public bool TryRead(Span<byte> destination, out int length)
    {
        length = 0;

        lock (_gate)
        {
            if (_handler is not null)
            {
                // 回调模式：两个消费者互斥，读取面不再取。
                return false;
            }
        }

        _ = Pump();
        return _queue.TryDequeue(destination, out length);
    }

    /// <summary>
    /// 改本会话接收队列的**饱和判据**（深度阈值 + 保持时长）与**积压上限**——即时生效，队列内容不动。
    /// </summary>
    /// <param name="hintDepth">深度提示阈值（单元数；大于队列容量时按容量算）。</param>
    /// <param name="hintHold">持续多久算饱和。</param>
    /// <param name="maxBacklogAge">最大积压年龄（0 ⇒ 不限）。</param>
    /// <remarks>
    /// 只动判据，不动容量与满时策略：那两项在会话创建时随队列一起定下。
    /// 它不经过 <see cref="Pump"/>：队列自带锁，下发判据与状态机推进互不阻塞。
    /// </remarks>
    public void UpdateFlowPolicy(int hintDepth, TimeSpan hintHold, TimeSpan maxBacklogAge)
    {
        _queue.UpdateFlowPolicy(hintDepth, hintHold, maxBacklogAge);
        _backlog.UpdatePolicy(maxBacklogAge, hintDepth, hintHold);
    }

    /// <summary>
    /// 改本会话的**写超时**（运行期设置变更）——只对支持该能力的适配件生效。
    /// </summary>
    /// <param name="timeout">写超时（负值 / 0 按 1 秒处理）。</param>
    /// <remarks>不改会话状态：能改就改，改不了（适配件不支持）就保持它自己的行为。</remarks>
    public void UpdateWriteTimeout(TimeSpan timeout)
    {
        _writeTimeout = NormalizeWriteTimeout(timeout);
        ApplyWriteTimeout();
    }

    /// <summary>
    /// 改本会话/适配件的一组通信参数（运行期设置变更）：探活节拍立即生效；关闭等待与丢失判定立即生效；
    /// 暂存容量只对**下次打开**生效（既有暂存不清空）。
    /// </summary>
    /// <param name="tuning">新参数（不为 <see langword="null"/>）。</param>
    public void UpdateTuning(ExternalAccessTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        _tuning = NormalizeTuning(tuning);
        Volatile.Write(ref _livenessIntervalMs, (long)_tuning.LivenessInterval.TotalMilliseconds);
        ApplyTuning();
    }

    /// <summary>把参数交给支持它的适配件（不支持 ⇒ 保持默认行为）。</summary>
    private void ApplyTuning()
    {
        if (_adapter is IExternalAccessTuning tunable)
        {
            tunable.ApplyTuning(_tuning);
        }
    }

    /// <summary>归一化：探活节拍夹到 50–5000 ms；其余项交给适配件自己夹。</summary>
    private static ExternalAccessTuning NormalizeTuning(ExternalAccessTuning? tuning)
    {
        var value = tuning ?? ExternalAccessTuning.Default;
        var interval = value.LivenessInterval > TimeSpan.Zero
            ? value.LivenessInterval
            : TimeSpan.FromMilliseconds(250);

        var clamped = Math.Clamp((long)interval.TotalMilliseconds, 50, 5000);

        return value with { LivenessInterval = TimeSpan.FromMilliseconds(clamped) };
    }

    /// <summary>把当前写超时下发给适配件（不支持该能力 ⇒ 保持原行为）。</summary>
    private void ApplyWriteTimeout()
    {
        if (_adapter is not IExternalAccessWriteTimeout writable)
        {
            return;
        }

        try
        {
            writable.SetWriteTimeout(_writeTimeout);
        }
        catch (Exception ex)
        {
            // 下发超时是尽力而为：适配件抛异常不得打断打开 / 重开，只记一条事实
            RecordFailure(
                AccessFailureKind.Open,
                DataPlaneText.Get("dataplane.access.writeTimeoutFailed", ex.GetType().Name, ex.Message));
        }
    }

    /// <summary>写超时的收敛：非正值 ⇒ 1 秒（0 会让底层变成无限等待，等于没有上限）。</summary>
    /// <param name="timeout">设置里的值。</param>
    private static TimeSpan NormalizeWriteTimeout(TimeSpan? timeout) =>
        timeout is { } value && value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(1);

    /// <summary>把队列里的单元交给已设的接收回调（回调模式；返回是否投递了数据）。</summary>
    public bool DrainToHandler()
    {
        ReceiveHandler? handler;
        lock (_gate)
        {
            handler = _handler;
        }

        if (handler is null)
        {
            return false;
        }

        var didWork = false;
        while (_queue.TryDequeue(_deliveryBuffer, out var length))
        {
            handler(_deliveryBuffer.AsSpan(0, length));
            didWork = true;
        }

        return didWork;
    }

    /// <summary>发送一个数据单元：**入队即返回**，实际写在写泵线程上完成。</summary>
    /// <param name="payload">有效字节。</param>
    /// <returns>是否接受（未打开 / 空载荷 / 超过积压容量 ⇒ <see langword="false"/>；丢弃均已计数）。</returns>
    /// <remarks>
    /// 实例线程不再做阻塞写：入队后立刻回来处理接收、命令与状态；阻塞写在专用写泵线程上。
    /// </remarks>
    public bool Write(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_state != ExternalAccessState.Open)
            {
                _rejectedUnits++;
                _rejectedBytes += payload.Length;
                MarkDroppedLocked(DropReason.SessionNotOpen);
                _pendingNotification = true;
                return false;
            }
        }

        if (!_backlog.TryEnqueue(payload, out var evictedBytes))
        {
            return false;
        }

        if (evictedBytes > 0)
        {
            lock (_gate)
            {
                MarkDroppedLocked(DropReason.SendBacklogEvicted);
                _pendingNotification = true;
            }
        }

        return true;
    }

    /// <summary>写泵线程：从发送积压取单元，调用阻塞的适配件写。</summary>
    private void RunWritePump()
    {
        while (Volatile.Read(ref _txStopping) == 0)
        {
            if (!_backlog.TryTake(out var buffer, out var length, 20, out var expiredBytes))
            {
                continue;
            }

            if (expiredBytes > 0)
            {
                lock (_gate)
                {
                    MarkDroppedLocked(DropReason.Expired);
                    _pendingNotification = true;
                }

            }

            try
            {
                if (Volatile.Read(ref _txStopping) != 0)
                {
                    break;
                }

                WriteOne(buffer, length);
            }
            finally
            {
                SendBacklog.Release(buffer);
            }
        }
    }

    private void WriteOne(byte[] buffer, int length)
    {
        ExternalAccessEndpoint? endpoint;

        lock (_gate)
        {
            if (_state != ExternalAccessState.Open)
            {
                _rejectedUnits++;
                _rejectedBytes += length;
                MarkDroppedLocked(DropReason.SessionNotOpen);
                _pendingNotification = true;
                return;
            }

            endpoint = _endpoint;
        }

        try
        {
            var sent = _adapter.Write(buffer.AsSpan(0, length));

            lock (_gate)
            {
                if (sent)
                {
                    _sentUnits++;
                    _sentBytes += length;
                }
                else
                {
                    _sendFailedUnits++;
                    _sendFailedBytes += length;
                    MarkDroppedLocked(DropReason.SessionWriteFailed);
                }

                _pendingNotification = true;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _sendFailedUnits++;
                _sendFailedBytes += length;
                MarkDroppedLocked(DropReason.SessionWriteFailed);
                _pendingWriteFailure = new ExternalAccessFailure(
                    ExternalAccessFailureKind.WriteFailed,
                    endpoint,
                    Detail: DetailOf(ex));
                _pendingWriteFailureMessage = DataPlaneText.Get("dataplane.access.writeFailed", ex.Message);
                _pendingNotification = true;
            }

            RecordFailure(AccessFailureKind.Write, DataPlaneText.Get("dataplane.access.writeFailedDetail", ex.GetType().Name, ex.Message));
            Volatile.Write(ref _txStopping, 1);
            Signal();
        }
    }

    /// <summary>把写泵线程上报的故障搬进状态机（只在实例线程调用）。</summary>
    private bool ApplyPendingWriteFailure()
    {
        ExternalAccessFailure? failure;
        string? message;

        lock (_gate)
        {
            failure = _pendingWriteFailure;
            message = _pendingWriteFailureMessage;
            _pendingWriteFailure = null;
            _pendingWriteFailureMessage = null;
        }

        if (failure is null)
        {
            return false;
        }

        Fail(failure, message ?? IssueMessageFor(failure));
        return true;
    }

    private void StartWritePump()
    {
        if (_writeThread is not null)
        {
            return;
        }

        Volatile.Write(ref _txStopping, 0);
        var thread = new Thread(RunWritePump)
        {
            IsBackground = true,
            Name = $"access-tx-{Name}",
        };

        _writeThread = thread;
        thread.Start();
    }

    private void StopWritePumpSignal()
    {
        Volatile.Write(ref _txStopping, 1);
        _backlog.Wake();
    }

    private void JoinWritePump(TimeSpan timeout, bool report = true)
    {
        var thread = _writeThread;
        _writeThread = null;

        if (thread is null || thread == Thread.CurrentThread)
        {
            return;
        }

        try
        {
            if (!thread.Join(timeout))
            {
                RecordFailureOnce(AccessFailureKind.Write, DataPlaneText.Get("dataplane.access.writePumpStuck"));
            }
        }
        catch (ThreadStateException)
        {
            // 宿主兜底关闭与打开并发：线程已挂上但还没 Start（启动者会立刻看到停止标志并退出）。
        }
    }

    /// <summary>取当前快照。</summary>
    /// <remarks>
    /// <para>
    /// <b>单来源分账</b>：每个事件只进一处（拒发 / 写失败 / 适配件溢出 / 超长 / 队列四类），
    /// 合计由各分项算出来，**不再另存一份总数**——两份总数迟早对不上。
    /// </para>
    /// <para>
    /// 读顺序：先读队列侧事实（队列自带同步），再进会话锁。反向嵌套不存在：队列从不回调会话。
    /// </para>
    /// </remarks>
    public ExternalAccessSnapshot GetSnapshot()
    {
        var queueEvicted = _queue.Evicted;
        var queueEvictedBytes = _queue.EvictedBytes;
        var queueRejected = _queue.Rejected;
        var queueRejectedBytes = _queue.RejectedBytes;
        var queueOversized = _queue.Oversized;
        var queueOversizedBytes = _queue.OversizedBytes;
        var queueExpired = _queue.Expired;
        var queueExpiredBytes = _queue.ExpiredBytes;
        var depth = _queue.Count;
        var capacity = _queue.Capacity;
        var saturated = _queue.Saturated;
        var saturatedFor = _queue.SaturatedFor;
        var queueLastReason = _queue.LastDropReason;
        var queueLastAt = _queue.LastDropAt;

        var backlogEvictedUnits = _backlog.EvictedUnits;
        var backlogEvictedBytes = _backlog.EvictedBytes;
        var backlogExpiredUnits = _backlog.ExpiredUnits;
        var backlogExpiredBytes = _backlog.ExpiredBytes;
        var backlogDiscardedUnits = _backlog.DiscardedUnits;
        var backlogDiscardedBytes = _backlog.DiscardedBytes;
        var backlogDepth = _backlog.Depth;
        var backlogBytes = _backlog.Bytes;
        var backlogCapacity = _backlog.CapacityBytes;
        var backlogPeak = _backlog.PeakBytes;
        var backlogSaturated = _backlog.Saturated;
        var backlogSaturatedFor = _backlog.SaturatedFor;

        lock (_gate)
        {
            var oversizedUnits = queueOversized + _oversizedDroppedUnits;
            var oversizedBytes = queueOversizedBytes + _oversizedDroppedBytes;

            var receiveDroppedUnits = queueEvicted + queueRejected + oversizedUnits + queueExpired + _adapterDroppedUnits;
            var receiveDroppedBytes = queueEvictedBytes + queueRejectedBytes + oversizedBytes
                + queueExpiredBytes + _adapterDroppedBytes;

            var sendBacklogDroppedUnits = backlogEvictedUnits + backlogExpiredUnits + backlogDiscardedUnits;
            var sendBacklogDroppedBytes = backlogEvictedBytes + backlogExpiredBytes + backlogDiscardedBytes;

            DropReason? lastReason;
            DateTimeOffset? lastDropAt;

            if (queueLastReason is { } queueReason
                && queueLastAt is { } queueAt
                && (_lastDropAt is not { } sessionAt || queueAt >= sessionAt))
            {
                lastReason = queueReason;
                lastDropAt = queueAt;
            }
            else
            {
                lastReason = _lastDropReason;
                lastDropAt = _lastDropAt;
            }

            return new ExternalAccessSnapshot(
                _state,
                _suspended,
                _endpoint,
                _lastFailure,
                _receivedUnits,
                _receivedBytes,
                _sentUnits,
                _sentBytes,
                _rejectedUnits + _sendFailedUnits + receiveDroppedUnits + sendBacklogDroppedUnits,
                _rejectedBytes + _sendFailedBytes + receiveDroppedBytes + sendBacklogDroppedBytes,
                _rejectedUnits,
                _connections,
                _lastReceivedAt)
            {
                RejectedBytes = _rejectedBytes,
                SendFailedUnits = _sendFailedUnits,
                SendFailedBytes = _sendFailedBytes,
                ReceiveDroppedUnits = receiveDroppedUnits,
                ReceiveDroppedBytes = receiveDroppedBytes,
                ReceiveQueueDroppedUnits = queueEvicted + queueRejected,
                AdapterDroppedUnits = _adapterDroppedUnits,
                OversizedDroppedUnits = oversizedUnits,
                ExpiredUnits = queueExpired,
                SendBacklogEvictedUnits = backlogEvictedUnits,
                SendBacklogEvictedBytes = backlogEvictedBytes,
                SendBacklogExpiredUnits = backlogExpiredUnits,
                SendBacklogExpiredBytes = backlogExpiredBytes,
                SendBacklogDiscardedUnits = backlogDiscardedUnits,
                SendBacklogDiscardedBytes = backlogDiscardedBytes,
                SendBacklogDepth = backlogDepth,
                SendBacklogBytes = backlogBytes,
                SendBacklogCapacityBytes = backlogCapacity,
                SendBacklogPeakBytes = backlogPeak,
                SendBacklogSaturated = backlogSaturated,
                SendBacklogSaturatedFor = backlogSaturatedFor,
                ReceiveQueueDepth = depth,
                ReceiveQueueCapacity = capacity,
                ReceiveSaturated = saturated,
                ReceiveSaturatedFor = saturatedFor,
                LastDropReason = lastReason,
                LastDropAt = lastDropAt,
            };
        }
    }

    /// <summary>清空本会话的计数（收 / 发 / 丢弃 / 被拒与最后接收时刻）。</summary>
    /// <remarks>
    /// 只清计数：队列里的数据、活动连接数、故障态与端点都保留——
    /// 清计数是排障动作，不是复位会话。
    /// </remarks>
    public void ClearCounters()
    {
        lock (_gate)
        {
            _receivedUnits = 0;
            _receivedBytes = 0;
            _sentUnits = 0;
            _sentBytes = 0;
            _rejectedUnits = 0;
            _rejectedBytes = 0;
            _sendFailedUnits = 0;
            _sendFailedBytes = 0;
            _adapterDroppedUnits = 0;
            _adapterDroppedBytes = 0;
            _oversizedDroppedUnits = 0;
            _oversizedDroppedBytes = 0;
            _lastDropReason = null;
            _lastDropAt = null;
            _lastReceivedAt = null;
            _pendingNotification = true;
        }

        _queue.ResetCounters();
        _backlog.ClearCounters();
        Signal();
    }

    /// <summary>取走全部待上报的失败（L4 转 Issue）。</summary>
    /// <param name="destination">收集器。</param>
    public void DrainFailures(List<AccessFailure> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        lock (_gate)
        {
            while (_failures.Count > 0)
            {
                destination.Add(_failures.Dequeue());
            }
        }
    }

    /// <summary>宿主兜底关闭（移除/退出）：置关闭意图并立即执行。</summary>
    /// <param name="timeout">关闭的有界等待时限。</param>
    public bool CloseFromHost(TimeSpan timeout)
    {
        lock (_gate)
        {
            _wantedOpen = false;
        }

        return CloseCore(timeout);
    }

    /// <summary>
    /// **激活中途失败时的兜底释放**：无论会话是否打开过，都通知适配件 <see cref="IExternalAccessAdapter.Close"/>，
    /// 并清空队列。清理路径不抛、不记录失败（不能掩盖最初的激活失败原因）。
    /// </summary>
    public void Discard()
    {
        lock (_gate)
        {
            _wantedOpen = false;
            _state = ExternalAccessState.Closed;
            _endpoint = null;
            _lastFailure = null;
            _connections = 0;
            _releasePending = false;
        }

        StopWritePumpSignal();

        try
        {
            _adapter.Close(_closeTimeout);
        }
        catch (Exception)
        {
            // 清理路径：适配器关闭异常不得掩盖最初的激活失败原因。
        }

        JoinWritePump(TimeSpan.FromMilliseconds(200), report: false);
        _backlog.Purge();
        _queue.Clear();
    }

    // ---------------------------------------------------------------- 状态机内部

    /// <summary>
    /// 故障后的适配件释放：每个故障只释放一次，状态与失败事实都保留。
    /// </summary>
    /// <remarks>
    /// 适配件可能在它自己的泵线程上报告故障，那条线程不能关传输（会与读泵/写路径交叉）；
    /// 因此 <see cref="Fail"/> 只置标记，真正释放由实例线程的这一拍完成。
    /// </remarks>
    /// <returns>这一拍是否释放了适配件。</returns>
    private bool ReleaseAdapterIfPending()
    {
        lock (_gate)
        {
            if (!_releasePending)
            {
                return false;
            }

            _releasePending = false;

            // 已经离开 Faulted（用户重试 / 被关闭）就不需要在这里释放：重开会先走适配件自己的 Close。
            if (_state != ExternalAccessState.Faulted)
            {
                return false;
            }
        }

        try
        {
            _adapter.Close(_closeTimeout);
        }
        catch (Exception)
        {
            // 清理路径：关闭异常不得掩盖最初的失败原因，也不再生 Issue。
        }

        JoinWritePump(TimeSpan.FromMilliseconds(200), report: false);
        _backlog.Purge();
        return true;
    }

    /// <summary>
    /// 探活：只有适配件会探活、且会话处于打开态时才调，节拍由单调时钟给出。
    /// </summary>
    /// <remarks>
    /// 不新建定时器——实例循环的空闲等待在毫秒量级，探活只是顺路做一次廉价检查。
    /// 节拍照常推进（含异常那次），坏适配件不会把实例循环压成忙等。
    /// </remarks>
    private void CheckLivenessIfDue()
    {
        if (_adapter is not IExternalAccessLiveness liveness)
        {
            return;
        }

        lock (_gate)
        {
            if (_state != ExternalAccessState.Open)
            {
                return;
            }
        }

        var now = Environment.TickCount64;

        if (_lastLivenessCheck != NoLivenessCheck && now - _lastLivenessCheck < Volatile.Read(ref _livenessIntervalMs))
        {
            return;
        }

        _lastLivenessCheck = now;

        try
        {
            liveness.CheckLiveness();
        }
        catch (Exception)
        {
            // 探活是尽力而为：适配件抛异常不得打断实例循环，按下一次节拍重试。
        }
    }

    private bool OpenCore()
    {
        lock (_gate)
        {
            if (_state != ExternalAccessState.Closed)
            {
                return false;
            }

            _state = ExternalAccessState.Opening;
            _endpoint = null;
            _lastFailure = null;
            _pendingNotification = true;
        }

        var parameters = _parametersProvider(_declaration);
        if (parameters is null)
        {
            var missing = new ExternalAccessFailure(
                ExternalAccessFailureKind.ParametersNotRegistered,
                Detail: _parametersType.Name);

            Fail(missing, DataPlaneText.Get("dataplane.access.parametersNotRegistered", _parametersType.Name));
            return true;
        }

        // 写超时属于**适配件能力**：打开前下发一次（适配件自己落到传输的写超时开关上）
        ApplyWriteTimeout();

        try
        {
            _adapter.Open(parameters, this);
        }
        catch (Exception ex)
        {
            ExternalAccessEndpoint? endpoint;
            lock (_gate)
            {
                endpoint = _endpoint;
            }

            var failure = new ExternalAccessFailure(
                ExternalAccessFailureKind.OpenFailed,
                endpoint,
                Detail: DetailOf(ex));

            RecordFailure(AccessFailureKind.Open, DataPlaneText.Get("dataplane.access.openFailedDetail", ex.GetType().Name, ex.Message));
            Fail(failure, DataPlaneText.Get("dataplane.access.openFailed", ex.Message));
        }

        return true;
    }

    private bool CloseCore() => CloseCore(_closeTimeout);

    private bool CloseCore(TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_state == ExternalAccessState.Closed)
            {
                return false;
            }

            _state = ExternalAccessState.Closing;
            _pendingNotification = true;
        }

        StopWritePumpSignal();

        try
        {
            _adapter.Close(timeout);
        }
        catch (Exception ex)
        {
            RecordFailure(AccessFailureKind.Close, DataPlaneText.Get("dataplane.access.closeFailedDetail", ex.GetType().Name, ex.Message));
            RecordFailure(AccessFailureKind.Close, DataPlaneText.Get("dataplane.access.closeFailed", ex.Message));
        }

        JoinWritePump(TimeSpan.FromMilliseconds(200), report: false);
        _backlog.Purge();
        lock (_gate)
        {
            _state = ExternalAccessState.Closed;
            _endpoint = null;
            _lastFailure = null;
            _connections = 0;
            _releasePending = false;
            _pendingNotification = true;
        }

        _queue.Clear();
        return true;
    }

    private bool PumpAdapter()
    {
        var didWork = false;
        var drained = 0;

        while (true)
        {
            ExternalAccessEndpoint? endpoint;

            lock (_gate)
            {
                if (_state != ExternalAccessState.Open)
                {
                    break;
                }

                // 只按"会话是否打开"决定能否读：分片交付后暂存里可能还有余量，
                // 是否还有数据由 `TryRead` 自己回答（不用二值信号去猜）。
                endpoint = _endpoint;
            }

            int length;
            try
            {
                if (!_adapter.TryRead(_readBuffer, out length))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                var failure = new ExternalAccessFailure(
                    ExternalAccessFailureKind.ReadFailed,
                    endpoint,
                    Detail: DetailOf(ex));

                RecordFailure(AccessFailureKind.Adapter, DataPlaneText.Get("dataplane.access.readFailedDetail", ex.GetType().Name, ex.Message));
                Fail(failure, DataPlaneText.Get("dataplane.access.readFailed", ex.Message));
                return true;
            }

            if (length <= 0)
            {
                break;
            }

            if (length > _declaration.MaxUnitBytes || length > _readBuffer.Length)
            {
                // 超长：丢弃并只报一次；不让一个坏单元把循环卡住。
                lock (_gate)
                {
                    _oversizedDroppedUnits++;
                    _oversizedDroppedBytes += length;
                    MarkDroppedLocked(DropReason.OversizedUnit);
                    _pendingNotification = true;
                }

                RecordFailureOnce(
                    AccessFailureKind.Queue,
                    DataPlaneText.Get("dataplane.access.unitTooLong", length, _declaration.MaxUnitBytes));
                break;
            }

            _ = _queue.TryEnqueue(_readBuffer.AsSpan(0, length));

            lock (_gate)
            {
                _receivedUnits++;
                _receivedBytes += length;
                _lastReceivedAt = DateTimeOffset.UtcNow;
                _pendingNotification = true;
            }

            didWork = true;

            // 上限只防"坏适配件永远返回数据"把实例循环钉死；正常适配器由 TryRead 返回 false 结束。
            if (++drained >= MaxUnitsPerPump)
            {
                break;
            }
        }

        return didWork;
    }

    private void Fail(ExternalAccessFailure failure, string issueMessage)
    {
        bool changed;

        lock (_gate)
        {
            if (_state is ExternalAccessState.Closed or ExternalAccessState.Closing)
            {
                return;
            }

            changed = _state != ExternalAccessState.Faulted || !Equals(_lastFailure, failure);
            _state = ExternalAccessState.Faulted;
            _endpoint = failure.Endpoint ?? _endpoint;
            _lastFailure = failure;
            _connections = 0;
            _releasePending = true;
            _pendingNotification = true;
            Volatile.Write(ref _txStopping, 1);

            if (changed)
            {
                _failures.Enqueue(new AccessFailure(Name, AccessFailureKind.Adapter, issueMessage));
            }
        }

        Signal();
        _backlog.Wake();
    }

    private void RecordFailure(AccessFailureKind kind, string message)
    {
        lock (_gate)
        {
            _failures.Enqueue(new AccessFailure(Name, kind, message));
        }
    }

    private void RecordFailureOnce(AccessFailureKind kind, string message)
    {
        lock (_gate)
        {
            if (string.Equals(_lastQueuedFailure, message, StringComparison.Ordinal))
            {
                return;
            }

            _lastQueuedFailure = message;
            _failures.Enqueue(new AccessFailure(Name, kind, message));
        }
    }

    /// <summary>Issue 文案：底层诊断文本 + 原始细节（不含面向用户的接入句子）。</summary>
    private static string IssueMessageFor(ExternalAccessFailure failure)
    {
        // 没有细节就没有可搬运的事实：用既有的"原因未知"兜底，不把枚举名混进中文句子。
        if (string.IsNullOrWhiteSpace(failure.Detail))
        {
            return DataPlaneText.Get("dataplane.access.adapterFailureNoReason");
        }

        var detail = failure.Detail!;

        return failure.Kind switch
        {
            ExternalAccessFailureKind.ReadFailed or ExternalAccessFailureKind.PeerClosed =>
                DataPlaneText.Get("dataplane.access.readFailed", detail),
            ExternalAccessFailureKind.DeviceLost =>
                DataPlaneText.Get("dataplane.access.deviceLost", detail),
            ExternalAccessFailureKind.WriteFailed =>
                DataPlaneText.Get("dataplane.access.writeFailed", detail),
            ExternalAccessFailureKind.Unknown =>
                DataPlaneText.Get("dataplane.access.adapterFailureNoReason"),
            _ => DataPlaneText.Get("dataplane.access.openFailed", detail),
        };
    }

    private static string DetailOf(Exception exception) =>
        exception.GetType().Name + ": " + exception.Message;

    private void Signal() => _signal?.Invoke();

    // ---------------------------------------------------------------- IExternalAccessReady

    /// <inheritdoc />
    public void NotifyReady()
    {
        lock (_gate)
        {
            if (_state is ExternalAccessState.Open or ExternalAccessState.Opening)
            {
                _pendingNotification = true;
            }
        }

        Signal();
    }

    /// <inheritdoc />
    public void SetConnecting(ExternalAccessEndpoint? endpoint = null)
    {
        lock (_gate)
        {
            if (!_wantedOpen
                || _state is ExternalAccessState.Closed or ExternalAccessState.Closing or ExternalAccessState.Faulted)
            {
                return;
            }

            _state = ExternalAccessState.Opening;
            _endpoint = endpoint;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <inheritdoc />
    public void SetOpen(ExternalAccessEndpoint? endpoint = null)
    {
        lock (_gate)
        {
            if (!_wantedOpen
                || _state is ExternalAccessState.Closed or ExternalAccessState.Closing or ExternalAccessState.Faulted)
            {
                return;
            }

            _state = ExternalAccessState.Open;
            _endpoint = endpoint;
            _lastFailure = null;
            _releasePending = false;
            _pendingNotification = true;
        }

        StartWritePump();
        Signal();
    }

    /// <inheritdoc />
    public void SetFaulted(ExternalAccessFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Fail(failure, IssueMessageFor(failure));
    }

    /// <inheritdoc />
    public void SetConnections(int count)
    {
        lock (_gate)
        {
            _connections = Math.Max(0, count);
            _pendingNotification = true;
        }

        Signal();
    }

    /// <inheritdoc />
    public void ReportDropped(long units, long bytes) => ReportDropped(DropReason.AdapterOverflow, units, bytes);

    /// <inheritdoc />
    /// <remarks>
    /// 只认两类：<see cref="DropReason.AdapterOverflow"/>（适配件自己的暂存满 / 截断）与
    /// <see cref="DropReason.OversizedUnit"/>（交出超长单元）。其余原因**忽略**——
    /// 队列满、会话未打开、写失败都是框架自己看得见的事实，适配件不得替它声明。
    /// </remarks>
    public void ReportDropped(DropReason reason, long units, long bytes)
    {
        if (units <= 0 && bytes <= 0)
        {
            return;
        }

        lock (_gate)
        {
            switch (reason)
            {
                case DropReason.AdapterOverflow:
                    _adapterDroppedUnits += units;
                    _adapterDroppedBytes += bytes;
                    break;

                case DropReason.OversizedUnit:
                    _oversizedDroppedUnits += units;
                    _oversizedDroppedBytes += bytes;
                    break;

                default:
                    return;
            }

            MarkDroppedLocked(reason);
            _pendingNotification = true;
        }

        Signal();
    }

    /// <summary>记下"本会话最近一次丢弃"（**调用方必须持有 <see cref="_gate"/>**）。</summary>
    /// <param name="reason">丢弃原因。</param>
    private void MarkDroppedLocked(DropReason reason)
    {
        _lastDropReason = reason;
        _lastDropAt = DateTimeOffset.UtcNow;
    }
}
