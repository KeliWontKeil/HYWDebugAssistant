using HYWDebugAssistant.Modules.Reference.Asserter;
using HYWDebugAssistant.Modules.Reference.DataGenerator;
using HYWDebugAssistant.Modules.Reference.Recorder;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **T6 契约测试套件**（`Sdk.Testing`）——docs/architecture/06 §6.5：模块作者在自己的测试项目里的用法示范。
/// </summary>
/// <remarks>
/// 两条用法：
/// <list type="number">
///   <item><b>契约自检</b>：<see cref="ModuleContract.CheckAll"/> 一行跑出声明面问题（不拉起框架）；</item>
///   <item><b>行为自检</b>：<see cref="TestModuleContext"/> 注入上下文，直接驱动模块的钩子与逻辑。</item>
/// </list>
/// </remarks>
public class ModuleContractTests
{
    // ------------------------------------------------------------ 契约自检

    [Fact]
    public void 三个参考模块的声明面应无契约问题()
    {
        var problems = ModuleContract.CheckAll(
            typeof(DataGeneratorModule),
            typeof(RecorderModule),
            typeof(AsserterModule),
            typeof(HYWDebugAssistant.Modules.Reference.ManualSender.ManualSenderModule),
            typeof(HYWDebugAssistant.Modules.Reference.Monitor.MonitorModule),
            typeof(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule));

        problems.ShouldBeEmpty(string.Join("；", problems));
    }

    [Fact]
    public void 参考模块_应声明同一个数据类型且形状一致()
    {
        // 【ADR-0046 D2/D6】六个参考模块都声明 `hyw/analog-sample@1` ⇒ 兼容判定走"**同标识同版本**"层；
        // 若谁的形状写歪了，这里立刻暴露（否则现场表现为"莫名其妙连不上"）。
        var expected = HYWDebugAssistant.Sdk.Contracts.CommonDataTypes.AnalogSampleShape;

        foreach (var type in new[]
                 {
                     typeof(DataGeneratorModule),
                     typeof(RecorderModule),
                     typeof(AsserterModule),
                     typeof(HYWDebugAssistant.Modules.Reference.ManualSender.ManualSenderModule),
                     typeof(HYWDebugAssistant.Modules.Reference.Monitor.MonitorModule),
                     typeof(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule),
                 })
        {
            var probe = ModuleContract.Probe(type);
            var analog = probe.DataTypes.SingleOrDefault(t =>
                t.TypeId == HYWDebugAssistant.Sdk.Contracts.CommonDataTypes.AnalogSampleTypeId && t.Version == 1);

            analog.ShouldNotBeNull($"{type.Name} 应声明 hyw/analog-sample@1");
            analog!.Shape.ShouldBe(expected, $"{type.Name} 的 hyw/analog-sample@1 形状必须与标准一致");
        }
    }

