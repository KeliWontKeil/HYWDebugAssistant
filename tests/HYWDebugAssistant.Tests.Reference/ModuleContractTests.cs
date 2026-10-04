using System.Text;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Tests.Reference.Stubs;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **T6 契约测试套件**（`Sdk.Testing`）——docs/architecture/06 §6.5：模块作者在自己的测试项目里的用法示范。
/// </summary>
/// <remarks>
/// <para>
/// 两条用法：
/// </para>
/// <list type="number">
///   <item><b>契约自检</b>：<see cref="ModuleContract.CheckAll"/> 一行跑出声明面问题（不拉起框架）；</item>
///   <item><b>行为自检</b>：<see cref="TestModuleContext"/> 注入上下文，直接驱动模块的钩子与逻辑。</item>
/// </list>
/// <para>
/// <b>载体 = 四个正式模块 + 本项目内的测试桩</b>：
/// </para>
/// <list type="bullet">
///   <item><b>声明面与数据契约</b>的载体是**四个正式模块**（`text-sender` / `hex-sender` /
///     `data-monitor` / `hex-string-converter`）——它们只引 SDK、只凭声明接入，正是本文件的示例对象；</item>
///   <item>框架能力（多源合并 / 定时出数 / 判据上报）**一个都没删**：由本项目内的**测试桩**承载
///     （见 <c>Stubs/</c>，桩不进产品模块目录、不打包）；</item>
///   <item>故意写坏的模块（下面的 <c>Broken*</c> 系列）**原样保留** —— 它们验证"自检确实能发现问题"，
///     与载体无关。</item>
/// </list>
/// </remarks>
public class ModuleContractTests
{
    // ------------------------------------------------------------ 契约自检

    [Fact]
    public void 四个业务模块与测试桩的声明面应无契约问题()
    {
        var problems = ModuleContract.CheckAll(
            typeof(TextSenderModule),
            typeof(HexSenderModule),
            typeof(DataMonitorModule),
            typeof(HexStringConverterModule),
            typeof(RateSourceStubModule),
            typeof(JudgementSinkStubModule),
            typeof(FusionStubModule));

        problems.ShouldBeEmpty(string.Join("；", problems));
    }

    [Fact]
    public void 业务模块应各自声明角色_融合桩一块两入一出_且都不声明内部顺序()
    {
        // 角色 = 模块声明的**功能分块**（一种数据类型 + 输入口/输出口），**框架不派生**。
        // 四个业务模块都是"单块"：两个发送器 = 纯源，显示器 = 纯汇，转换器 = 零口；
        // 融合桩 = **一块两入一出**（这正是"允许 >2 个口"的具体理由：合成一路是**一件事**）。
        var fusion = ModuleContract.Probe(typeof(FusionStubModule));

        fusion.Problems.ShouldBeEmpty(string.Join("；", fusion.Problems));

        var fuse = fusion.DeclaredRoles.ShouldHaveSingleItem();
        fuse.Id.ShouldBe(FusionStubModule.RoleId);
        fuse.Inputs.ShouldBe([FusionStubModule.InputPort1, FusionStubModule.InputPort2]);
        fuse.Outputs.ShouldBe([FusionStubModule.OutputPort]);
        fuse.Type.ShouldBe(
            new DataTypeRef(FusionStubModule.RawBytesTypeId, FusionStubModule.RawBytesVersion),
            "一个角色只有**一种**数据类型");

        fusion.DeclaredFlows.ShouldBeEmpty("单块模块没有「角色 → 角色」的内部顺序 ⇒ 零声明零线");

        // 多口块只得到**建议**（走 `Advisories`），**不是问题**：它可能在说
        // "两件事挤成一块"（该拆），也可能就是"多路合成一路"（不必拆）——框架判不了，故只说、不判定。
        fusion.Advisories.ShouldContain(
            a => a.Contains("3 个口", StringComparison.Ordinal) && a.Contains("不必拆", StringComparison.Ordinal),
            "融合桩的 2 入 1 出应当只得到一条**建议**（含「不必拆」的口径），且不得进 Problems");

        foreach (var type in new[]
                 {
                     typeof(TextSenderModule),
                     typeof(HexSenderModule),
                     typeof(DataMonitorModule),
                     typeof(RateSourceStubModule),
                     typeof(JudgementSinkStubModule),
                 })
        {
            var probe = ModuleContract.Probe(type);

            probe.DeclaredRoles.Count.ShouldBe(1, $"{type.Name} 是单块模块 ⇒ 一个角色");
            probe.DeclaredFlows.ShouldBeEmpty($"{type.Name} 没有内部处理结构 ⇒ 不声明（框架不猜）");
            probe.Advisories.ShouldBeEmpty($"{type.Name} 是一入一出/单口 ⇒ 连建议都没有（建议要精准，不能到处都是）");
        }

        // 零口模块（离线转换器）同样合法：**零角色 + 零建议**（建议的触发条件是"有口却零角色"）
        var converter = ModuleContract.Probe(typeof(HexStringConverterModule));
        converter.DeclaredRoles.ShouldBeEmpty("转换器不接数据面 ⇒ 没有功能分块可声明");
        converter.Advisories.ShouldBeEmpty("零口零角色**合法且不出建议**");
    }

