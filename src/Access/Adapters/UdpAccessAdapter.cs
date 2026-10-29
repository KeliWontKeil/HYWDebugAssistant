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
public sealed class UdpAccessAdapter : IExternalAccessAdapter
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    /// <summary>UDP 报文的理论上限（接收缓冲必须能容纳整个报文，否则平台会抛异常而不是截断）。</summary>
    private const int UdpMaxDatagram = 65507;

    private readonly AdapterBuffer _buffer = new();
    private readonly Lock _gate = new();

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private IExternalAccessReady? _ready;
    private IPEndPoint? _target;
    private volatile bool _open;

    /// <inheritdoc />
    public Type ParametersType => typeof(UdpConfig);

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (parameters is not UdpConfig config)
        {
            ready.SetFaulted(AccessText.Get("access.udp.parametersTypeInvalid", nameof(UdpConfig)));
            return;
        }

        if (config.LocalPort is < 0 or > 65535)
        {
            ready.SetFaulted(AccessText.Get("access.common.localPortOutOfRange", config.LocalPort));
            return;
        }

        if (!TryResolveAddress(config.LocalAddress, out var local))
        {
            ready.SetFaulted(AccessText.Get("access.common.localAddressInvalid", config.LocalAddress));
            return;
        }

        IPEndPoint? target = null;
        if (!string.IsNullOrWhiteSpace(config.RemoteAddress) && config.RemotePort > 0)
        {
            if (!TryResolveAddress(config.RemoteAddress, out var remote))
            {
                ready.SetFaulted(AccessText.Get("access.common.remoteAddressInvalid", config.RemoteAddress));
                return;
            }

            if (remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any))
            {
                ready.SetFaulted(
                    AccessText.Get("access.common.wildcardAddressNotAllowed"));
                return;
            }

            target = new IPEndPoint(remote, config.RemotePort);
        }

        Close(CloseTimeout);
        _buffer.Clear();

        var socket = new Socket(local.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
        {
            EnableBroadcast = config.Broadcast,
        };

        try
        {
            socket.Bind(new IPEndPoint(local, config.LocalPort));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            socket.Dispose();
            ready.SetFaulted(AccessText.Get("access.common.portInUse", config.LocalPort));
            return;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            ready.SetFaulted(AccessText.Get("access.udp.bindFailed", ex.GetType().Name, ex.Message));
            return;
        }

        var cts = new CancellationTokenSource();

        lock (_gate)
        {
            _socket = socket;
            _target = target;
            _ready = ready;
            _cts = cts;
            _open = true;
        }

        var actual = (IPEndPoint)socket.LocalEndPoint!;
        ready.SetOpen(target is null
            ? $"{actual.Address}:{actual.Port}"
            : $"{actual.Address}:{actual.Port} → {target}");

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
            ready?.SetFaulted(AccessText.Get("access.udp.sendFailed", ex.GetType().Name, ex.Message));
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
                    ready.SetFaulted(AccessText.Get("access.udp.receiveFailed", ex.GetType().Name, ex.Message));
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
