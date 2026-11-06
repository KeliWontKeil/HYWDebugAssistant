using System.Diagnostics;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// **过载下的响应性回归**：真实模块包（文本发送 + 串口）+ 真实运行时与承载循环，
/// 只把"串口"换成一个**按波特率阻塞**的假适配件（一次写阻塞到这批字节在线路上发完）。
/// </summary>
/// <remarks>
/// <para>
/// 假适配件复刻真实 USB-TTL 的硬上限：115200 波特率下约 11520 B/s，一条 91 B 的行要 ~7.9 ms。
/// 于是"模块把积压写完"这件事本身就要几百毫秒到几秒——投递循环若不按时间片回泵，
/// 命令 / 输入 / 会话泵 / 状态发布就会整段被饿死（界面上表现为"点了没反应"）。
/// </para>
/// <para>
/// 断言全部是**可观测事实**：命令生效时刻、显示源写入次数、移除耗时、适配器安静时刻、丢弃计数。
/// </para>
/// </remarks>
[Collection("SerialLoopback")]
[Trait("Category", "Pressure")]   // 压力测试：默认不进门禁；改数据面 / 外部接入 / 串口时用 dev.ps1 -Mode Test -Pressure
public sealed class SerialDeliveryResponsivenessTests
{
    private const string TextSenderId = "hyw/text-sender";
    private const string SerialId = "hyw/serial";
    private const string AccessName = "serial";
    private const int Baud = 115200;
    private const double LineCapacityBytesPerSecond = Baud / 10.0;

    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    private static string StagedModules => Path.Combine(FindRepoRoot(), "artifacts", "modules", Configuration);

    private readonly ITestOutputHelper _output;

    public SerialDeliveryResponsivenessTests(ITestOutputHelper output) => _output = output;

    /// <summary>过载期间：清计数命令与状态发布都不能被投递循环饿死。</summary>
    [Fact]
    public void 过载下_清计数命令与状态发布不应被饿死()
    {
        var harness = CreateHarness(lines: 3);
        try
        {
            Start(harness);
            Thread.Sleep(1500);

            // ① 命令：清计数应在 200 ms 内生效
            var before = harness.Snapshot().SentUnits;
            var clock = Stopwatch.StartNew();
            harness.Services.SendCommand("s1", "clearCounters").Succeeded.ShouldBeTrue();
            var applied = WaitForOrNull(
                () => harness.Snapshot().SentUnits < Math.Min(before, 5),
                TimeSpan.FromSeconds(2));
            clock.Stop();

            _output.WriteLine(
                $"过载下清计数：{(applied is null ? "2 s 内未生效" : Format(clock.Elapsed))}；此前 SentUnits={before}");

            applied.ShouldNotBeNull("过载下命令不得被饿死");
            clock.Elapsed.ShouldBeLessThanOrEqualTo(
                TimeSpan.FromMilliseconds(200),
                $"清计数应在 200 ms 内生效（实测 {clock.Elapsed.TotalMilliseconds:F0} ms）");

            // ② 状态发布：显示源在 1 s 内至少更新一次
            harness.Runtime.TryGetSubscriptions("s1", out var subscriptions).ShouldBeTrue();
            subscriptions!.TryGet("txCount", out var txCount).ShouldBeTrue();

            var shownBefore = txCount!.Written;
            var writesBefore = harness.Adapter.WriteCount;
            Thread.Sleep(1000);
            var shownAfter = txCount.Written;
            var writesInWindow = harness.Adapter.WriteCount - writesBefore;

            _output.WriteLine(
                $"1 s 内：适配器写 {writesInWindow} 次；显示源 txCount 写入 {shownBefore} → {shownAfter}");

            shownAfter.ShouldBeGreaterThan(shownBefore, "过载期间状态发布不得被饿死");
        }
        finally
        {
            harness.Runtime.Dispose();
        }
    }

