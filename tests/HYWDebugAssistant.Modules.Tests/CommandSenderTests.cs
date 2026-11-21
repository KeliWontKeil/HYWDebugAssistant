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

    private static string Get(TestModuleContext context, int slot, SlotField field) =>
        context.Values.ReadText(CommandSenderModule.SlotValue(slot, field)) ?? string.Empty;

    private static string PayloadText(byte[] payload)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        return Encoding.UTF8.GetString(payload, sizeof(ushort), length);
    }

    [Fact]
    public void 文本发送_应按UTF8编码并拆包到字符边界()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Content, "AB");

        module.SendSlot(context, 0, out var packets).ShouldBeTrue();
        packets.ShouldBe(1);

        var payload = context.Sent.ShouldHaveSingleItem().Payload;
        payload.Length.ShouldBe(2 + CommandSenderModule.TextCapacity);
        BinaryPrimitives.ReadUInt16LittleEndian(payload).ShouldBe((ushort)2);
        PayloadText(payload).ShouldBe("AB");

        context.Sent.Clear();
        Set(context, 0, SlotField.Content, new string('A', 2000));
        module.SendSlot(context, 0, out packets).ShouldBeTrue();
        packets.ShouldBe(2);
        context.Sent.Count.ShouldBe(2);
        PayloadText(context.Sent[0].Payload).Length.ShouldBe(1472);
        PayloadText(context.Sent[1].Payload).Length.ShouldBe(528);
    }

    [Fact]
    public void 文本发送_多字节字符不得被拆到两个包()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Content, new string('中', 800));

        module.SendSlot(context, 0, out var packets).ShouldBeTrue();
        packets.ShouldBe(2);

        foreach (var sent in context.Sent)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(sent.Payload);
            var text = Encoding.UTF8.GetString(sent.Payload, sizeof(ushort), length);
            text.ShouldAllBe(ch => ch == '中');
        }

        PayloadText(context.Sent[0].Payload).Length.ShouldBe(490);
        PayloadText(context.Sent[1].Payload).Length.ShouldBe(310);
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
    public void 勾选顺序_应决定批量发送顺序()
    {
        var (module, context) = Create();
        context.EnqueueCommand(CommandSenderModule.AddCommand);
        module.OnStep(context, StepReason.Command);

        Set(context, 0, SlotField.Content, "A");
        Set(context, 1, SlotField.Content, "B");
        Set(context, 0, SlotField.Hex, "false");
        Set(context, 1, SlotField.Hex, "false");
        context.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[1,0]");
        module.OnStep(context, StepReason.Input);

        module.SendSelection(context, out var packets).ShouldBeTrue();
        packets.ShouldBe(2);
        PayloadText(context.Sent[0].Payload).ShouldBe("B");
        PayloadText(context.Sent[1].Payload).ShouldBe("A");
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

        module.SendSelection(context, out var packets).ShouldBeFalse();
        packets.ShouldBe(0);
        context.Sent.ShouldBeEmpty();

        var reports = context.AppearanceReports;
        reports.ShouldContain(report =>
            report.SourceName == CommandSenderModule.SendAllCommand && report.Enabled == false);
        reports.ShouldContain(report =>
            report.SourceName == CommandSenderModule.ContinuousValue && report.Enabled == false);
    }

    [Fact]
    public void 换行档_应由CRLF勾选派生()
    {
        var (module, context) = Create();
        Set(context, 0, SlotField.Cr, "false");
        Set(context, 0, SlotField.Lf, "true");
        context.EnqueueInput(CommandSenderModule.SlotValue(0, SlotField.Cr), "false");
        module.OnStep(context, StepReason.Input);

        Get(context, 0, SlotField.Newline).ShouldBe("lf");
        Set(context, 0, SlotField.Cr, "true");
        Set(context, 0, SlotField.Lf, "false");
        context.EnqueueInput(CommandSenderModule.SlotValue(0, SlotField.Cr), "true");
        module.OnStep(context, StepReason.Input);

        Get(context, 0, SlotField.Newline).ShouldBe("cr");
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
