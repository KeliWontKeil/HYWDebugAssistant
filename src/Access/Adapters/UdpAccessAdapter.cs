using System.Globalization;
using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access.Adapters;

/// <summary>
/// **UDP 适配件**：绑定本地地址/端口，按数据报边界交付；写入默认目标。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>一次接收 = 一个数据报 = 一个单元（**不跨报文拼包**）；</item>
///   <item>超过单元容量的数据报会被平台截断；本适配件交付截断后的内容并用 <c>ReportDropped</c> 记账；</item>
///   <item>目标为空 ⇒ 只收不发（写入返回 <see langword="false"/>）；</item>
///   <item>不自动重试：绑定失败后用户再次〔绑定〕。</item>
/// </list>
/// </remarks>
public sealed class UdpAccessAdapter : IExternalAccessAdapter, IExternalAccessWriteTimeout, IExternalAccessTuning
{
    private static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(1);

    /// <summary>关闭旧传输的等待上限（可运行期改写）。</summary>
    private TimeSpan _closeTimeout = DefaultCloseTimeout;

    /// <summary>写（发送）超时的缺省值（毫秒）。</summary>
    private const int DefaultWriteTimeoutMs = 1000;

    /// <summary>UDP 报文的理论上限（接收缓冲必须能容纳整个报文，否则平台会抛异常而不是截断）。</summary>
    private const int UdpMaxDatagram = 65507;

    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private IExternalAccessReady? _ready;
    private IPEndPoint? _target;
    private ExternalAccessEndpoint? _endpoint;
    private int _writeTimeoutMs = DefaultWriteTimeoutMs;
    private volatile bool _open;

    /// <inheritdoc />
    public Type ParametersType => typeof(UdpConfig);

    /// <inheritdoc />
    /// <remarks>记住值并对**当前套接字**立即生效；重新绑定后的新套接字用当前值。</remarks>
    /// <inheritdoc />
    public void ApplyTuning(ExternalAccessTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        _closeTimeout = tuning.CloseTimeout > TimeSpan.Zero ? tuning.CloseTimeout : DefaultCloseTimeout;
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

        if (parameters is not UdpConfig config)
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.FrameworkInternal,
                Detail: nameof(UdpConfig)));
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

        if (!TryResolveAddress(config.LocalAddress, out var localAddress))
        {
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.AddressInvalid,
                local,
                Value: config.LocalAddress));
            return;
        }

        IPEndPoint? target = null;
        if (!string.IsNullOrWhiteSpace(config.RemoteAddress) && config.RemotePort > 0)
        {
            var remoteEndpoint = new ExternalAccessEndpoint(config.RemoteAddress, config.RemotePort);

            if (!TryResolveAddress(config.RemoteAddress, out var remote))
            {
                ready.SetFaulted(new ExternalAccessFailure(
                    ExternalAccessFailureKind.AddressInvalid,
                    remoteEndpoint,
                    Value: config.RemoteAddress));
                return;
            }

            if (remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any))
            {
                ready.SetFaulted(new ExternalAccessFailure(
                    ExternalAccessFailureKind.WildcardAddressNotAllowed,
                    remoteEndpoint,
                    Value: config.RemoteAddress));
                return;
            }

            target = new IPEndPoint(remote, config.RemotePort);
        }

        Close(_closeTimeout);
        _buffer.Clear();

        ready.SetConnecting(local);

        var socket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
        {
            EnableBroadcast = config.Broadcast,
            SendTimeout = Volatile.Read(ref _writeTimeoutMs),
        };

        try
        {
            socket.Bind(new IPEndPoint(localAddress, config.LocalPort));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            socket.Dispose();
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.PortInUse,
                local,
                Value: config.LocalPort.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            ready.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.BindFailed,
                local,
                Detail: DetailOf(ex)));
            return;
        }

        var actual = EndpointOf(socket);
        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _socket = socket;
            _target = target;
            _ready = ready;
            _endpoint = actual;
            _cts = cts;
            _open = true;
        }

        ready.SetOpen(actual);

        _pump = Task.Run(() => ReceiveAsync(socket, ready, cts.Token));
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
        IPEndPoint? target;
        IExternalAccessReady? ready;

        lock (_gate)
        {
            if (!_open || _socket is null || _target is null)
            {
                return false;
            }

            socket = _socket;
            target = _target;
            ready = _ready;
        }

        try
        {
            _ = socket.SendTo(payload, SocketFlags.None, target);
            return true;
        }
        catch (Exception ex)
        {
            ready?.SetFaulted(new ExternalAccessFailure(
                ExternalAccessFailureKind.WriteFailed,
                new ExternalAccessEndpoint(target.Address.ToString(), target.Port),
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
            _target = null;
            _endpoint = null;
        }

        cts?.Cancel();

        try
        {
            socket?.Close();
        }
        catch
        {
            // 关闭路径不向外抛。
        }

        if (pump is not null)
        {
            try
            {
                _ = pump.Wait(timeout);
            }
            catch (AggregateException)
            {
                // 取消/关闭导致的退出。
            }
        }

        cts?.Dispose();
        socket?.Dispose();
        _buffer.Clear();
    }

    private async Task ReceiveAsync(Socket socket, IExternalAccessReady ready, CancellationToken token)
    {
        // Windows 在"接收缓冲小于数据报"时会抛 SocketException 而不是截断 ⇒ 先按 UDP 最大报文收，
        // 再在应用层按单元容量截断并计数。
        var buffer = new byte[UdpMaxDatagram];
        var local = EndpointOf(socket);

        while (!token.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                result = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, remote, token)
                    .ConfigureAwait(false);
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
                        ExternalAccessFailureKind.ReadFailed,
                        local,
                        Detail: DetailOf(ex)));
                }

                return;
            }

            if (result.ReceivedBytes <= 0)
            {
                continue;
            }

            var length = Math.Min(result.ReceivedBytes, ExternalAccessDeclaration.DefaultUnitBytes);

            if (!_buffer.TryEnqueue(buffer.AsSpan(0, length), out var droppedBytes))
            {
                ready.ReportDropped(1, length);
            }
            else if (droppedBytes > 0)
            {
                ready.ReportDropped(1, droppedBytes);
            }

            if (result.ReceivedBytes > ExternalAccessDeclaration.DefaultUnitBytes)
            {
                // 数据报超过单元容量：交付截断后的内容，超出部分按 1 个单元记账。
                ready.ReportDropped(1, result.ReceivedBytes - ExternalAccessDeclaration.DefaultUnitBytes);
            }

            ready.NotifyReady();
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