    /// <summary>过载期间移除模块：必须在停止超时内返回，且线程不成为孤儿。</summary>
    [Fact]
    public void 过载下_移除模块应在停止超时内返回且不成孤儿()
    {
        var harness = CreateHarness(lines: 3, stopTimeoutSeconds: 1);
        try
        {
            Start(harness);
            Thread.Sleep(1500);

            harness.Runtime.Carriers.TryGet("s1", out var carrier).ShouldBeTrue();

            var clock = Stopwatch.StartNew();
            var remove = harness.Services.RemoveModule("s1");
            clock.Stop();

            _output.WriteLine(
                $"过载中移除：{Format(clock.Elapsed)}（停止超时 1 s），成功={remove.Succeeded}，孤儿={carrier!.IsOrphaned}");

            remove.Succeeded.ShouldBeTrue(string.Join("；", remove.Failures));
            clock.Elapsed.ShouldBeLessThanOrEqualTo(
                TimeSpan.FromSeconds(1),
                $"移除必须在停止超时内返回（实测 {clock.Elapsed.TotalMilliseconds:F0} ms）");
            carrier.IsOrphaned.ShouldBeFalse("先清尾再等待 ⇒ 线程应能退出，不应成为孤儿");
        }
        finally
        {
            harness.Runtime.Dispose();
        }
    }

    /// <summary>
    /// 停发之后：**默认参数（积压不过期）**下，已积压的单元仍会被完整写完——
    /// 这里只钉住"不会无限排空"：上界 = 容量 × 单单元线路时间 + 安静判定窗口 + 余量。
    /// </summary>
    /// <remarks>
    /// 实测 ~1.94 s（其中 400 ms 是"安静"的判定窗口，真正排空 ≈ 1.54 s ≈ 64 单元 × 23.7 ms）。
    /// 这不是缺陷：过期关闭时"把已收下的跑完"就是今天的语义；要更快就打开
    /// <see cref="CarrierOptions.MaxBacklogAge"/>（见下一条用例）。
    /// </remarks>
    [Fact]
    public void 停发后_默认参数下应在有界时间内安静()
    {
        var harness = CreateHarness(lines: 3);
        try
        {
            Start(harness);
            Thread.Sleep(1500);

            var clock = Stopwatch.StartNew();
            harness.ViewState.SetInputText("t1", "continuous", "false");
            var quiet = WaitForOrNull(
                () => harness.Adapter.IsQuietFor(TimeSpan.FromMilliseconds(400)),
                TimeSpan.FromSeconds(8));
            clock.Stop();

            _output.WriteLine(
                $"停发后（默认，积压不过期）安静：{(quiet is null ? "8 s 内仍在写" : Format(clock.Elapsed))}；" +
                $"队列容量 {harness.QueueCapacity}，单元 {harness.UnitBytes} B");

            quiet.ShouldNotBeNull("停发后必须安静下来（积压不得无限排空）");
            clock.Elapsed.ShouldBeLessThanOrEqualTo(
                TimeSpan.FromSeconds(2.75),
                $"有界排空：不超过「容量 × 单单元线路时间 + 窗口 + 余量」（实测 {clock.Elapsed.TotalMilliseconds:F0} ms）");
        }
        finally
        {
            harness.Runtime.Dispose();
        }
    }

