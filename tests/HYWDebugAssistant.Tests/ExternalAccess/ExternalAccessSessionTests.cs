using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// 会话级用例：被拒计数、清空计数的边界、故障即释放、探活节拍（真实会话 + 假适配件）。
/// </summary>
/// <remarks>
/// 判据只看会话对外的快照与适配件的调用次数，不碰端口 / 套接字。
/// </remarks>
public sealed class ExternalAccessSessionTests
{
    [Fact]
    public void 未打开时写入_计入被拒且开关不清零()
    {
        var session = NewSession(out var adapter);

        session.Write([1, 2, 3]).ShouldBeFalse();

        var rejected = session.GetSnapshot();
        rejected.RejectedUnits.ShouldBe(1);
        rejected.DroppedUnits.ShouldBe(1, "被拒的单元同时按丢弃口径计数");
        rejected.DroppedBytes.ShouldBe(3);

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);

        session.RequestClose();
        _ = session.Pump();

        var after = session.GetSnapshot();
        after.State.ShouldBe(ExternalAccessState.Closed);
        after.RejectedUnits.ShouldBe(1, "打开 / 关闭都不清零计数");
        after.DroppedUnits.ShouldBe(1);
        adapter.OpenCount.ShouldBe(1);
    }

    [Fact]
    public void 清空计数_只清计数_不动队列数据与连接数与状态()
    {
        var session = NewSession(out var adapter);

        // 先制造一次"未打开写入"，让被拒计数非零。
        session.Write([9]).ShouldBeFalse();

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);

        adapter.Publish([1, 2, 3]);
        _ = session.Pump();

        session.Write([4, 5]).ShouldBeTrue();
        SpinWait.SpinUntil(() => session.GetSnapshot().SentUnits == 1, 2000).ShouldBeTrue("写泵应把入队单元写出");
        session.ReportDropped(2, 6);
        session.SetConnections(3);

        var before = session.GetSnapshot();
        before.ReceivedUnits.ShouldBe(1);
        before.ReceivedBytes.ShouldBe(3);
        before.SentUnits.ShouldBe(1);
        before.SentBytes.ShouldBe(2);
        before.DroppedUnits.ShouldBe(3, "2 个上报丢弃 + 1 个被拒");
        before.DroppedBytes.ShouldBe(7, "6 字节上报丢弃 + 1 字节被拒");
        before.RejectedUnits.ShouldBe(1);
        before.LastReceivedAt.ShouldNotBeNull();
        before.ActiveConnections.ShouldBe(3);

        session.ClearCounters();

        var after = session.GetSnapshot();
        after.ReceivedUnits.ShouldBe(0);
        after.ReceivedBytes.ShouldBe(0);
        after.SentUnits.ShouldBe(0);
        after.SentBytes.ShouldBe(0);
        after.DroppedUnits.ShouldBe(0, "队列丢弃也必须一起归零");
        after.DroppedBytes.ShouldBe(0);
        after.RejectedUnits.ShouldBe(0);
        after.LastReceivedAt.ShouldBeNull();
        after.ActiveConnections.ShouldBe(3, "清空计数不动活动连接数");
        after.State.ShouldBe(ExternalAccessState.Open, "清空计数不动状态");

        var buffer = new byte[8];
        session.TryRead(buffer, out var length).ShouldBeTrue("清空计数不动队列里的数据");
        length.ShouldBe(3);
        buffer[..3].ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void 故障后只释放一次_状态与失败保留_再次请求能重开()
    {
        var session = NewSession(out var adapter);

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);

        session.SetFaulted(new ExternalAccessFailure(
            ExternalAccessFailureKind.ReadFailed,
            new ExternalAccessEndpoint("127.0.0.1", 1),
            Detail: "模拟读取失败"));

        _ = session.Pump();

        adapter.CloseCount.ShouldBe(1, "故障后的适配件释放在实例线程上做一次");
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Faulted);
        session.GetSnapshot().Failure.ShouldNotBeNull("释放适配件不得清掉失败事实");

        _ = session.Pump();
        adapter.CloseCount.ShouldBe(1, "重复推进不得再次关闭适配件");

        session.RequestOpen();
        _ = session.Pump();

        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);
        adapter.OpenCount.ShouldBe(2);
    }

    [Fact]
    public void 探活按拍_打开后首拍调到_紧接第二拍不调_关闭后不调()
    {
        var session = NewSession(out var adapter);

        session.RequestOpen();
        _ = session.Pump();

        adapter.LivenessCalls.ShouldBe(1, "打开后的首拍必须探活");

        _ = session.Pump();
        adapter.LivenessCalls.ShouldBe(1, "节拍间隔内的第二拍不重复探活");

        session.RequestClose();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Closed);

        _ = session.Pump();
        adapter.LivenessCalls.ShouldBe(1, "关闭后不再探活");
    }

    [Fact]
    public void 写超时_打开前下发一次且运行期可改写()
    {
        var session = NewSession(out var adapter, writeTimeout: TimeSpan.FromMilliseconds(750));

        session.RequestOpen();
        _ = session.Pump();

        adapter.WriteTimeouts.ShouldHaveSingleItem().ShouldBe(
            TimeSpan.FromMilliseconds(750),
            "打开前必须把写超时交给支持该能力的适配件");

        session.UpdateWriteTimeout(TimeSpan.FromMilliseconds(250));

        adapter.WriteTimeouts.Count.ShouldBe(2);
        adapter.WriteTimeouts[1].ShouldBe(TimeSpan.FromMilliseconds(250), "运行期改设置要即时下发");

        // 关闭后重开：又一次（新的一次打开，用当前值）
        session.RequestClose();
        _ = session.Pump();
        session.RequestOpen();
        _ = session.Pump();

        adapter.WriteTimeouts.Count.ShouldBe(3);
        adapter.WriteTimeouts[2].ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void 积压过期_接收队列的过期单元不出队且计入过期()
    {
        var queue = new AccessInboundBuffer(4, maxBacklogAge: TimeSpan.FromMilliseconds(50));
        var adapter = new FakeExternalAccessAdapter();

        var session = new ExternalAccessSession(
            new ExternalAccessDeclaration(FakeAccessModule.AccessName, FakeAccessModule.StandardId),
            adapter,
            typeof(FakeAccessParameters),
            _ => new FakeAccessParameters(),
            queue,
            TimeSpan.FromSeconds(1),
            signal: null);

        session.RequestOpen();
        _ = session.Pump();

        adapter.Publish([1, 2, 3]);
        _ = session.Pump();       // 泵进队列（入队时已打点）

        Thread.Sleep(TimeSpan.FromMilliseconds(120));

        var buffer = new byte[8];
        session.TryRead(buffer, out var length).ShouldBeFalse("过期单元不得交付给消费方");
        length.ShouldBe(0);
        queue.Expired.ShouldBe(1);
        queue.Count.ShouldBe(0);
    }

    [Fact]
    public void 丢弃不变量_五类来源各自只计一次且合计闭合()
    {
        var queue = new AccessInboundBuffer(2);
        var adapter = new FakeExternalAccessAdapter();
        var session = NewSession(queue, adapter, maxUnitBytes: 8);

        // (a) 会话未打开 ⇒ 拒发
        session.Write([1]).ShouldBeFalse();

        session.RequestOpen();
        _ = session.Pump();

        // (b) 适配件写失败
        adapter.WriteOk = false;
        session.Write([2, 3]).ShouldBeTrue();
        SpinWait.SpinUntil(() => session.GetSnapshot().SendFailedUnits == 1, 2000).ShouldBeTrue("写泵的写失败应被记账");
        adapter.WriteOk = true;

        // (c) 适配件上报自身溢出（两参重载 = AdapterOverflow）
        session.ReportDropped(4, 40);

        // (d) rx 超长：适配件声称交付 12 B，而声明上限是 8 B
        adapter.LengthOverride = 12;
        adapter.Publish([4, 5, 6, 7]);
        _ = session.Pump();
        adapter.LengthOverride = null;

        // (e) 队列满挤旧：容量 2，塞 3 个（每次 Publish 只通知一次 ⇒ 每只泵一拍）
        adapter.Publish([1, 1]);
        _ = session.Pump();
        adapter.Publish([2, 2]);
        _ = session.Pump();
        adapter.Publish([3, 3]);
        _ = session.Pump();

        var snapshot = session.GetSnapshot();

        // 每个来源各自只增一次（原始读数）
        snapshot.RejectedUnits.ShouldBe(1);
        snapshot.RejectedBytes.ShouldBe(1);
        snapshot.SendFailedUnits.ShouldBe(1);
        snapshot.SendFailedBytes.ShouldBe(2);
        snapshot.AdapterDroppedUnits.ShouldBe(4);
        snapshot.OversizedDroppedUnits.ShouldBe(1);
        snapshot.ExpiredUnits.ShouldBe(0);
        snapshot.ReceiveQueueDroppedUnits.ShouldBe(1, "容量 2 塞 3 个 ⇒ 挤旧 1 个");

        // 不变量：收侧合计 = 队列挤旧/拒新 + 超长 + 适配件溢出 + 过期
        snapshot.ReceiveDroppedUnits.ShouldBe(
            snapshot.ReceiveQueueDroppedUnits
            + snapshot.OversizedDroppedUnits
            + snapshot.AdapterDroppedUnits
            + snapshot.ExpiredUnits);
        snapshot.ReceiveDroppedUnits.ShouldBe(1 + 1 + 4);

        // 不变量：丢弃合计 = 拒发 + 写失败 + 收侧
        snapshot.DroppedUnits.ShouldBe(
            snapshot.RejectedUnits + snapshot.SendFailedUnits + snapshot.ReceiveDroppedUnits);
        snapshot.DroppedUnits.ShouldBe(1 + 1 + 6);
        snapshot.DroppedBytes.ShouldBe(
            snapshot.RejectedBytes + snapshot.SendFailedBytes + snapshot.ReceiveDroppedBytes);

        snapshot.LastDropReason.ShouldBe(DropReason.SessionQueueFullEvicted, "最近一次是队列挤旧");
        snapshot.LastDropAt.ShouldNotBeNull();
    }

    [Fact]
    public void 清空计数_应把分原因账本与最近丢弃一起归零且不动队列数据()
    {
        var queue = new AccessInboundBuffer(4);
        var adapter = new FakeExternalAccessAdapter();
        var session = NewSession(queue, adapter);

        session.Write([9]).ShouldBeFalse();          // 拒发 1
        session.RequestOpen();
        _ = session.Pump();

        adapter.WriteOk = false;
        session.Write([1, 2]).ShouldBeTrue();        // 入队成功，失败由写泵异步记账
        SpinWait.SpinUntil(() => session.GetSnapshot().SendFailedUnits == 1, 2000).ShouldBeTrue();
        adapter.WriteOk = true;
        session.ReportDropped(DropReason.AdapterOverflow, 3, 30);

        adapter.Publish([7, 7, 7]);
        _ = session.Pump();                          // 队列里留 1 个单元

        session.GetSnapshot().LastDropReason.ShouldNotBeNull();

        session.ClearCounters();

        var snapshot = session.GetSnapshot();
        snapshot.RejectedUnits.ShouldBe(0);
        snapshot.RejectedBytes.ShouldBe(0);
        snapshot.SendFailedUnits.ShouldBe(0);
        snapshot.SendFailedBytes.ShouldBe(0);
        snapshot.AdapterDroppedUnits.ShouldBe(0);
        snapshot.OversizedDroppedUnits.ShouldBe(0);
        snapshot.ExpiredUnits.ShouldBe(0);
        snapshot.ReceiveDroppedUnits.ShouldBe(0);
        snapshot.ReceiveQueueDroppedUnits.ShouldBe(0);
        snapshot.DroppedUnits.ShouldBe(0);
        snapshot.DroppedBytes.ShouldBe(0);
        snapshot.LastDropReason.ShouldBeNull("清完计数还说「刚丢过」就是假读数");
        snapshot.LastDropAt.ShouldBeNull();
        snapshot.ReceiveQueueDepth.ShouldBe(1, "清计数不清数据");

        var buffer = new byte[8];
        session.TryRead(buffer, out var length).ShouldBeTrue("队列里的数据仍在");
        length.ShouldBe(3);
    }

    [Fact]
    public void 快照_应给出接收队列的深度容量与饱和()
    {
        var queue = new AccessInboundBuffer(4, hintDepth: 2, hintHold: TimeSpan.FromMilliseconds(50));
        var adapter = new FakeExternalAccessAdapter();
        var session = NewSession(queue, adapter);

        session.RequestOpen();
        _ = session.Pump();

        adapter.Publish([1, 1]);
        _ = session.Pump();
        adapter.Publish([2, 2]);
        _ = session.Pump();       // 每次 Publish 只通知一次 ⇒ 每只泵一拍

        var early = session.GetSnapshot();
        early.ReceiveQueueDepth.ShouldBe(2);
        early.ReceiveQueueCapacity.ShouldBe(4);
        early.ReceiveSaturated.ShouldBeFalse("深度刚到阈值还不够");
        early.ReceiveSaturatedFor.ShouldBe(TimeSpan.Zero);

        Thread.Sleep(TimeSpan.FromMilliseconds(90));

        var saturated = session.GetSnapshot();
        saturated.ReceiveSaturated.ShouldBeTrue("持续超过保持时长 ⇒ 饱和");
        saturated.ReceiveSaturatedFor.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    private static ExternalAccessSession NewSession(
        AccessInboundBuffer queue,
        FakeExternalAccessAdapter adapter,
        int maxUnitBytes = ExternalAccessDeclaration.DefaultUnitBytes)
    {
        return new ExternalAccessSession(
            new ExternalAccessDeclaration(FakeAccessModule.AccessName, FakeAccessModule.StandardId)
            {
                MaxUnitBytes = maxUnitBytes,
            },
            adapter,
            typeof(FakeAccessParameters),
            _ => new FakeAccessParameters(),
            queue,
            TimeSpan.FromSeconds(1),
            signal: null);
    }

    [Fact]
    public void 一次Pump应把适配器暂存排空()
    {
        var session = NewSession(out var adapter);

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);

        adapter.Publish([1, 2, 3]);
        adapter.Publish([4, 5, 6]);
        adapter.Publish([7, 8, 9]);

        _ = session.Pump();

        session.GetSnapshot().ReceiveQueueDepth.ShouldBe(
            3,
            "一次 Pump 应把暂存里的多个单元全部取走（旧实现只取一个，长文本突发时会溢出）");
    }

    [Fact]
    public void 打开失败后_故障与原因应保留到用户显式重开_改参数不得自动重开()
    {
        var session = NewSession(out var adapter);
        adapter.ThrowOnOpen = true;

        session.RequestOpen();
        _ = session.Pump();

        var failed = session.GetSnapshot();
        failed.State.ShouldBe(ExternalAccessState.Faulted);
        failed.Failure.ShouldNotBeNull("打开失败必须保留失败原因");
        adapter.OpenCount.ShouldBe(1);

        // 实例线程释放一次坏句柄（故障即释放），但状态与原因不动。
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Faulted);
        session.GetSnapshot().Failure.ShouldNotBeNull("释放适配件不得清掉失败事实");

        // 切换端口/改参数 → NotifyConfigurationChanged：**不得**先 CloseCore 再把 Faulted 清成 Closed 并自动重开。
        session.NotifyConfigurationChanged();
        _ = session.Pump();

        var afterRestart = session.GetSnapshot();
        afterRestart.State.ShouldBe(ExternalAccessState.Faulted, "故障态不得被参数变更静默清成 Closed");
        afterRestart.Failure.ShouldNotBeNull("故障原因必须保留");
        adapter.OpenCount.ShouldBe(1, "没有显式重开意图时不得再尝试打开");

        // 用户改完参数、显式按"打开"：第二次 Open 并成功。
        adapter.ThrowOnOpen = false;
        session.RequestOpen();
        _ = session.Pump();

        var reopened = session.GetSnapshot();
        reopened.State.ShouldBe(ExternalAccessState.Open);
        reopened.Failure.ShouldBeNull("打开成功必须清掉上一次失败");
        adapter.OpenCount.ShouldBe(2);
    }

    [Fact]
    public void 打开失败后显式关闭_应清故障并回到已关闭_之后改参数也不再打开()
    {
        var session = NewSession(out var adapter);
        adapter.ThrowOnOpen = true;

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Faulted);

        session.RequestClose();
        _ = session.Pump();

        var closed = session.GetSnapshot();
        closed.State.ShouldBe(ExternalAccessState.Closed, "显式关闭允许 CloseCore 清掉故障态");
        closed.Failure.ShouldBeNull("显式关闭必须把残留故障一起清掉");

        session.NotifyConfigurationChanged();
        _ = session.Pump();

        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Closed);
        adapter.OpenCount.ShouldBe(1, "关闭之后没有打开意图，改参数不得自己打开");
    }

    private static ExternalAccessSession NewSession(
        out FakeExternalAccessAdapter adapter,
        TimeSpan? writeTimeout = null)
    {
        adapter = new FakeExternalAccessAdapter();

        return new ExternalAccessSession(
            new ExternalAccessDeclaration(FakeAccessModule.AccessName, FakeAccessModule.StandardId),
            adapter,
            typeof(FakeAccessParameters),
            _ => new FakeAccessParameters(),
            new AccessInboundBuffer(4),
            TimeSpan.FromSeconds(1),
            signal: null,
            writeTimeout);
    }
}
