using System.Globalization;
using System.Net;
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
public sealed class TcpClientAccessAdapter : IExternalAccessAdapter, IExternalAccessWriteTimeout, IExternalAccessTuning
{
    private static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(1);

    /// <summary>写（发送）超时的缺省值（毫秒）。</summary>
    private const int DefaultWriteTimeoutMs = 1000;

    private StreamAdapterBuffer _buffer = new();

    /// <summary>暂存容量（字节；下一次 Open 生效）。</summary>
    private int _bufferBytes = 64 * 1024;

    /// <summary>关闭旧传输的等待上限（可运行期改写）。</summary>
    private TimeSpan _closeTimeout = DefaultCloseTimeout;
    private readonly Lock _gate = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private IExternalAccessReady? _ready;
    private ExternalAccessEndpoint? _endpoint;
    private int _writeTimeoutMs = DefaultWriteTimeoutMs;
    private volatile bool _open;

    /// <inheritdoc />
    public Type ParametersType => typeof(TcpClientConfig);

    /// <inheritdoc />
    /// <remarks>记住值并对**当前套接字**立即生效；之后新建的套接字在连接时用当前值。</remarks>
    /// <inheritdoc />
    public void ApplyTuning(ExternalAccessTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        _closeTimeout = tuning.CloseTimeout > TimeSpan.Zero ? tuning.CloseTimeout : DefaultCloseTimeout;

        if (tuning.BufferBytes >= 4096)
        {
            _bufferBytes = tuning.BufferBytes;
        }
    }

    public void SetWriteTimeout(TimeSpan timeout)
    {
        var value = NormalizeWriteTimeoutMs(timeout);
        Interlocked.Exchange(ref _writeTimeoutMs, value);

        Socket? socket;
        lock (_gate)
        {
            socket = _socket;
        }

        if (socket is null)
        {
            return;
        }

        try
        {
            socket.SendTimeout = value;
        }
        catch (Exception)
        {
            // 套接字刚被关闭 / 平台不支持：保持原值
        }
    }

    /// <summary>写超时毫秒数的收敛（非正值 ⇒ 缺省 1 秒；0 在套接字上等于无限等待）。</summary>
    /// <param name="timeout">设置里的值。</param>
    private static int NormalizeWriteTimeoutMs(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue) : DefaultWriteTimeoutMs;

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not TcpClientConfig config)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.FrameworkInternal,
                Detail: nameof(TcpClientConfig)));
            return;
        }

        var target = new ExternalAccessEndpoint(config.RemoteHost, config.RemotePort);

        if (string.IsNullOrWhiteSpace(config.RemoteHost))
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.AddressEmpty,
                Value: config.RemoteHost));
            return;
        }

        if (IPAddress.TryParse(config.RemoteHost, out var parsedTarget)
            && (parsedTarget.Equals(IPAddress.Any) || parsedTarget.Equals(IPAddress.IPv6Any)))
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.WildcardAddressNotAllowed,
                target,
                Value: config.RemoteHost));
            return;
        }

        if (config.RemotePort is < 1 or > 65535)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.PortOutOfRange,
                Value: config.RemotePort.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        Close(_closeTimeout);
        _buffer = new StreamAdapterBuffer(_bufferBytes);

        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _cts = cts;
            _ready = ready;
            _endpoint = target;
            _open = true;
        }

        ready.SetConnecting(target);
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
        ExternalAccessEndpoint? endpoint;

        lock (_gate)
        {
            if (!_open || _socket is null)
            {
                return false;
            }

            socket = _socket;
            ready = _ready;
            endpoint = _endpoint;
        }

        try
        {
            _ = socket.Send(payload, SocketFlags.None);
            return true;
        }
        catch (Exception ex)
        {
            ready?.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.WriteFailed,
                endpoint,
                Detail: DetailOf(ex)));
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
            _endpoint = null;
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
        var target = new ExternalAccessEndpoint(config.RemoteHost, config.RemotePort);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            SendTimeout = Volatile.Read(ref _writeTimeoutMs),
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
                    ready.SetFaulted(new ExternalAccessFailure(
                        ExternalAccessFailureKind.ConnectTimeout,
                        target));
                }

                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                ready.SetFaulted(new ExternalAccessFailure(
                    ExternalAccessFailureKind.ConnectionRefused,
                    target));
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

            ready.SetOpen(target);

            var buffer = new byte[ExternalAccessDeclaration.DefaultUnitBytes];

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
                        ready.SetFaulted(new ExternalAccessFailure(
                            ExternalAccessFailureKind.ReadFailed,
                            target,
                            Detail: DetailOf(ex)));
                    }

                    return;
                }

                if (length == 0)
                {
                    if (!token.IsCancellationRequested)
                    {
                        ready.SetFaulted(new ExternalAccessFailure(
                            ExternalAccessFailureKind.PeerClosed,
                            target));
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
                ready.SetFaulted(new ExternalAccessFailure(
                    ExternalAccessFailureKind.OpenFailed,
                    target,
                    Detail: DetailOf(ex)));
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

    private static string DetailOf(Exception exception) =>
        exception.GetType().Name + ": " + exception.Message;
}
