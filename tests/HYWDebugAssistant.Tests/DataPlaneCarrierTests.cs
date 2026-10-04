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

            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, seq: 42, value: 1.5f))
                .ShouldBe(CarrierEnqueueResult.Accepted);

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
            healthyCarrier!.TryEnqueue("in", BuildUnit(host.Buffers, 7, 7f))
                .ShouldBe(CarrierEnqueueResult.Accepted);
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

        carrier!.TryEnqueue("in", BuildUnit(host.Buffers, 1, 1f)).ShouldBe(CarrierEnqueueResult.Accepted);
        carrier.TryEnqueue("in", BuildUnit(host.Buffers, 2, 2f)).ShouldBe(CarrierEnqueueResult.Accepted);

        host.TryDeactivate("stuck", out var result).ShouldBeTrue();

        result.ShouldBe(CarrierStopResult.TimedOut);
        carrier.IsOrphaned.ShouldBeTrue();
        host.Buffers.Abandoned.ShouldBeGreaterThan(0, "超时路径的缓冲必须放弃归还（避免被复用后仍被写）");
    }

    [Fact]
    public void 无承载的口_入队应被拒绝并立即计入丢弃()
    {
        // **每口一条队列** ⇒ 口不存在就**没有队列可入**：「无承载」是**入队即失败**，
        // 而不是"入队后在投递循环里丢掉"（后者会把两种性质不同的事混成一个数）。
        using var host = new CarrierHost();
        var target = new TestTarget("nohandler") { PortExists = false };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();
        try
        {
            carrier!.TryEnqueue("ghost", BuildUnit(host.Buffers, 1, 1f))
                .ShouldBe(CarrierEnqueueResult.NoCarrier, "口不在承载者的口集合里 ⇒ 无承载");

            carrier.Diagnostics.Dropped.ShouldBe(1, "无承载应**立即**计入丢弃（不经过投递循环）");
            carrier.Diagnostics.Delivered.ShouldBe(0);
            carrier.Diagnostics.Enqueued.ShouldBe(0, "无承载**不入队**");
        }
        finally
        {
            host.TryDeactivate("nohandler", out _);
        }
    }

    [Fact]
    public void 口存在但无接收处理_应入队并在投递时计入丢弃()
    {
        // 口**已声明**（有队列）但模块没注册处理 ⇒ 入队成功，投递循环里丢弃并计数（与"无承载"区分开）
        using var host = new CarrierHost();
        var target = new TestTarget("unhandled") { PortExists = false };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();
        try
        {
            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, 1, 1f))
                .ShouldBe(CarrierEnqueueResult.Accepted, "口存在 ⇒ 有队列承载");

            WaitUntil(() => carrier.Diagnostics.Dropped >= 1, TimeSpan.FromSeconds(2))
                .ShouldBeTrue("入队后投递时无处理 ⇒ 计入丢弃");
            carrier.Diagnostics.Delivered.ShouldBe(0);
        }
        finally
        {
            host.TryDeactivate("unhandled", out _);
        }
    }

    // ------------------------------------------------------------ 每 (实例, 口) 队列

    [Fact]
    public void 每口一条队列_满载的口不得挤占其它口的缓冲额度()
    {
        // 容量**按口计**：一个口被塞满，不得让另一个口的入队被拒。
        // 用**丢新**策略做判据：若两口共用一条容量 1 的队列，fast 的入队必然被拒（返回 QueueFull）；
        // 每口一条队列时应为 Accepted，且**丢弃按口归属**（slow 有丢弃、fast 一个都不丢）。
        using var host = new CarrierHost(options: new CarrierOptions
        {
            QueueCapacity = 1,
            DropPolicy = DropPolicy.DropNewest,
        });

        var target = new TwoPortTarget("two") { SlowDelay = TimeSpan.FromMilliseconds(150) };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();

        try
        {
            // 塞满 slow（消费者 150ms/单元，远慢于入队速度）
            for (var i = 0; i < 6; i++)
            {
                _ = carrier!.TryEnqueue("slow", BuildUnit(host.Buffers, (ulong)i, i));
            }

            WaitUntil(() => carrier!.TryGetQueue("slow", out var slow) && slow!.Dropped >= 1, TimeSpan.FromSeconds(2))
                .ShouldBeTrue("slow 口应被自己的容量限制丢弃（丢新）");

            // fast 口与 slow 无关：必须仍能入队，且不被计入 slow 的丢弃
            var result = carrier!.TryEnqueue("fast", BuildUnit(host.Buffers, 99, 99f));
            result.ShouldBe(CarrierEnqueueResult.Accepted, "**其它口的队列空着** ⇒ 不得因 slow 满而被拒");

            carrier.TryGetQueue("fast", out var fastQueue).ShouldBeTrue();
            fastQueue!.Dropped.ShouldBe(0, "fast 口一个都不该丢");

            WaitUntil(() => target.FastReceived.Count >= 1, TimeSpan.FromSeconds(3))
                .ShouldBeTrue("fast 的数据应在 slow 仍有积压时就送达（不被堵在后面）");
            target.FastReceived.ShouldBe([99f]);

            // 停止：**全部口**的队列都要清空归还（多队列 drain）
            host.TryDeactivate("two", out var stopResult).ShouldBeTrue();
            stopResult.ShouldBe(CarrierStopResult.Stopped);
            host.Buffers.Rented.ShouldBe(host.Buffers.Returned, "多口队列下缓冲借还仍应平衡");
        }
        finally
        {
            host.StopAll();
        }
    }

    [Fact]
    public void 每口一条队列_应按口名分别入队与出队()
    {
        using var host = new CarrierHost();
        var target = new TwoPortTarget("ports") { SlowDelay = TimeSpan.Zero };
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();

        try
        {
            carrier!.PortNames.ShouldBe(["slow", "fast"], "口集合 = 声明顺序（队列下标同序）");
            carrier.Queues.Count.ShouldBe(2, "每 (实例, 口) 一条队列");

            carrier.TryGetQueue("slow", out var slow).ShouldBeTrue();
            carrier.TryGetQueue("fast", out var fast).ShouldBeTrue();
            slow.ShouldNotBeSameAs(fast, "两个口各有独立队列实例");
            carrier.TryGetQueue("nope", out _).ShouldBeFalse("未声明的口没有队列");

            _ = carrier.TryEnqueue("slow", BuildUnit(host.Buffers, 1, 1f));
            WaitUntil(() => target.SlowReceived.Count >= 1, TimeSpan.FromSeconds(2)).ShouldBeTrue();
            WaitUntil(() => carrier.Diagnostics.Delivered >= 1, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        }
        finally
        {
            host.TryDeactivate("ports", out _);
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
            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, (ulong)i, i))
                .ShouldBe(CarrierEnqueueResult.Accepted);
        }

        WaitUntil(() => target.DeliveredCount >= 10, TimeSpan.FromSeconds(3)).ShouldBeTrue("10 个单元应全部投递");

        host.TryDeactivate("balance", out var result);
        result.ShouldBe(CarrierStopResult.Stopped);
        host.Buffers.Rented.ShouldBe(host.Buffers.Returned, "全部缓冲应已归还池（无泄漏）");
    }

    // ------------------------------------------------------------ 薄旁路头

    [Fact]
    public void 薄旁路头_应随数据单元送达并由观测面记录_不带头的单元不留痕()
    {
        // 头是**旁路元数据**：随单元走（不必进 payload），由**观测面**消费
        // （承载诊断记录最近序号/接收时刻）。**单源网络传 null ⇒ 零附加、不留任何痕迹**。
        using var host = new CarrierHost();
        var target = new TestTarget("hdr");
        host.TryActivate(target, out var carrier, out _).ShouldBeTrue();

        try
        {
            var header = new DatagramHeader("s1", "out", Sequence: 7, ReceivedTimestamp: 12345);

            carrier!.TryEnqueue("in", BuildUnit(host.Buffers, 1, 1f), header)
                .ShouldBe(CarrierEnqueueResult.Accepted);

            WaitUntil(() => carrier.Diagnostics.Delivered >= 1, TimeSpan.FromSeconds(2)).ShouldBeTrue();

            carrier.Diagnostics.Headered.ShouldBe(1, "带头的投递应被观测面计数");
            carrier.Diagnostics.LastHeaderSequence.ShouldBe(7, "序号原样送达（按源递增由生产侧保证）");
            carrier.Diagnostics.LastHeaderTimestamp.ShouldBe(12345);

            // 再来一个**不带头**的单元：头相关的计数**保持不变**（零附加不留痕）
            carrier.TryEnqueue("in", BuildUnit(host.Buffers, 2, 2f)).ShouldBe(CarrierEnqueueResult.Accepted);
            WaitUntil(() => carrier.Diagnostics.Delivered >= 2, TimeSpan.FromSeconds(2)).ShouldBeTrue();

            carrier.Diagnostics.Headered.ShouldBe(1, "不带头的单元不得计入带头投递");
            carrier.Diagnostics.LastHeaderSequence.ShouldBe(7, "不带头不得覆盖上次的头信息");
            target.Received.ShouldBe([1f, 2f], "payload 字节不受头影响（字节守恒）");
        }
        finally
        {
            host.TryDeactivate("hdr", out _);
        }
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

    /// <summary>
    /// **两个接收口**的夹具（`slow` 口慢、`fast` 口快）——用于验证"每 (实例, 口) 一条队列"。
    /// </summary>
    private sealed class TwoPortTarget(string instanceId) : ICarrierTarget
    {
        private readonly ConcurrentQueue<float> _slow = new();
        private readonly ConcurrentQueue<float> _fast = new();
        private readonly SimpleStopSignal _stop = new();

        public string InstanceId { get; } = instanceId;

        public IReadOnlyList<string> ReceivePortNames => ["slow", "fast"];

        /// <summary>`slow` 口每个单元的处理耗时。</summary>
        public TimeSpan SlowDelay { get; set; }

        public IReadOnlyList<float> SlowReceived => [.. _slow];

        public IReadOnlyList<float> FastReceived => [.. _fast];

        public bool HasActivity => false;

        public IStopSignal StopSignal => _stop;

        public void RequestStop() => _stop.Request();

        public bool TryGetReceiveHandler(string portName, out ReceiveHandler handler)
        {
            if (string.Equals(portName, "slow", StringComparison.Ordinal))
            {
                handler = OnSlow;
                return true;
            }

            if (string.Equals(portName, "fast", StringComparison.Ordinal))
            {
                handler = OnFast;
                return true;
            }

            handler = null!;
            return false;
        }

        public void RunActivity() => throw new NotSupportedException("本夹具不实现自发活动");

        public void ReportActivityFailure(Exception exception) => throw new NotSupportedException(exception.Message);

        private void OnSlow(ReadOnlySpan<byte> payload)
        {
            if (SlowDelay > TimeSpan.Zero)
            {
                Thread.Sleep(SlowDelay);
            }

            _slow.Enqueue(new PayloadReader(payload, Layout).ReadF32("value"));
        }

        private void OnFast(ReadOnlySpan<byte> payload) =>
            _fast.Enqueue(new PayloadReader(payload, Layout).ReadF32("value"));
    }
}
