using System.IO.Ports;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **串口适配件**：打开一个 COM 口，异步读泵把字节放进暂存队列，框架按就绪信号来取。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>参数类型 = <see cref="SerialConfig"/>（配置表单与持久化复用）；</item>
///   <item>读用 <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>，不自建线程；</item>
///   <item>写同步、有超时；失败立即报告并停止（<b>不自动重连</b>）；</item>
///   <item>关闭先取消、再关端口、再有界等待泵退出。</item>
/// </list>
/// </remarks>
public sealed class SerialAccessAdapter : IExternalAccessAdapter
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<ISerialTransport> _transportFactory;
    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();

    private ISerialTransport? _transport;
    private CancellationTokenSource? _cts;
    private Task? _pump;
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
            ready.SetFaulted($"参数类型不是 {nameof(SerialConfig)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.PortName))
        {
            ready.SetFaulted("未选择串口（端口号为空）");
            return;
        }

        Close(CloseTimeout);
        _buffer.Clear();

        ISerialTransport? transport = null;
        try
        {
            transport = _transportFactory();
            transport.Open(config);
        }
        catch (Exception ex)
        {
            transport?.Dispose();
            ready.SetFaulted($"打开串口失败：{ex.GetType().Name} — {ex.Message}");
            return;
        }

        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _transport = transport;
            _ready = ready;
            _cts = cts;
            _open = true;
        }

        ready.SetOpen(
            $"{config.PortName} · {(int)config.BaudRate} · {(int)config.DataBits}{ParityLetter(config.Parity)}{StopBitsText(config.StopBits)}");

        _pump = Task.Run(() => PumpAsync(transport, ready, cts.Token));
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
            ready?.SetFaulted($"串口写入失败：{ex.GetType().Name} — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void Close(TimeSpan timeout)
    {
        CancellationTokenSource? cts;
        Task? pump;
        ISerialTransport? transport;

        lock (_gate)
        {
            _open = false;
            cts = _cts;
            _cts = null;
            pump = _pump;
            _pump = null;
            transport = _transport;
            _transport = null;
            _ready = null;
        }

        cts?.Cancel();

        try
        {
            transport?.Close();
        }
        catch
        {
            // 关闭失败不改变"已经不再使用该会话"的事实；资源由 Dispose 兜底。
        }

        if (pump is not null)
        {
            try
            {
                _ = pump.Wait(timeout);
            }
            catch (AggregateException)
            {
                // 泵已经不在（取消/端口关闭）；这是关闭路径，不向外抛。
            }
        }

        cts?.Dispose();
        transport?.Dispose();
        _buffer.Clear();
    }

    private async Task PumpAsync(ISerialTransport transport, IExternalAccessReady ready, CancellationToken token)
    {
        var buffer = new byte[RawBytesFraming.Capacity];

        while (!token.IsCancellationRequested)
        {
            int length;
            try
            {
                length = await transport.ReadAsync(buffer, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ready.SetFaulted($"串口读取失败：{ex.GetType().Name} — {ex.Message}");
                }

                return;
            }

            if (length <= 0)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            if (!_buffer.TryEnqueue(buffer.AsSpan(0, length), out var droppedBytes))
            {
                ready.ReportDropped(1, length);
            }
            else if (droppedBytes > 0)
            {
                ready.ReportDropped(1, droppedBytes);
            }

            ready.NotifyReady();
        }
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
    void Open(SerialConfig config);

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token);

    void Write(ReadOnlySpan<byte> payload);

    void Close();
}

/// <summary>真实串口传输。</summary>
internal sealed class SerialPortTransport : ISerialTransport
{
    private SerialPort? _port;

    public void Open(SerialConfig config)
    {
        var port = new SerialPort(
            config.PortName,
            (int)config.BaudRate,
            (Parity)(int)config.Parity,
            (int)config.DataBits,
            (StopBits)(int)config.StopBits)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000,
        };

        port.Open();
        _port = port;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token) =>
        Port().BaseStream.ReadAsync(buffer, token);

    public void Write(ReadOnlySpan<byte> payload)
    {
        var stream = Port().BaseStream;
        stream.Write(payload);
        stream.Flush();
    }

    public void Close()
    {
        try
        {
            _port?.Close();
        }
        catch
        {
            // 端口已经断开时关闭会抛；关闭路径不向外抛。
        }
    }

    public void Dispose()
    {
        _port?.Dispose();
        _port = null;
    }

    private SerialPort Port() =>
        _port ?? throw new InvalidOperationException("串口尚未打开");
}
