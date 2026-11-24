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
        var generator = ModuleContract.Probe(
            typeof(WaveformGeneratorModule),
            portSpecs: ModulePortSpecs.Read("WaveformGenerator", typeof(WaveformGeneratorModule).Assembly));
        var display = ModuleContract.Probe(
            typeof(WaveformDisplayModule),
            portSpecs: ModulePortSpecs.Read("WaveformDisplay", typeof(WaveformDisplayModule).Assembly));

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
    public void 显示器_自动按钮按数据量设置跟踪窗口并开启自适应()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        for (var index = 0; index < 600; index++)
        {
            Deliver(context, groupId: 9, sequence: (uint)index, values: [index]);
        }

        module.OnStep(context, StepReason.Data);

        context.EnqueueCommand(WaveformDisplayModule.AutoCommand);
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        (snapshot.XRangeEnd - snapshot.XRangeStart + 1).ShouldBe(300, "600 个样本按一半设置跟踪窗口");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("true");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("300");
        context.ViewState.GetInputText(WaveformDisplayModule.YAutoSource).ShouldBe("true");
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
        SeedSettings(context, new DisplaySettings { KeepInactiveHistory = false });

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

        // 丢弃最旧 8500 个后跨过首块：保留 8500..8999，读头与索引仍正确。
        history.DropOldest(8500);
        history.Count.ShouldBe(500);
        history.StartIndex.ShouldBe(8500);
        float.IsNaN(history[8499]).ShouldBeTrue();
        history[8500].ShouldBe(8500f);
        history[8999].ShouldBe(8999f);

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

    [Fact]
    public void 发生器_配置非法时发送按钮应禁用()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 0,
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig()],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(false);
        LastEnabled(context, WaveformGeneratorModule.SendOnceCommand).ShouldBe(false);
        context.ValueValidities[WaveformGeneratorModule.GroupIdSource].IsInvalid.ShouldBeTrue();

        context.ViewState.SetInputText(WaveformGeneratorModule.GroupIdSource, "1");
        module.PumpOnce(context);

        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(true);
        LastEnabled(context, WaveformGeneratorModule.SendOnceCommand).ShouldBe(true);
        context.ValueValidities.ContainsKey(WaveformGeneratorModule.GroupIdSource).ShouldBeFalse();
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
    public void 发生器_通道编号重复应给出具体原因_不是就绪()
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
                        new GeneratorChannelConfig { ChannelNumber = 1 },
                        new GeneratorChannelConfig { ChannelNumber = 2 },
                    ],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformGeneratorModule.ChannelNumberSource, "2");
        module.OnStep(context, StepReason.Input);

        var validity = context.ValueValidities[WaveformGeneratorModule.ChannelNumberSource];
        validity.IsInvalid.ShouldBeTrue();
        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(false);
    }

    [Fact]
    public void 发生器_切换组时旧错误值应提示未应用()
    {
        var context = new TestModuleContext();
        context.Configuration.Write(new GeneratorConfig
        {
            Groups =
            [
                new GeneratorGroupConfig
                {
                    GroupId = 1,
                    Name = "G1",
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig { ChannelNumber = 1 }],
                },
                new GeneratorGroupConfig
                {
                    GroupId = 2,
                    Name = "G2",
                    Resolution = 100,
                    Channels = [new GeneratorChannelConfig { ChannelNumber = 1 }],
                },
            ],
        });

        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.ViewState.SetInputText(WaveformGeneratorModule.GroupIdSource, "2");
        module.OnStep(context, StepReason.Input);
        context.ValueValidities[WaveformGeneratorModule.GroupIdSource].IsInvalid.ShouldBeTrue();

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectGroup\",\"groupIndex\":1}");
        module.PumpOnce(context);

        context.ValueValidities.ContainsKey(WaveformGeneratorModule.GroupIdSource).ShouldBeFalse();
        LastEnabled(context, WaveformGeneratorModule.StartContinuousCommand).ShouldBe(true);
    }

    [Fact]
    public void 发生器_不同通道参数应独立保存()
    {
        var context = new TestModuleContext();
        var module = new WaveformGeneratorModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"addChannel\",\"groupIndex\":0}");
        module.PumpOnce(context);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectChannel\",\"groupIndex\":0,\"index\":0}");
        module.PumpOnce(context);
        context.EnqueueInput(WaveformGeneratorModule.PhaseSource, "30");
        module.PumpOnce(context);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectChannel\",\"groupIndex\":0,\"index\":1}");
        module.PumpOnce(context);
        context.EnqueueInput(WaveformGeneratorModule.PhaseSource, "90");
        module.PumpOnce(context);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectChannel\",\"groupIndex\":0,\"index\":0}");
        module.PumpOnce(context);
        GeneratorSnapshot(context).GetProperty("groups")[0]
            .GetProperty("channels")[0].GetProperty("phase").GetDouble().ShouldBe(30);
        GeneratorSnapshot(context).GetProperty("groups")[0]
            .GetProperty("channels")[1].GetProperty("phase").GetDouble().ShouldBe(90);

        var config = context.Configuration.Read<GeneratorConfig>();
        config.Groups[0].Channels[0].PhaseDegrees.ShouldBe(30);
        config.Groups[0].Channels[1].PhaseDegrees.ShouldBe(90);
    }

    [Fact]
    public void 发生器_重新装载应恢复各通道独立参数()
    {
        var context = new TestModuleContext();
        var first = new WaveformGeneratorModule();
        first.OnRegistered(context);
        first.OnStep(context, StepReason.Start);

        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"addChannel\",\"groupIndex\":0}");
        first.PumpOnce(context);
        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectChannel\",\"groupIndex\":0,\"index\":0}");
        first.PumpOnce(context);
        context.EnqueueInput(WaveformGeneratorModule.AmplitudeSource, "2");
        first.PumpOnce(context);
        context.EnqueueInput(
            WaveformGeneratorModule.ActionSource,
            "{\"op\":\"selectChannel\",\"groupIndex\":0,\"index\":1}");
        first.PumpOnce(context);
        context.EnqueueInput(WaveformGeneratorModule.AmplitudeSource, "3");
        first.PumpOnce(context);

        var second = new WaveformGeneratorModule();
        second.OnRegistered(context);
        second.OnStep(context, StepReason.Start);

        var config = context.Configuration.Read<GeneratorConfig>();
        config.Groups[0].Channels[0].Amplitude.ShouldBe(2);
        config.Groups[0].Channels[1].Amplitude.ShouldBe(3);
        var channels = GeneratorSnapshot(context).GetProperty("groups")[0].GetProperty("channels");
        channels[0].GetProperty("amplitude").GetDouble().ShouldBe(2);
        channels[1].GetProperty("amplitude").GetDouble().ShouldBe(3);
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
        SeedSettings(context, new DisplaySettings
        {
            Follow = false,
            FollowRangeSamples = 8,
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        for (var index = 0; index < 40; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"renameGroup\",\"id\":1,\"name\":\"group-a\"}");
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"renameChannel\",\"index\":0,\"name\":\"channel-a\"}");
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"setColor\",\"index\":0,\"color\":\"#FF112233\"}");
        module.PumpOnce(context);
        context.EnqueueCommand(WaveformDisplayModule.ClearCommand);
        module.PumpOnce(context);

        var cleared = Snapshot(context).ShouldNotBeNull();
        cleared.XRangeStart.ShouldBe(0);
        cleared.XRangeEnd.ShouldBe(255);
        cleared.YAuto.ShouldBeTrue();
        cleared.YMin.ShouldBe(-1);
        cleared.YMax.ShouldBe(1);
        cleared.Channels.ShouldBeEmpty();

        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("true");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("256");
        var meta = DisplayMeta(context);
        var groupMeta = meta.GetProperty("groups")[0];
        groupMeta.GetProperty("name").GetString().ShouldBe("group-a");
        groupMeta.GetProperty("channels").GetInt32().ShouldBe(1);
        meta.GetProperty("channels")[0].GetProperty("name").GetString().ShouldBe("channel-a");

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
        SeedSettings(context, new DisplaySettings
        {
            Follow = false,
            FollowRangeSamples = 8,
            XAxisMode = DisplayXAxisMode.Time,
            XTickStep = 0.01,
            Cursors = false,
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 40; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"renameGroup\",\"id\":1,\"name\":\"group-a\"}");
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"renameChannel\",\"index\":0,\"name\":\"channel-a\"}");
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"setColor\",\"index\":0,\"color\":\"#FF445566\"}");
        module.PumpOnce(context);
        context.EnqueueCommand(WaveformDisplayModule.ResetGroupCommand);
        module.PumpOnce(context);

        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("true");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("256");
        context.ViewState.GetInputText(WaveformDisplayModule.XAxisModeSource).ShouldBe("index");
        context.ViewState.GetInputText(WaveformDisplayModule.XTickSource).ShouldBe("0");
        context.ViewState.GetInputText(WaveformDisplayModule.CursorsSource).ShouldBe("true");
        var groupMeta = DisplayMeta(context).GetProperty("groups")[0];
        groupMeta.GetProperty("name").GetString().ShouldBe("group-a");
        groupMeta.GetProperty("channels").GetInt32().ShouldBe(1);
        DisplayMeta(context).GetProperty("channels")[0].GetProperty("name").GetString().ShouldBe("channel-a");
        groupMeta.GetProperty("yAuto").GetBoolean().ShouldBeTrue();
        groupMeta.GetProperty("yMin").GetDouble().ShouldBe(-1);
        groupMeta.GetProperty("yMax").GetDouble().ShouldBe(1);
        groupMeta.GetProperty("yFitChannel").GetInt32().ShouldBe(-1);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(0);
        snapshot.XRangeEnd.ShouldBe(39);
        snapshot.YAuto.ShouldBeTrue();
    }

    [Fact]
    public void 显示器_首帧只物化有效掩码通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        DeliverMasked(context, 1, 0, 3, 0b101, [1f, 0f, 3f]);
        module.OnStep(context, StepReason.Data);

        var meta = DisplayMeta(context);
        var channels = meta.GetProperty("channels");
        channels.GetArrayLength().ShouldBe(2);
        channels[0].GetProperty("index").GetInt32().ShouldBe(0);
        channels[1].GetProperty("index").GetInt32().ShouldBe(2);
        meta.GetProperty("groups")[0].GetProperty("channels").GetInt32().ShouldBe(2);

        Snapshot(context)!.Channels.Count.ShouldBe(2);

        // 通道 2 首次有效后才出现；再次无效时保留为缺失。
        DeliverMasked(context, 1, 1, 3, 0b111, [1f, 2f, 3f]);
        module.OnStep(context, StepReason.Data);
        channels = DisplayMeta(context).GetProperty("channels");
        channels.GetArrayLength().ShouldBe(3);
        channels[1].GetProperty("index").GetInt32().ShouldBe(1);
        channels[1].GetProperty("missing").GetBoolean().ShouldBeFalse();

        DeliverMasked(context, 1, 2, 3, 0b101, [4f, 0f, 5f]);
        module.OnStep(context, StepReason.Data);
        channels = DisplayMeta(context).GetProperty("channels");
        channels.GetArrayLength().ShouldBe(3);
        channels[1].GetProperty("missing").GetBoolean().ShouldBeTrue();
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

        DisplayMeta(context).GetProperty("activeChannelIndex").GetInt32().ShouldBe(1);
        DisplayMeta(context).GetProperty("channels").GetArrayLength().ShouldBe(1);

        // 数据帧仍报告 2 个通道 ⇒ 删除只移出本次显示，下一帧恢复槽位（不是永久消失）。
        Deliver(context, 1, 2, [3f, 30f]);
        module.OnStep(context, StepReason.Data);
        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.Count.ShouldBe(2);
        snapshot.Channels[0].Index.ShouldBe(0);
        snapshot.Channels[0].Values[^1].ShouldBe(3f, "删除清掉历史后，下一帧的新样本仍应进入该槽位");
        snapshot.Channels[1].Index.ShouldBe(1);
        snapshot.Channels[1].Values.ShouldBe([10f, 20f, 30f]);
        DisplayMeta(context).GetProperty("channels").GetArrayLength().ShouldBe(2);
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
        DisplayMeta(context).GetProperty("groups").GetArrayLength().ShouldBe(0);

        Deliver(context, 1, 1, [2f]);
        module.OnStep(context, StepReason.Data);
        var rebuilt = Snapshot(context).ShouldNotBeNull();
        rebuilt.Channels.ShouldHaveSingleItem().Index.ShouldBe(0);
        rebuilt.Channels[0].Values.ShouldBe([2f]);
        DisplayMeta(context).GetProperty("groups").GetArrayLength().ShouldBe(1);
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

        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("false");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("30");
        snapshot.YAuto.ShouldBeFalse();
    }

    [Fact]
    public void 显示器_右键缩放应绕中心改变跨度并保持未变化的轴()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings { Follow = false });

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
        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("true");
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("50");
    }

    [Fact]
    public void 显示器_时间模式范围动作应只换算一次()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings
        {
            XAxisMode = DisplayXAxisMode.Time,
            UseCustomRate = true,
            CustomRateHz = 1000,
            Follow = false,
            FollowRangeSamples = 256,
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        for (var index = 0; index < 1000; index++)
        {
            Deliver(context, 1, (uint)index, [index]);
        }

        module.OnStep(context, StepReason.Data);
        var before = Snapshot(context).ShouldNotBeNull();

        // 数据轴选择也提交轴单位；模块只换算一次。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":0.2,\"end\":0.249}");
        module.PumpOnce(context);

        var selected = Snapshot(context).ShouldNotBeNull();
        selected.XRangeStart.ShouldBe(200);
        selected.XRangeEnd.ShouldBe(249);
        selected.Channels[0].Values.Length.ShouldBe(50);

        // 右键缩放由 ChartSurface 给轴单位，视图原样提交；模块只换算一次。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"zoom\",\"xStart\":0.3,\"xEnd\":0.349,"
            + "\"yMin\":" + before.YMin.ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMax\":" + before.YMax.ToString(CultureInfo.InvariantCulture) + "}");
        module.PumpOnce(context);

        var zoomed = Snapshot(context).ShouldNotBeNull();
        zoomed.XRangeStart.ShouldBe(300);
        zoomed.XRangeEnd.ShouldBe(349);
        zoomed.Channels[0].Values.Length.ShouldBe(50);
        context.ViewState.GetInputText(WaveformDisplayModule.FollowRangeSource).ShouldBe("0.05");
    }

    [Fact]
    public void 显示器_左键平移到数据边界应停住而不是缩小范围()
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
        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"setRange\",\"start\":10,\"end\":20}");
        module.PumpOnce(context);
        var before = Snapshot(context).ShouldNotBeNull();

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"pan\",\"xStart\":-10,\"xEnd\":0,"
            + "\"yMin\":" + before.YMin.ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMax\":" + before.YMax.ToString(CultureInfo.InvariantCulture) + "}");
        module.PumpOnce(context);

        var panned = Snapshot(context).ShouldNotBeNull();
        panned.XRangeStart.ShouldBe(0);
        panned.XRangeEnd.ShouldBe(10, "平移到边界只应停住，宽度保持 11 个样本");
    }

    [Fact]
    public void 显示器_滚轮同时缩放XY并关闭跟随()
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
            "{\"op\":\"dragView\",\"mode\":\"wheel\",\"xStart\":20,\"xEnd\":70,\"yMin\":-2,\"yMax\":2}");
        module.PumpOnce(context);

        var zoomed = Snapshot(context).ShouldNotBeNull();
        zoomed.XRangeStart.ShouldBe(20);
        zoomed.XRangeEnd.ShouldBe(70);
        zoomed.YMin.ShouldBe(-2);
        zoomed.YMax.ShouldBe(2);
        context.ViewState.GetInputText(WaveformDisplayModule.FollowSource).ShouldBe("false");
        context.ViewState.GetInputText(WaveformDisplayModule.YAutoSource).ShouldBe("false");
    }

    [Fact]
    public void 显示器_滚轮任一轴触界时两轴都不变()
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

        // X 起点越过 0：整次滚轮作废，不能出现"X 不动、Y 继续缩放"。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"wheel\",\"xStart\":-5,\"xEnd\":45,\"yMin\":-2,\"yMax\":2}");
        module.PumpOnce(context);

        var after = Snapshot(context).ShouldNotBeNull();
        after.XRangeStart.ShouldBe(before.XRangeStart);
        after.XRangeEnd.ShouldBe(before.XRangeEnd);
        after.YMin.ShouldBe(before.YMin);
        after.YMax.ShouldBe(before.YMax);
    }

    [Fact]
    public void 显示器_右键缩放后自适应第一次按下即选中该通道()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);
        Deliver(context, 1, 0, [1f, 10f]);
        Deliver(context, 1, 1, [2f, 20f]);
        module.OnStep(context, StepReason.Data);

        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"fitY\",\"index\":0}");
        module.PumpOnce(context);
        var fitted = DisplayMeta(context).GetProperty("groups")[0];
        fitted.GetProperty("yAuto").GetBoolean().ShouldBeTrue();
        fitted.GetProperty("yFitChannel").GetInt32().ShouldBe(0);

        // 右键竖向缩放关闭 Y 自动，并应清掉旧的自适应通道。
        var before = Snapshot(context).ShouldNotBeNull();
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"zoom\","
            + "\"xStart\":" + before.XRangeStart.ToString(CultureInfo.InvariantCulture) + ","
            + "\"xEnd\":" + before.XRangeEnd.ToString(CultureInfo.InvariantCulture) + ","
            + "\"yMin\":-5,\"yMax\":5}");
        module.PumpOnce(context);

        var manual = DisplayMeta(context).GetProperty("groups")[0];
        manual.GetProperty("yAuto").GetBoolean().ShouldBeFalse();
        manual.GetProperty("yFitChannel").GetInt32().ShouldBe(-1);

        context.EnqueueInput(WaveformDisplayModule.ActionSource, "{\"op\":\"fitY\",\"index\":0}");
        module.PumpOnce(context);
        var refitted = DisplayMeta(context).GetProperty("groups")[0];
        refitted.GetProperty("yAuto").GetBoolean().ShouldBeTrue();
        refitted.GetProperty("yFitChannel").GetInt32().ShouldBe(0, "第一次按下就应选中该通道");
    }

    [Fact]
    public void 显示器_通道行应发布窗口数据范围()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [1f, 10f]);
        Deliver(context, 1, 1, [3f, float.NaN]);
        Deliver(context, 1, 2, [-2f, 20f]);
        module.OnStep(context, StepReason.Data);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        var first = snapshot.Channels.Single(channel => channel.Index == 0);
        first.WindowMin.ShouldBe(-2f);
        first.WindowMax.ShouldBe(3f);

        var second = snapshot.Channels.Single(channel => channel.Index == 1);
        second.WindowMin.ShouldBe(10f);
        second.WindowMax.ShouldBe(20f);
    }

    [Fact]
    public void 显示器_暂停应冻结全部组且恢复不补帧不计丢帧()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, 1, 0, [0f]);
        Deliver(context, 1, 1, [1f]);
        Deliver(context, 1, 2, [2f]);
        module.OnStep(context, StepReason.Data);
        var before = Snapshot(context).ShouldNotBeNull();

        context.EnqueueInput(WaveformDisplayModule.PausedSource, "true");
        module.PumpOnce(context);

        // 暂停期间两个组都到达；端口排空，但不写历史、不发现新组。
        Deliver(context, 1, 3, [3f]);
        Deliver(context, 1, 4, [4f]);
        Deliver(context, 2, 0, [99f]);
        module.PumpOnce(context);

        Snapshot(context).ShouldBeSameAs(before);
        DisplayMeta(context).GetProperty("groups").GetArrayLength().ShouldBe(1);

        context.EnqueueInput(WaveformDisplayModule.PausedSource, "false");
        module.PumpOnce(context);
        Deliver(context, 1, 7, [7f]);
        Deliver(context, 2, 4, [99f]);
        module.PumpOnce(context);
        context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(600));

        var after = Snapshot(context).ShouldNotBeNull();
        after.Channels.ShouldHaveSingleItem().Values.ShouldBe([0f, 1f, 2f, 7f]);
        var groups = DisplayMeta(context).GetProperty("groups");
        groups.GetArrayLength().ShouldBe(2);
        groups[0].GetProperty("lost").GetInt64().ShouldBe(0);
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

        // 数据轴（范围选择器）走的是 setRange，不是曲线区 dragView；无数据时同样要生效。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"setRange\",\"start\":10,\"end\":19}");
        module.PumpOnce(context);
        snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.XRangeStart.ShouldBe(10);
        snapshot.XRangeEnd.ShouldBe(19);
    }

    [Fact]
    public void 显示器_只有设置值声明持久化_视口值不持久化()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);

        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.FollowSource)
            .Persistent.ShouldBeTrue();
        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.XAxisModeSource)
            .Persistent.ShouldBeTrue();
        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.CustomRateSource)
            .Persistent.ShouldBeTrue();
        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.KeepInactiveSource)
            .Persistent.ShouldBeTrue();

        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.XStartSource)
            .Persistent.ShouldBeFalse();
        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.YMinSource)
            .Persistent.ShouldBeFalse();
        context.DeclaredValues.Single(value => value.Name == WaveformDisplayModule.YAutoSource)
            .Persistent.ShouldBeFalse();
    }

    [Fact]
    public void 显示器_设置应由值恢复_组只在数据帧到达后出现()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings
        {
            Follow = false,
            FollowRangeSamples = 10,
            Cursors = false,
        });

        var first = new WaveformDisplayModule();
        first.OnRegistered(context);
        first.OnStep(context, StepReason.Start);

        var second = new WaveformDisplayModule();
        second.OnRegistered(context);
        second.OnStep(context, StepReason.Start);

        var meta = DisplayMeta(context);
        meta.GetProperty("groups").GetArrayLength().ShouldBe(0, "组是数据帧派生的运行期对象，不应从配置恢复");
        meta.GetProperty("settings").GetProperty("follow").GetBoolean().ShouldBeFalse();
        meta.GetProperty("settings").GetProperty("followRange").GetInt32().ShouldBe(10);
        meta.GetProperty("settings").GetProperty("cursors").GetBoolean().ShouldBeFalse();

        Deliver(context, 7, 0, [1f]);
        second.OnStep(context, StepReason.Data);
        DisplayMeta(context).GetProperty("groups").GetArrayLength().ShouldBe(1);
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
    public void 显示器_无数据拖动曲线区应更新默认视口()
    {
        var context = new TestModuleContext();
        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"dragView\",\"mode\":\"pan\",\"xStart\":10,\"xEnd\":19,\"yMin\":-2,\"yMax\":2}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels.ShouldBeEmpty();
        snapshot.XRangeStart.ShouldBe(10);
        snapshot.XRangeEnd.ShouldBe(19);
        snapshot.YAuto.ShouldBeFalse();
        snapshot.YMin.ShouldBe(-2);
        snapshot.YMax.ShouldBe(2);
        context.ViewState.GetInputText(WaveformDisplayModule.XStartSource).ShouldBe("10");
        context.ViewState.GetInputText(WaveformDisplayModule.XEndSource).ShouldBe("19");
        context.ViewState.GetInputText(WaveformDisplayModule.YMinSource).ShouldBe("-2");
        context.ViewState.GetInputText(WaveformDisplayModule.YMaxSource).ShouldBe("2");
    }

    [Fact]
    public void 显示器_自定义频率应覆盖帧声明并驱动时间换算()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings
        {
            XAxisMode = DisplayXAxisMode.Time,
            UseCustomRate = true,
            CustomRateHz = 500,
            FollowRangeSamples = 100,
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
        SeedSettings(context, new DisplaySettings
        {
            XAxisMode = DisplayXAxisMode.Index,
            UseCustomRate = true,
            CustomRateHz = 1000,
            FollowRangeSamples = 100,
            XTickStep = 100,
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
        SeedSettings(context, new DisplaySettings
        {
            Follow = true,
            FollowRangeSamples = 5,
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
    public void 显示器_非活动组历史按样本数限制且活动组不受限()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings
        {
            KeepInactiveHistory = true,
            HistoryLimitSamples = 2,
            FollowRangeSamples = 2,
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
        // 达到上限时丢弃最旧、保留最新：group2 的第 2 帧被丢，最后两帧仍在。
        snapshot.Channels[0].Values.ShouldBe([3f, 4f]);
        snapshot.XRangeEnd.ShouldBe(2);
    }

    [Fact]
    public void 显示器_切换活动组时旧活动组按上限保留最新()
    {
        var context = new TestModuleContext();
        SeedSettings(context, new DisplaySettings
        {
            KeepInactiveHistory = true,
            HistoryLimitSamples = 2,
            FollowRangeSamples = 2,
        });

        var module = new WaveformDisplayModule();
        module.OnRegistered(context);
        module.OnStep(context, StepReason.Start);

        Deliver(context, groupId: 1, sequence: 0, values: [1f]);
        Deliver(context, groupId: 1, sequence: 1, values: [2f]);
        Deliver(context, groupId: 1, sequence: 2, values: [3f]);
        Deliver(context, groupId: 1, sequence: 3, values: [4f]);
        module.OnStep(context, StepReason.Data);

        // 切到不存在的组：旧活动组立即变为非活动组，按上限裁到最新两帧。
        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"selectGroup\",\"id\":2}");
        module.PumpOnce(context);

        context.EnqueueInput(
            WaveformDisplayModule.ActionSource,
            "{\"op\":\"selectGroup\",\"id\":1}");
        module.PumpOnce(context);

        var snapshot = Snapshot(context).ShouldNotBeNull();
        snapshot.Channels[0].Values.ShouldBe([3f, 4f]);
        snapshot.XRangeEnd.ShouldBe(3);
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

    private static void SeedSettings(TestModuleContext context, DisplaySettings settings)
    {
        context.ViewState.SetInputText(
            WaveformDisplayModule.XAxisModeSource,
            settings.XAxisMode == DisplayXAxisMode.Time ? "time" : "index");
        context.ViewState.SetInputText(WaveformDisplayModule.FollowSource, settings.Follow ? "true" : "false");
        context.ViewState.SetInputText(
            WaveformDisplayModule.FollowRangeSource,
            settings.FollowRangeSamples.ToString(CultureInfo.InvariantCulture));
        context.ViewState.SetInputText(WaveformDisplayModule.CursorsSource, settings.Cursors ? "true" : "false");
        context.ViewState.SetInputText(
            WaveformDisplayModule.PausedSource,
            settings.Paused ? "true" : "false");
        context.ViewState.SetInputText(
            WaveformDisplayModule.UseCustomRateSource,
            settings.UseCustomRate ? "true" : "false");
        context.ViewState.SetInputText(
            WaveformDisplayModule.CustomRateSource,
            settings.CustomRateHz.ToString(CultureInfo.InvariantCulture));
        context.ViewState.SetInputText(
            WaveformDisplayModule.XTickSource,
            settings.XTickStep.ToString(CultureInfo.InvariantCulture));
        context.ViewState.SetInputText(
            WaveformDisplayModule.YTickSource,
            settings.YTickStep.ToString(CultureInfo.InvariantCulture));
        context.ViewState.SetInputText(
            WaveformDisplayModule.KeepInactiveSource,
            settings.KeepInactiveHistory ? "true" : "false");
        context.ViewState.SetInputText(
            WaveformDisplayModule.HistoryLimitSource,
            settings.HistoryLimitSamples.ToString(CultureInfo.InvariantCulture));
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

    private static void DeliverMasked(
        TestModuleContext context,
        uint groupId,
        uint sequence,
        int channelCount,
        ulong validMask,
        float[] values)
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
            channelCount,
            validMask,
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
