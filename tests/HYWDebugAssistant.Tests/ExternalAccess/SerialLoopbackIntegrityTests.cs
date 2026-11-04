using System.Diagnostics;
using System.Text;
using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// **RXD/TXD 短接的回环完整性**：tx 写出的每一个字节都必须被 rx 完整收回，
/// 或**显式**计入接收丢弃——绝不允许静默丢失。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>用**真实** <see cref="SerialAccessAdapter"/>（含预切块/暂存/分片交付）驱动一个回环传输：
///     传输按 115200 模拟发送耗时，并把写出的字节**两两合并**回投（模拟驱动把多次写并成一块）；</item>
///   <item>真实模块包：文本发送器 + 串口 + 数据监视器；判据只看会话/队列公开快照；</item>
///   <item>核心守恒式：<c>SentBytes == ReceivedBytes + ReceiveDroppedBytes</c>。旧实现会在
///     “长文本低频”直接违反它（大块被整块丢弃且不计数）。</item>
/// </list>
/// </remarks>
[CollectionDefinition("SerialLoopback", DisableParallelization = true)]
public sealed class SerialLoopbackTestGroup
{
}

[Collection("SerialLoopback")]
public sealed class SerialLoopbackIntegrityTests
{
    private const string TextSenderId = "hyw/text-sender";
    private const string SerialId = "hyw/serial";
    private const string MonitorId = "hyw/data-monitor";
    private const string AccessName = "serial";
    private const int Baud = 115200;

    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    private static string StagedModules => Path.Combine(FindRepoRoot(), "artifacts", "modules", Configuration);

    private readonly ITestOutputHelper _output;

    public SerialLoopbackIntegrityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 回环_短文本低频_零丢失()
        => RunCase(bytes: 60, intervalMs: 1000, sends: 3, runMs: 0, expectLoss: false);

    [Fact]
    public void 回环_长文本低频_零丢失()
        => RunCase(bytes: 4096, intervalMs: 1000, sends: 2, runMs: 0, expectLoss: false);


    [Fact]
    public void 回环_短文本高频_零丢失()
        => RunCase(bytes: 60, intervalMs: 10, sends: 20, runMs: 0, expectLoss: false);

    [Fact]
    public void 回环_长文本高频_允许显式丢弃但不得静默丢失()
        => RunCase(bytes: 4096, intervalMs: 10, sends: 0, runMs: 1200, expectLoss: true);

    [Fact]
    public void 回环_超长文本高频_允许显式丢弃但不得静默丢失()
        => RunCase(bytes: 16384, intervalMs: 100, sends: 0, runMs: 1000, expectLoss: true);

    /// <summary>
    /// **真机回环**（RXD/TXD 短接）：只有设置了 `HYW_TEST_SERIAL`（如 `COM9`）才执行；
    /// 未设置时静默跳过（不占 CI 的串口）。低频三项必须逐字节零丢失。
    /// </summary>
    [Fact]
    public void 真机回环_低频三项_零丢失()
    {
        var port = Environment.GetEnvironmentVariable("HYW_TEST_SERIAL");

        if (string.IsNullOrWhiteSpace(port))
        {
            return;
        }

        RunCase(bytes: 60, intervalMs: 1000, sends: 3, runMs: 0, expectLoss: false, realPort: port);
        RunCase(bytes: 4096, intervalMs: 1000, sends: 2, runMs: 0, expectLoss: false, realPort: port);
        RunCase(bytes: 16384, intervalMs: 2000, sends: 2, runMs: 0, expectLoss: false, realPort: port);
    }

