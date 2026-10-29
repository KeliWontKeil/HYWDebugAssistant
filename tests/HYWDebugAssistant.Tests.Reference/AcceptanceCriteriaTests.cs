using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Tests.Reference.Stubs;
using Shouldly;
using Xunit;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **T1 判据测试（AC-1~6）**——主文档 §17 / `docs/architecture/06` §6.4、`08` §M1-12。
/// </summary>
/// <remarks>
/// <para>
/// <b>链路载体 = 正式模块 + 本项目内的测试桩</b>：
/// `hyw/data-monitor`（下游显示）+ `test/rate-source`（不需要人操作的持续数据源，见 <c>Stubs/</c>）+
/// `test/fusion-stub`（多源合并）。
/// </para>
/// <list type="bullet">
///   <item><b>判据口径</b>：AC-1 依赖图 + 接入即运行、AC-2 故障隔离、AC-3 五类在线变更、
///     AC-4 保存/加载往返无损、AC-5 批量新增不碰既有、AC-6 未知形状零改动；</item>
///   <item>框架程序集引用集断言（AC-1/AC-6）的对象是**框架自身**，与载体无关；
///     「模块只引 SDK」（AR-1）复核对象是**四个正式模块程序集**。</item>
/// </list>
/// </remarks>
public class AcceptanceCriteriaTests
{
    /// <summary>框架程序集（AC-1/AC-6 的"依赖图断言"对象）。</summary>
    private static readonly string[] FrameworkAssemblies =
    [
        "HYWDebugAssistant.Sdk",
        "HYWDebugAssistant.Hosting",
        "HYWDebugAssistant.DataPlane",
        "HYWDebugAssistant.Core",
        "HYWDebugAssistant.Core.Api",
    ];

    /// <summary>四个正式模块的项目名（AR-1 复核对象）。</summary>
    private static readonly string[] BusinessModules =
    [
        "TextSender",
        "HexSender",
        "DataMonitor",
        "HexStringConverter",
    ];

    private const string MonitorModuleId = "hyw/data-monitor/1.0.0";

    private const string RateSourceModuleId = "test/rate-source/1.0.0";

    private const string FusionStubModuleId = "test/fusion-stub/1.0.0";

    // ------------------------------------------------------------ AC-1

