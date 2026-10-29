using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// **一条外部接入会话**：适配件的生命周期 + 每会话有界队列 + 状态/计量。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>框架持有并驱动；模块只发打开/关闭请求与读写；</item>
///   <item>状态机：<c>Closed → Opening → Open → Closing → Closed</c>，任一环节失败 → <c>Faulted</c>；</item>
///   <item><b>不自动重试</b>：<c>Faulted</c> 要等用户再次请求；</item>
///   <item>队列满/超长/写失败都计数，绝不让异常逸出到模块。</item>
/// </list>
/// </remarks>
public sealed class ExternalAccessSession : IExternalAccessReady
{
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

    private bool _wantedOpen;
    private bool _suspended;
    private bool _restartPending;
    private bool _ready;
    private bool _pendingNotification;
    private ExternalAccessState _state = ExternalAccessState.Closed;
    private string _detail = string.Empty;
    private string _lastError = string.Empty;
    private long _receivedUnits;
    private long _receivedBytes;
    private long _sentUnits;
    private long _sentBytes;
    private long _extraDroppedUnits;
    private long _extraDroppedBytes;
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
    public ExternalAccessSession(
        ExternalAccessDeclaration declaration,
        IExternalAccessAdapter adapter,
        Type parametersType,
        Func<ExternalAccessDeclaration, object?> parametersProvider,
        AccessInboundBuffer queue,
        TimeSpan closeTimeout,
        Action? signal)
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
        _readBuffer = new byte[Math.Max(1, declaration.MaxUnitBytes)];
        _deliveryBuffer = new byte[Math.Max(1, declaration.MaxUnitBytes)];
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
                _lastError = string.Empty;
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
    /// <returns>这一拍是否做了工作（状态变化 / 开关 / 取到数据）。</returns>
    public bool Pump()
    {
        var didWork = false;
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
                    _lastError = string.Empty;
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

        bool isClosed;
        lock (_gate)
        {
            isClosed = _state == ExternalAccessState.Closed;
        }

        if (isClosed && OpenCore())
        {
            didWork = true;
        }

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

    /// <summary>发送一个数据单元（会话未打开/写失败都计入丢弃，不抛）。</summary>
    /// <param name="payload">有效字节。</param>
    public bool Write(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return false;
        }

        _ = Pump();

        lock (_gate)
        {
            if (_state != ExternalAccessState.Open)
            {
                _extraDroppedUnits++;
                _extraDroppedBytes += payload.Length;
                return false;
            }
        }

        try
        {
            var sent = _adapter.Write(payload);

            lock (_gate)
            {
                if (sent)
                {
                    _sentUnits++;
                    _sentBytes += payload.Length;
                }
                else
                {
                    _extraDroppedUnits++;
                    _extraDroppedBytes += payload.Length;
                }
            }

            return sent;
        }
        catch (Exception ex)
        {
            RecordFailure(AccessFailureKind.Write, DataPlaneText.Get("dataplane.access.writeFailedDetail", ex.GetType().Name, ex.Message));
            Fail(DataPlaneText.Get("dataplane.access.writeFailed", ex.Message));
            return false;
        }
    }

