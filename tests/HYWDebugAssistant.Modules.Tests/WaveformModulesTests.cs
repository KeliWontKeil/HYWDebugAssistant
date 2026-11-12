using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using HYWDebugAssistant.Modules.WaveformDisplay;
using HYWDebugAssistant.Modules.WaveformGenerator;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>波形帧约定、两模块契约、波形数学、范围窗口与发送状态（T1 层）。</summary>
public class WaveformModulesTests
{
    private const string GoldenShape =
        "{channelCount:u16, payloadBytes:u16, groupId:u32, sequence:u32, rateHz:f32, validMask:u64, samples:f32[64]}";

    private static readonly System.Text.Json.JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void 波形两模块_应声明同一类型与形状()
    {
        var generator = ModuleContract.Probe(typeof(WaveformGeneratorModule));
        var display = ModuleContract.Probe(typeof(WaveformDisplayModule));

        generator.Problems.ShouldBeEmpty();
        generator.Advisories.ShouldBeEmpty();
        display.Problems.ShouldBeEmpty();
        display.Advisories.ShouldBeEmpty();

        var generatorType = generator.DataTypes.ShouldHaveSingleItem();
        var displayType = display.DataTypes.ShouldHaveSingleItem();
        generatorType.TypeId.ShouldBe("WaveformFrame");
        displayType.TypeId.ShouldBe(generatorType.TypeId);
        generatorType.Shape.Describe().ShouldBe(GoldenShape);
        displayType.Shape.Describe().ShouldBe(GoldenShape);
        generatorType.Shape.SizeInBytes.ShouldBe(280);
        ContractShape.AreSame(generatorType.Shape, displayType.Shape).ShouldBeTrue();

        var generatorPort = generator.Ports.ShouldHaveSingleItem();
        var displayPort = display.Ports.ShouldHaveSingleItem();
        generatorPort.Direction.ShouldBe(HYWDebugAssistant.Sdk.Ports.PortDirection.Send);
        displayPort.Direction.ShouldBe(HYWDebugAssistant.Sdk.Ports.PortDirection.Receive);
        generatorPort.TypeId.ShouldBe(generatorType.TypeId);
        displayPort.TypeId.ShouldBe(displayType.TypeId);
        generatorPort.Semantics.ShouldBe(TransferSemantics.Stream);
        displayPort.Semantics.ShouldBe(TransferSemantics.Stream);
        ContractShape.AreSame(generatorPort.Shape, displayPort.Shape).ShouldBeTrue();
    }

    [Fact]
    public void 波形帧_写入后读取应保持字段与样本()
    {
        var frame = new byte[280];
        var samples = new float[64];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = index + 0.5f;
        }

        WaveformFrameWriter.Write(frame, 7, 42, 1000, 4, 0b1011, samples);

