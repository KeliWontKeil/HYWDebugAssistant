using System.Buffers.Binary;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **模块侧读数只有一个来源**：<c>drop</c> 只取会话轴有效字节，模块自己的口侧丢弃走上报口、不叠加。
/// </summary>
/// <remarks>
/// 用真实的 <see cref="SerialModule"/> + <see cref="TestModuleContext"/>（不拉线程、不起框架）：
/// 判据是"模块不自己攒第二个账本"。状态行措辞、外观配色与逐种类提示属呈现面，由人眼验收。
/// </remarks>
public sealed class ExternalAccessPressureStatusTests
{
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

    private static ExternalAccessSnapshot Snapshot(long droppedBytes) => new(
        ExternalAccessState.Open,
        Suspended: false,
        Endpoint: null,
        Failure: null,
        ReceivedUnits: 0,
        ReceivedBytes: 0,
        SentUnits: 0,
        SentBytes: 0,
        DroppedUnits: 0,
        DroppedBytes: droppedBytes,
        RejectedUnits: 0,
        ActiveConnections: 0,
        LastReceivedAt: null);

    private static ulong ReadU64(TestModuleContext context, string source)
    {
        context.Published.TryGetValue(source, out var values).ShouldBeTrue($"模块应发布 {source}");
        values.ShouldNotBeEmpty();

        return BinaryPrimitives.ReadUInt64LittleEndian(values[^1]);
    }
}