    /// <summary>
    /// **全双工判据**：回环过载（4 KB / 5 ms，远超 115200 线容量）时，实例线程不得被阻塞写占死——
    /// 清计数命令必须在 50 ms 内生效，回环接收必须仍在推进。
    /// </summary>
    /// <remarks>
    /// 旧实现里 `ExternalAccessSession.Write` 直接在实例线程上做阻塞写（1472 B @115200 ≈ 128 ms），
    /// 本用例的 50 ms 判据因此必然失败；写泵落地后实例线程只做入队，命令与接收回到毫秒级。
    /// </remarks>
    [Fact]
    public void 回环_过载下_命令与接收不应被写饿死()
    {
        Directory.Exists(StagedModules).ShouldBeTrue($"模块包未归拢：{StagedModules}");

        var transport = new LoopbackTransport(baud: Baud, coalesceWrites: 2);
        var adapter = new SerialAccessAdapter(() => transport);
        var catalog = new SingleStandardCatalog(
            new ExternalAccessDescriptor(ExternalAccessStandardIds.Serial, typeof(SerialConfig), () => adapter));

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(
            carrierOptions: new CarrierOptions
            {
                QueueCapacity = 64,
                StopTimeout = TimeSpan.FromSeconds(2),
                WatchdogInterval = TimeSpan.FromMilliseconds(20),
            },
            viewState: viewState,
            catalog: catalog);

        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([StagedModules]).ShouldBeGreaterThan(0);
        services.AddModule("t1", TextSenderId).Succeeded.ShouldBeTrue();
        var serialAdd = services.AddModule("s1", SerialId);
        serialAdd.Succeeded.ShouldBeTrue(string.Join("；", serialAdd.Failures));
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();

        TestLink.Link(services, new PortRef("t1", "out"), new PortRef("s1", "tx"), "duplex-tx");
        TestLink.Link(services, new PortRef("s1", "rx"), new PortRef("m1", "in"), "duplex-rx");

        services.UpdateConfigurationField("s1", nameof(SerialConfig.PortName), "LOOP")
            .Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField("s1", nameof(SerialConfig.BaudRate), "Baud115200")
            .Succeeded.ShouldBeTrue();

        runtime.Content.TryGetInstance("s1", out var serialInstance).ShouldBeTrue();
        var snapshot = () => serialInstance!.Context.Access.GetSnapshot(AccessName);

        services.SendCommand("s1", "open").Succeeded.ShouldBeTrue();
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "串口会话应打开");

        viewState.SetSelectedPage("t1", "text-pages", 0);
        viewState.SetInputText("t1", "text1", new string('A', 4096));
        viewState.SetInputText("t1", "interval", "5");
        viewState.SetInputText("t1", "continuous", "true");

        Thread.Sleep(800);

        var sentBefore = snapshot().SentBytes;
        var receivedBefore = snapshot().ReceivedBytes;

        var clock = Stopwatch.StartNew();
        services.SendCommand("s1", "clearCounters").Succeeded.ShouldBeTrue();
        WaitFor(
            () => snapshot().SentBytes < sentBefore,
            "过载下清计数命令应生效",
            timeoutMs: 5000);
        clock.Stop();

        var receivedAfterCommand = snapshot().ReceivedBytes;

        _output.WriteLine(
            $"过载清计数：{clock.Elapsed.TotalMilliseconds:F0} ms；" +
            $"SentBefore={sentBefore} ReceivedBefore={receivedBefore} ReceivedAfter={receivedAfterCommand}");

        clock.Elapsed.ShouldBeLessThanOrEqualTo(
            TimeSpan.FromMilliseconds(50),
            $"回环过载下命令不得被阻塞写饿死（实测 {clock.Elapsed.TotalMilliseconds:F0} ms）");

        var rxBeforeWindow = snapshot().ReceivedBytes;
        Thread.Sleep(500);
        var rxAfterWindow = snapshot().ReceivedBytes;

        rxAfterWindow.ShouldBeGreaterThan(
            rxBeforeWindow,
            "回环接收必须持续推进（不能只在写边界成串交付）");

