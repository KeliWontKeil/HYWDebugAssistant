using System.Collections.Concurrent;
using System.IO.Ports;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **串口适配件**：打开一个 COM 口，数据到达事件把字节放进暂存队列，框架按就绪信号来取。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>参数类型 = <see cref="SerialConfig"/>（配置表单与持久化复用）；</item>
///   <item>读用 <see cref="SerialPort.DataReceived"/> 事件，不设读超时、不自建线程；</item>
///   <item>写同步、有超时；失败立即报告并停止（<b>不自动重连</b>）；</item>
///   <item>关闭先退订事件、再关端口；读取异常只报一次，不把“空闲无数据”当成故障；</item>
///   <item>串口没有"连接"语义：设备拔出后端口只是从系统里消失，读泵收不到任何事件，因此靠
///     <see cref="IExternalAccessLiveness"/> 探活发现；读写异常时再看端口是否还在，据此区分
///     "设备已断开"与"读写失败"；</item>
///   <item><b>故障路径只报事实、不释放传输</b>：事件线程与探活路径都不 Close/Dispose，
///     释放由会话在实例线程上做一次（否则会与读泵、写路径交叉）。</item>
/// </list>
/// </remarks>
public sealed class SerialAccessAdapter : IExternalAccessAdapter, IExternalAccessLiveness, IExternalAccessWriteTimeout, IExternalAccessTuning
{
    private static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(1);

    /// <summary>写超时的缺省值：与框架默认承载参数同值（适配件单独使用时也有上限）。</summary>
    private static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromSeconds(1);

    /// <summary>连续缺失几次才判定设备已不在系统中：单次缺失可能只是端口枚举的瞬时抖动。</summary>
    private const int DefaultMissingChecksBeforeDeviceLost = 2;

    private readonly Func<ISerialTransport> _transportFactory;
    private StreamAdapterBuffer _buffer = new();

    /// <summary>暂存容量（字节；下一次 Open 生效）。</summary>
    private int _bufferBytes = 64 * 1024;

    /// <summary>关闭旧传输的等待上限（可运行期改写）。</summary>
    private TimeSpan _closeTimeout = DefaultCloseTimeout;

    /// <summary>连续多少次探活缺失判设备断开（可运行期改写）。</summary>
    private int _missingChecksBeforeDeviceLost = DefaultMissingChecksBeforeDeviceLost;
    private readonly Lock _gate = new();

    private ISerialTransport? _transport;

    /// <summary>后台关闭线程（串口 Close 会等在途写/驱动排空，不能占着关闭路径）。</summary>
    private Thread? _pendingClose;

    /// <summary>按端口名登记的「待完成关闭」（跨适配器实例：移除后重加/重开会遇到上一实例的后台关闭）。</summary>
    private static readonly ConcurrentDictionary<string, Thread> PendingCloses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次打开的端口名（后台关闭登记用）。</summary>
    private string _portName = string.Empty;
    private IExternalAccessReady? _ready;
    private ExternalAccessEndpoint? _endpoint;
    private TimeSpan _writeTimeout = DefaultWriteTimeout;
    private volatile bool _open;
    private int _missingChecks;

    /// <summary>创建串口适配件（默认使用真实 <see cref="SerialPort"/>）。</summary>
    public SerialAccessAdapter()
        : this(static () => new SerialPortTransport())
    {
    }

    /// <summary>测试缝：用可替换的传输实现驱动逻辑（不碰真实端口）。</summary>
    internal SerialAccessAdapter(Func<ISerialTransport> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        _transportFactory = transportFactory;
    }

    /// <inheritdoc />
    public Type ParametersType => typeof(SerialConfig);

    /// <inheritdoc />
    /// <remarks>
    /// 记住值并对**当前传输**立即生效；之后新建的传输在打开时用当前值。
    /// 记忆与下发都在锁外读快照，避免与读泵/写路径交叉。
    /// </remarks>
    public void SetWriteTimeout(TimeSpan timeout)
    {
        var value = timeout > TimeSpan.Zero ? timeout : DefaultWriteTimeout;

        ISerialTransport? transport;
        lock (_gate)
        {
            _writeTimeout = value;
            transport = _transport;
        }

        transport?.SetWriteTimeout(value);
    }

