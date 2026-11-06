using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3：口队列的**原因账本**、停止清尾、饱和压力与流动状态。
/// </summary>
/// <remarks>
/// 分层：全部判据都在有界队列层（确定性、无时序依赖）；只有"饱和要持续超过保持时长"那条
/// 必须真的等一小会儿——它验的正是"等得不够久不算饱和"。
/// </remarks>
public class DataPlaneQueueLedgerTests
{
    private static readonly ContractLayout Layout = ContractLayout.FromShape(
        ContractShape.OfPrimitive(ContractPrimitive.U64));

    [Fact]
    public void 丢弃episode_应在成功出队后结束()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 1, DropPolicy.DropNewest);

        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeFalse();

        queue.TryGetFlowState(out var dropped).ShouldBeTrue();
        dropped.DropActive.ShouldBeTrue("当前丢弃 episode 尚未被消费恢复");

        queue.TryDequeue(out var consumed).ShouldBeTrue();
        queue.TryGetFlowState(out var recovered).ShouldBeTrue();
        recovered.DropActive.ShouldBeFalse("成功消费一次后，丢弃 episode 结束");
        recovered.Depth.ShouldBe(0);
        consumed.Buffer.ShouldNotBeNull();
    }

    [Fact]
    public void 丢旧满队_应把挤掉的旧单元记成队列满挤旧()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 2, DropPolicy.DropOldest);

        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out var none).ShouldBeTrue();
        none.ShouldBeNull("未满队时不该挤出任何东西");
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();

        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out var evicted).ShouldBeTrue();

        queue.Count.ShouldBe(2);
        queue.Dropped.ShouldBe(1);
        queue.DroppedBytes.ShouldBe(Layout.SizeInBytes);
        queue.DroppedByReason(DropReason.QueueFullEvicted).ShouldBe(1);
        queue.DroppedBytesByReason(DropReason.QueueFullEvicted).ShouldBe(Layout.SizeInBytes);
        queue.DroppedByReason(DropReason.QueueFullRejected).ShouldBe(0);
        queue.LastDropReason.ShouldBe(DropReason.QueueFullEvicted);
        evicted.ShouldNotBeNull("被挤掉的旧单元必须交回调用方归还池");
    }

    [Fact]
    public void 丢新满队_应把被拒的新单元记成队列满拒新()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 1, DropPolicy.DropNewest);

        var kept = pool.Rent(Layout);
        var rejected = pool.Rent(Layout);

        queue.TryEnqueue(new InboundUnit(kept, "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(rejected, "in"), out var dropped).ShouldBeFalse();

        dropped.ShouldBeSameAs(rejected, "丢新策略交回的是**新到的**那个单元");
        queue.Dropped.ShouldBe(1);
        queue.DroppedBytes.ShouldBe(Layout.SizeInBytes);
        queue.DroppedByReason(DropReason.QueueFullRejected).ShouldBe(1);
        queue.DroppedByReason(DropReason.QueueFullEvicted).ShouldBe(0);
        queue.LastDropReason.ShouldBe(DropReason.QueueFullRejected);

        queue.TryDequeue(out var unit).ShouldBeTrue();
        unit.Buffer.ShouldBeSameAs(kept, "保留的仍是先到的单元");
    }

    [Fact]
    public void 模块内部丢弃_只进模块内部原因且不并入满队丢弃()
    {
        var queue = new InboundBuffer(capacity: 4);

        queue.RecordModuleLoss(units: 3, bytes: 3L * Layout.SizeInBytes);

        queue.DroppedByReason(DropReason.ModuleInternal).ShouldBe(3);
        queue.DroppedBytesByReason(DropReason.ModuleInternal).ShouldBe(3L * Layout.SizeInBytes);
        queue.Dropped.ShouldBe(0, "模块内部丢弃不是满队丢弃");
        queue.DroppedBytes.ShouldBe(0);
        queue.DroppedByReason(DropReason.QueueFullEvicted).ShouldBe(0);
        queue.DroppedByReason(DropReason.QueueFullRejected).ShouldBe(0);
        queue.LastDropReason.ShouldBe(DropReason.ModuleInternal);

        queue.TryGetFlowState(out var state).ShouldBeTrue();
        state.DroppedUnits.ShouldBe(3, "口的丢弃合计要含模块上报的那部分");
        state.DroppedBytes.ShouldBe(3L * Layout.SizeInBytes);
    }

    [Fact]
    public void 停止清尾_应把队列里的每个单元记成停止丢弃并把缓冲交回()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 4);
        for (var i = 0; i < 3; i++)
        {
            queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        }

        var handed = new List<PayloadBuffer>();
        queue.Purge(handed.Add);

        handed.Count.ShouldBe(3, "全部缓冲都要交给回调（由调用方归还池）");
        queue.Count.ShouldBe(0);
        queue.DroppedByReason(DropReason.DiscardedOnStop).ShouldBe(3);
        queue.DroppedBytesByReason(DropReason.DiscardedOnStop).ShouldBe(3L * Layout.SizeInBytes);
        queue.Dropped.ShouldBe(0, "停止清尾不是满队丢弃");
        queue.LastDropReason.ShouldBe(DropReason.DiscardedOnStop);

        queue.TryGetFlowState(out var state).ShouldBeTrue();
        state.Depth.ShouldBe(0);
        state.DroppedUnits.ShouldBe(3);
        state.DroppedBytes.ShouldBe(3L * Layout.SizeInBytes);
    }

    [Fact]
    public void 饱和_要持续超过保持时长才算且离开后段内丢弃归零()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(
            capacity: 4,
            DropPolicy.DropNewest,
            hintDepth: 2,
            hintHold: TimeSpan.FromMilliseconds(200));

        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();

        queue.TryGetFlowState(out var early).ShouldBeTrue();
        early.Depth.ShouldBe(2);
        early.PeakDepth.ShouldBe(2);
        early.Saturated.ShouldBeFalse("深度刚到阈值还不够——饱和要持续超过保持时长");
        early.SaturatedFor.ShouldBe(TimeSpan.Zero);

        Thread.Sleep(TimeSpan.FromMilliseconds(260));

        queue.TryGetFlowState(out var now).ShouldBeTrue();
        now.Saturated.ShouldBeTrue("持续超过保持时长 ⇒ 饱和");
        now.SaturatedFor.ShouldBeGreaterThan(TimeSpan.Zero);

        // 把队列填满再塞一个（丢新）：这次丢弃发生在压力段内，因此要计入"本段丢了多少"
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeFalse();

        queue.TryGetFlowState(out var dropping).ShouldBeTrue();
        dropping.Depth.ShouldBe(4);
        dropping.DroppedWhileSaturatedUnits.ShouldBe(1, "压力段内的丢弃才算这一段丢的");

        // 深度跌到半阈值以下（0）：压力段收尾，段内丢弃归零
        while (queue.TryDequeue(out _))
        {
        }

        queue.TryGetFlowState(out var drained).ShouldBeTrue();
        drained.Depth.ShouldBe(0);
        drained.Saturated.ShouldBeFalse();
        drained.SaturatedFor.ShouldBe(TimeSpan.Zero);
        drained.DroppedWhileSaturatedUnits.ShouldBe(0, "离开压力段即归零");
        drained.PeakDepth.ShouldBe(4, "峰值是历史事实，不随压力段重置");
        drained.DroppedUnits.ShouldBe(1, "原因账本不因离开压力段而清空");
    }

    [Fact]
    public void 饱和阈值大于容量_应退化为满即饱和()
    {
        // 阈值 8、容量 2：队列永远到不了 8，若按原值判定就**永远不会饱和**（判据静默失效）。
        // 口径是"阈值夹到容量"⇒ 深度到 2（满）并持续超过保持时长即饱和。
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(
            capacity: 2,
            DropPolicy.DropOldest,
            hintDepth: 8,
            hintHold: TimeSpan.FromMilliseconds(100));

        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();

        queue.TryGetFlowState(out var early).ShouldBeTrue();
        early.Depth.ShouldBe(2);
        early.Saturated.ShouldBeFalse("刚满还不够——要持续超过保持时长");

        Thread.Sleep(TimeSpan.FromMilliseconds(160));

        queue.TryGetFlowState(out var saturated).ShouldBeTrue();
        saturated.Saturated.ShouldBeTrue("阈值大于容量 ⇒ 满即视为到达阈值");
        saturated.SaturatedFor.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void 流动状态_应给出深度峰值与水位占比()
    {
        var pool = new PooledPayloadBufferPool();
        var queue = new InboundBuffer(capacity: 4);

        for (var i = 0; i < 3; i++)
        {
            queue.TryEnqueue(new InboundUnit(pool.Rent(Layout), "in"), out _).ShouldBeTrue();
        }

        queue.TryGetFlowState(out var full).ShouldBeTrue();
        full.Depth.ShouldBe(3);
        full.Capacity.ShouldBe(4);
        full.PeakDepth.ShouldBe(3);
        full.Fill.ShouldBe(0.75);
        full.Enqueued.ShouldBe(3);
        full.LastDropReason.ShouldBeNull("没丢过就没有原因");
        full.SinceLastDrop.ShouldBeNull("没丢过就没有最近丢弃时刻");

        _ = queue.TryDequeue(out _);
        _ = queue.TryDequeue(out _);

        queue.TryGetFlowState(out var partial).ShouldBeTrue();
        partial.Depth.ShouldBe(1);
        partial.PeakDepth.ShouldBe(3, "峰值保留");
        partial.Fill.ShouldBe(0.25);
    }
}
