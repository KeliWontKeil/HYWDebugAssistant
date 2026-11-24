using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HYWDebugAssistant.Modules.CommandSender;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>指令发送器的值表、编码、转换、闸门与导入导出（T1 层）。</summary>
public class CommandSenderTests
{
    private static (CommandSenderModule Module, TestModuleContext Context) Create()
    {
        var context = new TestModuleContext();
        var module = new CommandSenderModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        return (module, context);
    }

    private static void Set(TestModuleContext context, int slot, SlotField field, string text) =>
        context.ViewState.SetInputText(CommandSenderModule.SlotValue(slot, field), text);

    private static void SetMerge(TestModuleContext context, bool value) =>
        context.ViewState.SetInputText(CommandSenderModule.MergeValue, value ? "true" : "false");

    private static string Get(TestModuleContext context, int slot, SlotField field) =>
        context.Values.ReadText(CommandSenderModule.SlotValue(slot, field)) ?? string.Empty;

    private static bool IsSending(TestModuleContext context) =>
        bool.TryParse(context.Values.ReadText(CommandSenderModule.SendingValue), out var value) && value;

    private static void RequestSendAll(CommandSenderModule module, TestModuleContext context)
    {
        context.EnqueueCommand(CommandSenderModule.SendAllCommand);
        module.OnStep(context, StepReason.Command);
    }