    /// <summary>取当前快照。</summary>
    public ExternalAccessSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new ExternalAccessSnapshot(
                _state,
                _suspended,
                _detail,
                _lastError,
                _receivedUnits,
                _receivedBytes,
                _sentUnits,
                _sentBytes,
                _queue.Dropped + _extraDroppedUnits,
                _queue.DroppedBytes + _extraDroppedBytes,
                _connections,
                _lastReceivedAt);
        }
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
            _ready = false;
            _detail = string.Empty;
            _connections = 0;
        }

        try
        {
            _adapter.Close(_closeTimeout);
        }
        catch (Exception)
        {
            // 清理路径：适配器关闭异常不得掩盖最初的激活失败原因。
        }

        _queue.Clear();
    }

    // ---------------------------------------------------------------- 状态机内部

    private bool OpenCore()
    {
        lock (_gate)
        {
            if (_state != ExternalAccessState.Closed)
            {
                return false;
            }

            _state = ExternalAccessState.Opening;
            _detail = string.Empty;
            _lastError = string.Empty;
            _pendingNotification = true;
        }

        var parameters = _parametersProvider(_declaration);
        if (parameters is null)
        {
            Fail(DataPlaneText.Get("dataplane.access.parametersNotRegistered", _parametersType.Name));
            return true;
        }

        try
        {
            _adapter.Open(parameters, this);
        }
        catch (Exception ex)
        {
            RecordFailure(AccessFailureKind.Open, DataPlaneText.Get("dataplane.access.openFailedDetail", ex.GetType().Name, ex.Message));
            Fail(DataPlaneText.Get("dataplane.access.openFailed", ex.Message));
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

        try
        {
            _adapter.Close(timeout);
        }
        catch (Exception ex)
        {
            RecordFailure(AccessFailureKind.Close, DataPlaneText.Get("dataplane.access.closeFailedDetail", ex.GetType().Name, ex.Message));
            lock (_gate)
            {
                _lastError = DataPlaneText.Get("dataplane.access.closeFailed", ex.Message);
            }
        }

        lock (_gate)
        {
            _state = ExternalAccessState.Closed;
            _detail = string.Empty;
            _ready = false;
            _connections = 0;
            _pendingNotification = true;
        }

        _queue.Clear();
        return true;
    }

    private bool PumpAdapter()
    {
        var didWork = false;

        while (true)
        {
            lock (_gate)
            {
                if (_state != ExternalAccessState.Open || !_ready)
                {
                    break;
                }

                _ready = false;
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
                RecordFailure(AccessFailureKind.Adapter, DataPlaneText.Get("dataplane.access.readFailedDetail", ex.GetType().Name, ex.Message));
                Fail(DataPlaneText.Get("dataplane.access.readFailed", ex.Message));
                return true;
            }

            if (length <= 0)
            {
                break;
            }

            if (length > _declaration.MaxUnitBytes || length > _readBuffer.Length)
            {
                // 超长：丢弃并只报一次；不让一个坏单元把循环卡住。
                _queue.RecordDropped(1, length);
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
        }

        return didWork;
    }

    private void Fail(string message)
    {
        bool changed;

        lock (_gate)
        {
            if (_state is ExternalAccessState.Closed or ExternalAccessState.Closing)
            {
                return;
            }

            changed = _state != ExternalAccessState.Faulted || !string.Equals(_lastError, message, StringComparison.Ordinal);
            _state = ExternalAccessState.Faulted;
            _detail = string.Empty;
            _lastError = message;
            _ready = false;
            _connections = 0;
            _pendingNotification = true;

            if (changed)
            {
                _failures.Enqueue(new AccessFailure(Name, AccessFailureKind.Adapter, message));
            }
        }

        Signal();
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
            if (_lastError == message)
            {
                return;
            }

            _lastError = message;
            _failures.Enqueue(new AccessFailure(Name, kind, message));
        }
    }

    private void Signal() => _signal?.Invoke();

    // ---------------------------------------------------------------- IExternalAccessReady

    /// <inheritdoc />
    public void NotifyReady()
    {
        lock (_gate)
        {
            if (_state is ExternalAccessState.Open or ExternalAccessState.Opening)
            {
                _ready = true;
                _pendingNotification = true;
            }
        }

        Signal();
    }

    /// <inheritdoc />
    public void SetConnecting(string? detail = null)
    {
        lock (_gate)
        {
            if (!_wantedOpen
                || _state is ExternalAccessState.Closed or ExternalAccessState.Closing or ExternalAccessState.Faulted)
            {
                return;
            }

            _state = ExternalAccessState.Opening;
            _detail = detail ?? string.Empty;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <inheritdoc />
    public void SetOpen(string? detail = null)
    {
        lock (_gate)
        {
            if (!_wantedOpen
                || _state is ExternalAccessState.Closed or ExternalAccessState.Closing or ExternalAccessState.Faulted)
            {
                return;
            }

            _state = ExternalAccessState.Open;
            _detail = detail ?? string.Empty;
            _lastError = string.Empty;
            _pendingNotification = true;
        }

        Signal();
    }

    /// <inheritdoc />
    public void SetFaulted(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            message = DataPlaneText.Get("dataplane.access.adapterFailureNoReason");
        }

        Fail(message);
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
    public void ReportDropped(long units, long bytes)
    {
        _queue.RecordDropped(units, bytes);

        lock (_gate)
        {
            _pendingNotification = true;
        }

        Signal();
    }
}