        viewState.SetInputText("t1", "continuous", "false");
        transport.FlushEcho();
    }

    private void RunCase(int bytes, int intervalMs, int sends, int runMs, bool expectLoss, string? realPort = null)
    {
        Directory.Exists(StagedModules).ShouldBeTrue($"模块包未归拢：{StagedModules}");

        LoopbackTransport? transport = null;
        IExternalAccessCatalog catalog;

        if (realPort is null)
        {
            transport = new LoopbackTransport(baud: Baud, coalesceWrites: 2);
            var adapter = new SerialAccessAdapter(() => transport);
            catalog = new SingleStandardCatalog(
                new ExternalAccessDescriptor(ExternalAccessStandardIds.Serial, typeof(SerialConfig), () => adapter));
        }
        else
        {
            catalog = new BuiltInExternalAccessCatalog();
        }

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(
            carrierOptions: new CarrierOptions
            {
                // 16 单元：低频用例（单次 ≤11 单元）不丢；高频用例几毫秒就顶满。
                QueueCapacity = 16,
                SendBacklogBytes = 24 * 1024,
                StopTimeout = TimeSpan.FromSeconds(2),
                WatchdogInterval = TimeSpan.FromMilliseconds(20),
            },
            viewState: viewState,
            catalog: catalog);

        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([StagedModules]).ShouldBeGreaterThan(0);
        services.AddModule("t1", TextSenderId).Succeeded.ShouldBeTrue();
        var serialAdd = services.AddModule("s1", SerialId);
        serialAdd.Succeeded.ShouldBeTrue(string.Join("；", serialAdd.Failures));
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();

        TestLink.Link(services, new PortRef("t1", "out"), new PortRef("s1", "tx"), "loop-tx");
        TestLink.Link(services, new PortRef("s1", "rx"), new PortRef("m1", "in"), "loop-rx");

        services.UpdateConfigurationField("s1", nameof(SerialConfig.PortName), realPort ?? "LOOP")
            .Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField("s1", nameof(SerialConfig.BaudRate), "Baud115200")
            .Succeeded.ShouldBeTrue();

        runtime.Content.TryGetInstance("s1", out var serialInstance).ShouldBeTrue();
        var snapshot = () => serialInstance!.Context.Access.GetSnapshot(AccessName);

        services.SendCommand("s1", "open").Succeeded.ShouldBeTrue();
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "串口会话应打开");

        var text = new string('A', bytes);

        viewState.SetSelectedPage("t1", "text-pages", 0);
        viewState.SetInputText("t1", "text1", text);
        viewState.SetInputText("t1", "interval", intervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        viewState.SetInputText("t1", "continuous", "true");

        if (sends > 0)
        {
            var target = (long)bytes * sends;
            WaitFor(() => snapshot().SentBytes >= target, $"应写出至少 {target} B", timeoutMs: 60_000);
        }
        else
        {
            Thread.Sleep(runMs);
        }

        // 停发；等发送队列排空、写出的字节全部回显。
        viewState.SetInputText("t1", "continuous", "false");

        if (expectLoss)
        {
            WaitFor(
                () => services.GetQueueDepths()
                    .Single(q => q.InstanceId == "s1" && q.PortName == "tx").Dropped > 0
                || snapshot().SendBacklogEvictedUnits > 0,
                "高频过载应出现显式（队列满）丢弃（不是静默）",
                timeoutMs: 10_000);
        }

        WaitFor(
            () => services.GetQueueDepths()
                .Single(q => q.InstanceId == "s1" && q.PortName == "tx").Depth == 0,
            "发送队列应排空（停发后不再有新单元进入）",
            timeoutMs: 20_000);

        // 队列空只说明"没有待写单元"；最后一条可能还在写（≤128 ms）。等写计数静下来再回显尾巴。
        WaitForStable(() => snapshot().SentBytes, quietMs: 300, timeoutMs: 5000);
        transport?.FlushEcho();
        WaitFor(
            () => snapshot().ReceivedBytes + snapshot().ReceiveDroppedBytes >= snapshot().SentBytes,
            "写出的字节应全部回显或被显式计入接收丢弃",
            timeoutMs: 20_000);

        var final = snapshot();
        var txQueue = services.GetQueueDepths().Single(q => q.InstanceId == "s1" && q.PortName == "tx");
        var monitorQueue = services.GetQueueDepths().Single(q => q.InstanceId == "m1" && q.PortName == "in");

        _output.WriteLine(
            $"bytes={bytes} interval={intervalMs}ms sends={sends} run={runMs}ms ⇒ " +
            $"Sent={final.SentBytes} Received={final.ReceivedBytes} " +
            $"ReceiveDropped={final.ReceiveDroppedUnits} txDropped={txQueue.Dropped} " +
            $"monitorDropped={monitorQueue.Dropped} backlogEvicted={final.SendBacklogEvictedUnits} written={(transport is null ? "n/a" : transport.WrittenBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))}");

        // **核心守恒式**：写出的字节 = 收回 + 显式接收丢弃。违反它就是静默丢失。
        (final.ReceivedBytes + final.ReceiveDroppedBytes).ShouldBe(
            final.SentBytes,
            "回环字节守恒：写出的每个字节要么被收回，要么被显式计入接收丢弃");

        if (transport is not null)
        {
            transport.WrittenBytes.ShouldBe(final.SentBytes, "适配器实际写出的字节应与会话计数一致");
        }
        final.ReceiveDroppedUnits.ShouldBe(0, "接收侧不得丢弃（回环下没有线损）");

        if (!expectLoss)
        {
            final.SentBytes.ShouldBeGreaterThanOrEqualTo((long)bytes * sends);
            final.ReceivedBytes.ShouldBe(final.SentBytes, "低频用例必须逐字节零丢失");
            txQueue.Dropped.ShouldBe(0, "低频用例发送队列不得丢弃");
            monitorQueue.Dropped.ShouldBe(0, "低频用例监视器队列不得丢弃");
        }
        else
        {
            (txQueue.Dropped + final.SendBacklogEvictedUnits).ShouldBeGreaterThan(0, "过载应由发送队列或发送积压显式丢弃");
        }
    }

    // ------------------------------------------------------------------ 回环传输

    /// <summary>回环串口传输：按 115200 模拟发送耗时，并把写出的字节几笔合并后回投。</summary>
    private sealed class LoopbackTransport : ISerialTransport
    {
        private readonly double _bytesPerSecond;
        private readonly int _coalesceWrites;
        private readonly Lock _gate = new();
        private readonly List<byte> _pending = [];
        private int _pendingWrites;
        private long _writtenBytes;
        private long _writeCount;

        public LoopbackTransport(int baud, int coalesceWrites)
        {
            _bytesPerSecond = baud / 10.0;
            _coalesceWrites = Math.Max(1, coalesceWrites);
        }

        public event Action<byte[]>? DataReceived;

        public event Action<Exception>? ReadFaulted;

        public long WrittenBytes => Interlocked.Read(ref _writtenBytes);

        public long WriteCount => Interlocked.Read(ref _writeCount);

        public void Open(SerialConfig config) => _ = config;

        public bool IsPresent() => true;

        public void SetWriteTimeout(TimeSpan timeout) => _ = timeout;

        public void Write(ReadOnlySpan<byte> payload)
        {
            // 线路上发完才回显（与真实 USB-TTL 的阻塞写行为一致）。
            WaitForLine(payload.Length / _bytesPerSecond);

            _ = Interlocked.Add(ref _writtenBytes, payload.Length);
            _ = Interlocked.Increment(ref _writeCount);

            byte[]? echo = null;

            lock (_gate)
            {
                _pending.AddRange(payload.ToArray());
                _pendingWrites++;

                if (_pendingWrites >= _coalesceWrites)
                {
                    echo = [.. _pending];
                    _pending.Clear();
                    _pendingWrites = 0;
                }
            }

            if (echo is not null)
            {
                DataReceived?.Invoke(echo);
            }
        }

        /// <summary>把还没回显的尾巴投回去（测试在停发后调用）。</summary>
        public void FlushEcho()
        {
            byte[]? echo = null;

            lock (_gate)
            {
                if (_pending.Count > 0)
                {
                    echo = [.. _pending];
                    _pending.Clear();
                    _pendingWrites = 0;
                }
            }

            if (echo is not null)
            {
                DataReceived?.Invoke(echo);
            }
        }

        /// <summary>模拟读取故障（回环用例不用；保留给接口与将来用例）。</summary>
        public void Fail(Exception exception) => ReadFaulted?.Invoke(exception);

        public void Close()
        {
            lock (_gate)
            {
                _pending.Clear();
                _pendingWrites = 0;
            }
        }

        public void Dispose() => Close();

        private static void WaitForLine(double seconds)
        {
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var remaining = seconds - clock.Elapsed.TotalSeconds;

                if (remaining > 0.02)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(200);
                }
            }
        }
    }

    private sealed class SingleStandardCatalog(ExternalAccessDescriptor descriptor) : IExternalAccessCatalog
    {
        public IReadOnlyList<ExternalAccessDescriptor> Standards { get; } = [descriptor];

        public bool TryResolve(string standardId, out ExternalAccessDescriptor resolved)
        {
            resolved = descriptor;
            return string.Equals(descriptor.StandardId, standardId, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ 辅助

    private static void WaitForStable(Func<long> value, int quietMs, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        var last = value();
        var lastChange = clock.Elapsed;

        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(20);
            var current = value();

            if (current != last)
            {
                last = current;
                lastChange = clock.Elapsed;
                continue;
            }

            if ((clock.Elapsed - lastChange).TotalMilliseconds >= quietMs)
            {
                return;
            }
        }

        throw new TimeoutException($"计数在 {timeoutMs} ms 内未静下来（最后值 {last}）");
    }

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"{message}（等 {timeoutMs} ms 未满足）");
            }

            Thread.Sleep(5);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根");
    }
}
