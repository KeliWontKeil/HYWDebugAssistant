using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **TCP 服务端适配件**：监听一个本地端口，把 N 条连接复用一个会话（收到的字节汇入同一队列，写入广播给所有连接）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>最大连接数来自参数；超限的连接被接受后立即关闭并计数；</item>
///   <item>单条客户端断开只清理该连接，不把整个监听打成故障；监听本身失败才 <c>Faulted</c>；</item>
///   <item>不自动重试：监听失败后用户再次〔启动〕。</item>
/// </list>
/// </remarks>
public sealed class TcpServerAccessAdapter : IExternalAccessAdapter, IExternalAccessWriteTimeout, IExternalAccessTuning
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
    private readonly ConcurrentDictionary<Socket, Task> _clients = new();

    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _accept;
    private IExternalAccessReady? _ready;
    private TcpServerConfig? _config;
    private int _writeTimeoutMs = DefaultWriteTimeoutMs;
    private volatile bool _open;
    private int _connectionCount;

    /// <inheritdoc />
    public Type ParametersType => typeof(TcpServerConfig);

    /// <inheritdoc />
    /// <remarks>记住值并立即作用于**当前全部客户端**；新接受的连接在接入时用当前值。</remarks>
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

        foreach (var client in _clients.Keys)
        {
            try
            {
                client.SendTimeout = value;
            }
            catch (Exception)
            {
                // 单条连接刚断开 / 平台不支持：跳过它，不影响其它连接
            }
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

        if (parameters is not TcpServerConfig config)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.FrameworkInternal,
                Detail: nameof(TcpServerConfig)));
            return;
        }

        if (config.LocalPort is < 0 or > 65535)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.PortOutOfRange,
                Value: config.LocalPort.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        var local = new ExternalAccessEndpoint(config.LocalAddress, config.LocalPort);

        if (!TryResolveAddress(config.LocalAddress, out var address))
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.AddressInvalid,
                local,
                Value: config.LocalAddress));
            return;
        }

        Close(_closeTimeout);
        _buffer = new StreamAdapterBuffer(_bufferBytes);
        _clients.Clear();
        Interlocked.Exchange(ref _connectionCount, 0);

        ready.SetConnecting(local);

        var listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            listener.Bind(new IPEndPoint(address, config.LocalPort));
            listener.Listen(Math.Max(16, Math.Max(1, config.MaxConnections)));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listener.Dispose();
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.PortInUse,
                local,
                Value: config.LocalPort.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        catch (Exception ex)
        {
            listener.Dispose();
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.ListenFailed,
                local,
                Detail: DetailOf(ex)));
            return;
        }

        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _listener = listener;
            _config = config;
            _ready = ready;
            _cts = cts;
            _open = true;
        }

        ready.SetOpen(EndpointOf(listener));

        _accept = Task.Run(() => AcceptAsync(listener, config, ready, cts.Token));
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
        lock (_gate)
        {
            if (!_open)
            {
                return false;
            }
        }

        var sent = false;

        foreach (var client in _clients.Keys)
        {
            try
            {
                _ = client.Send(payload, SocketFlags.None);
                sent = true;
            }
            catch
            {
                // 单条连接坏了只清它，不把整个监听打成故障。
                CloseClient(client);
            }
        }

        return sent;
    }

    /// <inheritdoc />
    public void Close(TimeSpan timeout)
    {
        CancellationTokenSource? cts;
        Task? accept;
        Socket? listener;

        lock (_gate)
        {
            _open = false;
            cts = _cts;
            _cts = null;
            accept = _accept;
            _accept = null;
            listener = _listener;
            _listener = null;
            _config = null;
            _ready = null;
        }

        cts?.Cancel();

        try
        {
            listener?.Close();
        }
        catch
        {
            // 关闭路径不向外抛。
        }

        foreach (var client in _clients.Keys)
        {
            CloseClient(client);
        }

        if (accept is not null)
        {
            try
            {
                _ = accept.Wait(timeout);
            }
            catch (AggregateException)
            {
                // 取消/关闭导致的退出。
            }
        }

        var tasks = _clients.Values.ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                _ = Task.WaitAll(tasks, timeout);
            }
            catch (AggregateException)
            {
                // 同上。
            }
        }

        _clients.Clear();
        Interlocked.Exchange(ref _connectionCount, 0);
        cts?.Dispose();
        listener?.Dispose();
        _buffer.Clear();
    }

    private async Task AcceptAsync(
        Socket listener,
        TcpServerConfig config,
        IExternalAccessReady ready,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ready.SetFaulted(new ExternalAccessFailure(
                        ExternalAccessFailureKind.AcceptFailed,
                        EndpointOf(listener),
                        Detail: DetailOf(ex)));
                }

                return;
            }

            if (Volatile.Read(ref _connectionCount) >= Math.Max(1, config.MaxConnections))
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // 超限拒绝。
                }

                ready.ReportDropped(1, 0);
                continue;
            }

            try
            {
                client.NoDelay = config.NoDelay;
                client.SendTimeout = Volatile.Read(ref _writeTimeoutMs);
            }
            catch
            {
                // 个别平台/连接不支持；不影响后续收发。
            }

            var count = Interlocked.Increment(ref _connectionCount);
            ready.SetConnections(count);

            var task = Task.Run(() => ClientLoopAsync(client, ready, token), token);
            _clients[client] = task;
        }
    }

    private async Task ClientLoopAsync(Socket client, IExternalAccessReady ready, CancellationToken token)
    {
        var buffer = new byte[ExternalAccessDeclaration.DefaultUnitBytes];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int length;
                try
                {
                    length = await client.ReceiveAsync(buffer, SocketFlags.None, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    return;
                }

                if (length == 0)
                {
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
        finally
        {
            _ = _clients.TryRemove(client, out _);

            try
            {
                client.Close();
            }
            catch
            {
                // 关闭路径不向外抛。
            }

            var count = Interlocked.Decrement(ref _connectionCount);
            ready.SetConnections(Math.Max(0, count));
        }
    }

    private static void CloseClient(Socket client)
    {
        try
        {
            client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // 已经断开。
        }

        try
        {
            client.Close();
        }
        catch
        {
            // 关闭路径不向外抛。
        }
    }

    private static ExternalAccessEndpoint? EndpointOf(Socket socket) =>
        socket.LocalEndPoint is IPEndPoint endpoint
            ? new ExternalAccessEndpoint(endpoint.Address.ToString(), endpoint.Port)
            : null;

    private static string DetailOf(Exception exception) =>
        exception.GetType().Name + ": " + exception.Message;

    private static bool TryResolveAddress(string text, out IPAddress address)
    {
        if (IPAddress.TryParse(text, out var parsed))
        {
            address = parsed;
            return true;
        }

        try
        {
            var resolved = Dns.GetHostAddresses(text)
                .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);

            if (resolved is not null)
            {
                address = resolved;
                return true;
            }
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // 落到失败返回。
        }

        address = IPAddress.Any;
        return false;
    }
}