    [Fact]
    public void 多源融合器_三种策略应给出不同的合并结果()
    {
        // 【ADR-0050 M3/Q4】**合并由模块承担**：本模块是"两个发布者 → 一个结果"的参考实现
        var module = new HYWDebugAssistant.Modules.Reference.Fuser.FuserModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.DeclaredPorts.Count.ShouldBe(3, "两个输入口 + 一个输出口");
        context.DeclaredDataTypes.ShouldContain(t =>
            t.TypeId == HYWDebugAssistant.Sdk.Contracts.CommonDataTypes.AnalogSampleTypeId);

        // ① 按到达（默认）：后到的赢
        context.SetConfiguration(new HYWDebugAssistant.Modules.Reference.Fuser.FuserConfig
        {
            Mode = HYWDebugAssistant.Modules.Reference.Fuser.FusionMode.Latest,
        });
        module.OnActivated();

        module.Fuse(context, portIndex: 0, value: 10).ShouldBe(10);
        module.Fuse(context, portIndex: 1, value: 20).ShouldBe(20, "Latest = 后到的覆盖先到的");
        module.Winner.ShouldBe(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.WinnerSecond);

        // ② 固定优先级：1 路优先
        context.SetConfiguration(new HYWDebugAssistant.Modules.Reference.Fuser.FuserConfig
        {
            Mode = HYWDebugAssistant.Modules.Reference.Fuser.FusionMode.Priority,
        });
        module.OnConfigurationChanged();

        module.Fuse(context, portIndex: 1, value: 99).ShouldBe(10, "Priority = 1 路有数据就用 1 路");

        // ③ 平均：两路都有数据则取算术平均
        context.SetConfiguration(new HYWDebugAssistant.Modules.Reference.Fuser.FuserConfig
        {
            Mode = HYWDebugAssistant.Modules.Reference.Fuser.FusionMode.Average,
        });
        module.OnConfigurationChanged();

        module.Fuse(context, portIndex: 1, value: 30).ShouldBe(20, "Average = (10 + 30) / 2");
        module.Winner.ShouldBe(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.WinnerAverage);

        // 计数按**口**归属（这就是"多源用口区分来源"的落地方式）
        module.Count1.ShouldBe(1UL);
        module.Count2.ShouldBe(3UL);

        // 转发开关：融合值从输出口发给下游（下游看到的是**模块自己产出的**单元）
        module.Emitted.ShouldBe(4UL, "每次融合各转发一个单元（本用例调用了 4 次）");
        context.Sent.Count.ShouldBe(4);
        context.Sent[^1].PortName.ShouldBe(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.OutputPort);

        var reader = new PayloadReader(
            context.Sent[^1].Payload,
            HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.UnitLayout);
        reader.ReadF32("value").ShouldBe(20f);
        reader.ReadU64("seq").ShouldBe(4UL, "序号由模块自己维护（框架不代管）");    }

    [Fact]
    public void 多源融合器_转发关闭时不应产生输出单元()
    {
        var module = new HYWDebugAssistant.Modules.Reference.Fuser.FuserModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new HYWDebugAssistant.Modules.Reference.Fuser.FuserConfig
        {
            ForwardMerged = false,
        });
        module.OnActivated();

        module.Fuse(context, portIndex: 0, value: 1);