    private static string PayloadText(byte[] payload)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        return Encoding.UTF8.GetString(payload, sizeof(ushort), length);
    }

    [Fact]
    public void HEX发送_应解析字节_非法项不发送且标红()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Hex, "true");
        Set(context, 0, SlotField.Content, "48 65 6C 6C 6F");

        module.OnStep(context, StepReason.Input);
        module.SendSlot(context, 0, out var packets).ShouldBeTrue();
        packets.ShouldBe(1);
        PayloadText(context.Sent.ShouldHaveSingleItem().Payload).ShouldBe("Hello");

        context.Sent.Clear();
        Set(context, 0, SlotField.Content, "48 6");
        module.OnStep(context, StepReason.Input);

        module.SendSlot(context, 0, out packets).ShouldBeFalse();
        context.Sent.ShouldBeEmpty();
        context.ValueValidities[CommandSenderModule.SlotValue(0, SlotField.Content)].IsInvalid.ShouldBeTrue();
    }

    [Fact]
    public void 空内容_不发送也不报错()
    {
        var (module, context) = Create();

        module.SendSlot(context, 0, out var packets).ShouldBeFalse();
        packets.ShouldBe(0);
        context.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void 合并发送_应按勾选顺序拼成一条()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        Set(context, 0, SlotField.Hex, "false");
        Set(context, 1, SlotField.Hex, "false");
        SetMerge(context, true);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[1,0]");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);

        context.Sent.ShouldHaveSingleItem();
        PayloadText(context.Sent[0].Payload).ShouldBe("BA");
        module.IsSending.ShouldBeFalse();
    }

    [Fact]
    public void 逐条发送_应按勾选顺序和间隔依次发送()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        Set(context, 2, SlotField.Content, "C");
        SetMerge(context, false);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1,2]");
        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "10");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);

        context.Sent.Count.ShouldBe(1);
        PayloadText(context.Sent[0].Payload).ShouldBe("A");
        module.IsSending.ShouldBeTrue();
        IsSending(context).ShouldBeTrue();
        context.Timers.Timers.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
        context.AppearanceReports.Last(report =>
            report.SourceName == CommandSenderModule.SendAllCommand).Enabled.ShouldBe(false);

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(10));
        PayloadText(context.Sent[1].Payload).ShouldBe("B");
        module.IsSending.ShouldBeTrue();

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(20));
        PayloadText(context.Sent[2].Payload).ShouldBe("C");
        module.IsSending.ShouldBeFalse();
        IsSending(context).ShouldBeFalse();
        context.Timers.Timers.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
        context.AppearanceReports.Last(report =>
            report.SourceName == CommandSenderModule.SendAllCommand).Enabled.ShouldBe(true);
    }

    [Fact]
    public void 合并拆包_非法UTF8按容量切分()
    {
        var data = new byte[2000];
        Array.Fill(data, (byte)0xFF);

        var packets = CommandSenderCodec.BuildMergedPackets(data, CommandSenderModule.TextCapacity);

        packets.Count.ShouldBe(2);
        BinaryPrimitives.ReadUInt16LittleEndian(packets[0]).ShouldBe((ushort)1472);
        BinaryPrimitives.ReadUInt16LittleEndian(packets[1]).ShouldBe((ushort)528);
    }

    [Fact]
    public void 切换HEX_应转换内容且失败时回弹()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Content, "Hi");
        Set(context, 0, SlotField.Hex, "true");
        context.EnqueueInput(CommandSenderModule.SlotValue(0, SlotField.Hex), "true");
        module.OnStep(context, StepReason.Input);
        Get(context, 0, SlotField.Content).ShouldBe("48 69");

        Set(context, 0, SlotField.Hex, "false");
        context.EnqueueInput(CommandSenderModule.SlotValue(0, SlotField.Hex), "false");
        module.OnStep(context, StepReason.Input);
        Get(context, 0, SlotField.Content).ShouldBe("Hi");

        Set(context, 0, SlotField.Content, "0");
        module.OnStep(context, StepReason.Input);
        Set(context, 0, SlotField.Hex, "false");
        context.EnqueueInput(CommandSenderModule.SlotValue(0, SlotField.Hex), "false");
        module.OnStep(context, StepReason.Input);

        Get(context, 0, SlotField.Hex).ShouldBe("true");
        Get(context, 0, SlotField.Content).ShouldBe("0");
    }

    [Fact]
    public void 连续发送_应按间隔循环勾选项()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1]");
        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "10");
        module.OnStep(context, StepReason.Input);

        context.EnqueueInput(CommandSenderModule.ContinuousValue, "true");
        module.OnStep(context, StepReason.Input);

        module.IsContinuous.ShouldBeTrue();
        module.IntervalMilliseconds.ShouldBe(10);
        context.Timers.Timers.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(10));
        context.Sent.Count.ShouldBe(1);
        PayloadText(context.Sent[0].Payload).ShouldBe("A");

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(20));
        context.Sent.Count.ShouldBe(2);
        PayloadText(context.Sent[1].Payload).ShouldBe("B");

        context.EnqueueInput(CommandSenderModule.ContinuousValue, "false");
        module.OnStep(context, StepReason.Input);
        module.IsContinuous.ShouldBeFalse();
    }

    [Fact]
    public void 合并发送_中文跨容量应保持字符完整()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Content, new string('中', 500));
        SetMerge(context, true);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0]");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);

        context.Sent.Count.ShouldBe(2);
        const int chars = 500;
        var full = CommandSenderModule.TextCapacity / 3;
        PayloadText(context.Sent[0].Payload).Length.ShouldBe(full, "首包按 3 字节字符边界取整");
        PayloadText(context.Sent[1].Payload).Length.ShouldBe(chars - full);
        (PayloadText(context.Sent[0].Payload) + PayloadText(context.Sent[1].Payload))
            .ShouldBe(new string('中', chars));
    }

    [Fact]
    public void 逐条发送_单条不要求间隔()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Content, "A");
        SetMerge(context, false);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0]");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);

        context.Sent.ShouldHaveSingleItem();
        PayloadText(context.Sent[0].Payload).ShouldBe("A");
        module.IsSending.ShouldBeFalse();
        context.Timers.Timers.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void 逐条发送_间隔缺失或为零时发送按钮禁用且不启动()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        SetMerge(context, false);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1]");
        module.OnStep(context, StepReason.Input);

        context.AppearanceReports.Last(report =>
            report.SourceName == CommandSenderModule.SendAllCommand).Enabled.ShouldBe(false);

        RequestSendAll(module, context);
        context.Sent.ShouldBeEmpty();
        module.IsSending.ShouldBeFalse();

        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "0");
        module.OnStep(context, StepReason.Input);
        context.AppearanceReports.Last(report =>
            report.SourceName == CommandSenderModule.SendAllCommand).Enabled.ShouldBe(false);

        RequestSendAll(module, context);
        context.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void 逐条发送_改变勾选应取消()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        SetMerge(context, false);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1]");
        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "10");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);
        module.IsSending.ShouldBeTrue();

        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0]");
        module.OnStep(context, StepReason.Input);

        module.IsSending.ShouldBeFalse();
        IsSending(context).ShouldBeFalse();
        context.Timers.Timers.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
        context.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public void 连续发送_合并模式应每拍合并全部勾选项()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        SetMerge(context, true);
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[1,0]");
        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "10");
        module.OnStep(context, StepReason.Input);

        context.EnqueueInput(CommandSenderModule.ContinuousValue, "true");
        module.OnStep(context, StepReason.Input);

        module.IsContinuous.ShouldBeTrue();
        IsSending(context).ShouldBeTrue();
        context.AppearanceReports.Last(report =>
            report.SourceName == CommandSenderModule.SendAllCommand).Enabled.ShouldBe(false);

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(10));
        context.Sent.ShouldHaveSingleItem();
        PayloadText(context.Sent[0].Payload).ShouldBe("BA");

        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(20));
        context.Sent.Count.ShouldBe(2);
        PayloadText(context.Sent[1].Payload).ShouldBe("BA");

        context.EnqueueInput(CommandSenderModule.ContinuousValue, "false");
        module.OnStep(context, StepReason.Input);
        IsSending(context).ShouldBeFalse();
    }

    [Fact]
    public void 逐条发送期间行内发送应被忽略()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1]");
        context.ViewState.SetInputText(CommandSenderModule.IntervalValue, "10");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);
        context.Sent.Count.ShouldBe(1);

        context.EnqueueInput(
            CommandSenderModule.ActionSource,
            JsonSerializer.Serialize(new { op = CommandSenderModule.ActionSend, slot = 1 }));
        module.OnStep(context, StepReason.Input);

        context.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public void 导入导出_应往返并保持顺序()
    {
        var (module, context) = Create();
        const string json = """
            {
              "format": "hyw/command-sender",
              "version": 1,
              "intervalMs": 250,
              "commands": [
                { "label": "A", "content": "41", "hex": true, "cr": false, "lf": true },
                { "label": "B", "content": "hello", "hex": false, "cr": true, "lf": true }
              ]
            }
            """;

        module.ImportJson(context, json, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        module.OnStep(context, StepReason.Input);

        module.Order.ShouldBe([0, 1]);
        Get(context, 0, SlotField.Label).ShouldBe("A");
        Get(context, 0, SlotField.Content).ShouldBe("41");
        Get(context, 0, SlotField.Hex).ShouldBe("true");
        Get(context, 1, SlotField.Content).ShouldBe("hello");

        var exported = module.ExportJson();
        using var document = JsonDocument.Parse(exported);
        document.RootElement.GetProperty("commands").GetArrayLength().ShouldBe(2);
        module.ImportJson(context, "{ not json", out error).ShouldBeFalse();
        module.Order.ShouldBe([0, 1]);
    }

    [Fact]
    public void 勾选集合含非法_闸门应禁用批量与连续()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Hex, "true");
        Set(context, 0, SlotField.Content, "0");
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0]");
        module.OnStep(context, StepReason.Input);

        RequestSendAll(module, context);
        context.Sent.ShouldBeEmpty();

        var reports = context.AppearanceReports;
        reports.ShouldContain(report =>
            report.SourceName == CommandSenderModule.SendAllCommand && report.Enabled == false);
        reports.ShouldContain(report =>
            report.SourceName == CommandSenderModule.ContinuousValue && report.Enabled == false);
    }

    [Fact]
    public void 删除槽位_应清空值并把勾选一起移除()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);
        Set(context, 1, SlotField.Content, "B");
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[0,1]");
        module.OnStep(context, StepReason.Input);

        context.EnqueueInput(
            CommandSenderModule.ActionSource,
            JsonSerializer.Serialize(new { op = CommandSenderModule.ActionDelete, slot = 1 }));
        module.OnStep(context, StepReason.Input);

        module.Order.ShouldBe([0]);
        Get(context, 1, SlotField.Content).ShouldBe(string.Empty);
        context.ViewState.GetInputText(CommandSenderModule.SelectionValue).ShouldBe("[0]");
    }
}