    /// <summary>
    /// 停发之后：**打开最大滞留时间**（400 ms）时，陈旧积压被清掉 ⇒ 1.5 s 内安静。
    /// </summary>
    [Fact]
    public void 停发后_打开积压过期应在一秒半内安静()
    {
        var harness = CreateHarness(lines: 3, maxBacklogAgeMs: 400);
        try
        {
            Start(harness);
            Thread.Sleep(1500);

            var clock = Stopwatch.StartNew();
            harness.ViewState.SetInputText("t1", "continuous", "false");
            var quiet = WaitForOrNull(
                () => harness.Adapter.IsQuietFor(TimeSpan.FromMilliseconds(400)),
                TimeSpan.FromSeconds(5));
            clock.Stop();

            harness.Runtime.Carriers.TryGet("s1", out var carrier).ShouldBeTrue();

            var txIndex = -1;
            for (var index = 0; index < carrier!.PortNames.Count; index++)
            {
                if (string.Equals(carrier.PortNames[index], "tx", StringComparison.Ordinal))
                {
                    txIndex = index;
                    break;
                }
            }

            txIndex.ShouldBeGreaterThanOrEqualTo(0, "承载者应有 tx 口");

            var expired = carrier.Queues[txIndex].DroppedByReason(DropReason.Expired)
                + harness.Snapshot().SendBacklogExpiredUnits;

            _output.WriteLine(
                $"停发后（积压上限 400 ms）安静：{(quiet is null ? "5 s 内仍在写" : Format(clock.Elapsed))}；" +
                $"过期丢弃合计 {expired} 个单元");

            quiet.ShouldNotBeNull("打开过期之后，陈旧积压不得继续写");
            clock.Elapsed.ShouldBeLessThanOrEqualTo(
                TimeSpan.FromSeconds(1.5),
                $"停发后 1.5 s 内应安静（实测 {clock.Elapsed.TotalMilliseconds:F0} ms）");
            expired.ShouldBeGreaterThan(0, "安静必须是**过期丢弃**造成的（发送积压或 tx 队列），而不是别的路径");
        }
        finally
        {
            harness.Runtime.Dispose();
        }
    }

    /// <summary>正常负载（1 行 / 10 ms）：不饱和、不丢弃。</summary>
    [Fact]
    public void 正常负载_不应饱和也不应丢弃()
    {
        var harness = CreateHarness(lines: 1);
        try
        {
            Start(harness);
            Thread.Sleep(1500);

            var depth = harness.Services.GetQueueDepths()
                .Single(q => q.InstanceId == "s1" && q.PortName == "tx");

            harness.SerialInstance.Context.Ports.TryGetFlowState("tx", out var flow).ShouldBeTrue();

            _output.WriteLine(
                $"1 行/10 ms：tx 深度 {depth.Depth}/{depth.Capacity}，峰值 {flow.PeakDepth}，" +
                $"饱和={flow.Saturated}，丢弃 {depth.Dropped}");

            flow.Saturated.ShouldBeFalse("正常负载不应判饱和");
            depth.Dropped.ShouldBe(0, "正常负载不应丢弃");
        }
        finally
        {
            harness.Runtime.Dispose();
        }
    }

    // ------------------------------------------------------------------ 场景

    private static void Start(Harness harness)
    {
        harness.ViewState.SetSelectedPage("t1", "text-pages", 0);
        harness.ViewState.SetInputText("t1", "text1", harness.Text);
        harness.ViewState.SetInputText("t1", "interval", "10");
        harness.ViewState.SetInputText("t1", "continuous", "true");
    }

    private static Harness CreateHarness(
        int lines,
        int queueCapacity = 64,
        int stopTimeoutSeconds = 2,
        int maxBacklogAgeMs = 0)
    {
        Directory.Exists(StagedModules).ShouldBeTrue($"模块包未归拢：{StagedModules}");

        var line = new string('A', 89) + "\r\n";                 // 91 B/行
        var text = string.Join("\n", Enumerable.Repeat(line, lines));

        var adapter = new BaudModelAdapter(Baud);
        var catalog = new SingleStandardCatalog(
            new ExternalAccessDescriptor(
                ExternalAccessStandardIds.Serial,
                typeof(SerialConfig),
                () => adapter));

        var viewState = new ViewStateStore();
        var runtime = new ModuleRuntime(
            carrierOptions: new CarrierOptions
            {
                QueueCapacity = queueCapacity,
                StopTimeout = TimeSpan.FromSeconds(stopTimeoutSeconds),
                WatchdogInterval = TimeSpan.FromMilliseconds(20),
                MaxBacklogAge = TimeSpan.FromMilliseconds(maxBacklogAgeMs),
            },
            viewState: viewState,
            catalog: catalog);

        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([StagedModules]).ShouldBeGreaterThan(0);
        services.AddModule("t1", TextSenderId).Succeeded.ShouldBeTrue();
        services.AddModule("s1", SerialId).Succeeded.ShouldBeTrue();

        // 建网（只有一条用户路径：两端同名标签）
        TestLink.Link(services, new PortRef("t1", "out"), new PortRef("s1", "tx"), "responsiveness");

        services.UpdateConfigurationField("s1", nameof(SerialConfig.PortName), "PROBE0")
            .Succeeded.ShouldBeTrue();

        runtime.Content.TryGetInstance("s1", out var serialInstance).ShouldBeTrue();

        services.SendCommand("s1", "open").Succeeded.ShouldBeTrue();
        WaitFor(
            () => serialInstance!.Context.Access.GetSnapshot(AccessName).State == ExternalAccessState.Open,
            "串口会话应打开");

        var unitBytes = Encoding.UTF8.GetByteCount(text);

        return new Harness(runtime, services, viewState, adapter, serialInstance!, text, unitBytes, queueCapacity);
    }

