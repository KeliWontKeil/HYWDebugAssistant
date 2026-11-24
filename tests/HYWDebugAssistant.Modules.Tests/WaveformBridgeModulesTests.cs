using System.Buffers.Binary;
using System.Text;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.RawToWaveform;
using HYWDebugAssistant.Modules.WaveformDisplay;
using HYWDebugAssistant.Modules.WaveformToRaw;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 波形桥两模块（原始 ↔ 波形）的契约与行为：与内置对端同契约、三种协议编解码、双向转换。
/// </summary>
/// <remarks>
/// 运行期参数（帧头/帧尾/前缀/组号/采样率/小数位）由模块声明的输入源持有：
/// 用例统一用 <see cref="TestModuleContext.EnqueueInput"/> 播种，等价于用户在输入框里逐键键入。
/// </remarks>
public class WaveformBridgeModulesTests
{
    private static readonly ContractShape WaveShape = ContractShape.OfStruct(
        ("channelCount", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("payloadBytes", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("groupId", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U32)),
        ("rateHz", ContractShape.OfPrimitive(ContractPrimitive.F32)),
        ("validMask", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("samples", ContractShape.OfArray(ContractPrimitive.F32, 64)));

    private static readonly ContractLayout WaveLayout = ContractLayout.FromShape(WaveShape);

    private static readonly ContractLayout RawLayout = ContractLayout.FromShape(
        ContractShape.OfStruct(
            ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
            ("data", ContractShape.OfBytes(RawBytesFixture.Capacity))));

    [Fact]
    public void 两个转换模块与内置对端同契约()
    {
        var display = ModuleContract.Probe(
            typeof(WaveformDisplayModule),
            portSpecs: ModulePortSpecs.Read("WaveformDisplay", typeof(WaveformDisplayModule).Assembly));
        var monitor = ModuleContract.Probe(
            typeof(DataMonitorModule),
            portSpecs: ModulePortSpecs.Read("DataMonitor", typeof(DataMonitorModule).Assembly));
        var wavePeer = display.DataTypes.Single(type => type.TypeId == "WaveformFrame");
        var rawPeer = monitor.DataTypes.Single(type => type.TypeId == "RawBytes");

        foreach (var moduleType in new[] { typeof(RawToWaveformModule), typeof(WaveformToRawModule) })
        {
            var folder = moduleType.Name.EndsWith("Module", StringComparison.Ordinal)
                ? moduleType.Name[..^"Module".Length]
                : moduleType.Name;
            var probe = ModuleContract.Probe(
                moduleType,
                portSpecs: ModulePortSpecs.Read(folder, moduleType.Assembly));

            probe.Problems.ShouldBeEmpty();
            probe.Advisories.ShouldBeEmpty();

            var wave = probe.DataTypes.Single(type => type.TypeId == "WaveformFrame");
            wave.Semantics.ShouldBe(wavePeer.Semantics, "语义参与兼容判定，跨语义连不上");
            ContractShape.AreSame(wave.Shape, wavePeer.Shape).ShouldBeTrue();

            var raw = probe.DataTypes.Single(type => type.TypeId == "RawBytes");
            raw.Semantics.ShouldBe(rawPeer.Semantics);
            ContractShape.AreSame(raw.Shape, rawPeer.Shape).ShouldBeTrue();

            // 进出类型不同 ⇒ 两个角色 + 一条内部顺序。
            probe.DeclaredRoles.Count.ShouldBe(2);
            probe.DeclaredFlows.Count.ShouldBe(1);

            // 仓库几何：接收口在上、发送口在下，上下叠放即连。
            probe.Ports.Single(port => port.Direction == PortDirection.Receive).Edge.ShouldBe(PortEdge.Top);
            probe.Ports.Single(port => port.Direction == PortDirection.Send).Edge.ShouldBe(PortEdge.Bottom);
        }
    }

    [Fact]
    public void 文本帧_跨单元解码成波形帧_再按小数位编码回文本()
    {
        _ = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var decodeContext,
            (RawToWaveformModule.HeaderHexInput, "61 62"),
            (RawToWaveformModule.FooterHexInput, "63 64"),
            (RawToWaveformModule.GroupIdInput, "7"),
            (RawToWaveformModule.RateInput, "500"));

        DeliverRaw(decodeContext, "ab 1.2 4.3");
        decodeContext.Sent.ShouldBeEmpty("帧尾未到不应出帧");

        DeliverRaw(decodeContext, "5 3.6 cd");
        var sent = decodeContext.Sent.ShouldHaveSingleItem();
        sent.PortName.ShouldBe(RawToWaveformModule.OutPort);

        ReadWave(sent.Payload, out var channels, out var groupId, out var rateHz, out var samples);
        channels.ShouldBe(3);
        groupId.ShouldBe(7u);
        rateHz.ShouldBe(500f);
        samples[0].ShouldBe(1.2f);
        samples[2].ShouldBe(3.6f);

        _ = CreateEncoder(
            WaveformToRawProtocol.TextFrame,
            out var encodeContext,
            (WaveformToRawModule.HeaderHexInput, "61 62"),
            (WaveformToRawModule.FooterHexInput, "63 64"),
            (WaveformToRawModule.DecimalsInput, "2"));

        DeliverWave(encodeContext, BuildWave(3, 1, [1.2f, 4.35f, 3.6f]));
        ReadRawText(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe("ab 1.20 4.35 3.60 cd");
    }

    [Fact]
    public void FireWater_解码忽略前缀与图片帧_编码带行前缀()
    {
        _ = CreateDecoder(
            RawToWaveformProtocol.FireWater,
            out var decodeContext,
            (RawToWaveformModule.GroupIdInput, "2"));

        DeliverRaw(decodeContext, "image:0,120,4,8,1\n1.0,2.");
        decodeContext.Sent.ShouldBeEmpty();

        DeliverRaw(decodeContext, "0\n");
        var sent = decodeContext.Sent.ShouldHaveSingleItem();
        ReadWave(sent.Payload, out var channels, out var groupId, out _, out var samples);
        channels.ShouldBe(2);
        groupId.ShouldBe(2u);
        samples[1].ShouldBe(2f);

        // 前缀过滤：填写后只认同前缀的行，其余行跳过且不计错误。
        _ = CreateDecoder(
            RawToWaveformProtocol.FireWater,
            out var filteredContext,
            (RawToWaveformModule.PrefixInput, "samples"),
            (RawToWaveformModule.GroupIdInput, "3"));

        DeliverRaw(filteredContext, "other:9,9\n");
        filteredContext.Sent.ShouldBeEmpty();

        DeliverRaw(filteredContext, "samples:4,5\n");
        var filtered = filteredContext.Sent.ShouldHaveSingleItem();
        ReadWave(filtered.Payload, out var filteredChannels, out var filteredGroup, out _, out var filteredSamples);
        filteredChannels.ShouldBe(2);
        filteredGroup.ShouldBe(3u);
        filteredSamples[0].ShouldBe(4f);
        filteredSamples[1].ShouldBe(5f);

        _ = CreateEncoder(
            WaveformToRawProtocol.FireWater,
            out var encodeContext,
            (WaveformToRawModule.PrefixInput, "samples"),
            (WaveformToRawModule.DecimalsInput, "1"));

        DeliverWave(encodeContext, BuildWave(2, 1, [1f, 2.5f]));
        ReadRawText(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe("samples:1.0,2.5\n");
    }

    [Fact]
    public void JustFloat_往返字节一致_长度非四倍数时重新同步()
    {
        var frame = BuildJustFloat([3f, 4f]);

        _ = CreateDecoder(
            RawToWaveformProtocol.JustFloat,
            out var decodeContext,
            (RawToWaveformModule.GroupIdInput, "1"));

        DeliverRawBytes(decodeContext, new byte[] { 0xFF, 0x01 });
        DeliverRawBytes(decodeContext, frame);
        var sent = decodeContext.Sent.ShouldHaveSingleItem();
        ReadWave(sent.Payload, out var channels, out _, out _, out var samples);
        channels.ShouldBe(2);
        samples[1].ShouldBe(4f);

        _ = CreateEncoder(
            WaveformToRawProtocol.JustFloat,
            out var encodeContext);

        DeliverWave(encodeContext, BuildWave(2, 1, [3f, 4f]));
        ReadRawBytes(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe(frame);
    }

    [Fact]
    public void 原始到波形_支持一单元多帧与六十四通道()
    {
        _ = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var context,
            (RawToWaveformModule.HeaderHexInput, "61 62"),
            (RawToWaveformModule.FooterHexInput, "63 64"),
            (RawToWaveformModule.GroupIdInput, "1"));

        DeliverRaw(context, "ab 1 2 cd ab 3 cd");
        context.Sent.Count.ShouldBe(2);

        ReadWave(context.Sent[0].Payload, out var channels, out var groupId, out _, out _);
        channels.ShouldBe(2);
        groupId.ShouldBe(1u);

        var numbers = string.Join(' ', Enumerable.Range(0, 64).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        DeliverRaw(context, $"ab {numbers} cd");
        ReadWave(context.Sent[2].Payload, out var wideChannels, out _, out _, out var wideSamples);
        wideChannels.ShouldBe(64);
        wideSamples[63].ShouldBe(63f);
    }

    [Fact]
    public void 原始到波形_组号零不发送()
    {
        _ = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var context,
            (RawToWaveformModule.HeaderHexInput, "61 62"),
            (RawToWaveformModule.FooterHexInput, "63 64"),
            (RawToWaveformModule.GroupIdInput, "0"));

        DeliverRaw(context, "ab 1 2 cd");

        context.Sent.ShouldBeEmpty();
        context.ValueValidities[RawToWaveformModule.GroupIdInput].IsInvalid.ShouldBeTrue();
    }

    [Fact]
    public void 波形到原始_只转换配置的组_清空统计归零()
    {
        var module = CreateEncoder(
            WaveformToRawProtocol.TextFrame,
            out var context,
            (WaveformToRawModule.HeaderHexInput, "61 62"),
            (WaveformToRawModule.FooterHexInput, "63 64"),
            (WaveformToRawModule.GroupIdInput, "5"));

        DeliverWave(context, BuildWave(1, 9, [1f]));
        context.Sent.ShouldBeEmpty("组 9 不在筛选范围");

        DeliverWave(context, BuildWave(1, 5, [2f]));
        ReadRawText(context.Sent.ShouldHaveSingleItem().Payload).ShouldBe("ab 2 cd");
        ReadU64(context.Published[WaveformToRawModule.FramesSource][^1]).ShouldBe(1ul);

        context.EnqueueCommand(WaveformToRawModule.ResetCommand);
        module.OnStep(context, StepReason.Command);
        ReadU64(context.Published[WaveformToRawModule.FramesSource][^1]).ShouldBe(0ul);
    }

    [Fact]
    public void 改输入即生效_不需要回车或失焦()
    {
        var module = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var context,
            (RawToWaveformModule.HeaderHexInput, "61 62"),
            (RawToWaveformModule.FooterHexInput, "63 64"));

        DeliverRaw(context, "ab 1 cd");
        context.Sent.Count.ShouldBe(1);

        // 只改输入文本（等价用户逐键键入），不按回车、不失焦。
        context.EnqueueInput(RawToWaveformModule.HeaderHexInput, "78 79");
        module.OnStep(context, StepReason.Data | StepReason.Input);

        DeliverRaw(context, "ab 2 cd");
        context.Sent.Count.ShouldBe(1, "帧头已换成 xy，旧帧头不该再出帧");

        DeliverRaw(context, "xy 3 cd");
        context.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public void 帧头帧尾可使用不可输入字节()
    {
        _ = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var decode,
            (RawToWaveformModule.HeaderHexInput, "00 FF"),
            (RawToWaveformModule.FooterHexInput, "FE"));

        DeliverRawBytes(decode, [0x00, 0xFF, (byte)' ', (byte)'1', (byte)'.', (byte)'5', (byte)' ', 0xFE]);
        var sent = decode.Sent.ShouldHaveSingleItem();
        ReadWave(sent.Payload, out var channels, out _, out _, out var samples);
        channels.ShouldBe(1);
        samples[0].ShouldBe(1.5f);

        _ = CreateEncoder(
            WaveformToRawProtocol.TextFrame,
            out var encode,
            (WaveformToRawModule.HeaderHexInput, "00 FF"),
            (WaveformToRawModule.FooterHexInput, "FE"),
            (WaveformToRawModule.DecimalsInput, "1"));

        DeliverWave(encode, BuildWave(1, 1, [1.5f]));
        ReadRawBytes(encode.Sent.ShouldHaveSingleItem().Payload)
            .ShouldBe(new byte[] { 0x00, 0xFF, (byte)' ', (byte)'1', (byte)'.', (byte)'5', (byte)' ', 0xFE });
    }

    [Fact]
    public void 非法输入保留上次有效值()
    {
        var module = CreateDecoder(
            RawToWaveformProtocol.TextFrame,
            out var context,
            (RawToWaveformModule.HeaderHexInput, "61 62"),
            (RawToWaveformModule.FooterHexInput, "63 64"),
            (RawToWaveformModule.RateInput, "500"));

        DeliverRaw(context, "ab 1 cd");
        ReadWave(context.Sent[0].Payload, out _, out _, out var rate, out _);
        rate.ShouldBe(500f);

        context.EnqueueInput(RawToWaveformModule.RateInput, "abc");
        module.OnStep(context, StepReason.Data | StepReason.Input);

        DeliverRaw(context, "ab 2 cd");
        context.Sent.Count.ShouldBe(1, "非法采样率应停止出帧");
        context.ValueValidities[RawToWaveformModule.RateInput].IsInvalid.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- 辅助

    private static RawToWaveformModule CreateDecoder(
        RawToWaveformProtocol protocol,
        out TestModuleContext context,
        params (string Source, string Text)[] inputs)
    {
        context = new TestModuleContext();
        context.SetConfiguration(new RawToWaveformConfig { Protocol = protocol });
        SeedInputs(context, inputs);

        var module = new RawToWaveformModule();
        module.OnRegistered(context);
        module.OnActivated();
        return module;
    }

    private static WaveformToRawModule CreateEncoder(
        WaveformToRawProtocol protocol,
        out TestModuleContext context,
        params (string Source, string Text)[] inputs)
    {
        context = new TestModuleContext();
        context.SetConfiguration(new WaveformToRawConfig { Protocol = protocol });
        SeedInputs(context, inputs);

        var module = new WaveformToRawModule();
        module.OnRegistered(context);
        module.OnActivated();
        return module;
    }

    private static void SeedInputs(TestModuleContext context, (string Source, string Text)[] inputs)
    {
        foreach (var (source, text) in inputs)
        {
            context.EnqueueInput(source, text);
        }
    }

    private static void DeliverRaw(TestModuleContext context, string text) =>
        DeliverRawBytes(context, Encoding.UTF8.GetBytes(text));

    private static void DeliverRawBytes(TestModuleContext context, ReadOnlySpan<byte> content)
    {
        var unit = new byte[RawBytesFixture.FrameSize(RawBytesFixture.Capacity)];
        RawBytesFixture.WriteFrame(content, unit, RawBytesFixture.Capacity);
        context.Deliver(RawToWaveformModule.InPort, unit, RawLayout);
    }

    private static void DeliverWave(TestModuleContext context, byte[] unit) =>
        context.Deliver(WaveformToRawModule.InPort, unit, WaveLayout);

    private static byte[] BuildWave(int channelCount, uint groupId, float[] values, float rateHz = 1000f)
    {
        var unit = new byte[WaveShape.SizeInBytes];
        var writer = new HYWDebugAssistant.Sdk.Data.PayloadWriter(unit, WaveLayout);
        writer.Clear();
        writer.WriteU16("channelCount", (ushort)channelCount);
        writer.WriteU16("payloadBytes", (ushort)(channelCount * sizeof(float)));
        writer.WriteU32("groupId", groupId);
        writer.WriteU32("sequence", 1);
        writer.WriteF32("rateHz", rateHz);
        writer.WriteU64("validMask", channelCount >= 64 ? ulong.MaxValue : (1UL << channelCount) - 1);
        for (var i = 0; i < channelCount; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(unit.AsSpan(24 + (i * sizeof(float)), sizeof(float)), values[i]);
        }

        return unit;
    }

    private static byte[] BuildJustFloat(float[] values)
    {
        var frame = new byte[(values.Length * sizeof(float)) + 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(i * sizeof(float), sizeof(float)), values[i]);
        }

        frame[^4] = 0x00;
        frame[^3] = 0x00;
        frame[^2] = 0x80;
        frame[^1] = 0x7F;
        return frame;
    }

    private static void ReadWave(byte[] payload, out int channels, out uint groupId, out float rateHz, out float[] samples)
    {
        HYWDebugAssistant.Sdk.Data.PayloadReader.TryCreate(payload, WaveLayout, out var reader).ShouldBeTrue();
        channels = reader.ReadU16("channelCount");
        groupId = reader.ReadU32("groupId");
        rateHz = reader.ReadF32("rateHz");
        var raw = reader.ReadBytes("samples");
        samples = new float[channels];
        for (var i = 0; i < channels; i++)
        {
            samples[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.Slice(i * sizeof(float), sizeof(float)));
        }
    }

    private static string ReadRawText(byte[] payload) => Encoding.UTF8.GetString(ReadRawBytes(payload));

    private static byte[] ReadRawBytes(byte[] payload)
    {
        RawBytesFixture.TryReadContent(payload, RawBytesFixture.Capacity, out var content).ShouldBeTrue();
        return content.ToArray();
    }

    private static ulong ReadU64(byte[] value) => BinaryPrimitives.ReadUInt64LittleEndian(value);
}
