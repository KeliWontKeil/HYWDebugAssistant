using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 查询面的**队列原因账本**与**端口丢弃分项**：`GetQueueDepths` / `GetPortMetrics` / `GetPortFlowState`。
/// </summary>
/// <remarks>
/// 判据是"查询面读到的 == 队列自己账本里的"：查询面若另算一套，两边迟早对不上。
/// </remarks>
public class PortFlowQueryTests
{
    private static readonly ContractLayout Layout =
        ContractLayout.FromShape(ContractShape.OfPrimitive(ContractPrimitive.F32));

    private static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

    // ------------------------------------------------------------ 队列原因账本

    [Fact]
    public void 队列原因账本_应经查询面逐项读到()
    {
        using var runtime = new ModuleRuntime(carrierOptions: new CarrierOptions { QueueCapacity = 2 });
        var target = new LedgerTarget("q1");
        runtime.Carriers.TryActivate(target, out var carrier, out var failure).ShouldBeTrue(failure);

        try
        {
            var services = new RuntimeServices(runtime);

            for (var i = 0; i < 3; i++)
            {
                _ = carrier!.TryEnqueue("in", BuildUnit(runtime.Carriers.Buffers, i));
            }

            runtime.ReportModuleLoss("q1", "in", units: 2, bytes: 8).ShouldBeTrue();

            carrier!.TryGetQueue("in", out var queue).ShouldBeTrue();
            queue!.RecordExpired(units: 1, bytes: 4);

            // 清尾：保持承载者活着（清完还能被查询面读到），并让"停止清尾"这一项非零
            carrier.PurgeQueues();

            var view = services.GetQueueDepths().Single(q => q.InstanceId == "q1" && q.PortName == "in");

            view.Depth.ShouldBe(0);
            view.Capacity.ShouldBe(2);
            view.Enqueued.ShouldBe(3);
            view.PeakDepth.ShouldBe(2, "峰值是历史事实，清尾之后仍在");
            view.Dropped.ShouldBe(1, "满队丢弃 = 挤旧 + 拒新");

            view.DroppedEvictedUnits.ShouldBe(1);
            view.DroppedRejectedUnits.ShouldBe(0);
            view.DroppedBytes.ShouldBe(Layout.SizeInBytes, "1 个被挤掉的单元 × 契约尺寸");
            view.DiscardedOnStopUnits.ShouldBe(2, "清尾清掉 2 个");
            view.ModuleInternalDroppedUnits.ShouldBe(2);
            view.ExpiredUnits.ShouldBe(1);

            view.Saturated.ShouldBeFalse();
            view.SaturatedFor.ShouldBe(TimeSpan.Zero);
            view.LastDropReason.ShouldBe(DropReason.DiscardedOnStop, "最近一次是清尾");
            view.SinceLastDrop.ShouldNotBeNull();

            // 与队列自己的账本逐项一致（查询面不得另算一套）
            view.DroppedEvictedUnits.ShouldBe(queue.DroppedByReason(DropReason.QueueFullEvicted));
            view.DroppedRejectedUnits.ShouldBe(queue.DroppedByReason(DropReason.QueueFullRejected));
            view.DiscardedOnStopUnits.ShouldBe(queue.DroppedByReason(DropReason.DiscardedOnStop));
            view.ModuleInternalDroppedUnits.ShouldBe(queue.DroppedByReason(DropReason.ModuleInternal));
            view.ExpiredUnits.ShouldBe(queue.DroppedByReason(DropReason.Expired));
            view.DroppedBytes.ShouldBe(queue.DroppedBytes);
        }
        finally
        {
            runtime.Carriers.TryDeactivate("q1", out _);
        }
    }

    // ------------------------------------------------------------ 端口丢弃分项

    [Theory]
    [InlineData(DropPolicy.DropOldest, true)]
    [InlineData(DropPolicy.DropNewest, false)]
    public void 端口指标_应把队列满拆成挤旧与拒新(DropPolicy policy, bool expectEvicted)
    {
        var metrics = MeasureQueueFull(policy);

        if (expectEvicted)
        {
            metrics.DropQueueFullEvicted.ShouldBeGreaterThan(0, "丢旧策略：应当记成挤旧");
            metrics.DropQueueFullRejected.ShouldBe(0);
        }
        else
        {
            metrics.DropQueueFullRejected.ShouldBeGreaterThan(0, "丢新策略：应当记成拒新");
            metrics.DropQueueFullEvicted.ShouldBe(0);
        }

        metrics.DropQueueFull.ShouldBe(
            metrics.DropQueueFullEvicted + metrics.DropQueueFullRejected,
            "合并不变：DropQueueFull 仍是两个分项之和");
    }