    [Fact]
    public void 内部处理顺序写错_契约自检应当场报出来()
    {
        // 内部处理顺序 = 「**角色 → 角色**」：两端角色必须已声明、不得自环、同（起点, 终点）不得重复。
        var problems = ModuleContract.CheckAll(typeof(BrokenFlowModule)).ToList();

        problems.ShouldContain(p => p.Contains("不得自环", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未声明的角色", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("重复声明", StringComparison.Ordinal));
    }

    [Fact]
    public void 角色声明写错_契约自检应当场报出来()
    {
        // SDK 侧自检与框架侧（运行期忽略 + Issue）**同一套规则**：
        // Id 唯一 · 类型可在本模块解析 · 口存在且方向相符 · 一个口最多属于一个角色 · 至少一个口。
        var problems = ModuleContract.CheckAll(typeof(BrokenRoleModule)).ToList();

        problems.ShouldContain(p => p.Contains("角色标识重复", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("未声明的数据类型", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("引用了未声明的口", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("方向应为", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("一个口最多属于一个角色", StringComparison.Ordinal));

        // **口数不设上限**：3 个输出的角色是合法形态（融合块就是 2 入 1 出）⇒ 只给**建议**，不进 Problems
        var probe = ModuleContract.Probe(typeof(BrokenRoleModule));
        probe.DeclaredRoles.ShouldContain(r => r.Id == "many" && r.Outputs.Count == 3, "多口角色照常成立");
        probe.Advisories.ShouldContain(a => a.Contains("3 个口", StringComparison.Ordinal), "三输出应当得到一条建议");
        problems.ShouldNotContain(p => p.Contains("3 个口", StringComparison.Ordinal), "建议**不是问题** ⇒ 不得进 Problems");
    }

    [Fact]
    public void 角色两侧口类型不一致_契约自检应报出并指向拆成两个角色()
    {
        // 一个角色的**输入口与输出口必须同一种数据类型**（= 角色的 Type）：
        // 「一个角色只有一种数据类型」不含**两侧是否同型**，故两侧同型单独判。
        var problems = ModuleContract.CheckAll(typeof(MixedTypeRoleModule)).ToList();

        problems.ShouldContain(
            p => p.Contains("不一致", StringComparison.Ordinal)
                 && p.Contains("拆成两个角色", StringComparison.Ordinal),
            "收 A 出 B 写在同一个角色里 ⇒ 必须报出，并给出正写法（两个角色 + DeclareFlow）");
    }

    [Fact]
    public void 有效长度字段写错_契约自检应当场报出来()
    {
        // `MeteringBlock.PayloadLengthField` 是「逐单元读哪个字段」
        // 的声明 ⇒ 只在运行期「读不到就静默算静态值」是不行的（表现为「有效速率恒等于搬运速率」，无人察觉）
        // ⇒ 加载期的契约自检必须把两种写错**明确报出来**。
        var problems = ModuleContract.CheckAll(typeof(BrokenMeteringModule)).ToList();

        problems.ShouldContain(
            p => p.Contains("没有这个顶层字段", StringComparison.Ordinal),
            "字段名拼错（形状里没有）⇒ 必须报出来");
        problems.ShouldContain(
            p => p.Contains("必须是**整型**原语", StringComparison.Ordinal),
            "把浮点/字节块当「有效长度」用 ⇒ 必须报出来");

        // 写对的形态（形状里有 `len:u16`）⇒ 不得报任何问题
        ModuleContract.CheckAll(typeof(GoodMeteringModule)).ShouldBeEmpty(
            string.Join("；", ModuleContract.CheckAll(typeof(GoodMeteringModule))));
    }

    [Fact]
    public void 类型转换模块_按两个角色声明则无问题_且角色数为二()
    {
        // α 的**正写法**：收 A 的角色 + 发 B 的角色 + DeclareFlow（叠加层上 = 两个方块，各按自己的类型色）。
        // 正式模块 `hex-string-converter` 是**另一种**正写法（不接数据面 ⇒ 零口零角色）——见上面的断言。
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
        // 「口 → 角色归属」的完整性**只给建议、不进 Problems、运行期不报**：
        // 与「口数 > 2 只给建议」同口径；契约的措辞是「**应当**」（不是「必须」）⇒ 不得升级为错误。
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
    public void 业务模块_应声明同一个数据类型且形状一致()
    {
        // 四个业务模块与两个「接数据面」的桩都声明 `RawBytes@1` ⇒ 兼容判定走「**同标识同版本**」层；
        // 若谁的形状写歪了，这里立刻暴露（否则现场表现为"莫名其妙连不上"）。
        var expected = TextSenderModule.RawBytesShape;

        foreach (var type in new[]
                 {
                     typeof(TextSenderModule),
                     typeof(HexSenderModule),
                     typeof(DataMonitorModule),
                     typeof(RateSourceStubModule),
                     typeof(FusionStubModule),
                 })
        {
            var probe = ModuleContract.Probe(type);
            var rawBytes = probe.DataTypes.SingleOrDefault(t =>
                t.TypeId == TextSenderModule.RawBytesTypeId && t.Version == TextSenderModule.RawBytesVersion);

            rawBytes.ShouldNotBeNull($"{type.Name} 应声明 {TextSenderModule.RawBytesTypeId}@{TextSenderModule.RawBytesVersion}");
            ContractShape.AreSame(rawBytes!.Shape, expected)
                .ShouldBeTrue($"{type.Name} 的 {TextSenderModule.RawBytesTypeId}@{TextSenderModule.RawBytesVersion} 形状必须逐字段一致");
        }
    }

    [Fact]
    public void 契约自检_应能发现重复口名()
    {
        var problems = ModuleContract.CheckAll(typeof(BrokenModule)).ToList();

        problems.ShouldContain(p => p.Contains("口名重复", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 行为自检（模块级，无需框架）

    [Fact]
    public void 融合桩_转发关闭时只在界面显示不产生输出单元()
    {
        var module = new FusionStubModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new FusionStubConfig { ForwardMerged = false });

        // 用"与正式发送器逐字节相同"的单元喂第 1 路（桩按 `len` 切片解析文本数值）
        module.Handle(context, FusionStubModule.InputPort1, RateSourceStubModule.EncodeUnit(1.5));

        context.Sent.ShouldBeEmpty("转发关闭 = 只在界面显示，不产生输出");
        context.Published.ShouldContainKey(FusionStubModule.FusedSource);

        var fused = context.Published[FusionStubModule.FusedSource];
        fused.ShouldNotBeEmpty();
        BitConverter.ToDouble(fused[^1], 0).ShouldBe(1.5d, "Latest 策略下第 1 路的到达即成为当前融合值");
    }

    [Fact]
    public void 定值数据源_应在测试上下文中发出定长单元并写入订阅()
    {
        var module = new RateSourceStubModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new RateSourceStubConfig { IntervalMs = 1, Value = 0.5 });

        // 活动体在测试上下文里跑一小段后请求停止（不用框架线程）
        var runner = new Thread(() => module.RunActivity(context));

        runner.Start();
        WaitUntil(
            () => context.Published.TryGetValue(RateSourceStubModule.ValueSource, out var list) && list.Count > 3,
            TimeSpan.FromSeconds(2)).ShouldBeTrue("定值数据源应在测试上下文里写入订阅样本");
        context.RequestStop();
        runner.Join(TimeSpan.FromSeconds(2)).ShouldBeTrue();

        context.Sent.ShouldNotBeEmpty("应发出契约数据单元");
        context.Sent[0].PortName.ShouldBe(RateSourceStubModule.OutPort);
        context.Sent[0].Payload.Length.ShouldBe(RateSourceStubModule.RawBytesShape.SizeInBytes);
        context.Sent[0].Payload.ShouldBe(
            RateSourceStubModule.EncodeUnit(0.5),
            "同一配置 ⇒ 逐字节相同的单元（可复现；`data` 为文本、尾部补零）");

        // 单元按声明**可被第三方解码**（`{ len: u16, data: bytes[1472] }`）
        var reader = new PayloadReader(context.Sent[0].Payload, RateSourceStubModule.UnitLayout);
        var length = reader.ReadU16("len");
        Encoding.UTF8.GetString(reader.ReadBytes("data")[..length]).ShouldBe("0.5");

        // 数值另写一份订阅样本（呈现层据此画数值；跨度 = 4 字节浮点）
        context.Published[RateSourceStubModule.ValueSource][0].Length.ShouldBe(sizeof(float));
        BitConverter.ToSingle(context.Published[RateSourceStubModule.ValueSource][0], 0).ShouldBe(0.5f);
    }

    [Fact]
    public void 判据接收桩_应记录统计并按类别限流上报()
    {
        var module = new JudgementSinkStubModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.DeclaredPorts.ShouldHaveSingleItem().Name.ShouldBe(JudgementSinkStubModule.InPort);

        context.SetConfiguration(new JudgementSinkStubConfig
        {
            AssertRange = true,
            ExpectedMin = 0,
            ExpectedMax = 1,
            MaxIssues = 2,
        });

        module.Judge(context, 1, 0.5f);
        module.Judge(context, 2, 0.75f);
        module.Judge(context, 2, 2.0f);   // 序号未递增 + 值超上限
        module.Judge(context, 4, 0.2f);

        module.ReceivedCount.ShouldBe(4UL);
        module.LastValue.ShouldBe(0.2, 1e-6);
        module.MinValue.ShouldBe(0.2, 1e-6);
        module.MaxValue.ShouldBe(2.0, 1e-6);
        module.SequenceViolations.ShouldBe(1);
        module.RangeViolations.ShouldBe(1);

        // 违规上报（模块自己判定，框架只登记呈现）
        context.ReportedIssues.ShouldContain(i =>
            i.Message.Contains("序号非单调", StringComparison.Ordinal) && i.Severity == IssueSeverity.Warning);
        context.ReportedIssues.ShouldContain(i => i.Message.Contains("值超出期望范围", StringComparison.Ordinal));

        // 上限**按类别**计（MaxIssues = 2）：两类各自到顶后都不再刷屏，且互不挤占
        var monotonic = context.ReportedIssues.Count(i => i.Message.Contains("序号非单调", StringComparison.Ordinal));
        var range = context.ReportedIssues.Count(i => i.Message.Contains("值超出期望范围", StringComparison.Ordinal));
        monotonic.ShouldBe(1);
        range.ShouldBe(1);

        module.Judge(context, 4, 3.0f);   // 序号非单调（4 ≤ 4）+ 超范围 ⇒ 两类各 +1，正好到上限
        module.Judge(context, 4, 4.0f);   // 两类都已超上限 ⇒ 一条都不再上报

        context.ReportedIssues.Count(i => i.Message.Contains("序号非单调", StringComparison.Ordinal))
            .ShouldBe(2, "序号类到上限 2 后不再增加");
        context.ReportedIssues.Count(i => i.Message.Contains("值超出期望范围", StringComparison.Ordinal))
            .ShouldBe(2, "范围类同理（两类各自计数，互不挤占）");

        // 限流只影响"上报"，**不影响判定**：违规计数照旧累计
        module.SequenceViolations.ShouldBe(3);
        module.RangeViolations.ShouldBe(3);
    }

    [Fact]
    public void 判据接收桩_应给出偏差并上报超差()
    {
        var module = new JudgementSinkStubModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.SetConfiguration(new JudgementSinkStubConfig
        {
            AssertTolerance = true,
            Expected = 1.0,
            Tolerance = 0.01,
            MaxIssues = 1,
        });

        module.Judge(context, 1, 1.005f).ShouldBe(0.005, 1e-6);
        module.Judge(context, 2, 1.5f).ShouldBe(0.5, 1e-6);

        module.PassedCount.ShouldBe(1UL);
        module.FailedCount.ShouldBe(1UL);
        module.LastDeviation.ShouldBe(0.5, 1e-6);
        context.ReportedIssues.ShouldContain(i =>
            i.Message.Contains("值超出容差", StringComparison.Ordinal) && i.Severity == IssueSeverity.Warning);

        // MaxIssues = 1 ⇒ 再超差也不刷屏（这是"限流"的语义，不是"不再判定"：判定照旧，只是不上报）
        module.Judge(context, 3, 2.0f);

        module.FailedCount.ShouldBe(2UL, "判定照旧执行");
        context.ReportedIssues.Count(i => i.Message.Contains("值超出容差", StringComparison.Ordinal))
            .ShouldBe(1, "同类问题上限 1 ⇒ 第二条起不再上报");
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

    /// <summary>
    /// **有效长度字段写错**的计量模块：① 字段名在形状里不存在 ② 字段存在但不是整型。
    /// </summary>
    /// <remarks>用于验证契约自检**真的会报**（否则写错只会表现为"有效速率恒等于搬运速率"这种静默归零）。</remarks>
    [ModuleDefinition("test", "broken-metering", "1.0.0")]
    public sealed class BrokenMeteringModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(16)));

            // ① 字段名拼错：形状里没有 `length`
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-metering-a", 1, shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "length" },
            });

            // ② 字段存在但**不是整型**（浮点不能当字节数）
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-metering-b", 1, shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "data" },
            });
        }
    }

    /// <summary>**有效长度字段写对**的计量模块（对照组：不得报任何问题）。</summary>
    [ModuleDefinition("test", "good-metering", "1.0.0")]
    public sealed class GoodMeteringModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(16)));

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/good-metering", 1, shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "len" },
            });
        }
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
        }
    }

    /// <summary>内部处理顺序写错的模块：自环 / 引用了未声明的角色 / 重复。</summary>
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

    /// <summary>角色声明写错的模块：重复 Id / 类型未声明 / 口不存在 / 方向不符 / 口被两个角色占 / 3 个口。</summary>
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

            // 刻意不声明任何角色（契约的措辞是「应当」⇒ 只给建议）
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