    private sealed record Harness(
        ModuleRuntime Runtime,
        RuntimeServices Services,
        ViewStateStore ViewState,
        BaudModelAdapter Adapter,
        ModuleInstance SerialInstance,
        string Text,
        int UnitBytes,
        int QueueCapacity)
    {
        public ExternalAccessSnapshot Snapshot() =>
            SerialInstance.Context.Access.GetSnapshot(AccessName);
    }

    // ------------------------------------------------------------------ 假串口（按波特率阻塞）

    private sealed class BaudModelAdapter : IExternalAccessAdapter
    {
        private readonly double _bytesPerSecond;
        private readonly Lock _gate = new();
        private readonly Queue<byte[]> _inbound = new();
        private IExternalAccessReady? _ready;
        private long _lastActivityTicks;
        private int _writeCount;

        public BaudModelAdapter(int baud) => _bytesPerSecond = baud / 10.0;

        public int OpenCount { get; private set; }

        public int WriteCount => Volatile.Read(ref _writeCount);

        public Type ParametersType => typeof(SerialConfig);

        public void Open(object parameters, IExternalAccessReady ready)
        {
            OpenCount++;
            _ready = ready;
            ready.SetConnecting(new ExternalAccessEndpoint("PROBE0"));
            ready.SetOpen(new ExternalAccessEndpoint("PROBE0"));
        }

        public void Close(TimeSpan timeout)
        {
            _ready = null;

            lock (_gate)
            {
                _inbound.Clear();
            }
        }

        public bool TryRead(Span<byte> destination, out int length)
        {
            lock (_gate)
            {
                length = 0;

                if (_inbound.Count == 0)
                {
                    return false;
                }

                var payload = _inbound.Dequeue();
                payload.AsSpan().CopyTo(destination);
                length = payload.Length;
                return true;
            }
        }

        /// <inheritdoc />
        /// <remarks>真实 USB-TTL 的行为：一次写阻塞到这批字节在线路上发完（线容量是硬上限）。</remarks>
        public bool Write(ReadOnlySpan<byte> payload)
        {
            var start = Stopwatch.GetTimestamp();
            var duration = payload.Length / _bytesPerSecond;
            SpinUntil(start + (long)(duration * Stopwatch.Frequency));

            _ = Interlocked.Increment(ref _writeCount);
            Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
            return true;
        }

        public void Push(byte[] payload)
        {
            lock (_gate)
            {
                _inbound.Enqueue(payload);
            }

            _ready?.NotifyReady();
        }

        public bool IsQuietFor(TimeSpan duration) =>
            Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastActivityTicks)
                > (long)(duration.TotalSeconds * Stopwatch.Frequency);

        private static void SpinUntil(long target)
        {
            while (Stopwatch.GetTimestamp() < target)
            {
                Thread.SpinWait(200);
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

    private static string Format(TimeSpan value) =>
        value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms";

    private static TimeSpan? WaitForOrNull(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            if (clock.Elapsed > timeout)
            {
                return null;
            }

            Thread.Sleep(5);
        }

        return clock.Elapsed;
    }

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        if (WaitForOrNull(condition, TimeSpan.FromMilliseconds(timeoutMs)) is null)
        {
            throw new TimeoutException(message);
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
