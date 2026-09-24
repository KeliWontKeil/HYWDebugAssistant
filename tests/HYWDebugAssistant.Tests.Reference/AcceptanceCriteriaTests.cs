using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Instances;
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
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **T1 判据测试（AC-1~6）**——主文档 §17 / `docs/architecture/06` §6.4、`08` §M1-12。
/// </summary>
/// <remarks>
/// 参考组合 = 三个参考桩模块（数据发生器 → 记录器 + 断言器，1:N 多播）。
/// 三个模块都是 Core **从未见过**的业务模块：只引 SDK、只凭声明接入。
/// </remarks>
public class AcceptanceCriteriaTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string Configuration =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    private static string PackagePath(string moduleName) => Path.Combine(
        RepoRoot, "src", "Modules", "Reference", moduleName, "bin", Configuration, "net10.0");

    /// <summary>框架程序集（AC-1/AC-6 的"依赖图断言"对象）。</summary>
    private static readonly string[] FrameworkAssemblies =
    [
        "HYWDebugAssistant.Sdk",
        "HYWDebugAssistant.Hosting",
        "HYWDebugAssistant.DataPlane",
        "HYWDebugAssistant.Core",
        "HYWDebugAssistant.Core.Api",
    ];

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

        // ② 参考桩模块自身只引 SDK（AR-1 在真实模块上的复核）
        foreach (var module in new[] { "DataGenerator", "Recorder", "Asserter" })
        {
            var moduleAssembly = $"HYWDebugAssistant.Modules.Reference.{module}";
            var references = ReadAssemblyReferences(moduleAssembly);
            references.Where(r => r.StartsWith("HYWDebugAssistant.", StringComparison.Ordinal))
                .ShouldBe(["HYWDebugAssistant.Sdk"], $"{module} 只允许引用 Sdk");
        }

        // ③ 接入运行：装包 → 加入 → 建网 → 数据流
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("gen", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("rec", "hyw/reference-recorder/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("asr", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("gen", new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Constant,
            Offset = 0.5,
            PeriodMs = 5,
        }).Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("asr", new AsserterConfig { Expected = 0.5, Tolerance = 1e-6 })
            .Succeeded.ShouldBeTrue();

        composition.Services.Connect(
            new PortRef("gen", DataGeneratorModule.OutputPort),
            [new PortRef("rec", RecorderModule.InputPort), new PortRef("asr", AsserterModule.InputPort)],
            "link-1").Succeeded.ShouldBeTrue();

        composition.WaitForFlow("asr", AsserterModule.PassedSource).ShouldBeTrue("断言器应通过样本（数据真的流过了）");
        composition.WaitForFlow("rec", RecorderModule.CountSource).ShouldBeTrue("记录器应记录样本");

        composition.IssuesWith("断言器").ShouldBeEmpty("常量 0.5 与期望一致 → 不应有断言失败");
    }

    // ------------------------------------------------------------ AC-2

    [Fact]
    public void AC2_模块故障不影响无关模块_上游无数据是合法无输入()
    {
        using var composition = new Composition();

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
        composition.WaitForFlow("h1", "value").ShouldBeTrue("健康链路应照常产生数据");

        composition.IssuesWith("活动体异常").ShouldNotBeEmpty("故障模块的异常应被边界捕获并上报（不逸出）");

        var healthyWrites = composition.Written("h1", "value");
        WaitUntil(() => composition.Written("h1", "value") > healthyWrites, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("故障发生后，无关模块应继续运行");

        composition.Services.GetInstances().Single(i => i.InstanceId == "sb").State
            .ShouldBe(nameof(ModuleInstanceState.Active), "故障模块的实例仍在场（框架不擅自清理）");

        // ② 上游无数据 = 合法"无输入"：禁用上游后下游仍是 Active（不是错误）
        composition.Services.SetModuleEnabled("h1", false).Succeeded.ShouldBeTrue();
        Thread.Sleep(60);

        composition.Services.GetInstances().Single(i => i.InstanceId == "sa").State
            .ShouldBe(nameof(ModuleInstanceState.Active), "上游停流对下游是合法状态，非错误");
        composition.IssuesWith("无输入").ShouldBeEmpty();
    }

    // ------------------------------------------------------------ AC-3

    [Fact]
    public void AC3_五类变更_可在常驻运行中在线生效且不中断既有数据流()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("gen", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("asr", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.Connect(
            new PortRef("gen", DataGeneratorModule.OutputPort),
            [new PortRef("asr", AsserterModule.InputPort)],
            "main").Succeeded.ShouldBeTrue();

        composition.WaitForFlow("asr", AsserterModule.PassedSource).ShouldBeTrue();

        // ① 加入（Add Module）
        composition.Services.AddModule("asr2", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        composition.AssertFlowContinues("asr");

        // ② 建网（Connect）
        composition.Services.Connect(
            new PortRef("gen", DataGeneratorModule.OutputPort),
            [new PortRef("asr2", AsserterModule.InputPort)],
            "second").Succeeded.ShouldBeTrue();
        composition.WaitForFlow("asr2", AsserterModule.PassedSource).ShouldBeTrue("新建网络应立即可流");
        composition.AssertFlowContinues("asr");

        // ③ 配置更新（模块热生效，不重建实例）
        var instanceBefore = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "gen");
        composition.Services.UpdateConfiguration("gen", new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Constant,
            Offset = 1.25,
            PeriodMs = 5,
        }).Succeeded.ShouldBeTrue();
        composition.Services.UpdateConfiguration("asr", new AsserterConfig { Expected = 1.25, Tolerance = 1e-6 })
            .Succeeded.ShouldBeTrue();
        composition.AssertFlowContinues("asr");
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "gen").ShouldBeSameAs(instanceBefore, "配置热生效不重建实例");

        // ④ 禁用 / 启用（保留拓扑与配置，只停流）
        composition.Services.SetModuleEnabled("asr2", false).Succeeded.ShouldBeTrue();
        composition.Services.SetModuleEnabled("asr2", true).Succeeded.ShouldBeTrue();
        composition.AssertFlowContinues("asr");

        // ⑤ 断开与移除（级联）
        composition.Services.Disconnect("second").Succeeded.ShouldBeTrue();
        composition.Services.RemoveModule("asr2").Succeeded.ShouldBeTrue();
        composition.AssertFlowContinues("asr");

        composition.Services.GetInstances().Count.ShouldBe(2, "移除后现场只剩 gen 与 asr（新批次已被级联清理）");
        composition.Services.GetNetworks().ShouldContain(n => n.Name == "main");
        composition.Services.GetNetworks().ShouldNotContain(n => n.Name == "second");
    }

    // ------------------------------------------------------------ AC-4

    [Fact]
    public void AC4_参考组合件的保存加载运行再保存应无损往返()
    {
        using var first = new Composition();
        first.InstallReferencePackages();

        first.Services.AddModule("gen", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        first.Services.AddModule("rec", "hyw/reference-recorder/1.0.0").Succeeded.ShouldBeTrue();
        first.Services.AddModule("asr", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        first.Services.Connect(
            new PortRef("gen", DataGeneratorModule.OutputPort),
            [new PortRef("rec", RecorderModule.InputPort), new PortRef("asr", AsserterModule.InputPort)],
            "link-1").Succeeded.ShouldBeTrue();
        first.Services.UpdateConfiguration("gen", new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Constant,
            Offset = 0.75,
            PeriodMs = 5,
        }).Succeeded.ShouldBeTrue();
        first.Services.UpdateConfiguration("asr", new AsserterConfig { Expected = 0.75, Tolerance = 1e-6 })
            .Succeeded.ShouldBeTrue();
        first.Services.UpdateModuleLayout(new ModuleLayoutView("gen", 10, 20, 240, 140)).Succeeded.ShouldBeTrue();

        var path = first.PathFor("reference-composite.json");
        first.Services.SaveComposite(path, "reference").Succeeded.ShouldBeTrue();

        using var second = new Composition();
        second.InstallReferencePackages();
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        second.Services.GetInstances().Count.ShouldBe(3);
        second.Services.GetInstances().ShouldNotContain(i => i.IsPlaceholder);

        // 加载后**可运行**：两侧配置都已恢复（发生器常量 0.75、断言器期望 0.75）→ 直接通过
        second.WaitForFlow("asr", AsserterModule.PassedSource).ShouldBeTrue("加载后的组合应能立即运行");
        second.WaitForFlow("asr", AsserterModule.FailedSource).ShouldBeTrue();
        second.IssuesWith("断言器").ShouldBeEmpty("配置随组合件恢复，断言应通过");

        var resaved = second.PathFor("reference-composite-2.json");
        second.Services.SaveComposite(resaved, "reference").Succeeded.ShouldBeTrue();

        var before = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var after = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(resaved))!.AsObject();
        _ = before.Remove("savedAt");
        _ = after.Remove("savedAt");
        System.Text.Json.Nodes.JsonNode.DeepEquals(before, after)
            .ShouldBeTrue($"参考组合的往返应无损：\n前：{before}\n后：{after}");
    }

    // ------------------------------------------------------------ AC-5

    [Fact]
    public void AC5_同批新增多个模块不要求修改既有模块()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("gen", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("asr", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.Connect(
            new PortRef("gen", DataGeneratorModule.OutputPort),
            [new PortRef("asr", AsserterModule.InputPort)],
            "base").Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("gen", new DataGeneratorConfig
        {
            Waveform = GeneratorWaveform.Constant,
            Offset = 0,
            PeriodMs = 5,
        }).Succeeded.ShouldBeTrue();
        composition.Services.UpdateConfiguration("asr", new AsserterConfig { Expected = 0 }).Succeeded.ShouldBeTrue();

        composition.WaitForFlow("asr", AsserterModule.PassedSource).ShouldBeTrue();

        var existingGenerator = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "gen");
        var existingAsserter = composition.Runtime.Content.Instances.Single(i => i.InstanceId == "asr");
        var passedBefore = composition.Written("asr", AsserterModule.PassedSource);

        // 一批新增（2 组）——**不改动既有模块**
        composition.Services.AddModule("gen2", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("gen3", "hyw/reference-data-generator/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("asr2", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("asr3", "hyw/reference-asserter/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.Connect(new PortRef("gen2", DataGeneratorModule.OutputPort), [new PortRef("asr2", AsserterModule.InputPort)], "batch-2").Succeeded.ShouldBeTrue();
        composition.Services.Connect(new PortRef("gen3", DataGeneratorModule.OutputPort), [new PortRef("asr3", AsserterModule.InputPort)], "batch-3").Succeeded.ShouldBeTrue();

        composition.WaitForFlow("asr2", AsserterModule.PassedSource).ShouldBeTrue("新批次应即刻可流");
        composition.WaitForFlow("asr3", AsserterModule.PassedSource).ShouldBeTrue();

        // 既有模块：实例与配置未被触碰，数据流持续
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "gen").ShouldBeSameAs(existingGenerator);
        composition.Runtime.Content.Instances.Single(i => i.InstanceId == "asr").ShouldBeSameAs(existingAsserter);
        composition.Written("asr", AsserterModule.PassedSource).ShouldBeGreaterThan(passedBefore);
    }

    // ------------------------------------------------------------ AC-6

    [Fact]
    public void AC6_未知新形状的数据类型可原样承载_框架零改动()
    {
        // 依赖图断言：新增"新形状模块"后，框架程序集引用集仍不变（Core 零改动）
        var frameworkRefsBefore = FrameworkAssemblies.ToDictionary(a => a, ReadAssemblyReferences);

        using var composition = new Composition();
        composition.Register("new-shape", typeof(NewShapeSourceModule));
        composition.Register("new-shape-sink", typeof(NewShapeSinkModule));

        composition.Services.AddModule("ns", "test/new-shape/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("nk", "test/new-shape-sink/1.0.0").Succeeded.ShouldBeTrue();

        composition.Services.Connect(new PortRef("ns", NewShapeSourceModule.OutputPort), [new PortRef("nk", NewShapeSinkModule.InputPort)], "new-shape-link")
            .Succeeded.ShouldBeTrue();

        composition.WaitForFlow("ns", NewShapeSourceModule.ValueSource).ShouldBeTrue("新形状模块应能自行运行并写订阅缓冲");

        var frameworkRefsAfter = FrameworkAssemblies.ToDictionary(a => a, ReadAssemblyReferences);
        foreach (var (assembly, references) in frameworkRefsBefore)
        {
            frameworkRefsAfter[assembly].ShouldBe(references, $"{assembly} 的引用集不应因新增模块而改变");
        }
    }

    // ------------------------------------------------------------ 基础设施

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未能定位解决方案根目录（未找到 *.slnx/*.sln）");
    }

    /// <summary>参考组合现场：真实运行时 + 真实服务面 + 临时目录。</summary>
    private sealed class Composition : IDisposable
    {
        private readonly string _directory;

        public Composition()
        {
            _directory = Path.Combine(Path.GetTempPath(), "hyw-ac-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);

            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        /// <summary>装入三个参考桩模块包（走真实包加载路径）。</summary>
        public void InstallReferencePackages()
        {
            foreach (var module in new[] { "DataGenerator", "Recorder", "Asserter" })
            {
                var directory = PackagePath(module);
                Directory.Exists(directory).ShouldBeTrue($"参考模块未构建：{directory}（请先 build）");
                Services.InstallModulePackage(directory).Succeeded.ShouldBeTrue($"{module} 包应能加载");
            }
        }

        /// <summary>直接注册一个模块定义（测试局部模块用；不经过包加载）。</summary>
        public void Register(string name, Type entryType)
        {
            var definition = new Hosting.Registry.ModuleDefinition(
                ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        public string PathFor(string fileName) => Path.Combine(_directory, fileName);

        /// <summary>某实例某数据源的累计写入数（UI 侧可观测指标）。</summary>
        public long Written(string instanceId, string dataSourceName) =>
            Services.GetSubscriptionSources()
                .Where(s => string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal)
                         && string.Equals(s.DataSourceName, dataSourceName, StringComparison.Ordinal))
                .Select(s => s.Written)
                .FirstOrDefault();

        /// <summary>等待某实例某数据源开始产生数据。</summary>
        public bool WaitForFlow(string instanceId, string dataSourceName) =>
            WaitUntil(() => Written(instanceId, dataSourceName) > 0, TimeSpan.FromSeconds(5));

        /// <summary>断言数据流持续（计数还在增长）——用于"变更不中断既有链路"。</summary>
        public void AssertFlowContinues(string instanceId, string dataSourceName = AsserterModule.PassedSource)
        {
            var before = Written(instanceId, dataSourceName);
            WaitUntil(() => Written(instanceId, dataSourceName) > before, TimeSpan.FromSeconds(3))
                .ShouldBeTrue($"{instanceId} 的数据流不应因在线变更而中断");
        }

        /// <summary>归属对象或描述中包含关键词的 Issue。</summary>
        public IReadOnlyList<IssueView> IssuesWith(string keyword) =>
            [.. Services.GetIssues(200).Where(i =>
                i.Message.Contains(keyword, StringComparison.Ordinal)
                || i.ObjectPath.Contains(keyword, StringComparison.Ordinal))];

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
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
            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.Numeric, "value", "值")],
                [new ViewDataSource("value", unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32)]));
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
            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.Numeric, ValueSource, "计数")],
                [new ViewDataSource(ValueSource, unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U32)]));
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