    /// <inheritdoc />
    public void ApplyTuning(ExternalAccessTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        _closeTimeout = tuning.CloseTimeout > TimeSpan.Zero ? tuning.CloseTimeout : DefaultCloseTimeout;
        Volatile.Write(ref _missingChecksBeforeDeviceLost, Math.Clamp(tuning.DeviceLostMisses, 1, 10));

        if (tuning.BufferBytes >= 4096)
        {
            _bufferBytes = tuning.BufferBytes;
        }
    }

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not SerialConfig config)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.FrameworkInternal,
                Detail: nameof(SerialConfig)));
            return;
        }

        if (string.IsNullOrWhiteSpace(config.PortName))
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.PortNotSelected,
                Value: config.PortName));
            return;
        }

        Close(_closeTimeout);
        JoinPendingClose(_closeTimeout);
        JoinPendingClose(config.PortName, _closeTimeout);
        _portName = config.PortName;
        _buffer = new StreamAdapterBuffer(_bufferBytes);

        var endpoint = new ExternalAccessEndpoint(config.PortName);
        ready.SetConnecting(endpoint);

        ISerialTransport? transport = null;
        try
        {
            transport = _transportFactory();

            // 先订阅再打开：端口打开后立即到达的字节不会因为“还没挂处理器”而丢。
            transport.DataReceived += OnTransportData;
            transport.ReadFaulted += OnTransportFaulted;

            lock (_gate)
            {
                _transport = transport;
                _ready = ready;
                _endpoint = endpoint;
                _open = true;
                _missingChecks = 0;
            }

            // 打开前把写超时交给传输：第一次写就受它约束（不等到第一次设置变更）
            transport.SetWriteTimeout(_writeTimeout);
            transport.Open(config);
        }
        catch (Exception ex)
        {
            if (transport is not null)
            {
                transport.DataReceived -= OnTransportData;
                transport.ReadFaulted -= OnTransportFaulted;
                transport.Dispose();
            }

            lock (_gate)
            {
                _open = false;
                _transport = null;
                _ready = null;
                _endpoint = null;
            }

            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.OpenFailed,
                endpoint,
                Detail: DetailOf(ex)));
            return;
        }

        ready.SetOpen(endpoint);
    }

    /// <inheritdoc />
    public bool TryRead(Span<byte> destination, out int length)
    {
        length = 0;
        return _open && _buffer.TryDequeue(destination, out length);
    }

    /// <inheritdoc />
    public bool Write(ReadOnlySpan<byte> payload)
    {
        ISerialTransport? transport;

        lock (_gate)
        {
            if (!_open || _transport is null)
            {
                return false;
            }

            transport = _transport;
        }

        try
        {
            transport.Write(payload);
            return true;
        }
        catch (Exception ex)
        {
            IExternalAccessReady? ready;
            ExternalAccessEndpoint? endpoint;

            lock (_gate)
            {
                if (!_open)
                {
                    // 故障已由读泵 / 探活报过：同一拍不重复上报。
                    return false;
                }

                _open = false;
                ready = _ready;
                endpoint = _endpoint;
            }

            ready?.SetFaulted(FailureOf(ex, ExternalAccessFailureKind.WriteFailed, endpoint));
            return false;
        }
    }

    /// <inheritdoc />
    public void Close(TimeSpan timeout)
    {
        ISerialTransport? transport;

        lock (_gate)
        {
            _open = false;
            _missingChecks = 0;
            transport = _transport;
            _transport = null;
            _ready = null;
            _endpoint = null;
        }

        if (transport is not null)
        {
            transport.DataReceived -= OnTransportData;
            transport.ReadFaulted -= OnTransportFaulted;

            // **不在关闭路径上等串口的阻塞工作**：SerialPort.Close 会等在途写/驱动排空；
            // 把这段等待放进后台线程，关闭调用最多只等 50 ms，端口最终由该线程释放。
            var closer = new Thread(() =>
            {
                try
                {
                    transport.Close();
                }
                catch
                {
                    // 关闭失败不改变"已经不再使用该会话"的事实；资源由 Dispose 兜底。
                }

                try
                {
                    transport.Dispose();
                }
                catch
                {
                    // 已断开/已释放：关闭路径不向外抛。
                }
            })
            {
                IsBackground = true,
                Name = "access-serial-close",
            };

            _pendingClose = closer;
            if (_portName.Length > 0)
            {
                PendingCloses[_portName] = closer;
            }

            closer.Start();
            _ = closer.Join(TimeSpan.FromMilliseconds(50));
        }

        _buffer.Clear();
    }

    /// <inheritdoc />
    public void CheckLiveness()
    {
        if (!_open)
        {
            return;
        }

        if (TransportPresent())
        {
            _missingChecks = 0;
            return;
        }

        if (++_missingChecks < Volatile.Read(ref _missingChecksBeforeDeviceLost))
        {
            return;
        }

        IExternalAccessReady? ready;
        ExternalAccessEndpoint? endpoint;

        lock (_gate)
        {
            if (!_open)
            {
                return;
            }

            // 只把适配器置为不可用：传输的释放归会话（实例线程），探活路径不 Close/Dispose。
            _open = false;
            ready = _ready;
            endpoint = _endpoint;
        }

        ready?.SetFaulted(new ExternalAccessFailure(
            ExternalAccessFailureKind.DeviceLost,
            endpoint,
            Detail: AccessText.Get("access.diag.serialDeviceLost")));
    }

    /// <summary>等同一端口上一次的后台关闭完成（跨实例；有界）。</summary>
    private static void JoinPendingClose(string portName, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(portName) || !PendingCloses.TryRemove(portName, out var pending))
        {
            return;
        }

        if (pending.IsAlive && pending != Thread.CurrentThread)
        {
            _ = pending.Join(timeout);
        }
    }

    /// <summary>等上次的后台关闭完成（有界；重开需要端口真的释放）。</summary>
    private void JoinPendingClose(TimeSpan timeout)
    {
        var pending = _pendingClose;
        _pendingClose = null;

        if (pending is null || !pending.IsAlive || pending == Thread.CurrentThread)
        {
            return;
        }

        _ = pending.Join(timeout);
    }

    private void OnTransportData(byte[] data)
    {
        IExternalAccessReady? ready;

        lock (_gate)
        {
            if (!_open)
            {
                return;
            }

            ready = _ready;
        }

        if (ready is null || data.Length == 0)
        {
            return;
        }

        // 字节流暂存：不按"一次事件一单元"记账——驱动把长文本拆成几十个小块是常态，
        // 会话侧会按契约单元（≤1472 B）切出来取；这里只负责把字节按序收好。
        if (!_buffer.TryEnqueue(data, out var droppedBytes))
        {
            ready.ReportDropped(1, data.Length);
        }
        else if (droppedBytes > 0)
        {
            ready.ReportDropped(1, droppedBytes);
        }

        ready.NotifyReady();
    }

    private void OnTransportFaulted(Exception exception)
    {
        IExternalAccessReady? ready;
        ExternalAccessEndpoint? endpoint;

        lock (_gate)
        {
            if (!_open)
            {
                return;
            }

            // 故障后不再把它当可写/可读会话；用户重新打开时先走 Close。
            _open = false;
            ready = _ready;
            endpoint = _endpoint;
        }

        // 事件线程只报事实：是"设备没了"还是"读失败"由端口是否还在判定，释放留给实例线程。
        ready?.SetFaulted(FailureOf(exception, ExternalAccessFailureKind.ReadFailed, endpoint));
    }

    /// <summary>端口是否仍在系统中。</summary>
    private bool TransportPresent()
    {
        ISerialTransport? transport;

        lock (_gate)
        {
            transport = _transport;
        }

        // 端口枚举有系统调用开销，且可能与数据/故障回调交叉：放在锁外做。
        return transport is not null && transport.IsPresent();
    }

    /// <summary>
    /// 把一次读写异常翻译成失败事实：端口已不在系统里就是"设备已断开"，否则才是读写失败。
    /// </summary>
    /// <remarks>只看端口是否还在，不猜异常类型：设备拔出时底层异常的形态并不稳定。</remarks>
    private ExternalAccessFailure FailureOf(
        Exception exception,
        ExternalAccessFailureKind presentKind,
        ExternalAccessEndpoint? endpoint)
    {
        var kind = TransportPresent() ? presentKind : ExternalAccessFailureKind.DeviceLost;

        return new ExternalAccessFailure(
            kind,
            endpoint,
            Detail: kind == ExternalAccessFailureKind.DeviceLost
                ? AccessText.Get("access.diag.serialDeviceLost")
                : DetailOf(exception));
    }

    private static string DetailOf(Exception exception) =>
        exception.GetType().Name + ": " + exception.Message;
}

