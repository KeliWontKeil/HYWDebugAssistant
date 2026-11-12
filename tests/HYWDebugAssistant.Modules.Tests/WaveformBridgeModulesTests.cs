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
            new RawToWaveformConfig
            {
                Protocol = RawToWaveformProtocol.TextFrame,
                TextHeader = "ab",
                TextFooter = "cd",
                GroupId = 7,
                RateHz = 500,
            },
            out var decodeContext);

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
            new WaveformToRawConfig
            {
                Protocol = WaveformToRawProtocol.TextFrame,
                TextHeader = "ab",
                TextFooter = "cd",
                Decimals = 2,
            },
            out var encodeContext);

        DeliverWave(encodeContext, BuildWave(3, 1, [1.2f, 4.35f, 3.6f]));
        ReadRawText(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe("ab 1.20 4.35 3.60 cd");
    }

    [Fact]
    public void FireWater_解码忽略前缀与图片帧_编码带行前缀()
    {
        _ = CreateDecoder(
            new RawToWaveformConfig { Protocol = RawToWaveformProtocol.FireWater, GroupId = 2 },
            out var decodeContext);

        DeliverRaw(decodeContext, "image:0,120,4,8,1\n1.0,2.");
        decodeContext.Sent.ShouldBeEmpty();

        DeliverRaw(decodeContext, "0\n");
        var sent = decodeContext.Sent.ShouldHaveSingleItem();
        ReadWave(sent.Payload, out var channels, out var groupId, out _, out var samples);
        channels.ShouldBe(2);
        groupId.ShouldBe(2u);
        samples[1].ShouldBe(2f);

        _ = CreateEncoder(
            new WaveformToRawConfig
            {
                Protocol = WaveformToRawProtocol.FireWater,
                FireWaterPrefix = "samples",
                Decimals = 1,
            },
            out var encodeContext);

        DeliverWave(encodeContext, BuildWave(2, 1, [1f, 2.5f]));
        ReadRawText(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe("samples:1.0,2.5\n");
    }

    [Fact]
    public void JustFloat_往返字节一致_长度非四倍数时重新同步()
    {
        var frame = BuildJustFloat([3f, 4f]);

        _ = CreateDecoder(
            new RawToWaveformConfig { Protocol = RawToWaveformProtocol.JustFloat, GroupId = 1 },
            out var decodeContext);

        DeliverRawBytes(decodeContext, new byte[] { 0xFF, 0x01 });
        DeliverRawBytes(decodeContext, frame);
        var sent = decodeContext.Sent.ShouldHaveSingleItem();
        ReadWave(sent.Payload, out var channels, out _, out _, out var samples);
        channels.ShouldBe(2);
        samples[1].ShouldBe(4f);

        _ = CreateEncoder(
            new WaveformToRawConfig { Protocol = WaveformToRawProtocol.JustFloat },
            out var encodeContext);

        DeliverWave(encodeContext, BuildWave(2, 1, [3f, 4f]));
        ReadRawBytes(encodeContext.Sent.ShouldHaveSingleItem().Payload).ShouldBe(frame);
    }

    [Fact]
    public void 原始到波形_组号兜底为一_支持一单元多帧与六十四通道()
    {
        _ = CreateDecoder(
            new RawToWaveformConfig
            {
                Protocol = RawToWaveformProtocol.TextFrame,
                TextHeader = "ab",
                TextFooter = "cd",
                GroupId = 0,
            },
            out var context);

        DeliverRaw(context, "ab 1 2 cd ab 3 cd");
        context.Sent.Count.ShouldBe(2);

        ReadWave(context.Sent[0].Payload, out var channels, out var groupId, out _, out _);
        channels.ShouldBe(2);
        groupId.ShouldBe(1u, "groupId 0 是保留值，发送端必须兜底");

        var numbers = string.Join(' ', Enumerable.Range(0, 64).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        DeliverRaw(context, $"ab {numbers} cd");
        ReadWave(context.Sent[2].Payload, out var wideChannels, out _, out _, out var wideSamples);
        wideChannels.ShouldBe(64);
        wideSamples[63].ShouldBe(63f);
    }

    [Fact]
    public void 波形到原始_只转换配置的组_清空统计归零()
    {

        var module = CreateEncoder(
            new WaveformToRawConfig
            {
                Protocol = WaveformToRawProtocol.TextFrame,
                TextHeader = "ab",
                TextFooter = "cd",
                GroupId = 5,
            },
            out var context);

        DeliverWave(context, BuildWave(1, 9, [1f]));
        context.Sent.ShouldBeEmpty("组 9 不在筛选范围");

        DeliverWave(context, BuildWave(1, 5, [2f]));
        ReadRawText(context.Sent.ShouldHaveSingleItem().Payload).ShouldBe("ab 2 cd");
        ReadU64(context.Published[WaveformToRawModule.FramesSource][^1]).ShouldBe(1ul);

        context.EnqueueCommand(WaveformToRawModule.ResetCommand);
        module.OnStep(context, StepReason.Command);
        ReadU64(context.Published[WaveformToRawModule.FramesSource][^1]).ShouldBe(0ul);
    }

    // ---------------------------------------------------------------- 辅助

    private static RawToWaveformModule CreateDecoder(RawToWaveformConfig config, out TestModuleContext context)
    {
        context = new TestModuleContext();
        context.SetConfiguration(config);
        var module = new RawToWaveformModule();
        module.OnRegistered(context);
        module.OnActivated();
        return module;
    }

    private static WaveformToRawModule CreateEncoder(WaveformToRawConfig config, out TestModuleContext context)
    {
        context = new TestModuleContext();
        context.SetConfiguration(config);
        var module = new WaveformToRawModule();
        module.OnRegistered(context);
        module.OnActivated();
        return module;
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
