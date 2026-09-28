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
    public void 参考模块应各自声明角色_融合器一块两入一出_且都不声明内部顺序()
    {
        // 【ADR-0052】角色 = 模块声明的**功能分块**（一种数据类型 + 输入口/输出口），**框架不派生**。
        // 六个参考模块都是"单块"：数据发生 / 手动发送 = 纯源，显示 / 记录 / 断言 = 纯汇，
        // 融合 = **一块两入一出**（这正是"允许 >2 个口"的具体理由：合成一路是**一件事**）。
        var fuser = ModuleContract.Probe(typeof(HYWDebugAssistant.Modules.Reference.Fuser.FuserModule));

        fuser.Problems.ShouldBeEmpty(string.Join("；", fuser.Problems));

        var fuse = fuser.DeclaredRoles.ShouldHaveSingleItem();
        fuse.Id.ShouldBe("fuse");
        fuse.Inputs.ShouldBe(
            [HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.InputPort1,
             HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.InputPort2]);
        fuse.Outputs.ShouldBe([HYWDebugAssistant.Modules.Reference.Fuser.FuserModule.OutputPort]);
        fuse.Type.ShouldBe(
            new DataTypeRef(HYWDebugAssistant.Sdk.Contracts.CommonDataTypes.AnalogSampleTypeId, 1),
            "一个角色只有**一种**数据类型");

        fuser.DeclaredFlows.ShouldBeEmpty("单块模块没有「角色 → 角色」的内部顺序 ⇒ 零声明零线");

        // 【ADR-0052 Q3】多口块只得到**建议**（走 `Advisories`），**不是问题**：它可能在说
        // "两件事挤成一块"（该拆），也可能就是"多路合成一路"（不必拆）——框架判不了，故只说、不判定。
        fuser.Advisories.ShouldContain(
            a => a.Contains("3 个口", StringComparison.Ordinal) && a.Contains("不必拆", StringComparison.Ordinal),
            "融合器的 2 入 1 出应当只得到一条**建议**（含「不必拆」的口径），且不得进 Problems");

        foreach (var type in new[]
                 {
                     typeof(DataGeneratorModule),
                     typeof(RecorderModule),
                     typeof(AsserterModule),
                     typeof(HYWDebugAssistant.Modules.Reference.ManualSender.ManualSenderModule),
                     typeof(HYWDebugAssistant.Modules.Reference.Monitor.MonitorModule),
                 })
        {
            var probe = ModuleContract.Probe(type);

            probe.DeclaredRoles.Count.ShouldBe(1, $"{type.Name} 是单块模块 ⇒ 一个角色");
            probe.DeclaredFlows.ShouldBeEmpty($"{type.Name} 没有内部处理结构 ⇒ 不声明（框架不猜）");
            probe.Advisories.ShouldBeEmpty($"{type.Name} 是一入一出/单口 ⇒ 连建议都没有（建议要精准，不能到处都是）");
        }
    }

    [Fact]
    public void 内部处理顺序写错_契约自检应当场报出来()
    {
        // 【ADR-0052】内部处理顺序 = 「**角色 → 角色**」：两端角色必须已声明、不得自环、同（起点, 终点）不得重复。
        var problems = ModuleContract.CheckAll(typeof(BrokenFlowModule)).ToList();

        problems.ShouldContain(p => p.Contains("不得自环", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未声明的角色", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("重复声明", StringComparison.Ordinal));
    }

    [Fact]
    public void 角色声明写错_契约自检应当场报出来()
    {
        // 【ADR-0052 §3 R3′】SDK 侧自检与框架侧（运行期忽略 + Issue）**同一套规则**：
        // Id 唯一 · 类型可在本模块解析 · 口存在且方向相符 · 一个口最多属于一个角色 · 至少一个口。
        var problems = ModuleContract.CheckAll(typeof(BrokenRoleModule)).ToList();

        problems.ShouldContain(p => p.Contains("角色标识重复", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未声明的数据类型", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("引用了未声明的口", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("方向应为", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("一个口最多属于一个角色", StringComparison.Ordinal));

        // **口数不设上限**（ADR-0052 Q3）：3 个输出的角色是合法形态（融合块就是 2 入 1 出）⇒ 只给**建议**，不进 Problems
        var probe = ModuleContract.Probe(typeof(BrokenRoleModule));
        probe.DeclaredRoles.ShouldContain(r => r.Id == "many" && r.Outputs.Count == 3, "多口角色照常成立");
        probe.Advisories.ShouldContain(a => a.Contains("3 个口", StringComparison.Ordinal), "三输出应当得到一条建议");
        problems.ShouldNotContain(p => p.Contains("3 个口", StringComparison.Ordinal), "建议**不是问题** ⇒ 不得进 Problems");
    }

    [Fact]
    public void 角色两侧口类型不一致_契约自检应报出并指向拆成两个角色()
    {
        // 【2026-09 补充裁定 α】一个角色的**输入口与输出口必须同一种数据类型**（= 角色的 Type）。
        // ADR-0052 Q2 只裁了"一个角色只有一种数据类型"，α 裁的是**两侧是否同型**。
        var problems = ModuleContract.CheckAll(typeof(MixedTypeRoleModule)).ToList();

        problems.ShouldContain(
            p => p.Contains("不一致", StringComparison.Ordinal)
                 && p.Contains("拆成两个角色", StringComparison.Ordinal),
            "收 A 出 B 写在同一个角色里 ⇒ 必须报出，并给出正写法（两个角色 + DeclareFlow）");
    }

    [Fact]
    public void 类型转换模块_按两个角色声明则无问题_且角色数为二()
    {
        // α 的**正写法**：收 A 的角色 + 发 B 的角色 + DeclareFlow（叠加层上 = 两个方块，各按自己的类型色）。
        var problems = ModuleContract.CheckAll(typeof(ConverterModule)).ToList();
        problems.ShouldBeEmpty(string.Join("；", problems));

        var probe = ModuleContract.Probe(typeof(ConverterModule));
        probe.DeclaredRoles.Count.ShouldBe(2, "类型转换 = 两个数据角色（一个角色只有一种数据类型）");
        probe.DeclaredFlows.Count.ShouldBe(1, "两个角色之间用 DeclareFlow 表达先后");
    }

    [Fact]
    public void 口未声明类型时_同型检查跳过不报()
    {
        // "可声明 ≠ 必须声明"：旧模块形态（口不带 `Type`）**无可比对象** ⇒ 同型检查跳过，
        // 不得因此让旧模块的契约自检失败（向后兼容，与 `PortDeclaration.Type` 的既有语义一致）。
        var problems = ModuleContract.CheckAll(typeof(UntypedPortRoleModule)).ToList();
        problems.ShouldBeEmpty(string.Join("；", problems));
    }

    [Fact]
    public void 有数据口却零角色_应给一条建议而不是问题()
    {
        // 【B5 · 2026-09 裁定】"口 → 角色归属"的完整性**只给建议、不进 Problems、运行期不报**：
        // 与"口数 > 2 只给建议"同口径（ADR-0052 Q3）；ADR-0052 的措辞是"**应当**"（不是"必须"）⇒ 不得升级为错误。
        var probe = ModuleContract.Probe(typeof(NoRoleModule));

        probe.Advisories.ShouldContain(
            a => a.Contains("没有任何有效的角色声明", StringComparison.Ordinal),
            "有数据口却零角色 ⇒ 叠加层不画任何功能块，应当建议作者补 DeclareRole");
        probe.Problems.ShouldBeEmpty(string.Join("；", probe.Problems));
    }

    [Fact]
    public void 口不属于任何角色_应给一条建议而不是问题()
    {
        // 同 B5：**有角色，但某些口没有归属** ⇒ 那些口在叠加层上没有方块 ⇒ 建议（不是问题）。
        var probe = ModuleContract.Probe(typeof(OrphanPortModule));

        probe.Advisories.ShouldContain(
            a => a.Contains("不属于任何角色", StringComparison.Ordinal) && a.Contains("'in2'", StringComparison.Ordinal),
            "未归属的口应当被点名");
        probe.Problems.ShouldBeEmpty(string.Join("；", probe.Problems));
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

    /// <summary>内部处理顺序写错的模块（ADR-0052 §3 R4′）：自环 / 引用了未声明的角色 / 重复。</summary>
    [ModuleDefinition("test", "broken-flow", "1.0.0")]
    public sealed class BrokenFlowModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var type = new DataTypeRef("test/broken-flow", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-flow", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in1", PortDirection.Receive, shape) { Type = type });
            context.Declaration.DeclarePort(new PortDeclaration("out1", PortDirection.Send, shape) { Type = type });
            context.Declaration.DeclarePort(new PortDeclaration("in2", PortDirection.Receive, shape) { Type = type });

            context.Declaration.DeclareRole(RoleDeclaration.Both("a", type, "in1", "out1", "甲"));
            context.Declaration.DeclareRole(RoleDeclaration.Input("b", type, "in2", "乙"));

            context.Declaration.DeclareFlow(new FlowDeclaration("a", "a"));          // 自环
            context.Declaration.DeclareFlow(new FlowDeclaration("nope", "b"));      // 引用了未声明的角色
            context.Declaration.DeclareFlow(new FlowDeclaration("a", "b"));         // 合法
            context.Declaration.DeclareFlow(new FlowDeclaration("a", "b"));         // 重复
        }
    }

    /// <summary>角色声明写错的模块（ADR-0052 §3 R3′）：重复 Id / 类型未声明 / 口不存在 / 方向不符 / 口被两个角色占 / 3 个口。</summary>
    [ModuleDefinition("test", "broken-role", "1.0.0")]
    public sealed class BrokenRoleModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var known = new DataTypeRef("test/broken-role", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-role", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { Type = known });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { Type = known });

            for (var i = 1; i <= 3; i++)
            {
                context.Declaration.DeclarePort(new PortDeclaration($"out{i}", PortDirection.Send, shape) { Type = known });
            }

            context.Declaration.DeclareRole(RoleDeclaration.Both("dup", known, "in", "out", "甲"));
            context.Declaration.DeclareRole(RoleDeclaration.Both("dup", known, "in", "out", "甲（重复 Id）"));   // ① Id 重复
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "no-type", new DataTypeRef("test/missing", 1), "in", "out"));                                   // ② 类型未声明
            context.Declaration.DeclareRole(RoleDeclaration.Both("no-port", known, "nope", "out"));            // ③ 口不存在
            context.Declaration.DeclareRole(RoleDeclaration.Output("wrong-dir", known, "in"));                 // ④ 方向不符（拿接收口当输出）
            context.Declaration.DeclareRole(RoleDeclaration.Input("steal", known, "in"));                      // ⑤ 口已被 dup 占用
            context.Declaration.DeclareRole(new RoleDeclaration(
                "many", known, [], ["out1", "out2", "out3"], "三输出"));                                        // ⑥ 口数 > 2：**合法**（不再提示）
        }
    }

    /// <summary>**α 的反例**：把"收 A 出 B"写进**同一个**角色（角色的输入口与输出口类型不同）。</summary>
    [ModuleDefinition("test", "mixed-role", "1.0.0")]
    public sealed class MixedTypeRoleModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var typeA = new DataTypeRef("test/type-a", 1);
            var typeB = new DataTypeRef("test/type-b", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/type-a", 1, shape));
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/type-b", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { Type = typeA });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { Type = typeB });

            // ✗ 一个角色跨两种类型 ⇒ 报 Problem（α）
            context.Declaration.DeclareRole(RoleDeclaration.Both("convert", typeA, "in", "out", "转换（写法错误）"));
        }
    }

    /// <summary>**α 的正例**：类型转换 = **两个角色**（收 A / 发 B）+ 一条内部顺序。</summary>
    [ModuleDefinition("test", "converter", "1.0.0")]
    public sealed class ConverterModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var typeA = new DataTypeRef("test/recv-a", 1);
            var typeB = new DataTypeRef("test/send-b", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/recv-a", 1, shape));
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/send-b", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { Type = typeA });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { Type = typeB });

            context.Declaration.DeclareRole(RoleDeclaration.Input("recv", typeA, "in", "接收 A"));
            context.Declaration.DeclareRole(RoleDeclaration.Output("send", typeB, "out", "发出 B"));
            context.Declaration.DeclareFlow(new FlowDeclaration("recv", "send"));
        }
    }

    /// <summary>**旧模块形态**：口**未声明** `Type` ⇒ 同型检查无可比对象，不报（向后兼容）。</summary>
    [ModuleDefinition("test", "untyped-port-role", "1.0.0")]
    public sealed class UntypedPortRoleModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var type = new DataTypeRef("test/untyped-role", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/untyped-role", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape));    // 不带 Type
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape));     // 不带 Type

            context.Declaration.DeclareRole(RoleDeclaration.Both("block", type, "in", "out", "块"));
        }
    }

    /// <summary>**B5 反例之一**：有数据口，但**一个角色都没声明** ⇒ 建议补 `DeclareRole`（不是问题）。</summary>
    [ModuleDefinition("test", "no-role", "1.0.0")]
    public sealed class NoRoleModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var type = new DataTypeRef("test/no-role", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/no-role", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { Type = type });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { Type = type });

            // 刻意不声明任何角色（ADR-0052 的措辞是"应当"⇒ 只给建议）
        }
    }

    /// <summary>**B5 反例之二**：有角色，但 `in2` **不属于任何角色** ⇒ 建议（不是问题）。</summary>
    [ModuleDefinition("test", "orphan-port", "1.0.0")]
    public sealed class OrphanPortModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var type = new DataTypeRef("test/orphan-port", 1);

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/orphan-port", 1, shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { Type = type });
            context.Declaration.DeclarePort(new PortDeclaration("in2", PortDirection.Receive, shape) { Type = type });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { Type = type });

            // 只声明一块功能，且**没有**把 in2 收进去 ⇒ in2 成为"无归属口"
            context.Declaration.DeclareRole(RoleDeclaration.Both("blk", type, "in", "out", "块"));
        }
    }
}