        module.Emitted.ShouldBe(0UL);
        context.Sent.ShouldBeEmpty("转发关闭 = 只在界面显示，不产生输出");
        context.Published.ShouldContainKey(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.FusedSource);
    }

    [Fact]
    public void 契约自检_应能发现重复口名与未声明数据源()
    {
        var problems = ModuleContract.CheckAll(typeof(BrokenModule)).ToList();

        problems.ShouldContain(p => p.Contains("口名重复", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未声明的数据源", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未给出 ValueType", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 行为自检（模块级，无需框架）

    [Fact]
    public void 发生器_波形应由序号决定且可复现()
    {
        var config = new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Sine,
            Frequency = 1.0,
            Amplitude = 2.0,
            Offset = 0.5,
            PeriodMs = 10,
        };

        var first = new Random(config.Seed);
        var second = new Random(config.Seed);

        for (ulong sequence = 0; sequence < 16; sequence++)
        {
            DataGeneratorModule.NextValue(config, sequence, first)
                .ShouldBe(DataGeneratorModule.NextValue(config, sequence, second));
        }

        // 常量波形 = 偏置（与序号无关）
        var constant = new DataGeneratorConfig { Waveform = GeneratorWaveform.Constant, Offset = 0.25 };
        var random = new Random(1);
        DataGeneratorModule.NextValue(constant, 0, random).ShouldBe(0.25);
        DataGeneratorModule.NextValue(constant, 12345, random).ShouldBe(0.25);

        // 正弦在 1/4 周期处达到峰值（幅值 2 → 0.5 + 2 = 2.5）
        var quarter = new DataGeneratorConfig { Waveform = GeneratorWaveform.Sine, Frequency = 1.0, Amplitude = 2.0, Offset = 0.5, PeriodMs = 10 };
        DataGeneratorModule.NextValue(quarter, 25, new Random(1)).ShouldBe(2.5, 1e-9);
    }

    [Fact]
    public void 记录器_应记录并断言序号单调与值域()
    {
        var module = new RecorderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.DeclaredPorts.ShouldHaveSingleItem().Name.ShouldBe(RecorderModule.InputPort);
        context.DeclaredView.ShouldNotBeNull();

        context.SetConfiguration(new RecorderConfig { ExpectedMin = 0, ExpectedMax = 1, MaxIssues = 2 });
        module.OnActivated();

        module.Record(context, 1, 0.5f);
        module.Record(context, 2, 0.75f);
        module.Record(context, 2, 2.0f);   // 序号未递增 + 值超上限
        module.Record(context, 4, 0.2f);

        module.ReceivedCount.ShouldBe(4UL);
        module.LastValue.ShouldBe(0.2, 1e-6);
        module.MinValue.ShouldBe(0.2, 1e-6);
        module.MaxValue.ShouldBe(2.0, 1e-6);
        module.SequenceViolations.ShouldBe(1);
        module.RangeViolations.ShouldBe(1);

        // 违规上报（受上限约束，不刷屏）
        context.ReportedIssues.ShouldContain(i => i.Message.Contains("序号非单调", StringComparison.Ordinal));
        context.ReportedIssues.ShouldContain(i => i.Message.Contains("值超出期望范围", StringComparison.Ordinal));

        // 触发上限后不再增加（MaxIssues = 2）
        var reported = context.ReportedIssues.Count;
        module.Record(context, 5, 3.0f);
        module.Record(context, 6, 4.0f);
        context.ReportedIssues.Count.ShouldBeLessThanOrEqualTo(
            reported + 1,
            "同类问题超过上限后不刷屏；实际上报：" + string.Join(" | ", context.ReportedIssues.Select(i => i.Message)));
    }

    [Fact]
    public void 断言器_应给出偏差并上报超差()
    {
        var module = new AsserterModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new AsserterConfig { Expected = 1.0, Tolerance = 0.01, MaxIssues = 1 });
        module.OnActivated();

        module.Assert(context, 1.005f).ShouldBe(0.005, 1e-6);
        module.Assert(context, 1.5f).ShouldBe(0.5, 1e-6);

        module.PassedCount.ShouldBe(1UL);
        module.FailedCount.ShouldBe(1UL);
        module.LastDeviation.ShouldBe(0.5, 1e-6);
        context.ReportedIssues.ShouldContain(i =>
            i.Message.Contains("值超出容差", StringComparison.Ordinal) && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void 发生器_在测试上下文中应发出契约单元并写入订阅()
    {
        var module = new DataGeneratorModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Constant,
            Offset = 0.5,
            PeriodMs = 1,
        });
        module.OnActivated();

        // 活动体在测试上下文里跑一小段后请求停止（不用框架线程）
        var runner = new Thread(() =>
        {
            module.RunActivity(context);
        });

        runner.Start();
        WaitUntil(() => context.Published.TryGetValue(DataGeneratorModule.ValueSource, out var list) && list.Count > 3, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("发生器应在测试上下文里写入订阅样本");
        context.RequestStop();
        runner.Join(TimeSpan.FromSeconds(2)).ShouldBeTrue();

        context.Sent.ShouldNotBeEmpty("应发出契约数据单元");
        context.Sent[0].PortName.ShouldBe(DataGeneratorModule.OutputPort);
        context.Sent[0].Payload.Length.ShouldBe(DataGeneratorModule.OutputLayout.SizeInBytes);

        var reader = new PayloadReader(context.Sent[0].Payload, DataGeneratorModule.OutputLayout);
        reader.ReadU64("seq").ShouldBe(0UL);
        reader.ReadF32("value").ShouldBe(0.5f);

        context.Published[DataGeneratorModule.ValueSource][0].Length.ShouldBe(sizeof(float));
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    /// <summary>故意写坏的模块（验证契约自检**确实能发现问题**）。</summary>
    [ModuleDefinition("test", "broken", "1.0.0")]
    public sealed class BrokenModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

            context.Declaration.DeclarePort(new PortDeclaration("dup", PortDirection.Send, shape));
            context.Declaration.DeclarePort(new PortDeclaration("dup", PortDirection.Receive, shape));

            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.Numeric, "missing", "缺失的数据源")],
                [new ViewDataSource("series", unit: null, format: null, kind: ViewDataSourceKind.Sequence, valueType: null)]));
        }
    }
}
