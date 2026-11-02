using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单 3：端到端延迟——三跳链 **A→B→C**（两次跨线程交接），走**真实运行时**
/// （每模块专属线程 + 有界队列 + 绑定投递），非模拟实现。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>**一次基准调用 = 一个数据单元的完整往返**（A 发出 → B 转发 → C 收到），
///     由 BenchmarkDotNet 直接给出 ns/op = 单单元端到端延迟（不拿"整批时间"当延迟）；</item>
///   <item>**两种空闲策略对照**（对应 `04a` §7 的收敛手段）：
///     <c>false</c> = 走框架停止信号的阻塞等待（受 Windows 定时器粒度影响）；
///     <c>true</c> = 模块自行自旋轮询（拿到"传输本身"的量级，代价是占用一个 CPU 核）；</item>
///   <item>数据单元带发送时刻戳（<c>ticks</c>），末端模块另算一遍延迟供交叉核对。</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class PipelineBenchmarks : IDisposable
{
    /// <summary>是否用自旋轮询替代阻塞等待（对照项；见类型备注）。</summary>
    [Params(false, true)]
    public bool SpinPolling { get; set; }

    private ModuleRuntime _runtime = null!;
    private RuntimeServices _services = null!;
    private ModuleInstance _source = null!;
    private LatencySinkModule _sink = null!;

    [GlobalSetup]
    public void Setup()
    {
        SpinPoll = SpinPolling;

        _runtime = new ModuleRuntime();
        _services = new RuntimeServices(_runtime);

        Register("source", typeof(LatencySourceModule));
        Register("mid", typeof(LatencyMidModule));
        Register("sink", typeof(LatencySinkModule));

        Require(_services.AddModule("a", "bench/source").Succeeded, "加入源实例");
        Require(_services.AddModule("b", "bench/mid").Succeeded, "加入中间实例");
        Require(_services.AddModule("c", "bench/sink").Succeeded, "加入末端实例");

        Require(Link("a", "out", "b", "in", "ab"), "建网 ab");
        Require(Link("b", "out", "c", "in", "bc"), "建网 bc");

        Require(_runtime.Content.TryGetInstance("a", out var source) && source is not null, "取源实例");
        Require(_runtime.Content.TryGetInstance("c", out var sink) && sink is not null, "取末端实例");

        _source = source!;
        _sink = (LatencySinkModule)sink!.Module;
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _runtime.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>单个数据单元的三跳端到端往返（BDN 的 Mean 即单单元延迟，ns）。</summary>
    [Benchmark(OperationsPerInvoke = 1)]
    public void ThreeHopRoundTrip()
    {
        _sink.Reset();

        var layout = LatencySourceModule.UnitLayout;
        Span<byte> unit = stackalloc byte[layout.SizeInBytes];

        var writer = new PayloadWriter(unit, layout);
        writer.WriteU64("seq", 0);
        writer.WriteU64("ticks", (ulong)Stopwatch.GetTimestamp());
        _ = _source.Context.Ports.TrySend("out", unit);

        // 等末端收到（自旋等待在**基准线程**上，不影响被测路径）
        if (!_sink.WaitForAll(1, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"单元未在期限内抵达末端（当前空闲策略：{(SpinPoll ? "自旋" : "阻塞等待")}）");
        }
    }

    /// <summary>末端实测的平均延迟（ns；与 BDN 的 Mean 交叉核对用的诊断值）。</summary>
    [Benchmark(OperationsPerInvoke = 1)]
    public double SinkAverageLatencyNs()
    {
        ThreeHopRoundTrip();
        return _sink.AverageLatencyNs();
    }

    /// <summary>当前空闲策略（两个模块读它；每个参数组合各自 Setup）。</summary>
    internal static bool SpinPoll { get; private set; }

    /// <summary>空闲一次：自旋轮询或框架停止信号等待。</summary>
    internal static void Idle(IStopSignal signal, TimeSpan fallback)
    {
        if (SpinPoll)
        {
            Thread.SpinWait(20);
            return;
        }

        _ = signal.Wait(fallback);
    }

    /// <summary>建网（只有一条用户路径：两端同名标签 ⇒ 推导 ⇒ 命名网络）。</summary>
    private bool Link(string sourceInstance, string sourcePort, string targetInstance, string targetPort, string name) =>
        _services.UpdatePortNetwork(sourceInstance, sourcePort, name).Succeeded
        && _services.UpdatePortNetwork(targetInstance, targetPort, name).Succeeded
        && _services.GetNetworks().Any(network => string.Equals(network.Name, name, StringComparison.Ordinal));

    private static void Require(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"基准前置失败：{what}");
        }
    }

    private void Register(string name, Type entryType)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("bench", name),
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            entryType,
            string.Empty,
            string.Empty);

        Require(_services.Registry.TryRegister(definition, out _), $"注册 {name}");
    }

    /// <summary>源模块：只声明发送口（发送由基准驱动）。</summary>
    [ModuleDefinition("bench", "source")]
    public sealed class LatencySourceModule : IModule
    {
        public static readonly ContractShape UnitShape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("ticks", ContractShape.OfPrimitive(ContractPrimitive.U64)));

        public static readonly ContractLayout UnitLayout = ContractLayout.FromShape(UnitShape);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, UnitShape));
    }

    /// <summary>中间模块：主动收 → 原样转发（一次处理 + 一次再投递）。</summary>
    [ModuleDefinition("bench", "mid")]
    public sealed class LatencyMidModule : IModule, IActivitySource
    {
        public ActivityKind Kind => ActivityKind.EventDriven;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, LatencySourceModule.UnitShape));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, LatencySourceModule.UnitShape));
        }

        public void RunActivity(IModuleContext context)
        {
            while (!context.StopSignal.IsStopRequested)
            {
                var drained = false;

                while (context.Ports.TryReceive(out var buffer, out _))
                {
                    drained = true;
                    if (buffer is null)
                    {
                        continue;
                    }

                    try
                    {
                        _ = context.Ports.TrySend("out", buffer.Span);
                    }
                    finally
                    {
                        context.Ports.ReleaseBuffer(buffer);
                    }
                }

                if (!drained)
                {
                    Idle(context.StopSignal, TimeSpan.FromMilliseconds(1));
                }
            }
        }
    }

    /// <summary>末端模块：逐个记录端到端延迟（主动接收）。</summary>
    [ModuleDefinition("bench", "sink")]
    public sealed class LatencySinkModule : IModule, IActivitySource
    {
        private readonly Lock _gate = new();

        private int _arrived;
        private int _received;
        private double _totalTicks;

        public ActivityKind Kind => ActivityKind.EventDriven;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, LatencySourceModule.UnitShape));

        public void RunActivity(IModuleContext context)
        {
            while (!context.StopSignal.IsStopRequested)
            {
                var drained = false;

                while (context.Ports.TryReceive(out var buffer, out _))
                {
                    drained = true;
                    if (buffer is null)
                    {
                        continue;
                    }

                    try
                    {
                        var reader = new PayloadReader(buffer.Span, buffer.Layout);
                        _ = reader.ReadU64("seq");
                        var ticks = reader.ReadU64("ticks");
                        var latency = Stopwatch.GetTimestamp() - (long)ticks;

                        lock (_gate)
                        {
                            _received++;
                            _totalTicks += latency;
                        }

                        _ = Interlocked.Increment(ref _arrived);
                    }
                    finally
                    {
                        context.Ports.ReleaseBuffer(buffer);
                    }
                }

                if (!drained)
                {
                    Idle(context.StopSignal, TimeSpan.FromMilliseconds(1));
                }
            }
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _received = 0;
                _totalTicks = 0;
            }

            Volatile.Write(ref _arrived, 0);
        }

        /// <summary>等到收满 <paramref name="expected"/> 个单元（或超时）。</summary>
        internal bool WaitForAll(int expected, TimeSpan timeout)
        {
            var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

            while (Volatile.Read(ref _arrived) < expected)
            {
                if (Stopwatch.GetTimestamp() > deadline)
                {
                    return false;
                }

                Thread.Yield();
            }

            return true;
        }

        internal double AverageLatencyNs()
        {
            lock (_gate)
            {
                return _received == 0
                    ? double.NaN
                    : _totalTicks / _received * (1_000_000_000d / Stopwatch.Frequency);
            }
        }
    }
}
