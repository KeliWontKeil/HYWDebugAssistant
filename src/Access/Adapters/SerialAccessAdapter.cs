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
///   <item>关闭先退订事件、再关端口；读取异常只报一次，不把“空闲无数据”当成故障。</item>
/// </list>
/// </remarks>
public sealed class SerialAccessAdapter : IExternalAccessAdapter
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<ISerialTransport> _transportFactory;
    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();

    private ISerialTransport? _transport;
    private IExternalAccessReady? _ready;
    private volatile bool _open;

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
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not SerialConfig config)
        {
            ready.SetFaulted(AccessText.Get("access.serial.parametersTypeInvalid", nameof(SerialConfig)));
            return;
        }

        if (string.IsNullOrWhiteSpace(config.PortName))
        {
            ready.SetFaulted(AccessText.Get("access.serial.portNotSelected"));
            return;
        }

        Close(CloseTimeout);
        _buffer.Clear();

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
                _open = true;
            }

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
            }

            ready.SetFaulted(AccessText.Get("access.serial.openFailed", ex.GetType().Name, ex.Message));
            return;
        }

        ready.SetOpen(
            $"{config.PortName} · {(int)config.BaudRate} · {(int)config.DataBits}{ParityLetter(config.Parity)}{StopBitsText(config.StopBits)}");
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
        IExternalAccessReady? ready;

        lock (_gate)
        {
            if (!_open || _transport is null)
            {
                return false;
            }

            transport = _transport;
            ready = _ready;
        }

        try
        {
            transport.Write(payload);
            return true;
        }
        catch (Exception ex)
        {
            ready?.SetFaulted(AccessText.Get("access.serial.writeFailed", ex.GetType().Name, ex.Message));
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
            transport = _transport;
            _transport = null;
            _ready = null;
        }

        if (transport is not null)
        {
            transport.DataReceived -= OnTransportData;
            transport.ReadFaulted -= OnTransportFaulted;

            try
            {
                transport.Close();
            }
            catch
            {
                // 关闭失败不改变"已经不再使用该会话"的事实；资源由 Dispose 兜底。
            }

            transport.Dispose();
        }

        _buffer.Clear();
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

        lock (_gate)
        {
            if (!_open)
            {
                return;
            }

            // 故障后不再把它当可写/可读会话；用户重新打开时先走 Close。
            _open = false;
            ready = _ready;
        }

        ready?.SetFaulted(AccessText.Get("access.serial.readFailed", exception.GetType().Name, exception.Message));
    }

    private static string ParityLetter(SerialParity parity) => parity switch
    {
        SerialParity.Odd => "O",
        SerialParity.Even => "E",
        SerialParity.Mark => "M",
        SerialParity.Space => "S",
        _ => "N",
    };

    private static string StopBitsText(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.Two => "2",
        SerialStopBits.OnePointFive => "1.5",
        _ => "1",
    };
}

/// <summary>串口传输的最小缝（测试可替换；真实实现包 <see cref="SerialPort"/>）。</summary>
internal interface ISerialTransport : IDisposable
{
    /// <summary>串口收到一段字节（已从端口读出；长度 = 本次实际读到的字节数）。</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>读取路径发生故障（设备拔出 / 端口被关闭等）；空闲无数据不触发。</summary>
    event Action<Exception>? ReadFaulted;

    void Open(SerialConfig config);

    void Write(ReadOnlySpan<byte> payload);

    void Close();
}

/// <summary>真实串口传输。</summary>
internal sealed class SerialPortTransport : ISerialTransport
{
    private SerialPort? _port;
    private volatile bool _faulted;

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
            WriteTimeout = 1000,
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

        _port = port;
    }

    public void Write(ReadOnlySpan<byte> payload)
    {
        var stream = Port().BaseStream;
        stream.Write(payload);
        stream.Flush();
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
        _port ?? throw new InvalidOperationException(AccessText.Get("access.serial.notOpen"));
}