/// <summary>串口传输的最小缝（测试可替换；真实实现包 <see cref="SerialPort"/>）。</summary>
internal interface ISerialTransport : IDisposable
{
    /// <summary>串口收到一段字节（已从端口读出；长度 = 本次实际读到的字节数）。</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>读取路径发生故障（设备拔出 / 端口被关闭等）；空闲无数据不触发。</summary>
    event Action<Exception>? ReadFaulted;

    /// <summary>
    /// 端口是否仍在系统中且处于打开状态：仍在 ⇒ <see langword="true"/>；已释放 / 端口不在系统 ⇒ <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 读写故障但端口仍在时必须返回 <see langword="true"/>：适配器据此把故障种类判成读写失败，
    /// 而不是"设备已断开"。
    /// </remarks>
    /// <returns>端口是否可继续使用。</returns>
    bool IsPresent();

    void Open(SerialConfig config);

    void Write(ReadOnlySpan<byte> payload);

    /// <summary>
    /// 设置写超时（打开前后都可调用；实现尽力落实，**不抛**）。
    /// </summary>
    /// <param name="timeout">写超时（大于 0）。</param>
    void SetWriteTimeout(TimeSpan timeout);

    void Close();
}

/// <summary>真实串口传输。</summary>
internal sealed class SerialPortTransport : ISerialTransport
{
    /// <summary>写超时的缺省值（毫秒）：与 <see cref="SerialAccessAdapter"/> 的缺省同值。</summary>
    private const int DefaultWriteTimeoutMs = 1000;

