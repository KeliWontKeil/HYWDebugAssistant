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
            typeof(AsserterModule));

        problems.ShouldBeEmpty(string.Join("；", problems));
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
