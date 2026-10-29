using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 适配件的逻辑用例：串口用假 transport，网络协议用本机回环。
/// </summary>
/// <remarks>
/// 判据只看"开/关/读/写/失败"，不碰真实外部设备；真实串口由人工闸门覆盖。
/// </remarks>
public sealed class ExternalAccessAdapterTests
{
    [Fact]
    public void 串口适配件_打开后应能收数据并写出_关闭应释放()
    {
        var transport = new FakeSerialTransport();
        var ready = new ReadySink();
        var adapter = new SerialAccessAdapter(() => transport);

        adapter.Open(new SerialConfig { PortName = "COM_TEST" }, ready);

        ready.State.ShouldBe(ExternalAccessState.Open);
        transport.OpenCalled.ShouldBeTrue();

        transport.Feed([1, 2, 3]);

        var buffer = new byte[RawBytesFraming.Capacity];
        WaitFor(() => adapter.TryRead(buffer, out _), "串口读泵应把数据交给适配件");
        buffer[..3].ShouldBe(new byte[] { 1, 2, 3 });

        adapter.Write([4, 5]).ShouldBeTrue();
        transport.Written.ShouldHaveSingleItem().ShouldBe(new byte[] { 4, 5 });

        adapter.Close(TimeSpan.FromSeconds(1));
        transport.Closed.ShouldBeTrue();
    }

