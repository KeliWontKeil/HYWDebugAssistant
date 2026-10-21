using System.Collections.Concurrent;
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
public sealed class TcpServerAccessAdapter : IExternalAccessAdapter
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<Socket, Task> _clients = new();

    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _accept;
    private IExternalAccessReady? _ready;
    private TcpServerConfig? _config;
    private volatile bool _open;
    private int _connectionCount;

    /// <inheritdoc />
    public Type ParametersType => typeof(TcpServerConfig);

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not TcpServerConfig config)
        {
            ready.SetFaulted($"参数类型不是 {nameof(TcpServerConfig)}");
            return;
        }

        if (config.LocalPort is < 0 or > 65535)
        {
            ready.SetFaulted($"本地端口超出范围：{config.LocalPort}");
            return;
        }

        if (!TryResolveAddress(config.LocalAddress, out var address))
        {
            ready.SetFaulted($"本地地址不合法：{config.LocalAddress}");
            return;
        }

        Close(CloseTimeout);
        _buffer.Clear();
        _clients.Clear();
        Interlocked.Exchange(ref _connectionCount, 0);

        var listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            listener.Bind(new IPEndPoint(address, config.LocalPort));
            listener.Listen(Math.Max(16, Math.Max(1, config.MaxConnections)));
        }
        catch (Exception ex)
        {
            listener.Dispose();
            ready.SetFaulted($"监听失败：{ex.GetType().Name} — {ex.Message}");
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

        var actual = (IPEndPoint)listener.LocalEndPoint!;
        ready.SetOpen($"{actual.Address}:{actual.Port}");

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
                    ready.SetFaulted($"接受连接失败：{ex.GetType().Name} — {ex.Message}");
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
                client.SendTimeout = 1000;
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
        var buffer = new byte[RawBytesFraming.Capacity];

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