        WaveformFrameReader.TryRead(frame, out var view, out var error).ShouldBeTrue();
        error.ShouldBe(WaveformFrameReadError.None);
        view.ChannelCount.ShouldBe(4);
        view.PayloadBytes.ShouldBe(16);
        view.GroupId.ShouldBe(7u);
        view.Sequence.ShouldBe(42u);
        view.RateHz.ShouldBe(1000f);
        view.ValidMask.ShouldBe(0b1011UL);
        view.Samples[0].ShouldBe(0.5f);
        view.Samples[1].ShouldBe(1.5f);
        view.Samples[2].ShouldBe(0f);
        view.Samples[3].ShouldBe(3.5f);
        view.IsValid(0).ShouldBeTrue();
        view.IsValid(2).ShouldBeFalse();
    }

    [Fact]
    public void 波形帧_畸形输入应拒绝()
    {
        WaveformFrameReader.TryRead(new byte[10], out _, out var shortError).ShouldBeFalse();
        shortError.ShouldBe(WaveformFrameReadError.Short);

        var frame = new byte[280];
        WaveformFrameWriter.Write(frame, 7, 1, 1000, 2, 0b11, new float[2]);

        frame[0] = 0;
        WaveformFrameReader.TryRead(frame, out _, out var countError).ShouldBeFalse();
        countError.ShouldBe(WaveformFrameReadError.ChannelCount);

        WaveformFrameWriter.Write(frame, 7, 1, 1000, 2, 0b11, new float[2]);
        frame[2] = 0;
        WaveformFrameReader.TryRead(frame, out _, out var payloadError).ShouldBeFalse();
        payloadError.ShouldBe(WaveformFrameReadError.PayloadBytes);

        WaveformFrameWriter.Write(frame, 0, 1, 1000, 2, 0b11, new float[2]);
        WaveformFrameReader.TryRead(frame, out _, out var groupError).ShouldBeFalse();
        groupError.ShouldBe(WaveformFrameReadError.GroupId);
    }

    [Fact]
    public void 波形数学_组帧率与一次性帧数应按组分辨率()
    {
        var one = new GeneratorGroupConfig
        {
            GroupId = 1,
            FrequencyHz = 10,
            Resolution = 100,
            Channels = [new GeneratorChannelConfig()],
        };

        WaveformMath.GroupRate(one).ShouldBe(1000);
        WaveformMath.OneShotFrames(one, 5).ShouldBe(500);

        var two = new GeneratorGroupConfig
        {
            GroupId = 2,
            FrequencyHz = 50,
            Resolution = 200,
            Channels = [new GeneratorChannelConfig(), new GeneratorChannelConfig()],
        };

        WaveformMath.GroupRate(two).ShouldBe(10_000);
        WaveformMath.OneShotFrames(two, 5).ShouldBe(1000);

        var disabled = two with
        {
            Channels =
            [
                new GeneratorChannelConfig { Enabled = false },
                new GeneratorChannelConfig { Enabled = false },
            ],
        };
        WaveformMath.GroupRate(disabled).ShouldBe(0);
    }

    [Fact]
    public void 波形数学_波形形状应在幅值与偏移内()
    {
        var random = new Random(1);
        var sine = new GeneratorChannelConfig
        {
            Kind = WaveformKind.Sine,
            PhaseDegrees = 0,
            Amplitude = 2,
            Offset = 1,
        };

        WaveformMath.Sample(sine, 1, 0, random).ShouldBe(1f, 1e-5f);
        WaveformMath.Sample(sine, 1, 0.25, random).ShouldBe(3f, 1e-5f);
        WaveformMath.Sample(sine, 1, 0.75, random).ShouldBe(-1f, 1e-5f);

        var noise = new GeneratorChannelConfig
        {
            Kind = WaveformKind.Noise,
            Amplitude = 1,
            Offset = 0,
        };

        for (var index = 0; index < 100; index++)
        {
            var value = WaveformMath.Sample(noise, 1, index, random);
            value.ShouldBeInRange(-1f, 1f);
        }
    }

    [Fact]
    public void 波形数学_马鞍波应为归一化的三次谐波注入()
    {
        WaveformMath.TryParseKind("saddle", out var saddle).ShouldBeTrue();
        saddle.ShouldBe(WaveformKind.Saddle);
        WaveformMath.KindKey(WaveformKind.Saddle).ShouldBe("saddle");
        WaveformMath.IsModulated(WaveformKind.Saddle).ShouldBeFalse();

        var channel = new GeneratorChannelConfig
        {
            Kind = WaveformKind.Saddle,
            Amplitude = 1,
            Offset = 0,
            PhaseDegrees = 0,
        };
        var random = new Random(1);

        WaveformMath.Sample(channel, 1, 0, random).ShouldBe(0f, 1e-4f);
        WaveformMath.Sample(channel, 1, 1d / 6, random).ShouldBe(1f, 1e-4f);
        WaveformMath.Sample(channel, 1, 0.25, random).ShouldBe(0.96225f, 1e-4f);
        WaveformMath.Sample(channel, 1, 1d / 3, random).ShouldBe(1f, 1e-4f);
        WaveformMath.Sample(channel, 1, 0.5, random).ShouldBe(0f, 1e-4f);
        WaveformMath.Sample(channel, 1, 2d / 3, random).ShouldBe(-1f, 1e-4f);

        var shifted = channel with { PhaseDegrees = 90, Amplitude = 2, Offset = 1 };
        WaveformMath.Sample(shifted, 1, 0, random).ShouldBe(1f + (2f * 0.96225f), 1e-4f);
    }

    [Fact]
    public void 波形数学_SPWM与SVPWM每个参数都应改变输出()
    {
        WaveformMath.TryParseKind("spwm", out var spwm).ShouldBeTrue();
        spwm.ShouldBe(WaveformKind.Spwm);
        WaveformMath.TryParseKind("svpwm", out var svpwm).ShouldBeTrue();
        svpwm.ShouldBe(WaveformKind.Svpwm);

        var baseChannel = new GeneratorChannelConfig
        {
            Kind = WaveformKind.Spwm,
            CarrierFrequencyHz = 100,
            ModulationIndex = 0.8,
            CarrierPhaseDegrees = 0,
        };

        var samples = new HashSet<float>();
        var random = new Random(1);
        for (var index = 0; index < 200; index++)
        {
            samples.Add(WaveformMath.Sample(baseChannel, 10, index / 1000d, random));
        }

        samples.ShouldContain(1f);
        samples.ShouldContain(-1f);
        Differs(baseChannel with { ModulationIndex = 0.1 }, baseChannel).ShouldBeTrue();
        Differs(baseChannel with { CarrierFrequencyHz = 300 }, baseChannel).ShouldBeTrue();
        Differs(baseChannel with { CarrierPhaseDegrees = 90 }, baseChannel).ShouldBeTrue();

        var svpwmChannel = baseChannel with { Kind = WaveformKind.Svpwm };
        for (var index = 0; index < 200; index++)
        {
            WaveformMath.Sample(svpwmChannel, 10, index / 1000d, random).ShouldBeInRange(-1f, 1f);
        }
    }

    [Fact]
    public void 发生器_一次性按周期应发完计算出的帧数()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueCommand(WaveformGeneratorModule.SendOnceCommand);
        module.PumpOnce(context);

        // 默认：10 Hz、100 点/周期 ⇒ 1000 Hz；5 周期 ⇒ 500 帧。
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(500));

        context.Sent.Count.ShouldBe(500);
        var frame = context.Sent[0].Payload;
        WaveformFrameReader.TryRead(frame, out var first, out _).ShouldBeTrue();
        first.GroupId.ShouldBe(1u);
        first.ChannelCount.ShouldBe(1);
        first.Sequence.ShouldBe(0u);
        first.ValidMask.ShouldBe(1UL);

        WaveformFrameReader.TryRead(context.Sent[^1].Payload, out var last, out _).ShouldBeTrue();
        last.Sequence.ShouldBe(499u);
    }

    [Fact]
    public void 发生器_重复一次性发送应从相位零重新开始而序号继续()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueCommand(WaveformGeneratorModule.SendOnceCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(500));
        context.Sent.Count.ShouldBe(500);

        context.EnqueueCommand(WaveformGeneratorModule.SendOnceCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(1000));
        context.Sent.Count.ShouldBe(1000);

        var firstSample = ReadSample(context.Sent[0].Payload);
        var secondRunFirstSample = ReadSample(context.Sent[500].Payload);
        secondRunFirstSample.ShouldBe(firstSample);
        WaveformFrameReader.TryRead(context.Sent[500].Payload, out var second, out _).ShouldBeTrue();
        second.Sequence.ShouldBe(500u);
    }

    [Fact]
    public void 发生器_禁用组不应发送()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 1,
                    Enabled = false,
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig()],
                },
            ],
            OneShotPeriods = 5,
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        context.EnqueueCommand(WaveformGeneratorModule.SendOnceCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(500));

        context.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void 发生器_载波超过奈奎斯特应拒绝发送并说明()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 1,
                    Resolution = 100,
                    Channels =
                    [
                        new GeneratorChannelConfig
                        {
                            Kind = WaveformKind.Spwm,
                            CarrierFrequencyHz = 10_000,
                            ModulationIndex = 0.8,
                        },
                    ],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        context.EnqueueCommand(WaveformGeneratorModule.SendOnceCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(500));

        context.Sent.ShouldBeEmpty();
        context.SetTexts.ShouldContain(item =>
            item.SourceName == WaveformGeneratorModule.StatusSource
            && item.Text.Contains("载波", StringComparison.Ordinal)); // i18n-test-data: 断言状态句包含载波关键词
    }

    [Fact]
    public void 显示器_应发现组并发布范围内全部点的快照()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 9, sequence: 0, values: [1.5f]);
        Deliver(context, groupId: 9, sequence: 1, values: [2.5f]);
        Deliver(context, groupId: 9, sequence: 2, values: [3.5f]);
        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(2);
        snapshot.Channels.Count.ShouldBe(1);
        snapshot.Channels[0].Values.ShouldBe([1.5f, 2.5f, 3.5f]);

        BinaryPrimitives.ReadUInt64LittleEndian(context.Published[WaveformDisplayModule.LatestIndexSource][^1])
            .ShouldBe(2UL);
    }

    [Fact]
    public void 显示器_选择范围应改变快照与局部保存而不影响全部保存()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        for (var index = 1; index <= 10; index++)
        {
            Deliver(context, groupId: 9, sequence: (uint)(index - 1), values: [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":2,\"end\":5}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(2);
        snapshot.XRangeEnd.ShouldBe(5);
        snapshot.Channels[0].Values.ShouldBe([3f, 4f, 5f, 6f]);

        context.EnqueueCommand(WaveformDisplayModule.SaveSelectionCommand);
        module.PumpOnce(context);
        var path = Path.Combine(Path.GetTempPath(), "waveform-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            context.EnqueueInput(WaveformDisplayModule.SavePathSource, path);
            module.PumpOnce(context);

            var lines = File.ReadAllLines(path);
            lines.Length.ShouldBe(5); // header + 4 rows
            lines[1].ShouldStartWith("2,");
            lines[^1].ShouldStartWith("5,");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void 显示器_畸形帧只丢弃不打断()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.Deliver(WaveformDisplayModule.InPort, new byte[10]);
        module.OnStep(context, StepReason.Data);

        context.SetTexts.Any(item => item.SourceName == WaveformDisplayModule.StatusSource).ShouldBeTrue();
        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.ShouldBeEmpty();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(255);
    }

    [Fact]
    public void 显示器_关闭非活动组历史后切换不应带回旧数据()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig { KeepInactiveHistory = false });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 1, sequence: 0, values: [1f]);
        Deliver(context, groupId: 2, sequence: 0, values: [2f]);
        module.OnStep(context, StepReason.Data);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"selectGroup\",\"id\":2}");
        module.PumpOnce(context);

        BinaryPrimitives.ReadUInt64LittleEndian(
                context.Published[WaveformDisplayModule.LatestIndexSource][^1])
            .ShouldBe(0UL);
    }

    [Fact]
    public void 通道历史_分块增长与清空()
    {
        var history = new ChannelHistory();
        for (var index = 0; index < 9000; index++)
        {
            history.Append(index);
        }

        history.Count.ShouldBe(9000);
        history.EstimatedBytes.ShouldBeGreaterThanOrEqualTo(2L * 8192 * sizeof(float));

        var tail = new float[3];
        history.ReadTail(tail).ShouldBe(3);
        tail.ShouldBe([8997f, 8998f, 8999f]);

        history.Clear();
        history.Count.ShouldBe(0);
        history.EstimatedBytes.ShouldBe(0);
    }

    [Fact]
    public void 显示器_保存图片应把请求交给视图并接收结果()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 9, sequence: 0, values: [1f]);
        module.OnStep(context, StepReason.Data);

        context.EnqueueCommand(WaveformDisplayModule.SaveImageCommand);
        module.PumpOnce(context);
        var request = context.FilePickRequests.ShouldHaveSingleItem();
        request.SourceName.ShouldBe(WaveformDisplayModule.SavePathSource);

        context.EnqueueInput(WaveformDisplayModule.SavePathSource, "waveform.png");
        module.PumpOnce(context);

        var requestText = context.SetTexts
            .Last(item => item.SourceName == WaveformDisplayModule.ImageRequestSource)
            .Text;
        using var document = System.Text.Json.JsonDocument.Parse(requestText);
        var id = document.RootElement.GetProperty("id").GetInt64();
        document.RootElement.GetProperty("path").GetString().ShouldBe("waveform.png");

        context.EnqueueInput(
            WaveformDisplayModule.ImageResultSource,
            "{\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"ok\":true}");
        module.PumpOnce(context);

        context.SetTexts.ShouldContain(item =>
            item.SourceName == WaveformDisplayModule.StatusSource
            && item.Text.Contains("图片已保存", StringComparison.Ordinal)); // i18n-test-data: 断言状态句包含保存成功关键词
    }

    [Fact]
    public void 显示器_Y自适应动作应写回组配置()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 9, sequence: 0, values: [1f, 2f]);
        module.OnStep(context, StepReason.Data);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"fitY\",\"index\":1}");
        module.PumpOnce(context);

        context.SetTexts
            .Last(item => item.SourceName == WaveformDisplayModule.MetaSource)
            .Text.ShouldContain("\"yFitChannel\":1");

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"fitY\",\"index\":1}");
        module.PumpOnce(context);

        context.SetTexts
            .Last(item => item.SourceName == WaveformDisplayModule.MetaSource)
            .Text.ShouldContain("\"yFitChannel\":-1");
    }

    [Fact]
    public void 显示器_双通道应默认全部可见且可隐藏恢复()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 9, sequence: 0, values: [1f, 2f]);
        Deliver(context, groupId: 9, sequence: 1, values: [1.5f, 2.5f]);
        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(2);
        snapshot.Channels[0].Values.ShouldBe([1f, 1.5f]);
        snapshot.Channels[1].Values.ShouldBe([2f, 2.5f]);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setVisible\",\"index\":1,\"visible\":false}");
        module.PumpOnce(context);
        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(1);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setVisible\",\"index\":1,\"visible\":true}");
        module.PumpOnce(context);
        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(2);
    }

    [Fact]
    public void 显示器_Y自适应只增不减且显式适应才缩小()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 9, sequence: 0, values: [1f]);
        Deliver(context, groupId: 9, sequence: 1, values: [5f]);
        Deliver(context, groupId: 9, sequence: 2, values: [1f]);
        Deliver(context, groupId: 9, sequence: 3, values: [1f]);
        Deliver(context, groupId: 9, sequence: 4, values: [1f]);
        module.OnStep(context, StepReason.Data);

        var high = Snapshot(context).ShouldNotBeNull().YMax;
        high.ShouldBeGreaterThan(5);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":3,\"end\":4}");
        module.PumpOnce(context);
        Snapshot(context).ShouldNotBeNull().YMax.ShouldBe(high);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"fitY\",\"index\":-1}");
        module.PumpOnce(context);
        Snapshot(context).ShouldNotBeNull().YMax.ShouldBeLessThan(high);
    }

    [Fact]
    public void 发生器_连续发送命令应替代开关()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 1,
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig()],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        context.EnqueueCommand(WaveformGeneratorModule.StartContinuousCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(100));
        context.Sent.Count.ShouldBeGreaterThan(0);

        var sent = context.Sent.Count;
        context.EnqueueCommand(WaveformGeneratorModule.StopCommand);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(200));
        context.Sent.Count.ShouldBe(sent);
    }

    [Fact]
    public void 发生器_发送命令的可用状态应随发送变化()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 1,
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig()],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(true);
        LastEnabled(context, WaveformGeneratorModule.SendOnceCommand).ShouldBe(true);
        LastEnabled(context, WaveformGeneratorModule.StopCommand).ShouldBe(false);

        context.EnqueueCommand(WaveformGeneratorModule.StartContinuousCommand);
        module.PumpOnce(context);

        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(false);
        LastEnabled(context, WaveformGeneratorModule.SendOnceCommand).ShouldBe(false);
        LastEnabled(context, WaveformGeneratorModule.StopCommand).ShouldBe(true);

        context.EnqueueCommand(WaveformGeneratorModule.StopCommand);
        module.PumpOnce(context);

        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(true);
        LastEnabled(context, WaveformGeneratorModule.SendOnceCommand).ShouldBe(true);
        LastEnabled(context, WaveformGeneratorModule.StopCommand).ShouldBe(false);
    }

    private static bool? LastEnabled(TestModuleContext context, string command) =>
        context.AppearanceReports
            .LastOrDefault(report => string.Equals(report.SourceName, command, StringComparison.Ordinal)
                && report.Enabled is not null)
            .Enabled;

    [Fact]
    public void 发生器_组选中应显示组属性并可树形增删通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectGroup\",\"groupIndex\":0}");
        module.PumpOnce(context);

        var snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("activeChannelIndex").GetInt32().ShouldBe(-1);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"addChannel\",\"groupIndex\":0}");
        module.PumpOnce(context);

        snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("activeChannelIndex").GetInt32().ShouldBe(1);
        snapshot.GetProperty("groups")[0].GetProperty("channels").GetArrayLength().ShouldBe(2);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"removeChannel\",\"groupIndex\":0,\"index\":1}");
        module.PumpOnce(context);

        snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("groups")[0].GetProperty("channels").GetArrayLength().ShouldBe(1);
        snapshot.GetProperty("activeChannelIndex").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void 发生器_组频率与周期应更新组并共享给所有通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformGeneratorModule.FrequencySource, "25");
        module.OnStep(context, StepReason.Input);

        var snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("groups")[0].GetProperty("frequency").GetDouble().ShouldBe(25);
        snapshot.GetProperty("groups")[0].GetProperty("rate").GetDouble().ShouldBe(2500);
        snapshot.GetProperty("groups")[0].GetProperty("channels")[0].TryGetProperty("frequency", out _).ShouldBeFalse();

        context.ViewState.SetInputText(WaveformGeneratorModule.PeriodSource, "0.04");
        module.OnStep(context, StepReason.Input);

        snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("groups")[0].GetProperty("frequency").GetDouble().ShouldBe(25);
    }

    [Fact]
    public void 发生器_旧配置的通道频率应迁移为组频率()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<GeneratorConfig>(
            "{\"Groups\":[{\"GroupId\":1,\"Name\":\"G1\",\"Channels\":[{\"Name\":\"CH1\",\"FrequencyHz\":25,\"Resolution\":200}]}],\"OneShotPeriods\":5}",
            LegacyJsonOptions);
        old.ShouldNotBeNull();

        var context = new TestModuleContext();
        context.Configuration.Write(old!);
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        var snapshot = GeneratorSnapshot(context);
        snapshot.GetProperty("groups")[0].GetProperty("frequency").GetDouble().ShouldBe(25);
        snapshot.GetProperty("groups")[0].GetProperty("resolution").GetInt32().ShouldBe(200);
        snapshot.GetProperty("groups")[0].GetProperty("channels")[0].TryGetProperty("resolution", out _).ShouldBeFalse();

        var normalized = context.Configuration.Read<GeneratorConfig>();
        normalized.Groups[0].FrequencyHz.ShouldBe(25);
        normalized.Groups[0].Resolution.ShouldBe(200);
        normalized.Groups[0].Channels[0].LegacyFrequencyHz.ShouldBeNull();
        normalized.Groups[0].Channels[0].LegacyResolution.ShouldBeNull();
    }

    [Fact]
    public void 发生器_树形启用动作应同时更新组与通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"setGroupEnabled\",\"groupIndex\":0,\"enabled\":false}");
        module.PumpOnce(context);
        GeneratorSnapshot(context).GetProperty("groups")[0].GetProperty("enabled").GetBoolean().ShouldBeFalse();

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"setChannelEnabled\",\"groupIndex\":0,\"index\":0,\"enabled\":false}");
        module.PumpOnce(context);
        GeneratorSnapshot(context).GetProperty("groups")[0].GetProperty("channels")[0].GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void 显示器_清空应复位视口且下一帧继续跟随()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                Follow = false,
                FollowRangeSamples = 8,
            },
            Groups =
            [
                new DisplayGroupConfig
                {
                    GroupId = 1,
                    Name = "group-a",
                    Channels = [new DisplayChannelConfig { Name = "channel-a", ColorArgb = 0xFF112233 }],
                    YAuto = false,
                    YMin = 0,
                    YMax = 2,
                },
            ],
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        for (var index = 0; index < 40; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueCommand(WaveformDisplayModule.ClearCommand);
        module.PumpOnce(context);

        var cleared = Snapshot(context).ShouldNotBeNull();
        cleared.XRangeStart.ShouldBe(0);
        cleared.XRangeEnd.ShouldBe(255);
        cleared.YAuto.ShouldBeTrue();
        cleared.YMin.ShouldBe(-1);
        cleared.YMax.ShouldBe(1);
        cleared.Channels.ShouldBeEmpty();

        var config = context.Configuration.Read<DisplayConfig>();
        config.Settings.Follow.ShouldBeTrue();
        config.Settings.FollowRangeSamples.ShouldBe(256);
        var group = config.FindGroup(1).ShouldNotBeNull();
        group.Name.ShouldBe("group-a");
        group.YAuto.ShouldBeTrue();
        group.Channels[0].Name.ShouldBe("channel-a");
        group.Channels[0].ColorArgb.ShouldBe(0xFF112233u);

        for (var index = 0; index < 300; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        var next = Snapshot(context).ShouldNotBeNull();
        next.XRangeStart.ShouldBe(44);
        next.XRangeEnd.ShouldBe(299);
        next.YAuto.ShouldBeTrue();
        next.Channels.ShouldHaveSingleItem().Values.Length.ShouldBe(256);
    }

    [Fact]
    public void 显示器_显示复位应保留组与通道身份()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                Follow = false,
                FollowRangeSamples = 8,
                XAxisMode = DisplayXAxisMode.Time,
                XTickStep = 0.01,
                Cursors = false,
            },
            Groups =
            [
                new DisplayGroupConfig
                {
                    GroupId = 1,
                    Name = "group-a",
                    Channels = [new DisplayChannelConfig { Name = "channel-a", ColorArgb = 0xFF445566 }],
                    YAuto = false,
                    YMin = 0,
                    YMax = 2,
                    YFitChannel = 0,
                },
            ],
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 40; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueCommand(WaveformDisplayModule.ResetGroupCommand);
        module.PumpOnce(context);

        var config = context.Configuration.Read<DisplayConfig>();
        config.Settings.Follow.ShouldBeTrue();
        config.Settings.FollowRangeSamples.ShouldBe(256);
        config.Settings.XAxisMode.ShouldBe(DisplayXAxisMode.Index);
        config.Settings.XTickStep.ShouldBe(0);
        config.Settings.Cursors.ShouldBeTrue();
        var group = config.FindGroup(1).ShouldNotBeNull();
        group.Name.ShouldBe("group-a");
        group.Channels[0].Name.ShouldBe("channel-a");
        group.Channels[0].ColorArgb.ShouldBe(0xFF445566u);
        group.YAuto.ShouldBeTrue();
        group.YMin.ShouldBe(-1);
        group.YMax.ShouldBe(1);
        group.YFitChannel.ShouldBe(-1);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(39);
        snapshot.YAuto.ShouldBeTrue();
    }

    [Fact]
    public void 显示器_通道数减少应保留历史并标记缺失()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [1f, 10f]);
        Deliver(context, 1, 1, [2f, 20f]);
        Deliver(context, 1, 2, [3f]);
        Deliver(context, 1, 3, [4f]);
        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(3);
        snapshot.Channels.ShouldHaveSingleItem().Index.ShouldBe(0);
        snapshot.Channels[0].Values.ShouldBe([1f, 2f, 3f, 4f]);

        var missingMeta = DisplayMeta(context);
        var missingChannels = missingMeta.GetProperty("channels");
        missingChannels.GetArrayLength().ShouldBe(2);
        missingChannels[1].GetProperty("missing").GetBoolean().ShouldBeTrue();

        Deliver(context, 1, 4, [5f, 50f]);
        module.OnStep(context, StepReason.Data);

        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(2);
        snapshot.Channels[1].Index.ShouldBe(1);
        snapshot.Channels[1].Values[0].ShouldBe(10f);
        snapshot.Channels[1].Values[1].ShouldBe(20f);
        float.IsNaN(snapshot.Channels[1].Values[2]).ShouldBeTrue();
        float.IsNaN(snapshot.Channels[1].Values[3]).ShouldBeTrue();
        snapshot.Channels[1].Values[4].ShouldBe(50f);

        var restoredMeta = DisplayMeta(context);
        restoredMeta.GetProperty("channels")[1].GetProperty("missing").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void 显示器_通道数增加应从新通道出现位置开始()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [1f]);
        Deliver(context, 1, 1, [2f]);
        module.OnStep(context, StepReason.Data);

        Deliver(context, 1, 2, [3f, 30f]);
        Deliver(context, 1, 3, [4f, 40f]);
        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(2);
        snapshot.Channels[0].Values.ShouldBe([1f, 2f, 3f, 4f]);
        snapshot.Channels[1].Index.ShouldBe(1);
        snapshot.Channels[1].Values.Length.ShouldBe(4);
        float.IsNaN(snapshot.Channels[1].Values[0]).ShouldBeTrue();
        float.IsNaN(snapshot.Channels[1].Values[1]).ShouldBeTrue();
        snapshot.Channels[1].Values[2].ShouldBe(30f);
        snapshot.Channels[1].Values[3].ShouldBe(40f);
    }

    [Fact]
    public void 显示器_删除通道应清空该通道并保留其他通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [1f, 10f]);
        Deliver(context, 1, 1, [2f, 20f]);
        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"deleteChannel\",\"index\":0}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.ShouldHaveSingleItem().Index.ShouldBe(1);
        snapshot.Channels[0].Values.ShouldBe([10f, 20f]);

        var config = context.Configuration.Read<DisplayConfig>();
        config.FindGroup(1).ShouldNotBeNull().Channels[0].Deleted.ShouldBeTrue();
        config.ActiveChannelIndex.ShouldBe(1);
        DisplayMeta(context).GetProperty("activeChannelIndex").GetInt32().ShouldBe(1);
        DisplayMeta(context).GetProperty("channels").GetArrayLength().ShouldBe(1);

        Deliver(context, 1, 2, [3f, 30f]);
        module.OnStep(context, StepReason.Data);
        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.ShouldHaveSingleItem().Index.ShouldBe(1);
        snapshot.Channels[0].Values.ShouldBe([10f, 20f, 30f]);
    }

    [Fact]
    public void 显示器_删除最后一个通道应移除组并按下一帧重建()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [1f]);
        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"deleteChannel\",\"index\":0}");
        module.PumpOnce(context);

        Snapshot(context).ShouldNotBeNull().Channels.ShouldBeEmpty();
        context.Configuration.Read<DisplayConfig>().Groups.ShouldBeEmpty();
        DisplayMeta(context).GetProperty("groups").GetArrayLength().ShouldBe(0);

        Deliver(context, 1, 1, [2f]);
        module.OnStep(context, StepReason.Data);
        var rebuilt = Snapshot(context).ShouldNotBeNull();
        rebuilt.Channels.ShouldHaveSingleItem().Index.ShouldBe(0);
        rebuilt.Channels[0].Values.ShouldBe([2f]);
        context.Configuration.Read<DisplayConfig>().Groups.Count.ShouldBe(1);
    }

    [Fact]
    public void 显示器_左键拖动应平移视口并关闭对应自动轴()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 100; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"pan\",\"xStart\":20,\"xEnd\":49,\"yMin\":2,\"yMax\":4}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(20);
        snapshot.XRangeEnd.ShouldBe(49);
        snapshot.YAuto.ShouldBeFalse();
        snapshot.YMin.ShouldBe(2);
        snapshot.YMax.ShouldBe(4);
        snapshot.Channels[0].Values.ShouldBe(Enumerable.Range(20, 30).Select(value => (float)value));

        var config = context.Configuration.Read<DisplayConfig>();
        config.Settings.Follow.ShouldBeFalse();
        config.Settings.FollowRangeSamples.ShouldBe(30);
        config.FindGroup(1).ShouldNotBeNull().YAuto.ShouldBeFalse();
    }

    [Fact]
    public void 显示器_右键缩放应绕中心改变跨度并保持未变化的轴()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings { Follow = false },
            Groups =
            [
                new DisplayGroupConfig
                {
                    GroupId = 1,
                    Channels = [new DisplayChannelConfig()],
                },
            ],
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 100; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        var before = Snapshot(context).ShouldNotBeNull();
        before.YAuto.ShouldBeTrue();
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"zoom\",\"xStart\":25,\"xEnd\":74,"
            + "\"yMin\":" + before.YMin.ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMax\":" + before.YMax.ToString(CultureInfo.InvariantCulture) + "}");
        module.PumpOnce(context);

        var zoomed = Snapshot(context).ShouldNotBeNull();
        zoomed.XRangeStart.ShouldBe(25);
        zoomed.XRangeEnd.ShouldBe(74);
        zoomed.YAuto.ShouldBeTrue();

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"zoom\",\"xStart\":25,\"xEnd\":74,"
            + "\"yMin\":" + (zoomed.YMin + 1).ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMax\":" + (zoomed.YMax - 1).ToString(CultureInfo.InvariantCulture) + "}");
        module.PumpOnce(context);

        var vertical = Snapshot(context).ShouldNotBeNull();
        vertical.XRangeStart.ShouldBe(25);
        vertical.XRangeEnd.ShouldBe(74);
        vertical.YAuto.ShouldBeFalse();
        vertical.YMin.ShouldBe(zoomed.YMin + 1);
        vertical.YMax.ShouldBe(zoomed.YMax - 1);
    }

    [Fact]
    public void 显示器_跟踪开启时横向缩放应保持跟踪并更新宽度()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 100; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        var before = Snapshot(context).ShouldNotBeNull();
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"zoom\",\"xStart\":25,\"xEnd\":74,"
            + "\"yMin\":" + before.YMin.ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMax\":" + before.YMax.ToString(CultureInfo.InvariantCulture) + "}");
        module.PumpOnce(context);

        var zoomed = Snapshot(context).ShouldNotBeNull();
        zoomed.XRangeStart.ShouldBe(50);
        zoomed.XRangeEnd.ShouldBe(99);
        var config = context.Configuration.Read<DisplayConfig>();
        config.Settings.Follow.ShouldBeTrue();
        config.Settings.FollowRangeSamples.ShouldBe(50);
    }

    [Fact]
    public void 显示器_无数据时应发布可调初始视口()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.ShouldBeEmpty();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(255);
        snapshot.YMin.ShouldBe(-1);
        snapshot.YMax.ShouldBe(1);
        snapshot.YAuto.ShouldBeTrue();

        context.ViewState.SetInputText(WaveformDisplayModule.XStartSource, "10");
        context.ViewState.SetInputText(WaveformDisplayModule.XEndSource, "20");
        module.OnStep(context, StepReason.Input);

        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(10);
        snapshot.XRangeEnd.ShouldBe(20);

        context.EnqueueInput(WaveformDisplayModule.YAutoSource, "false");
        module.PumpOnce(context);
        context.ViewState.SetInputText(WaveformDisplayModule.YMinSource, "0");
        context.ViewState.SetInputText(WaveformDisplayModule.YMaxSource, "2");
        module.OnStep(context, StepReason.Input);

        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.YAuto.ShouldBeFalse();
        snapshot.YMin.ShouldBe(0);
        snapshot.YMax.ShouldBe(2);
    }

    [Fact]
    public void 显示器_输入应逐键生效而不是等待提交()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformDisplayModule.XTickSource, "0.5");
        module.OnStep(context, StepReason.Input);

        // 没有 EnqueueInput / SubmitInput；仅靠 ViewState 的逐键文本。
        context.SetTexts.ShouldContain(item =>
            item.SourceName == WaveformDisplayModule.MetaSource
            && item.Text.Contains("\"xTick\":0.5", StringComparison.Ordinal));
    }

    [Fact]
    public void 显示器_跟踪范围输入应更新无数据默认窗口()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformDisplayModule.FollowRangeSource, "10");
        module.OnStep(context, StepReason.Input);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(9);
        context.SetTexts.ShouldContain(item =>
            item.SourceName == WaveformDisplayModule.MetaSource
            && item.Text.Contains("\"followRange\":10", StringComparison.Ordinal));
    }

    [Fact]
    public void 显示器_自定义频率应覆盖帧声明并驱动时间换算()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                XAxisMode = DisplayXAxisMode.Time,
                UseCustomRate = true,
                CustomRateHz = 500,
                FollowRangeSamples = 100,
            },
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 200; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.RateHz.ShouldBe(500f);
        snapshot.XRangeStart.ShouldBe(100);
        snapshot.XRangeEnd.ShouldBe(199);
        context.ViewState.GetInputText(WaveformDisplayModule.CustomRateSource).ShouldBe("500");
        context.ViewState.GetInputText(WaveformDisplayModule.XEndSource).ShouldBe("0.398");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("0.2");

        context.EnqueueInput(WaveformDisplayModule.UseCustomRateSource, "false");
        module.PumpOnce(context);
        Snapshot(context).ShouldNotBeNull().RateHz.ShouldBe(1000f);
    }

    [Fact]
    public void 显示器_轴模式切换应协同换算X刻度与跟踪范围()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                XAxisMode = DisplayXAxisMode.Index,
                UseCustomRate = true,
                CustomRateHz = 1000,
                FollowRangeSamples = 100,
                XTickStep = 100,
            },
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.GetInputText(WaveformDisplayModule.XTickSource).ShouldBe("100");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("100");

        context.EnqueueInput(WaveformDisplayModule.XAxisModeSource, "time");
        module.PumpOnce(context);
        context.ViewState.GetInputText(WaveformDisplayModule.XTickSource).ShouldBe("0.1");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("0.1");

        context.EnqueueInput(WaveformDisplayModule.XAxisModeSource, "index");
        module.PumpOnce(context);
        context.ViewState.GetInputText(WaveformDisplayModule.XTickSource).ShouldBe("100");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("100");
    }

    [Fact]
    public void 显示器_跟踪状态应只由最后一次操作决定()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                Follow = true,
                FollowRangeSamples = 5,
            },
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 10; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(5);
        Snapshot(context)!.XRangeEnd.ShouldBe(9);

        // 拖左端：只改宽度，跟踪保持；下一帧继续推进。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":2,\"end\":9,\"startOnly\":true}");
        module.PumpOnce(context);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(2);
        Snapshot(context)!.XRangeEnd.ShouldBe(9);
        Deliver(context, 1, 10, [10f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(3);
        Snapshot(context)!.XRangeEnd.ShouldBe(10);

        // X 起也是宽度意图：保持跟踪，宽度更新。
        context.ViewState.SetInputText(WaveformDisplayModule.XStartSource, "1");
        module.OnStep(context, StepReason.Input);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(1);
        Snapshot(context)!.XRangeEnd.ShouldBe(10);
        Deliver(context, 1, 11, [11f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(2);
        Snapshot(context)!.XRangeEnd.ShouldBe(11);

        // X 止是移动意图：关闭跟踪，下一帧不再推进。
        context.ViewState.SetInputText(WaveformDisplayModule.XEndSource, "8");
        module.OnStep(context, StepReason.Input);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(2);
        Snapshot(context)!.XRangeEnd.ShouldBe(8);
        Deliver(context, 1, 12, [12f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(2);
        Snapshot(context)!.XRangeEnd.ShouldBe(8);

        // 关闭状态下的 X 起仍是宽度意图，不会重新打开跟踪。
        context.ViewState.SetInputText(WaveformDisplayModule.XStartSource, "0");
        module.OnStep(context, StepReason.Input);
        Deliver(context, 1, 13, [13f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(0);
        Snapshot(context)!.XRangeEnd.ShouldBe(8);

        // 显式打开跟踪，使用最后一次宽度并贴 latest。
        context.EnqueueInput(WaveformDisplayModule.FollowSource, "true");
        module.PumpOnce(context);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(5);
        Snapshot(context)!.XRangeEnd.ShouldBe(13);
        Deliver(context, 1, 14, [14f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(6);
        Snapshot(context)!.XRangeEnd.ShouldBe(14);

        // 整体平移是移动意图：关闭跟踪；非法 X 起不改变状态。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":4,\"end\":9}");
        module.PumpOnce(context);
        context.ViewState.SetInputText(WaveformDisplayModule.XStartSource, "bad");
        module.OnStep(context, StepReason.Input);
        Deliver(context, 1, 15, [15f]);
        module.OnStep(context, StepReason.Data);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(4);
        Snapshot(context)!.XRangeEnd.ShouldBe(9);
    }

    [Fact]
    public void 显示器_修改跟踪范围不应改变跟踪状态()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            Settings = new DisplaySettings
            {
                Follow = true,
                FollowRangeSamples = 5,
            },
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 10; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.ViewState.SetInputText(WaveformDisplayModule.FollowRangeSource, "3");
        module.OnStep(context, StepReason.Input);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(7);
        Snapshot(context)!.XRangeEnd.ShouldBe(9);
        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("true");

        context.EnqueueInput(WaveformDisplayModule.FollowSource, "false");
        module.PumpOnce(context);
        context.ViewState.SetInputText(WaveformDisplayModule.FollowRangeSource, "2");
        module.OnStep(context, StepReason.Input);
        Snapshot(context).ShouldNotBeNull().XRangeStart.ShouldBe(7);
        Snapshot(context)!.XRangeEnd.ShouldBe(9);
        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("false");
    }

    [Fact]
    public void 显示器_非活动组历史按样本数限制且活动组不受限()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new DisplayConfig
        {
            KeepInactiveHistory = true,
            HistoryLimitSamples = 2,
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 1, sequence: 0, values: [1f]);
        module.OnStep(context, StepReason.Data);

        Deliver(context, groupId: 2, sequence: 0, values: [2f]);
        Deliver(context, groupId: 2, sequence: 1, values: [3f]);
        Deliver(context, groupId: 2, sequence: 2, values: [4f]);
        module.OnStep(context, StepReason.Data);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"selectGroup\",\"id\":2}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(1);
        snapshot.Channels[0].Values.ShouldBe([2f, 3f]);
        snapshot.XRangeEnd.ShouldBe(1);
    }

    [Fact]
    public void 显示器_刻度过密应标红并回退自动刻度()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(
            WaveformDisplayModule.ReadableTickLimitsSource,
            "{\"chartX\":4,\"chartY\":4,\"rangeX\":4}");
        context.ViewState.SetInputText(WaveformDisplayModule.XTickSource, "1");
        module.OnStep(context, StepReason.Input);

        context.AppearanceReports.ShouldContain(item =>
            item.SourceName == WaveformDisplayModule.XTickSource
            && item.OutlineRole == WaveformDisplayModule.AlertRole);

        // 缩小到 0..2 后 2/1 不再超过上限。
        context.ViewState.SetInputText(WaveformDisplayModule.XStartSource, "0");
        context.ViewState.SetInputText(WaveformDisplayModule.XEndSource, "2");
        module.OnStep(context, StepReason.Input);

        context.AppearanceReports.Last(item => item.SourceName == WaveformDisplayModule.XTickSource)
            .OutlineRole.ShouldBeNull();
    }

    [Fact]
    public void 发生器_逐键输入非法应标红合法应清除()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformGeneratorModule.FrequencySource, "abc");
        module.OnStep(context, StepReason.Input);

        context.AppearanceReports.ShouldContain(item =>
            item.SourceName == WaveformGeneratorModule.FrequencySource
            && item.OutlineRole == WaveformGeneratorModule.AlertRole);

        context.ViewState.SetInputText(WaveformGeneratorModule.FrequencySource, "10");
        module.OnStep(context, StepReason.Input);

        context.AppearanceReports.Last(item => item.SourceName == WaveformGeneratorModule.FrequencySource)
            .OutlineRole.ShouldBeNull();
    }

    private static System.Text.Json.JsonElement GeneratorSnapshot(TestModuleContext context)
    {
        var text = context.SetTexts
            .Last(item => item.SourceName == WaveformGeneratorModule.SnapshotSource)
            .Text;
        using var document = System.Text.Json.JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Fact]
    public void 图表数学_刻度映射与夹紧()
    {
        ChartScale.NiceStep(100, 10).ShouldBe(10);
        ChartScale.NiceStep(10, 10).ShouldBe(1);
        ChartScale.Map(5, 0, 10, 0, 100).ShouldBe(50);
        ChartScale.Unmap(50, 0, 10, 0, 100).ShouldBe(5);
        ChartScale.Clamp(12, 0, 10).ShouldBe(10);
        ChartScale.Clamp(-2, 0, 10).ShouldBe(0);
    }

    [Fact]
    public void 图表数学_拖动应平移范围并绕中心缩放()
    {
        var pan = ChartScale.Pan(10, 20, 5);
        pan.Min.ShouldBe(15);
        pan.Max.ShouldBe(25);

        var zoomIn = ChartScale.Zoom(-1, 1, 100, 500);
        ((zoomIn.Min + zoomIn.Max) / 2).ShouldBe(0, 1e-12);
        (zoomIn.Max - zoomIn.Min).ShouldBeLessThan(2);

        var zoomOut = ChartScale.Zoom(-1, 1, -100, 500);
        ((zoomOut.Min + zoomOut.Max) / 2).ShouldBe(0, 1e-12);
        (zoomOut.Max - zoomOut.Min).ShouldBeGreaterThan(2);

        var unchanged = ChartScale.Zoom(10, 20, 0, 500);
        unchanged.Min.ShouldBe(10);
        unchanged.Max.ShouldBe(20);
    }

    [Fact]
    public void 轴格式_小范围增加精度且大数值收敛宽度()
    {
        ChartAxisFormatter.DecimalsForStep(1000).ShouldBe(0);
        ChartAxisFormatter.DecimalsForStep(0.001).ShouldBeGreaterThanOrEqualTo(3);

        ChartAxisFormatter.Format(0.0001234, ChartAxisKind.Time, 0.00001, 20)
            .ShouldContain("0.00012");
        ChartAxisFormatter.Format(1_234_567, ChartAxisKind.Time, 1000, 6)
            .Length.ShouldBeLessThanOrEqualTo(6);
        ChartAxisFormatter.Format(1_234_567, ChartAxisKind.Index, 1000, 4)
            .ShouldNotContain(".");
    }

    private static SeriesWindowSnapshot? Snapshot(TestModuleContext context) =>
        context.ViewState.ReadSeriesWindow(WaveformDisplayModule.SeriesSource);

    private static System.Text.Json.JsonElement DisplayMeta(TestModuleContext context)
    {
        var text = context.SetTexts
            .Last(item => item.SourceName == WaveformDisplayModule.MetaSource)
            .Text;
        using var document = System.Text.Json.JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static float ReadSample(byte[] frame) =>
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(24));

    private static void Deliver(TestModuleContext context, uint groupId, uint sequence, float[] values)
    {
        var frame = new byte[280];
        var samples = new float[64];
        for (var index = 0; index < values.Length; index++)
        {
            samples[index] = values[index];
        }

        WaveformFrameWriter.Write(
            frame,
            groupId,
            sequence,
            1000,
            values.Length,
            (1UL << values.Length) - 1,
            samples);

        context.Deliver(WaveformDisplayModule.InPort, frame);
    }

    private static bool Differs(GeneratorChannelConfig left, GeneratorChannelConfig right)
    {
        var random = new Random(1);
        for (var index = 0; index < 400; index++)
        {
            var time = index / 1000d;
            if (WaveformMath.Sample(left, 10, time, random) != WaveformMath.Sample(right, 10, time, random))
            {
                return true;
            }
        }

        return false;
    }
}