    [Fact]
    public void 串口适配件_打开失败应报告Faulted()
    {
        var transport = new FakeSerialTransport { ThrowOnOpen = true };
        var ready = new ReadySink();
        var adapter = new SerialAccessAdapter(() => transport);

        adapter.Open(new SerialConfig { PortName = "COM_TEST" }, ready);

        ready.State.ShouldBe(ExternalAccessState.Faulted);
        ready.LastError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void 串口适配件_读取故障应报告Faulted()
    {
        var transport = new FakeSerialTransport();
        var ready = new ReadySink();
        var adapter = new SerialAccessAdapter(() => transport);

        adapter.Open(new SerialConfig { PortName = "COM_TEST" }, ready);
        transport.Fail(new IOException("假串口：读取失败"));

        ready.State.ShouldBe(ExternalAccessState.Faulted);
        ready.LastError.ShouldNotBeNull().ShouldContain("串口读取失败");
    }

    [Fact]
    public void 串口硬件回声不得触发适配器二次写()
    {
        var transport = new FakeSerialTransport { EchoWrites = true };
        var ready = new ReadySink();
        var adapter = new SerialAccessAdapter(() => transport);

        adapter.Open(new SerialConfig { PortName = "COM_TEST" }, ready);
        adapter.Write([1, 2, 3]).ShouldBeTrue();

        var buffer = new byte[RawBytesFraming.Capacity];
        WaitFor(() => adapter.TryRead(buffer, out _), "硬件回声应作为接收数据到达");
        buffer[..3].ShouldBe([1, 2, 3]);

        // 适配器只写了一次；回声不会触发第二次 Write。
        transport.Written.Count.ShouldBe(1);
        adapter.Close(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void 默认地址与端口应使用本机回环和60721()
    {
        var tcpClient = new TcpClientConfig();
        var tcpServer = new TcpServerConfig();
        var udp = new UdpConfig();

        tcpClient.RemoteHost.ShouldBe("127.0.0.1");
        tcpClient.RemotePort.ShouldBe(60721);
        tcpServer.LocalAddress.ShouldBe("127.0.0.1");
        tcpServer.LocalPort.ShouldBe(60721);
        udp.LocalAddress.ShouldBe("127.0.0.1");
        udp.LocalPort.ShouldBe(60721);
        udp.RemoteAddress.ShouldBe("127.0.0.1");
        udp.RemotePort.ShouldBe(60721);
    }

    [Fact]
    public void TCP服务端端口占用应给出明确原因()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();

        try
        {
            var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            var ready = new ReadySink();
            var adapter = new TcpServerAccessAdapter();

            adapter.Open(
                new TcpServerConfig { LocalAddress = "127.0.0.1", LocalPort = port },
                ready);

            ready.State.ShouldBe(ExternalAccessState.Faulted);
            ready.LastError.ShouldNotBeNull().ShouldContain("已被占用");
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public void UDP端口占用应给出明确原因()
    {
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var port = ((IPEndPoint)occupied.LocalEndPoint!).Port;
        var ready = new ReadySink();
        var adapter = new UdpAccessAdapter();

        adapter.Open(
            new UdpConfig { LocalAddress = "127.0.0.1", LocalPort = port },
            ready);

        ready.State.ShouldBe(ExternalAccessState.Faulted);
        ready.LastError.ShouldNotBeNull().ShouldContain("已被占用");
    }

    [Fact]
    public void TCP服务端与UDP的本地地址选项应包含回环()
    {
        var tcp = new TcpServerConfig().GetChoices(nameof(TcpServerConfig.LocalAddress));
        var udp = new UdpConfig().GetChoices(nameof(UdpConfig.LocalAddress));

        tcp.ShouldNotBeNull().ShouldContain("127.0.0.1");
        udp.ShouldNotBeNull().ShouldContain("127.0.0.1");
    }

    [Fact]
    public void UDP本地地址0点0点0点0应能绑定()
    {
        var ready = new ReadySink();
        var adapter = new UdpAccessAdapter();

        adapter.Open(new UdpConfig { LocalAddress = "0.0.0.0", LocalPort = 0 }, ready);

        ready.State.ShouldBe(ExternalAccessState.Open);
        adapter.Close(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void UDP目标0点0点0点0应明确拒绝()
    {
        var ready = new ReadySink();
        var adapter = new UdpAccessAdapter();

        adapter.Open(
            new UdpConfig
            {
                LocalAddress = "0.0.0.0",
                LocalPort = 0,
                RemoteAddress = "0.0.0.0",
                RemotePort = 12345,
            },
            ready);

        ready.State.ShouldBe(ExternalAccessState.Faulted);
        ready.LastError.ShouldNotBeNull().ShouldContain("目标地址不能是 0.0.0.0");
    }

    [Fact]
    public void TCP客户端目标0点0点0点0应明确拒绝()
    {
        var ready = new ReadySink();
        var adapter = new TcpClientAccessAdapter();

        adapter.Open(
            new TcpClientConfig { RemoteHost = "0.0.0.0", RemotePort = 12345 },
            ready);

        ready.State.ShouldBe(ExternalAccessState.Faulted);
        ready.LastError.ShouldNotBeNull().ShouldContain("目标地址不能是 0.0.0.0");
    }

    [Fact]
    public async Task TCP客户端_应连接_收发_并在对端关闭后报告Faulted()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync();

            var ready = new ReadySink();
            var adapter = new TcpClientAccessAdapter();
            adapter.Open(
                new TcpClientConfig
                {
                    RemoteHost = "127.0.0.1",
                    RemotePort = port,
                    ConnectTimeout = 2,
                    NoDelay = true,
                },
                ready);

            using var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            WaitFor(() => ready.State == ExternalAccessState.Open, "客户端应连上");

            await server.GetStream().WriteAsync(new byte[] { 1, 2, 3 });
            var buffer = new byte[RawBytesFraming.Capacity];
            WaitFor(() => adapter.TryRead(buffer, out _), "客户端应收到数据");
            buffer[..3].ShouldBe(new byte[] { 1, 2, 3 });

            adapter.Write([4, 5]).ShouldBeTrue();
            var received = new byte[2];
            await server.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            received.ShouldBe(new byte[] { 4, 5 });

            server.Close();
            WaitFor(() => ready.State == ExternalAccessState.Faulted, "对端关闭应报告 Faulted");
            ready.LastError.ShouldNotBeNullOrWhiteSpace();

            adapter.Close(TimeSpan.FromSeconds(1));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TCP服务端_应接受多连接_广播写_并在断开后更新连接数()
    {
        var ready = new ReadySink();
        var adapter = new TcpServerAccessAdapter();
        adapter.Open(
            new TcpServerConfig
            {
                LocalAddress = "127.0.0.1",
                LocalPort = 0,
                MaxConnections = 4,
                NoDelay = true,
            },
            ready);

        try
        {
            WaitFor(() => ready.State == ExternalAccessState.Open, "服务端应开始监听");
            var port = ParsePort(ready.Detail);

            using var first = new System.Net.Sockets.TcpClient();
            using var second = new System.Net.Sockets.TcpClient();
            await first.ConnectAsync(IPAddress.Loopback, port);
            await second.ConnectAsync(IPAddress.Loopback, port);

            WaitFor(() => ready.Connections == 2, "应接受两条连接");

            await first.GetStream().WriteAsync(new byte[] { 7, 8, 9 });
            var buffer = new byte[RawBytesFraming.Capacity];
            WaitFor(() => adapter.TryRead(buffer, out _), "服务端应收到客户端数据");
            buffer[..3].ShouldBe(new byte[] { 7, 8, 9 });

            adapter.Write([10, 11]).ShouldBeTrue();

            var firstReceived = new byte[2];
            var secondReceived = new byte[2];
            await first.GetStream().ReadExactlyAsync(firstReceived).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await second.GetStream().ReadExactlyAsync(secondReceived).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            firstReceived.ShouldBe(new byte[] { 10, 11 });
            secondReceived.ShouldBe(new byte[] { 10, 11 });

            first.Close();
            WaitFor(() => ready.Connections == 1, "客户端断开后连接数应下降");
        }
        finally
        {
            adapter.Close(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task UDP_应收发数据报_并截断超长数据报()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerPort = ((IPEndPoint)peer.Client.LocalEndPoint!).Port;

        var ready = new ReadySink();
        var adapter = new UdpAccessAdapter();
        adapter.Open(
            new UdpConfig
            {
                LocalAddress = "127.0.0.1",
                LocalPort = 0,
                RemoteAddress = "127.0.0.1",
                RemotePort = peerPort,
            },
            ready);

        try
        {
            WaitFor(() => ready.State == ExternalAccessState.Open, "UDP 应绑定");
            var localPort = ParsePort(ready.Detail);

            await peer.SendAsync(new byte[] { 1, 2, 3 }, new IPEndPoint(IPAddress.Loopback, localPort));

            var buffer = new byte[RawBytesFraming.Capacity];
            WaitFor(() => adapter.TryRead(buffer, out _), "UDP 应收到数据报");
            buffer[..3].ShouldBe(new byte[] { 1, 2, 3 });

            adapter.Write([4, 5]).ShouldBeTrue();
            using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await peer.ReceiveAsync(receiveTimeout.Token);
            received.Buffer.ShouldBe(new byte[] { 4, 5 });

            await peer.SendAsync(
                new byte[RawBytesFraming.Capacity + 8],
                new IPEndPoint(IPAddress.Loopback, localPort));

            WaitFor(
                () => ready.DroppedUnits >= 1,
                $"超长数据报应被检测到（state={ready.State}, error={ready.LastError}）");

            WaitFor(() => adapter.TryRead(buffer, out var length) && length == RawBytesFraming.Capacity, "超长数据报应被截断");
        }
        finally
        {
            adapter.Close(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void 串口模块_命令应发请求_外部数据应组帧进rx_口数据应写回外部()
    {
        var context = new TestModuleContext();
        var module = new SerialModule();
        module.OnRegistered(context);
        module.OnActivated();

        context.DeclaredExternalAccesses.ShouldHaveSingleItem().Name.ShouldBe(SerialModule.AccessName);
        context.DeclaredPorts.Count.ShouldBe(2);

        module.OnCommand(SerialModule.OpenCommand);
        context.Access.OpenRequests.ShouldContain(SerialModule.AccessName);

        // 外部 → rx：组 {len,data} 并发出定长载荷。
        context.Access.Deliver(SerialModule.AccessName, [1, 2, 3]);
        var sent = context.Sent.ShouldHaveSingleItem();
        sent.PortName.ShouldBe(SerialModule.RxPort);
        sent.Payload.Length.ShouldBe(RawBytesFraming.FrameSize(RawBytesFraming.Capacity));
        BinaryPrimitives.ReadUInt16LittleEndian(sent.Payload).ShouldBe((ushort)3);
        sent.Payload[2..5].ShouldBe(new byte[] { 1, 2, 3 });

        // tx → 外部：按 len 取有效字节，不把补零写出去。
        var frame = new byte[RawBytesFraming.FrameSize(RawBytesFraming.Capacity)];
        RawBytesFraming.WriteFrame([6, 7], frame, RawBytesFraming.Capacity);
        context.Deliver(SerialModule.TxPort, frame);

        var written = context.Access.Written.ShouldHaveSingleItem();
        written.AccessName.ShouldBe(SerialModule.AccessName);
        written.Payload.ShouldBe(new byte[] { 6, 7 });

        // 状态/计数发布。
        context.Access.SetSnapshot(SerialModule.AccessName, new ExternalAccessSnapshot(
            ExternalAccessState.Open,
            Suspended: false,
            Detail: "COM_TEST",
            LastError: string.Empty,
            ReceivedUnits: 1,
            ReceivedBytes: 3,
            SentUnits: 1,
            SentBytes: 2,
            DroppedUnits: 0,
            DroppedBytes: 0,
            ActiveConnections: 0,
            LastReceivedAt: DateTimeOffset.Now));

        module.OnWork();

        context.SetTexts.Any(pair => pair.SourceName == SerialModule.StateSource && pair.Text.Contains("COM_TEST"))
            .ShouldBeTrue("状态文字应发布到 state 源");
        context.AppearanceReports.Any(report =>
                report.SourceName == SerialModule.LinkSource && report.OutlineRole == Sdk.Views.ViewInkRoles.Success)
            .ShouldBeTrue("打开状态应把状态灯设成 success 角色");
        context.Published.ContainsKey(SerialModule.RxCountSource).ShouldBeTrue();
        BinaryPrimitives.ReadUInt64LittleEndian(context.Published[SerialModule.RxCountSource][^1]).ShouldBe(3UL);
    }

    private static int ParsePort(string detail)
    {
        // UDP 的 detail 可能是 "地址:本地端口 → 目标:目标端口"：只取第一个地址段的端口。
        var local = detail.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var index = local.LastIndexOf(':');
        index.ShouldBeGreaterThanOrEqualTo(0, $"状态说明里没有端口：{detail}");
        return int.Parse(local[(index + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException(message);
            }

            Thread.Sleep(10);
        }
    }

    private sealed class ReadySink : IExternalAccessReady
    {
        private readonly Lock _gate = new();
        private ExternalAccessState _state = ExternalAccessState.Closed;
        private string _detail = string.Empty;
        private string _lastError = string.Empty;
        private int _connections;
        private long _droppedUnits;
        private long _droppedBytes;

        public ExternalAccessState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public string Detail
        {
            get
            {
                lock (_gate)
                {
                    return _detail;
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_gate)
                {
                    return _lastError;
                }
            }
        }

        public int Connections
        {
            get
            {
                lock (_gate)
                {
                    return _connections;
                }
            }
        }

        public long DroppedUnits
        {
            get
            {
                lock (_gate)
                {
                    return _droppedUnits;
                }
            }
        }

        public long DroppedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _droppedBytes;
                }
            }
        }

        public void NotifyReady()
        {
        }

        public void SetConnecting(string? detail = null)
        {
            lock (_gate)
            {
                _state = ExternalAccessState.Opening;
                _detail = detail ?? string.Empty;
            }
        }

        public void SetOpen(string? detail = null)
        {
            lock (_gate)
            {
                _state = ExternalAccessState.Open;
                _detail = detail ?? string.Empty;
            }
        }

        public void SetFaulted(string message)
        {
            lock (_gate)
            {
                _state = ExternalAccessState.Faulted;
                _lastError = message;
            }
        }

        public void SetConnections(int count)
        {
            lock (_gate)
            {
                _connections = Math.Max(0, count);
            }
        }

        public void ReportDropped(long units, long bytes)
        {
            lock (_gate)
            {
                _droppedUnits += units;
                _droppedBytes += bytes;
            }
        }
    }

    private sealed class FakeSerialTransport : ISerialTransport
    {
        public event Action<byte[]>? DataReceived;

        public event Action<Exception>? ReadFaulted;

        public bool OpenCalled { get; private set; }

        public bool Closed { get; private set; }

        public bool ThrowOnOpen { get; init; }

        /// <summary>模拟 RXD/TXD 短接：写入后把同一段字节回投到接收事件。</summary>
        public bool EchoWrites { get; init; }

        public List<byte[]> Written { get; } = [];

        public void Open(SerialConfig config)
        {
            if (ThrowOnOpen)
            {
                throw new IOException("假串口：打开失败");
            }

            _ = config;
            OpenCalled = true;
        }

        public void Write(ReadOnlySpan<byte> payload)
        {
            var copy = payload.ToArray();
            Written.Add(copy);

            if (EchoWrites)
            {
                DataReceived?.Invoke(copy);
            }
        }

        public void Feed(ReadOnlySpan<byte> payload) =>
            DataReceived?.Invoke(payload.ToArray());

        public void Fail(Exception exception) => ReadFaulted?.Invoke(exception);

        public void Close() => Closed = true;

        public void Dispose()
        {
        }
    }
}
