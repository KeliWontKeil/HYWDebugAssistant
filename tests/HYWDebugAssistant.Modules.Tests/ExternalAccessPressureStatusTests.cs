using System.Buffers.Binary;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **模块侧只呈现框架事实**：状态行的"跟不上"由骨架选、模块用自己的表写；
/// <c>drop</c> 读数只有会话轴一个来源；模块自己看见的丢弃走上报口。
/// </summary>
/// <remarks>
/// 用真实的 <see cref="SerialModule"/> + <see cref="TestModuleContext"/>（不拉线程、不起框架）：
/// 判据是"骨架给什么、模块写成什么"，以及"模块不自己攒第二个账本"。
/// </remarks>
public sealed class ExternalAccessPressureStatusTests
{
    private static readonly System.Reflection.Assembly SerialAssembly = typeof(SerialModule).Assembly;

    [Fact]
    public void 状态行_故障压过压力句()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(
            SerialModule.AccessName,
            Snapshot(
                ExternalAccessState.Faulted,
                failure: new ExternalAccessFailure(ExternalAccessFailureKind.DeviceLost),
                receiveSaturated: true,
                receiveDepth: 9));

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusFailedDeviceLost),
            "非打开态 ⇒ 用 Describe 的故障句（故障压过一切）");
        StateText(context).ShouldNotBe(Text(SerialText.StatusRxSaturated, 9));
    }

    [Fact]
    public void 状态行_接收与发送同时饱和时先说接收()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(
            SerialModule.AccessName,
            Snapshot(receiveSaturated: true, receiveDepth: 5));

        context.PortFlowStates[SerialModule.TxPort] = SaturatedFlow(depth: 3, droppedWhileSaturated: 0);

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusRxSaturated, 5),
            "接收优先于发送（外部来的一侧先说话）");
    }

    [Fact]
    public void 状态行_发送饱和时带积压与本段丢弃数()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot());
        context.PortFlowStates[SerialModule.TxPort] = SaturatedFlow(depth: 7, droppedWhileSaturated: 0);

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusTxSaturated, 7, 0),
            "发送跟不上：积压取 tx 口深度，丢弃取**本段**丢弃数");
    }

    [Fact]
    public void 状态行_无饱和时回到连接句()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot());

        module.OnWork();

        StateText(context).ShouldBe(Text(SerialText.StatusOpened), "正常 ⇒ 连接句");
    }

    [Fact]
    public void 丢弃读数_只有会话轴一个来源()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot(droppedBytes: 123));

        // 让模块自己看见一次丢弃（非法帧 ⇒ ReportLoss），它不得再往 drop 读数里叠字节
        context.Deliver(SerialModule.TxPort, InvalidFrame());

        module.OnWork();

        context.ReportedLosses.ShouldHaveSingleItem("非法帧应走模块上报口");
        ReadU64(context, SerialModule.DropCountSource).ShouldBe(
            123UL,
            "drop 只取会话轴有效字节（模块不再叠加自己的口侧字节）");
    }

    [Fact]
    public void 非法帧_应经上报口记为模块内部丢弃()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot());

        var frame = InvalidFrame();
        context.Deliver(SerialModule.TxPort, frame);

        module.OnWork();

        var loss = context.ReportedLosses.ShouldHaveSingleItem();
        loss.Port.ShouldBe(SerialModule.TxPort);
        loss.Reason.ShouldBe(DropReason.ModuleInternal);
        loss.Units.ShouldBe(1);
        loss.Bytes.ShouldBe(frame.Length);
    }

    [Fact]
    public void 状态行_发送积压应报出数量()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot(sendBacklogDepth: 7));

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusTxBacklog, 7),
            "有积压时要显式报出积压数量（丢弃另有 drop 计数）");
    }

    [Fact]
    public void 状态行_接收丢失带计数_不再只有数字()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(
            SerialModule.AccessName,
            Snapshot(receiveDroppedUnits: 3, lastDropAt: DateTimeOffset.UtcNow));

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusReceiveLost),
            "接收侧丢过就要说一句，不能只把数字放进 drop 读数");
    }

    [Fact]
    public void 状态行_发送失败带计数()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot(sendFailedUnits: 2));

        module.OnWork();

        StateText(context).ShouldBe(Text(SerialText.StatusSendLost));
    }

    [Fact]
    public void 状态行_发送队列丢弃带计数()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(SerialModule.AccessName, Snapshot());
        context.PortFlowStates[SerialModule.TxPort] = SaturatedFlow(depth: 0, droppedWhileSaturated: 5);

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusSendLost),
            "tx 口队列丢过（最近 10 s）也要说一句");
    }

    [Fact]
    public void 状态行_丢失超出时间窗回到连接句()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(
            SerialModule.AccessName,
            Snapshot(
                receiveDroppedUnits: 3,
                lastDropAt: DateTimeOffset.UtcNow.AddSeconds(-11)));

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusOpened),
            "超过提示时间窗就回落连接句（累计数字仍在 drop 读数里）");
    }

    [Fact]
    public void 状态行_接收丢失优先于发送丢失()
    {
        var (context, module) = CreateSerial();

        context.Access.SetSnapshot(
            SerialModule.AccessName,
            Snapshot(receiveDroppedUnits: 2, sendFailedUnits: 4, lastDropAt: DateTimeOffset.UtcNow));

        module.OnWork();

        StateText(context).ShouldBe(
            Text(SerialText.StatusReceiveLost),
            "接收侧先说话（与饱和的优先级一致）");
    }

    // ------------------------------------------------------------ 辅助

    private static (TestModuleContext Context, SerialModule Module) CreateSerial()
    {
        var context = new TestModuleContext();
        var module = new SerialModule();
        module.OnRegistered(context);
        module.OnActivated();

        return (context, module);
    }

    /// <summary>一帧 <c>{len,data}</c>：`len` 远大于可用字节 ⇒ 拆帧失败。</summary>
    private static byte[] InvalidFrame()
    {
        var frame = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, 0xFFFF);
        return frame;
    }

    private static ExternalAccessSnapshot Snapshot(
        ExternalAccessState state = ExternalAccessState.Open,
        ExternalAccessFailure? failure = null,
        bool receiveSaturated = false,
        int receiveDepth = 0,
        long droppedBytes = 0,
        long receiveDroppedUnits = 0,
        long sendFailedUnits = 0,
        DateTimeOffset? lastDropAt = null,
        int sendBacklogDepth = 0) =>
        new(state, false, null, failure, 0, 0, 0, 0, 0, droppedBytes, 0, 0, null)
        {
            ReceiveSaturated = receiveSaturated,
            ReceiveQueueDepth = receiveDepth,
            ReceiveDroppedUnits = receiveDroppedUnits,
            SendBacklogDepth = sendBacklogDepth,
            SendFailedUnits = sendFailedUnits,
            LastDropAt = lastDropAt,
        };

    private static PortFlowState SaturatedFlow(int depth, long droppedWhileSaturated) => new(
        Depth: depth,
        Capacity: 64,
        PeakDepth: depth,
        Enqueued: 100,
        DroppedUnits: droppedWhileSaturated,
        DroppedBytes: droppedWhileSaturated * 4,
        DroppedWhileSaturatedUnits: droppedWhileSaturated,
        Saturated: true,
        SaturatedFor: TimeSpan.FromMilliseconds(600),
        LastDropReason: DropReason.QueueFullEvicted,
        SinceLastDrop: TimeSpan.FromMilliseconds(10));

    private static string StateText(TestModuleContext context) => context.SetTexts[^1].Text;

    private static string Text(string key) => TextCatalog.Get(SerialAssembly, key);

    private static string Text(string key, params object?[] args) => TextCatalog.Get(SerialAssembly, key, args);

    private static ulong ReadU64(TestModuleContext context, string source)
    {
        context.Published.TryGetValue(source, out var values).ShouldBeTrue($"模块应发布 {source}");
        values.ShouldNotBeEmpty();

        return BinaryPrimitives.ReadUInt64LittleEndian(values[^1]);
    }
}