    [Fact]
    public void 端口指标_应给出承载已停且不混入无承载()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));
        Connect(runtime, "s1", "out", "r1", "in").ShouldBeTrue();

        runtime.Carriers.TryGet("r1", out var carrier).ShouldBeTrue();
        carrier!.Stop(TimeSpan.FromSeconds(2)).ShouldBe(CarrierStopResult.Stopped);

        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        sender!.Context.Ports.TrySend("out", payload).ShouldBeFalse("承载已停 ⇒ 投递不成立");

        var metrics = new RuntimeServices(runtime).GetPortMetrics("s1", "out");

        metrics.DropCarrierStopped.ShouldBe(1);
        metrics.DropNoCarrier.ShouldBe(0, "承载已停与无承载是两件事");
        metrics.Dropped.ShouldBe(1, "传输丢弃合计包含承载已停");
    }

    // ------------------------------------------------------------ 流动状态查询

    [Fact]
    public void 流动状态查询_未知实例或口应给空状态()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));

        var services = new RuntimeServices(runtime);

        services.GetPortFlowState("nope", "in").ShouldBe(PortFlowState.None);
        services.GetPortFlowState("r1", "ghost").ShouldBe(PortFlowState.None);

        var state = services.GetPortFlowState("r1", "in");
        state.Capacity.ShouldBeGreaterThan(0, "已声明的接收口有队列");
        state.Depth.ShouldBe(0);
    }

    // ------------------------------------------------------------ 测试辅助

    private static PortMetricsView MeasureQueueFull(DropPolicy policy)
    {
        using var runtime = new ModuleRuntime(carrierOptions: new CarrierOptions
        {
            QueueCapacity = 1,
            DropPolicy = policy,
        });

        AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        AddModule(runtime, "r1", "slow-receiver", typeof(SlowReceiverModule));
        Connect(runtime, "s1", "out", "r1", "in").ShouldBeTrue();

        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();

        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 1f);

        // 处理器每单元占 200 ms ⇒ 容量 1 的队列必然被后续投递撞满
        for (var i = 0; i < 8; i++)
        {
            _ = sender!.Context.Ports.TrySend("out", payload);
        }

        return new RuntimeServices(runtime).GetPortMetrics("s1", "out");
    }

    private static PayloadBuffer BuildUnit(PooledPayloadBufferPool pool, int value) =>
        pool.RentAndFill(Layout, span => BitConverter.TryWriteBytes(span, (float)value));

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name),
            name, string.Empty, [], ModuleCapability.None, [], true,
            entryType, TestViewPackages.For(entryType), string.Empty);

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    /// <summary>建网（**只有一条用户路径**：两端同名标签 ⇒ 推导 ⇒ 命名网络）。</summary>
    private static bool Connect(ModuleRuntime runtime, string sourceInstance, string sourcePort, string targetInstance, string targetPort)
    {
        var services = new RuntimeServices(runtime);
        return TestLink.TryLink(
            services,
            new PortRef(sourceInstance, sourcePort),
            new PortRef(targetInstance, targetPort),
            $"{sourceInstance}-{targetInstance}");
    }

    /// <summary>接收口（f32）；处理器每单元占 200 ms —— 用"慢消费者"把队列顶满。</summary>
    [ModuleDefinition("test", "slow-receiver")]
    public sealed class SlowReceiverModule : IModule
    {
        /// <summary>接收口名。</summary>
        public const string InPort = "in";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape));
            context.Ports.SetReceiveHandler(InPort, static _ => Thread.Sleep(200));
        }
    }

    /// <summary>只提供一条接收口、step Pull 但不消费的承载目标（队列内容完全由测试掌控）。</summary>
    private sealed class LedgerTarget(string instanceId) : ICarrierTarget
    {
        public string InstanceId { get; } = instanceId;

        public IReadOnlyList<string> ReceivePortNames => ["in"];

        public bool HasStepModel => true;

        public ReceiveMode ReceiveMode => ReceiveMode.Pull;

        public bool TryGetReceiveHandler(string portName, out ReceiveHandler handler)
        {
            handler = null!;
            return false;
        }

        /// <summary>不取队列；短睡避免 step 循环在有数据时空转。</summary>
        public void OnStep(StepReason reason)
        {
            if (reason.HasFlag(StepReason.Data))
            {
                Thread.Sleep(1);
            }
        }

        public void ReportFailure(Exception exception)
        {
            // 测试夹具：不产生承载异常
        }
    }
}
