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

namespace HYWDebugAssistant.Benchmarks;

/// <summary>接收模式（同一份契约、同一拓扑，只换"消费者怎么取数据"）。</summary>
public enum ReceiveIdleMode
{
    /// <summary>**被动接收**：模块只注册接收处理，框架投递循环用事件（入队即唤醒）等待。</summary>
    PassiveFrameworkLoop = 0,

    /// <summary>**step Pull**：框架 step 循环投工作信号，模块在 OnStep 中自取。</summary>
    StepPull = 1,
}

/// <summary>
/// 清单 3 的**对照实验**：两种"消费者取数据"的方式，在同一两跳链 A→B 上测单单元端到端延迟。
/// </summary>
/// <remarks>
/// 这组数值回答的是**机制选择问题**（而非实现细节）：被动回调与 step 自取各自的端到端延迟。
/// </remarks>
[MemoryDiagnoser]
public class ReceiveModeBenchmarks : IDisposable
{
    /// <summary>被实测的接收模式。</summary>
    [Params(ReceiveIdleMode.PassiveFrameworkLoop, ReceiveIdleMode.StepPull)]
    public ReceiveIdleMode Mode { get; set; }

    private ModuleRuntime _runtime = null!;
    private RuntimeServices _services = null!;
    private ModuleInstance _source = null!;
    private ReceiveProbe _sink = null!;

    [GlobalSetup]
    public void Setup()
    {
        CurrentMode = Mode;

        _runtime = new ModuleRuntime();
        _services = new RuntimeServices(_runtime);

        switch (Mode)
        {
            case ReceiveIdleMode.PassiveFrameworkLoop:
                Register("passive-sink", typeof(PassiveSinkModule));
                break;
            default:
                Register("pull-sink", typeof(StepPullSinkModule));
                break;
        }

        Register("source", typeof(SourceModule));

        Require(_services.AddModule("a", "bench/source").Succeeded, "加入源实例");

        var sinkLiteral = Mode switch
        {
            ReceiveIdleMode.PassiveFrameworkLoop => "bench/passive-sink",
            _ => "bench/pull-sink",
        };

        Require(_services.AddModule("b", sinkLiteral).Succeeded, "加入宿实例");
        Require(Link("a", "out", "b", "in", "ab"), "建网 ab");

        Require(_runtime.Content.TryGetInstance("a", out var source) && source is not null, "取源实例");
        Require(_runtime.Content.TryGetInstance("b", out var sink) && sink is not null, "取宿实例");

        _source = source!;
        _sink = (ReceiveProbe)sink!.Module;
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _runtime.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>一个数据单元的两跳往返（BDN 的 Mean 即单单元延迟）。</summary>
    [Benchmark(OperationsPerInvoke = 1)]
    public void TwoHopRoundTrip()
    {
        _sink.Reset();

        var layout = SourceModule.UnitLayout;
        Span<byte> unit = stackalloc byte[layout.SizeInBytes];

        var writer = new PayloadWriter(unit, layout);
        writer.WriteU64("seq", 0);
        writer.WriteU64("ticks", (ulong)Stopwatch.GetTimestamp());
        _ = _source.Context.Ports.TrySend("out", unit);

        if (!_sink.WaitForUnit(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"单元未在期限内抵达宿模块（模式：{Mode}）");
        }
    }

    /// <summary>宿模块侧测得的延迟（ns；与 BDN 的 Mean 交叉核对）。</summary>
    [Benchmark(OperationsPerInvoke = 1)]
    public double SinkMeasuredLatencyNs()
    {
        TwoHopRoundTrip();
        return _sink.AverageLatencyNs();
    }

    /// <summary>当前模式的静态投影（模块类读它；每个参数组合各自 Setup）。</summary>
    internal static ReceiveIdleMode CurrentMode { get; private set; }

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

    /// <summary>三种宿模块共用的探针接口（由基类实现，保证测法一致）。</summary>
    public abstract class ReceiveProbe : IModule
    {
        private readonly Lock _gate = new();

        private int _arrived;
        private int _received;
        private double _totalTicks;

        /// <summary>是否已收到单元（供基准线程等待）。</summary>
        internal bool HasUnit => Volatile.Read(ref _arrived) > 0;

        /// <inheritdoc />
        public abstract void OnRegistered(IModuleContext context);

        /// <summary>等待一个单元抵达（基准线程轮询，不影响被测路径）。</summary>
        internal bool WaitForUnit(TimeSpan timeout)
        {
            var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

            while (!HasUnit)
            {
                if (Stopwatch.GetTimestamp() > deadline)
                {
                    return false;
                }

                Thread.Yield();
            }

            return true;
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

        internal double AverageLatencyNs()
        {
            lock (_gate)
            {
                return _received == 0
                    ? double.NaN
                    : _totalTicks / _received * (1_000_000_000d / Stopwatch.Frequency);
            }
        }

        /// <summary>记录一个抵达的单元（读取发送时刻戳算延迟）。</summary>
        protected void Record(ReadOnlySpan<byte> payload, ContractLayout layout)
        {
            var reader = new PayloadReader(payload, layout);
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
    }

    /// <summary>源模块：只声明发送口（发送由基准驱动）。</summary>
    [ModuleDefinition("bench", "source")]
    public sealed class SourceModule : IModule
    {
        public static readonly ContractShape UnitShape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("ticks", ContractShape.OfPrimitive(ContractPrimitive.U64)));

        public static readonly ContractLayout UnitLayout = ContractLayout.FromShape(UnitShape);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, UnitShape));
    }

    /// <summary>模式一：**被动接收**（框架投递循环用事件唤醒）——模块不实现活动源。</summary>
    [ModuleDefinition("bench", "passive-sink")]
    public sealed class PassiveSinkModule : ReceiveProbe
    {
        public override void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, SourceModule.UnitShape));
            context.Ports.SetReceiveHandler("in", payload => Record(payload, SourceModule.UnitLayout));
        }
    }

    /// <summary>模式二：**step Pull**（框架投工作信号，模块在 OnStep 中自取）。</summary>
    [ModuleDefinition("bench", "pull-sink")]
    public sealed class StepPullSinkModule : ReceiveProbe, IActivitySource
    {
        public ReceiveMode Receive => ReceiveMode.Pull;

        public override void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, SourceModule.UnitShape));

        public void OnStep(IModuleContext context, StepReason reason)
        {
            while (context.Ports.TryReceive(out var buffer, out _))
            {
                if (buffer is null)
                {
                    continue;
                }

                try
                {
                    Record(buffer.Span, buffer.Layout);
                }
                finally
                {
                    context.Ports.ReleaseBuffer(buffer);
                }
            }
        }
    }
}