    [Fact]
    public void AC1_新增模块仅凭声明即可接入运行_且框架程序集引用集不变()
    {
        // ① 依赖图断言：框架程序集**不得引用任何模块程序集**（Core 零改动的结构级保证）
        foreach (var assembly in FrameworkAssemblies)
        {
            var references = ReadAssemblyReferences(assembly);
            references.ShouldNotContain(
                r => r.StartsWith("HYWDebugAssistant.Modules.", StringComparison.Ordinal),
                $"{assembly} 不得引用模块程序集（模块是新增物，框架不认识它）");
        }

        // ② 四个正式模块只引 SDK + 界面契约（AR-1 在真实模块上的复核）：
        //    模块自带 XAML 视图后，Avalonia 控件与视图契约住在 `Sdk.Ui`，它是模块允许的第二个程序集。
        foreach (var module in BusinessModules)
        {
            var references = ReadAssemblyReferences($"HYWDebugAssistant.Modules.{module}");
            references.Where(r => r.StartsWith("HYWDebugAssistant.", StringComparison.Ordinal))
                .ShouldBe(
                    ["HYWDebugAssistant.Sdk", "HYWDebugAssistant.Sdk.Ui"],
                    $"{module} 只允许引用 Sdk 与 Sdk.Ui");
        }

        // ③ 装包 → 加入 → 建网 → 数据流：**一个源、两个下游**（1:N 多播）
        using var composition = new BusinessChainTests.Composition();
        composition.InstallBusinessPackages();
        composition.Register("rate-source", typeof(RateSourceStubModule));
        composition.Register("fusion-stub", typeof(FusionStubModule));

        composition.Services.AddModule("src", RateSourceModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("mon", MonitorModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("fus", FusionStubModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("src", new RateSourceStubConfig { Value = 0.5, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();

        // 显示器「接收换行」开 ⇒ 每个包立即成行（便于断言内容）
        composition.Submit("mon", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // 建网：同一网络标签 ⇒ 一个源、两个宿
        composition.Services.UpdatePortNetwork("src", RateSourceStubModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fus", FusionStubModule.InputPort1, "bus").Succeeded.ShouldBeTrue();

        composition.Services.GetNetworks().ShouldHaveSingleItem().Targets.Count.ShouldBe(2, "1:N 多播：两个下游同属一条网络");

        composition.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "显示器应收到样本"));
        composition.WaitForCount("fus", FusionStubModule.Count1Source, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "融合桩应在其第 1 个口上收到样本"));

        composition.WaitUntilDisplayText("mon", DataMonitorModule.LogSource, "0.5")
            .ShouldBeTrue("定值 0.5 真的流到了下游（模块只凭声明接入，框架零改动）");

        // 健康链路上不应有**错误级** Issue（原判据"断言器不应有断言失败"的等价物；
        // `IssueView.Severity` 是枚举名文本，故按名字比较）
        composition.Services.GetIssues(200)
            .ShouldNotContain(
                i => string.Equals(i.Severity, nameof(IssueSeverity.Error), StringComparison.Ordinal),
                IssueSummary(composition, "健康链路不应产生错误"));
    }

    // ------------------------------------------------------------ AC-2

    [Fact]
    public void AC2_模块故障不影响无关模块_上游无数据是合法无输入()
    {
        using var composition = new BusinessChainTests.Composition();

        composition.Register("healthy", typeof(HealthyGeneratorModule));
        composition.Register("faulty", typeof(FaultyGeneratorModule));
        composition.Register("sink-a", typeof(CountingSinkModule));
        composition.Register("sink-b", typeof(CountingSinkModule));

        composition.Services.AddModule("h1", "test/healthy/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("f1", "test/faulty/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("sa", "test/sink-a/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("sb", "test/sink-b/1.0.0").Succeeded.ShouldBeTrue();

        composition.Services.Connect(new PortRef("h1", "out"), [new PortRef("sa", "in")], "healthy-link").Succeeded.ShouldBeTrue();
        composition.Services.Connect(new PortRef("f1", "out"), [new PortRef("sb", "in")], "faulty-link").Succeeded.ShouldBeTrue();

        // ① 故障模块的活动体抛异常 → 受控捕获 + 上报，**不影响**健康链路
        composition.WaitForCount("h1", "value", atLeast: 1).ShouldBeTrue("健康链路应照常产生数据");

        IssuesWith(composition, "活动体异常").ShouldNotBeEmpty("故障模块的异常应被边界捕获并上报（不逸出）");

        var healthyWrites = composition.Written("h1", "value");
        BusinessChainTests.Composition.WaitUntil(
            () => composition.Written("h1", "value") > healthyWrites,
            TimeSpan.FromSeconds(2)).ShouldBeTrue("故障发生后，无关模块应继续运行");

        composition.Services.GetInstances().Single(i => i.InstanceId == "sb").State
            .ShouldBe(nameof(ModuleInstanceState.Active), "故障模块的实例仍在场（框架不擅自清理）");

        // ② 上游无数据 = 合法"无输入"：禁用上游后下游仍是 Active（不是错误）
        composition.Services.SetModuleEnabled("h1", false).Succeeded.ShouldBeTrue();
        Thread.Sleep(60);

        composition.Services.GetInstances().Single(i => i.InstanceId == "sa").State
            .ShouldBe(nameof(ModuleInstanceState.Active), "上游停流对下游是合法状态，非错误");
        IssuesWith(composition, "无输入").ShouldBeEmpty();
    }

    // ------------------------------------------------------------ AC-3

    [Fact]
    public void AC3_五类变更_可在常驻运行中在线生效且不中断既有数据流()
    {
        using var composition = new BusinessChainTests.Composition();
        composition.InstallBusinessPackages();
        composition.Register("rate-source", typeof(RateSourceStubModule));

        composition.Services.AddModule("src", RateSourceModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("mon", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("src", new RateSourceStubConfig { Value = 1.0, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();
        composition.Submit("mon", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("src", RateSourceStubModule.OutPort, "main").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon", DataMonitorModule.InPort, "main").Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "既有链路应先跑起来"));

        // ① 加入（Add Module）
        composition.Services.AddModule("mon2", MonitorModuleId).Succeeded.ShouldBeTrue();
        AssertFlowContinues(composition, "src");

        // ② 建网（写同名网络标签）：第二个下游立即加入同一条网络并开始收数
        composition.Submit("mon2", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon2", DataMonitorModule.InPort, "main").Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("mon2", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "新建网络应立即可流"));
        AssertFlowContinues(composition, "src");

        // ③ 配置更新（模块热生效，不重建实例，新值真的流到下游）
        var instanceBefore = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "src");
        composition.Services.UpdateConfiguration("src", new RateSourceStubConfig { Value = 1.25, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();

        composition.WaitUntilDisplayText("mon", DataMonitorModule.LogSource, "1.25")
            .ShouldBeTrue("配置热生效后新值应流到下游");
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "src")
            .ShouldBeSameAs(instanceBefore, "配置热生效不重建实例");
        AssertFlowContinues(composition, "src");

        // ④ 禁用 / 启用（保留拓扑与配置，只停流）
        composition.Services.SetModuleEnabled("mon2", false).Succeeded.ShouldBeTrue();
        composition.Services.SetModuleEnabled("mon2", true).Succeeded.ShouldBeTrue();
        AssertFlowContinues(composition, "src");

        // ⑤ 断开与移除（级联）：mon2 先退出网络，再被移除
        composition.Services.UpdatePortNetwork("mon2", DataMonitorModule.InPort, null).Succeeded.ShouldBeTrue();

        var network = composition.Services.GetNetworks().Single(n => n.Name == "main");
        network.Targets.Count.ShouldBe(1, "断开后网络只剩既有显示器");

        composition.Services.RemoveModule("mon2").Succeeded.ShouldBeTrue();
        AssertFlowContinues(composition, "src");

        composition.Services.GetInstances().Count.ShouldBe(2, "移除后现场只剩 src 与 mon（新批次已被级联清理）");
        composition.Services.GetNetworks().ShouldContain(n => n.Name == "main");
        composition.Services.GetNetworks().Single(n => n.Name == "main").Targets.Count.ShouldBe(1);
    }

    // ------------------------------------------------------------ AC-4

    [Fact]
    public void AC4_组合件的保存加载运行再保存应无损往返()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-ac-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "composite.json");
        var resaved = Path.Combine(directory, "composite-2.json");

        try
        {
            using (var first = new BusinessChainTests.Composition())
            {
                first.InstallBusinessPackages();
                first.Register("rate-source", typeof(RateSourceStubModule));

                first.Services.AddModule("src", RateSourceModuleId).Succeeded.ShouldBeTrue();
                first.Services.AddModule("mon", MonitorModuleId).Succeeded.ShouldBeTrue();

                first.Services.UpdatePortNetwork("src", RateSourceStubModule.OutPort, "bus").Succeeded.ShouldBeTrue();
                first.Services.UpdatePortNetwork("mon", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

                first.Services.UpdateConfiguration("src", new RateSourceStubConfig { Value = 0.75, IntervalMs = 5 })
                    .Succeeded.ShouldBeTrue();
                first.Submit("mon", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();
                first.Services.UpdateModuleLayout(new ModuleLayoutView("src", 10, 20, 240, 140)).Succeeded.ShouldBeTrue();

                first.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: 1)
                    .ShouldBeTrue(IssueSummary(first, "保存前链路应先跑起来"));

                first.Services.SaveComposite(path, "基础组合").Succeeded.ShouldBeTrue();
            }

            using (var second = new BusinessChainTests.Composition())
            {
                second.InstallBusinessPackages();
                second.Register("rate-source", typeof(RateSourceStubModule));

                second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

                second.Services.GetInstances().Count.ShouldBe(2);
                second.Services.GetInstances().ShouldNotContain(i => i.IsPlaceholder);

                // 加载后**可运行**：配置随组合件恢复（定值 0.75、间隔 5ms）⇒ 无需人工干预即出数
                second.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: 1)
                    .ShouldBeTrue(IssueSummary(second, "加载后的组合应能立即运行"));
                second.WaitUntilDisplayText("mon", DataMonitorModule.LogSource, "0.75")
                    .ShouldBeTrue("配置随组合件恢复 ⇒ 定值 0.75 应流到下游（不是靠界面里的输入文本）");

                second.Services.SaveComposite(resaved, "基础组合").Succeeded.ShouldBeTrue();
            }

            // 往返无损：除时间戳外逐字段一致
            var before = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var after = JsonNode.Parse(File.ReadAllText(resaved))!.AsObject();
            _ = before.Remove("savedAt");
            _ = after.Remove("savedAt");
            JsonNode.DeepEquals(before, after).ShouldBeTrue($"组合件往返应无损：\n前：{before}\n后：{after}");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    // ------------------------------------------------------------ AC-5

    [Fact]
    public void AC5_同批新增多个模块不要求修改既有模块()
    {
        using var composition = new BusinessChainTests.Composition();
        composition.InstallBusinessPackages();
        composition.Register("rate-source", typeof(RateSourceStubModule));

        composition.Services.AddModule("src", RateSourceModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("mon", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("src", RateSourceStubModule.OutPort, "base").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon", DataMonitorModule.InPort, "base").Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("src", new RateSourceStubConfig { Value = 0, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();
        composition.Submit("mon", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "既有链路应先跑起来"));

        var existingSource = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "src");
        var existingMonitor = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "mon");
        var linesBefore = composition.DisplayLines("mon", DataMonitorModule.LogSource).Count;

        // 一批新增（2 组）——**不改动既有模块**
        composition.Services.AddModule("src2", RateSourceModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("src3", RateSourceModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("mon2", MonitorModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("mon3", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("src2", new RateSourceStubConfig { Value = 2, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();
        composition.Services.UpdateConfiguration("src3", new RateSourceStubConfig { Value = 3, IntervalMs = 5 })
            .Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("src2", RateSourceStubModule.OutPort, "batch-2").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon2", DataMonitorModule.InPort, "batch-2").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("src3", RateSourceStubModule.OutPort, "batch-3").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("mon3", DataMonitorModule.InPort, "batch-3").Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("mon2", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "新批次应即刻可流"));
        composition.WaitForDisplayCount("mon3", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(IssueSummary(composition, "新批次应即刻可流"));

        // 既有模块：实例未被触碰，数据流持续
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "src").ShouldBeSameAs(existingSource);
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "mon").ShouldBeSameAs(existingMonitor);
        composition.WaitForDisplayCount("mon", DataMonitorModule.LogSource, atLeast: linesBefore + 1)
            .ShouldBeTrue(IssueSummary(composition, "既有模块的数据流应持续"));
    }

    // ------------------------------------------------------------ AC-6

    [Fact]
    public void AC6_未知新形状的数据类型可原样承载_框架零改动()
    {
        // 依赖图断言：新增"新形状模块"后，框架程序集引用集仍不变（Core 零改动）
        var frameworkRefsBefore = FrameworkAssemblies.ToDictionary(a => a, ReadAssemblyReferences);

        using var composition = new BusinessChainTests.Composition();
        composition.Register("new-shape", typeof(NewShapeSourceModule));
        composition.Register("new-shape-sink", typeof(NewShapeSinkModule));

        composition.Services.AddModule("ns", "test/new-shape/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("nk", "test/new-shape-sink/1.0.0").Succeeded.ShouldBeTrue();

        composition.Services.Connect(new PortRef("ns", NewShapeSourceModule.OutputPort), [new PortRef("nk", NewShapeSinkModule.InputPort)], "new-shape-link")
            .Succeeded.ShouldBeTrue();

        composition.WaitForCount("ns", NewShapeSourceModule.ValueSource, atLeast: 1)
            .ShouldBeTrue("新形状模块应能自行运行并写订阅缓冲");

        var frameworkRefsAfter = FrameworkAssemblies.ToDictionary(a => a, ReadAssemblyReferences);
        foreach (var (assembly, references) in frameworkRefsBefore)
        {
            frameworkRefsAfter[assembly].ShouldBe(references, $"{assembly} 的引用集不应因新增模块而改变");
        }
    }

    // ------------------------------------------------------------ 基础设施

    /// <summary>归属对象或描述中包含关键词的 Issue。</summary>
    private static IReadOnlyList<IssueView> IssuesWith(BusinessChainTests.Composition composition, string keyword) =>
        [.. composition.Services.GetIssues(200).Where(i =>
            i.Message.Contains(keyword, StringComparison.Ordinal)
            || i.ObjectPath.Contains(keyword, StringComparison.Ordinal))];

    /// <summary>断言数据流持续（计数还在增长）——用于"变更不中断既有链路"。</summary>
    private static void AssertFlowContinues(BusinessChainTests.Composition composition, string instanceId, string dataSourceName = RateSourceStubModule.ValueSource)
    {
        var before = composition.Written(instanceId, dataSourceName);
        BusinessChainTests.Composition.WaitUntil(
            () => composition.Written(instanceId, dataSourceName) > before,
            TimeSpan.FromSeconds(3)).ShouldBeTrue($"{instanceId} 的数据流不应因在线变更而中断");
    }

    /// <summary>失败时的现场说明（链路断点定位；含 Issue 与网络成员）。</summary>
    private static string IssueSummary(BusinessChainTests.Composition composition, string what)
    {
        var networks = string.Join(
            "；",
            composition.Services.GetNetworks().Select(n => $"{n.Name}: {string.Join(",", n.Sources)} → {string.Join(",", n.Targets)}（停流={n.IsStalled}）"));

        var issues = string.Join(
            "；",
            composition.Services.GetIssues(10).Select(i => $"[{i.Severity}] {i.ObjectPath}: {i.Message}"));

        return $"{what}；网络：{networks}；Issue：{issues}";
    }

    /// <summary>读取程序集的直接引用名集合（AC-1/AC-6 的依赖图断言）。</summary>
    private static List<string> ReadAssemblyReferences(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        File.Exists(path).ShouldBeTrue($"未找到已编译的程序集：{path}");

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        var names = new List<string>();
        foreach (var handle in metadata.AssemblyReferences)
        {
            names.Add(metadata.GetString(metadata.GetAssemblyReference(handle).Name));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // ------------------------------------------------------------ 测试局部模块

    /// <summary>健康模块：周期写订阅 + 发一个 u32 口。</summary>
    [ModuleDefinition("test", "healthy", "1.0.0")]
    public sealed class HealthyGeneratorModule : IModule, IActivitySource
    {
        public const string OutPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.U32);

        public ActivityKind Kind => ActivityKind.Periodic;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(5);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape));
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration("value", ViewDataSourceKind.Value, ContractPrimitive.F32));
        }

        public void RunActivity(IModuleContext context)
        {
            var payload = new byte[4];
            Span<byte> sample = stackalloc byte[4];
            var value = 0u;

            while (!context.StopSignal.IsStopRequested)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload, value++);
                BinaryPrimitives.WriteSingleLittleEndian(sample, value);
                _ = context.Ports.TrySend(OutPort, payload);
                context.Ports.Publish("value", sample);
                _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(5));
            }
        }
    }

    /// <summary>故障模块：活动体立刻抛异常（验证异常边界）。</summary>
    [ModuleDefinition("test", "faulty", "1.0.0")]
    public sealed class FaultyGeneratorModule : IModule, IActivitySource
    {
        public ActivityKind Kind => ActivityKind.EventDriven;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(5);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.U32)));

        public void RunActivity(IModuleContext context) =>
            throw new InvalidOperationException("模拟模块内部故障（测试注入）");
    }

    /// <summary>计数接收模块（被动接收）。</summary>
    [ModuleDefinition("test", "sink-a", "1.0.0")]
    public sealed class CountingSinkModule : IModule
    {
        /// <summary>输入端口名。</summary>
        public const string InputPort = "in";

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                InputPort,
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.U32)));

            context.Ports.SetReceiveHandler(InputPort, _ => { });
        }
    }

    /// <summary>新形状源模块：<c>{ id: u32, block: bytes[16] }</c>（AC-6 的"新数据类型"）。</summary>
    [ModuleDefinition("test", "new-shape", "1.0.0")]
    public sealed class NewShapeSourceModule : IModule, IActivitySource
    {
        public const string OutPort = "out";

        /// <summary>视图数据源名。</summary>
        public const string ValueSource = "value";

        /// <summary>输出端口名。</summary>
        public const string OutputPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("id", ContractShape.OfPrimitive(ContractPrimitive.U32)),
            ("block", ContractShape.OfBytes(16)));

        public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

        public ActivityKind Kind => ActivityKind.Periodic;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(5);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape));
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ValueSource, ViewDataSourceKind.Value, ContractPrimitive.U32));
        }

        public void RunActivity(IModuleContext context)
        {
            Span<byte> unit = stackalloc byte[Layout.SizeInBytes];
            Span<byte> sample = stackalloc byte[4];
            var id = 0u;

            while (!context.StopSignal.IsStopRequested)
            {
                var writer = new PayloadWriter(unit, Layout);
                writer.WriteU32("id", id++);
                writer.WriteBytes("block", "hyw-reference-16"u8);
                _ = context.Ports.TrySend(OutPort, unit);

                BinaryPrimitives.WriteUInt32LittleEndian(sample, id);
                context.Ports.Publish(ValueSource, sample);
                _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(5));
            }
        }
    }

    /// <summary>新形状宿模块。</summary>
    [ModuleDefinition("test", "new-shape-sink", "1.0.0")]
    public sealed class NewShapeSinkModule : IModule
    {
        /// <summary>输入端口名。</summary>
        public const string InputPort = "in";

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(InputPort, PortDirection.Receive, NewShapeSourceModule.Shape));
            context.Ports.SetReceiveHandler(InputPort, payload =>
            {
                var reader = new PayloadReader(payload, NewShapeSourceModule.Layout);
                _ = reader.ReadU32("id");
                _ = reader.ReadBytes("block");
            });
        }
    }
}
