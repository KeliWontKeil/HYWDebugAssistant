using System.Collections.Concurrent;
using System.Diagnostics;
using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3/T5（M1-5）：数据面承载——投递闭环、队列丢弃语义、异常隔离、停止协议与资源处置。
/// </summary>
/// <remarks>
/// 分层验证：<b>丢弃语义</b>在有界队列层（确定性、无时序依赖）；<b>承载行为</b>（线程/回调/停止）
/// 在承载者层（用独立夹具，不依赖建网/绑定）——即 docs/architecture/08 §2 M1-5 要求的验收方式。
/// </remarks>
public class DataPlaneCarrierTests
{
    private static readonly ContractLayout Layout = ContractLayout.FromShape(
        ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32))));

    // ------------------------------------------------------------ 有界队列（丢弃语义）

    [Fact]
    public void 有界队列_丢旧策略应丢弃最旧并接受新数据()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 2, DropPolicy.DropOldest);
        var droppedBuffers = new List<PayloadBuffer>();

        for (var i = 0; i < 3; i++)
        {
            var buffer = pool.RentAndFill(Layout, span => new PayloadWriter(span, Layout).WriteU64("seq", (ulong)i));
            queue.TryEnqueue(new InboundUnit(buffer, "in"), out var dropped).ShouldBeTrue();
            if (dropped is not null)
            {
                droppedBuffers.Add(dropped);
            }
        }

        queue.Count.ShouldBe(2);
        queue.Enqueued.ShouldBe(3);
        queue.Dropped.ShouldBe(1);
        droppedBuffers.Count.ShouldBe(1);

        // 保留的是**最新**的两个（seq 1、2）：FIFO 顺序 + 丢旧
        var firstKept = ReadSeq(queue);
        firstKept.ShouldBe(1UL);
        ReadSeq(queue).ShouldBe(2UL);
    }

    [Fact]
    public void 有界队列_丢新策略应拒绝新数据并返回被拒缓冲()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 1, DropPolicy.DropNewest);

        var first = pool.RentAndFill(Layout, span => new PayloadWriter(span, Layout).WriteU64("seq", 1));
        var second = pool.RentAndFill(Layout, span => new PayloadWriter(span, Layout).WriteU64("seq", 2));

        queue.TryEnqueue(new InboundUnit(first, "in"), out var none).ShouldBeTrue();
        none.ShouldBeNull();

        queue.TryEnqueue(new InboundUnit(second, "in"), out var dropped).ShouldBeFalse();
        dropped.ShouldBeSameAs(second, "丢新策略应把**新来的**缓冲交回调用方归还");

        queue.Count.ShouldBe(1);
        queue.Dropped.ShouldBe(1);

        queue.TryDequeue(out var kept).ShouldBeTrue();
        kept.Buffer.ShouldBeSameAs(first, "保留的应是先到的单元");
    }

    [Fact]
    public void 有界队列_清空应把全部缓冲交回调用方()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 4);
        for (var i = 0; i < 3; i++)
        {
            queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _);
        }

        var drained = new List<PayloadBuffer>();
        queue.Drain(drained.Add);

        drained.Count.ShouldBe(3);
        queue.Count.ShouldBe(0);
    }

    // ------------------------------------------------------------ 承载者（线程 + 回调）

    [Fact]
    public void 投递闭环_目标实例应在其专属线程上收到回调并读到正确字段()
    {
        using var host = new CarrierHost();
        var target = new TestTarget("t1");
        host.TryActivate(target, out var carrier, out var failure).ShouldBeTrue(failure);
        try
        {
            var mainThreadId = Environment.CurrentManagedThreadId;

            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, seq: 42, value: 1.5f)).ShouldBeTrue();

            target.WaitForFirstDelivery(TimeSpan.FromSeconds(2)).ShouldBeTrue("目标实例应在超时前收到投递");
            target.Received.ShouldBe([1.5f]);
            target.DeliveryThreadId.ShouldNotBe(mainThreadId, "投递必须发生在模块的专属线程上");
            carrier.Diagnostics.Delivered.ShouldBe(1);
            carrier.Diagnostics.LastSequence.ShouldBe(1);
            carrier.Diagnostics.LastDataTime.ShouldNotBeNull();
        }
        finally
        {
            host.TryDeactivate("t1", out _);
        }
    }

    [Fact]
    public void 慢消费者_发送方绝不因队列满而被阻塞()
    {
        using var host = new CarrierHost(options: new CarrierOptions { QueueCapacity = 2 });
        var target = new TestTarget("slow") { DeliveryDelay = TimeSpan.FromMilliseconds(30) };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++)
            {
                carrier!.TryEnqueue("in", BuildUnit(host.Buffers, (ulong)i, i));
            }

            stopwatch.Stop();

            // 20 次投递若被 30ms/单元的消费拖住，会需要 ~600ms；不阻塞应远低于此
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(300));
            carrier!.Diagnostics.Dropped.ShouldBeGreaterThan(0, "慢消费必然导致丢弃（容量 2）");
        }
        finally
        {
            host.TryDeactivate("slow", out _);
        }
    }

    [Fact]
    public void 活动体异常_应上报且不影响其他实例()
    {
        using var host = new CarrierHost();
        var broken = new TestTarget("broken")
        {
            HasActivity = true,
            ActivityBody = _ => throw new InvalidOperationException("契约：活动体故意抛出"),
        };
        var healthy = new TestTarget("healthy");

        host.TryActivate(broken, out _, out _).ShouldBeTrue();
        host.TryActivate(healthy, out var healthyCarrier, out _).ShouldBeTrue();
        try
        {
            broken.WaitForFailure(TimeSpan.FromSeconds(2)).ShouldBeTrue("活动体异常应被边界捕获并上报");

            // 另一实例不受影响（AC-2 隔离性）
            healthyCarrier!.TryEnqueue("in", BuildUnit(host.Buffers, 7, 7f)).ShouldBeTrue();
            healthy.WaitForFirstDelivery(TimeSpan.FromSeconds(2)).ShouldBeTrue();
            healthy.Received.ShouldBe([7f]);
        }
        finally
        {
            host.TryDeactivate("broken", out _);
            host.TryDeactivate("healthy", out _);
        }
    }

    [Fact]
    public void 停止_模块响应时应正常停止且停止信号已置位()
    {
        using var host = new CarrierHost();
        var target = new TestTarget("s1") { HasActivity = true };

        // 活动体阻塞等待停止信号（模拟"读硬件→处理→投递"循环）
        target.ActivityBody = self => self.StopSignal.Wait(TimeSpan.FromSeconds(10));

        host.TryActivate(target, out _, out _).ShouldBeTrue();
        try
        {
            host.TryDeactivate("s1", out var result).ShouldBeTrue();

            result.ShouldBe(CarrierStopResult.Stopped);
            target.StopSignal.IsStopRequested.ShouldBeTrue("停止协议必须置位模块的停止信号");
        }
        finally
        {
            host.StopAll();
        }
    }

    [Fact]
    public void 停止超时_应标记孤儿且放弃缓冲不归还池()
    {
        using var host = new CarrierHost(
            options: new CarrierOptions { QueueCapacity = 2, StopTimeout = TimeSpan.FromMilliseconds(200) });
        var target = new TestTarget("stuck")
        {
            HasActivity = true,
            ActivityBody = _ => Thread.Sleep(TimeSpan.FromSeconds(30)),   // 故意不响应停止
        };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();

        carrier!.TryEnqueue("in", BuildUnit(host.Buffers, 1, 1f)).ShouldBeTrue();
        carrier.TryEnqueue("in", BuildUnit(host.Buffers, 2, 2f)).ShouldBeTrue();

        host.TryDeactivate("stuck", out var result).ShouldBeTrue();

        result.ShouldBe(CarrierStopResult.TimedOut);
        carrier.IsOrphaned.ShouldBeTrue();
        host.Buffers.Abandoned.ShouldBeGreaterThan(0, "超时路径的缓冲必须放弃归还（避免被复用后仍被写）");
    }

    [Fact]
    public void 无接收处理的口_应计入丢弃而不抛出()
    {
        using var host = new CarrierHost();
        var target = new TestTarget("nohandler") { PortExists = false };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();
        try
        {
            carrier!.TryEnqueue("ghost", BuildUnit(host.Buffers, 1, 1f)).ShouldBeTrue();

            WaitUntil(() => carrier.Diagnostics.Dropped >= 1, TimeSpan.FromSeconds(2))
                .ShouldBeTrue("目标口没有接收处理时应计入丢弃");
            carrier.Diagnostics.Delivered.ShouldBe(0);
        }
        finally
        {
            host.TryDeactivate("nohandler", out _);
        }
    }

    [Fact]
    public void 正常停止后_缓冲借还应平衡()
    {
        using var host = new CarrierHost();
        var target = new TestTarget("balance");
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();

        for (var i = 0; i < 10; i++)
        {
            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, (ulong)i, i)).ShouldBeTrue();
        }

        WaitUntil(() => target.DeliveredCount >= 10, TimeSpan.FromSeconds(3)).ShouldBeTrue("10 个单元应全部投递");

        host.TryDeactivate("balance", out var result);
        result.ShouldBe(CarrierStopResult.Stopped);
        host.Buffers.Rented.ShouldBe(host.Buffers.Returned, "全部缓冲应已归还池（无泄漏）");
    }

    // ------------------------------------------------------------ 测试辅助

    private static ulong ReadSeq(InboundBuffer queue)
    {
        queue.TryDequeue(out var unit).ShouldBeTrue();
        return new PayloadReader(unit.Buffer.Span, Layout).ReadU64("seq");
    }

    private static PayloadBuffer BuildUnit(PooledPayloadBufferPool pool, ulong seq, float value) =>
        pool.RentAndFill(Layout, span =>
        {
            var writer = new PayloadWriter(span, Layout);
            writer.WriteU64("seq", seq);
            writer.WriteF32("value", value);
        });

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    /// <summary>测试夹具：数据面对模块的最小视图（不依赖分层模型，独立验证承载行为）。</summary>
    /// <remarks>等待用轮询实现，避免持有可释放类型（CA1001）。</remarks>
    private sealed class TestTarget(string instanceId) : ICarrierTarget
    {
        private readonly ConcurrentQueue<float> _received = new();
        private readonly SimpleStopSignal _stop = new();
        private volatile bool _firstDelivery;
        private volatile bool _failureReported;
        private int _delivered;

        public string InstanceId { get; } = instanceId;

        public IReadOnlyList<string> ReceivePortNames => ["in"];

        public bool HasActivity { get; set; }

        public Action<TestTarget>? ActivityBody { get; set; }

        public Exception? Failure { get; private set; }

        public bool PortExists { get; set; } = true;

        /// <summary>每个单元的处理耗时（模拟慢消费者）。</summary>
        public TimeSpan DeliveryDelay { get; set; }

        public int? DeliveryThreadId { get; private set; }

        public int DeliveredCount => Volatile.Read(ref _delivered);

        public IReadOnlyList<float> Received => [.. _received];

        public IStopSignal StopSignal => _stop;

        public void RequestStop() => _stop.Request();

        public bool WaitForFirstDelivery(TimeSpan timeout) => WaitUntil(() => _firstDelivery, timeout);

        public bool WaitForFailure(TimeSpan timeout) => WaitUntil(() => _failureReported, timeout);

        public bool TryGetReceiveHandler(string portName, out ReceiveHandler handler)
        {
            if (PortExists && string.Equals(portName, "in", StringComparison.Ordinal))
            {
                handler = OnData;
                return true;
            }

            handler = null!;
            return false;
        }

        public void RunActivity() => ActivityBody?.Invoke(this);

        public void ReportActivityFailure(Exception exception)
        {
            Failure = exception;
            _failureReported = true;
        }

        private void OnData(ReadOnlySpan<byte> payload)
        {
            if (DeliveryDelay > TimeSpan.Zero)
            {
                Thread.Sleep(DeliveryDelay);
            }

            DeliveryThreadId = Environment.CurrentManagedThreadId;
            _received.Enqueue(new PayloadReader(payload, Layout).ReadF32("value"));
            Interlocked.Increment(ref _delivered);
            _firstDelivery = true;
        }
    }

    /// <summary>测试用停止信号（轮询实现，避免可释放字段）。</summary>
    private sealed class SimpleStopSignal : IStopSignal
    {
        private volatile bool _requested;

        public bool IsStopRequested => _requested;

        public bool Wait(TimeSpan timeout) => WaitUntil(() => _requested, timeout);

        public void Request() => _requested = true;
    }
}