    private SerialPort? _port;
    private volatile string _portName = string.Empty;
    private volatile bool _faulted;
    private int _writeTimeoutMs = DefaultWriteTimeoutMs;

    /// <inheritdoc />
    public event Action<byte[]>? DataReceived;

    /// <inheritdoc />
    public event Action<Exception>? ReadFaulted;

    public void Open(SerialConfig config)
    {
        var port = new SerialPort(
            config.PortName,
            (int)config.BaudRate,
            (Parity)(int)config.Parity,
            (int)config.DataBits,
            (StopBits)(int)config.StopBits)
        {
            // 读由 DataReceived 驱动：不设读超时，绝不把“暂时没有数据”变成 IOException。
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = _writeTimeoutMs,
        };

        port.DataReceived += OnDataReceived;
        _faulted = false;

        try
        {
            port.Open();
        }
        catch
        {
            port.DataReceived -= OnDataReceived;
            port.Dispose();
            throw;
        }

        _portName = config.PortName;
        _port = port;
    }

    /// <inheritdoc />
    public bool IsPresent()
    {
        try
        {
            var name = _portName;

            return _port is { IsOpen: true }
                && name.Length > 0
                && Array.Exists(
                    SerialPort.GetPortNames(),
                    candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // 热插拔瞬间枚举端口会抛：按"不在系统中"处理，由会话进入故障态。
            return false;
        }
    }

    public void Write(ReadOnlySpan<byte> payload)
    {
        var stream = Port().BaseStream;
        stream.Write(payload);
        stream.Flush();
    }

    public void SetWriteTimeout(TimeSpan timeout)
    {
        var value = timeout > TimeSpan.Zero
            ? (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue)
            : DefaultWriteTimeoutMs;

        Interlocked.Exchange(ref _writeTimeoutMs, value);

        var port = _port;
        if (port is null)
        {
            return;   // 尚未打开：打开时按当前值设置
        }

        try
        {
            // 打开后也能改：写超时是"每次写等多久"，不是端口形态的一部分
            port.WriteTimeout = value;
        }
        catch (Exception)
        {
            // 端口刚被关闭 / 平台不支持：保持原值。写路径失败会按既有失败路径上报。
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        var port = _port;

        if (port is null)
        {
            return;
        }

        _port = null;
        port.DataReceived -= OnDataReceived;

        try
        {
            port.Close();
        }
        catch
        {
            // 设备已经断开时关闭会抛；关闭路径不向外抛。
        }

        port.Dispose();
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (_faulted || sender is not SerialPort port)
        {
            return;
        }

        try
        {
            var available = port.BytesToRead;

            if (available <= 0)
            {
                return;
            }

            var buffer = new byte[available];
            var read = port.Read(buffer, 0, available);

            if (read <= 0)
            {
                return;
            }

            DataReceived?.Invoke(read == buffer.Length ? buffer : buffer[..read]);
        }
        catch (TimeoutException)
        {
            // 瞬时空闲不是故障：等下一次 DataReceived。绝不把“暂时没有数据”报成读取失败。
        }
        catch (Exception ex) when (ex is IOException
                                      or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            _faulted = true;
            ReadFaulted?.Invoke(ex);
        }
    }

    private SerialPort Port() =>
        _port ?? throw new InvalidOperationException(AccessText.Get("access.diag.serialNotOpen"));
}
