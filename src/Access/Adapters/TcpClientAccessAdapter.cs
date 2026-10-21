using System.Net.Sockets;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **TCP 客户端适配件**：连到远端，异步接收，按就绪信号交给框架。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>连接超时取参数；<c>NoDelay</c> 在连上后应用；</item>
///   <item><b>不自动重试</b>：连接失败或对端关闭 ⇒ 报告失败并停止，用户再次点〔连接〕才会重开；</item>
///   <item>写同步、有发送超时；关闭先取消、再关套接字、再有界等待泵退出。</item>
/// </list>
/// </remarks>
public sealed class TcpClientAccessAdapter : IExternalAccessAdapter
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private IExternalAccessReady? _ready;
    private volatile bool _open;

    /// <inheritdoc />
    public Type ParametersType => typeof(TcpClientConfig);

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not TcpClientConfig config)
        {
            ready.SetFaulted($"参数类型不是 {nameof(TcpClientConfig)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.RemoteHost))
        {
            ready.SetFaulted("目标地址为空");
            return;
        }

        if (config.RemotePort is < 1 or > 65535)
        {
            ready.SetFaulted($"目标端口超出范围：{config.RemotePort}");
            return;
        }

        Close(CloseTimeout);
        _buffer.Clear();

        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _cts = cts;
            _ready = ready;
            _open = true;
        }

        ready.SetConnecting($"{config.RemoteHost}:{config.RemotePort}");
        _pump = Task.Run(() => RunAsync(config, ready, cts.Token));
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
        Socket? socket;
        IExternalAccessReady? ready;

        lock (_gate)
        {
            if (!_open || _socket is null)
            {
                return false;
            }

            socket = _socket;
            ready = _ready;
        }

        try
        {
            _ = socket.Send(payload, SocketFlags.None);
            return true;
        }
        catch (Exception ex)
        {
            ready?.SetFaulted($"发送失败：{ex.GetType().Name} — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void Close(TimeSpan timeout)
    {
        CancellationTokenSource? cts;
        Task? pump;
        Socket? socket;

        lock (_gate)
        {
            _open = false;
            cts = _cts;
            _cts = null;
            pump = _pump;
            _pump = null;
            socket = _socket;
            _socket = null;
            _ready = null;
        }

        cts?.Cancel();

        try
        {
            socket?.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // 没连上 / 已经断开；关闭路径不向外抛。
        }

        try
        {
            socket?.Close();
        }
        catch
        {
            // 同上。
        }

        if (pump is not null)
        {
            try
            {
                _ = pump.Wait(timeout);
            }
            catch (AggregateException)
            {
                // 取消/断开导致的泵退出；关闭路径不向外抛。
            }
        }

        cts?.Dispose();
        socket?.Dispose();
        _buffer.Clear();
    }

    private async Task RunAsync(
        TcpClientConfig config,
        IExternalAccessReady ready,
        CancellationToken token)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            SendTimeout = 1000,
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.ConnectTimeout)));

            try
            {
                await socket.ConnectAsync(config.RemoteHost, config.RemotePort, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested)
                {
                    ready.SetFaulted($"连接超时（{config.ConnectTimeout} 秒）：{config.RemoteHost}:{config.RemotePort}");
                }

                return;
            }

            socket.NoDelay = config.NoDelay;

            lock (_gate)
            {
                if (!_open)
                {
                    return;
                }

                _socket = socket;
            }

            ready.SetOpen($"{config.RemoteHost}:{config.RemotePort}");

            var buffer = new byte[RawBytesFraming.Capacity];

            while (!token.IsCancellationRequested)
            {
                int length;
                try
                {
                    length = await socket.ReceiveAsync(buffer, SocketFlags.None, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        ready.SetFaulted($"接收失败：{ex.GetType().Name} — {ex.Message}");
                    }

                    return;
                }

                if (length == 0)
                {
                    if (!token.IsCancellationRequested)
                    {
                        ready.SetFaulted($"对端已关闭连接：{config.RemoteHost}:{config.RemotePort}");
                    }

                    return;
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
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                ready.SetFaulted($"连接失败：{ex.GetType().Name} — {ex.Message}");
            }
        }
        finally
        {
            try
            {
                socket.Close();
            }
            catch
            {
                // 关闭路径不向外抛。
            }

            lock (_gate)
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                }
            }
        }
    }
}
