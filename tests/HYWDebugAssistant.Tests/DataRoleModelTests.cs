using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1【ADR-0048 §6 判据 1–9】：**模块数据角色的派生**——角色 =（方向 × 类型身份），模块内聚合、纯派生、只读。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>判据 1–5：角色的**粒度与身份**（多口聚合 / 方向分离 / 不同标识不合并 / 未声明类型回落 / 零口为空）；</item>
///   <item>判据 6–7：**角色对**的聚合与条数（跨网络 + 多源网络）；</item>
///   <item>判据 8–9：**反向断言**——角色不参与判定（口级连接不被聚合折叠、角色不进入持久化），
///     且角色视图为**只读**（调用前后组合快照逐字段一致）。</item>
/// </list>
/// <para>
/// 这些判据全部落在 <c>Core</c> 的公开查询面（<c>IRuntimeQuery.GetDataRoles/GetRolePairs</c>）上，
/// **不经过 UI**：P4 的方块与线只是这里结果的投影。
/// </para>
/// </remarks>
public class DataRoleModelTests
{
    /// <summary>参考形状（12B）——多个用例共用，保证"形状相同"这一点是受控的。</summary>
    private static readonly ContractShape Sample = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    private const string SampleTypeId = "test/role-sample";

    private const string SampleTypeKey = SampleTypeId + "@v1";

    private const string KeyA = "test/role-a";

    private const string KeyB = "test/role-b";

    // ------------------------------------------------------------ 判据 1–5：角色的粒度与身份

    [Fact]
    public void 判据1_同方向同类型的多个口_应聚合为一个角色()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "dual", "role-dual-sender", typeof(DualPortSender));

        var role = services.GetDataRoles("dual").ShouldHaveSingleItem();

        role.Key.ShouldBe($"send:{SampleTypeKey}");
        role.IsSend.ShouldBeTrue();
        role.TypeKey.ShouldBe(SampleTypeKey);
        role.Ports.ShouldBe(["out1", "out2"], "同方向 + 同类型身份的口属于同一个角色（一个角色多个口）");
        role.ConnectionCount.ShouldBe(0, "还没连：连接数是口级连接的计数，不是口数");
    }

    [Fact]
    public void 判据2_同类型但方向不同_应分为两个角色()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "both", "role-both-ways", typeof(SendReceiveModule));

        var roles = services.GetDataRoles("both");

        roles.Count.ShouldBe(2, "发送角色与接收角色即使类型身份相同，也是两个角色");
        roles[0].Key.ShouldBe($"send:{SampleTypeKey}");
        roles[0].IsSend.ShouldBeTrue();
        roles[1].Key.ShouldBe($"recv:{SampleTypeKey}");
        roles[1].IsSend.ShouldBeFalse();
        roles.SelectMany(r => r.Ports).ShouldBe(["out", "in"], "发送角色在前（渲染顺序稳定）");
    }

    [Fact]
    public void 判据3_不同标识但形状相同_不得合并为一个角色()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "twin", "role-twin-type", typeof(TwinTypeSender));

        var roles = services.GetDataRoles("twin");

        roles.Count.ShouldBe(2, "形状相同但标识不同 ⇒ **不是同一种量**（ADR-0051）⇒ 两个角色");
        roles.Select(r => r.Key).ShouldBe([$"send:{KeyA}@v1", $"send:{KeyB}@v1"]);
        roles.SelectMany(r => r.Ports).ShouldBe(["outA", "outB"]);
        roles.ShouldAllBe(r => r.TypeText == Sample.Describe(), "呈现文本相同，但角色键按类型身份区分");
    }

    [Fact]
    public void 判据4_未声明类型的旧模块_角色键应回落为结构描述()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "legacy", "role-legacy", typeof(LegacyUndeclaredSender));

        var role = services.GetDataRoles("legacy").ShouldHaveSingleItem();

        role.TypeKey.ShouldBe(Sample.Describe(), "未声明类型 ⇒ 类型身份 = 结构描述（角色照常可用）");
        role.TypeKey.Contains("@v", StringComparison.Ordinal)
            .ShouldBeFalse("回落路径不得伪装成「已声明类型」");
        role.Key.ShouldBe($"send:{Sample.Describe()}");
    }

    [Fact]
    public void 判据5_零口模块与不存在的实例_角色集合应为空()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "portless", "role-portless", typeof(PortlessModule));

        services.GetDataRoles("portless").ShouldBeEmpty("零口 ⇒ 没有角色（调用方不画任何方块）");
        services.GetRolePairs("portless").ShouldBeEmpty();
        services.GetDataRoles("does-not-exist").ShouldBeEmpty();
        services.GetRolePairs("does-not-exist").ShouldBeEmpty();
    }

    // ------------------------------------------------------------ 判据 6–7：角色对的聚合与条数

    [Fact]
    public void 判据6_同一角色对由多条网络承载_应聚合成一条并给出条数()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "dual", "role-dual-sender", typeof(DualPortSender));
        Add(runtime, "sink", "role-dual-receiver", typeof(DualPortReceiver));

        Connect(runtime, "dual", "out1", "sink", "in1", "n1");
        Connect(runtime, "dual", "out2", "sink", "in2", "n2");

        var pairs = services.GetRolePairs("dual");

        var pair = pairs.ShouldHaveSingleItem();
        pair.LocalRoleKey.ShouldBe($"send:{SampleTypeKey}");
        pair.PeerInstanceId.ShouldBe("sink");
        pair.PeerRoleKey.ShouldBe($"recv:{SampleTypeKey}");
        pair.Count.ShouldBe(2, "两条网络（两个口对）落在同一个角色对上 ⇒ 一条线 + ×2");
        pair.NetworkKeys.Count.ShouldBe(2);
        pair.NetworkKeys.ShouldBe(pair.NetworkKeys.Distinct(StringComparer.Ordinal), "网络身份键去重");
        pair.NetworkKeys.ShouldAllBe(k => !string.IsNullOrWhiteSpace(k));

        // 对称：宿端看到的也是**一条**角色对
        var reverse = services.GetRolePairs("sink").ShouldHaveSingleItem();
        reverse.LocalRoleKey.ShouldBe($"recv:{SampleTypeKey}");
        reverse.PeerInstanceId.ShouldBe("dual");
        reverse.PeerRoleKey.ShouldBe($"send:{SampleTypeKey}");
        reverse.Count.ShouldBe(2);

        // 角色级的连接数同样按口对计
        services.GetDataRoles("dual").ShouldHaveSingleItem().ConnectionCount.ShouldBe(2);
        services.GetDataRoles("sink").ShouldHaveSingleItem().ConnectionCount.ShouldBe(2);
    }

    [Fact]
    public void 判据7_多源网络_不同模块的源各出一条_同模块同类型口聚合成一条()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "a", "role-dual-sender", typeof(DualPortSender));
        Add(runtime, "b", "role-sample-sender", typeof(SampleSender));
        Add(runtime, "c", "role-sample-receiver", typeof(SampleReceiver));

        // 网络标签 = 端口的 UI 面（ADR-0034）：同名标签的发送口与接收口合成**一条多源网络**
        Label(runtime, "a", "out1", "bus");
        Label(runtime, "a", "out2", "bus");
        Label(runtime, "b", "out", "bus");
        Label(runtime, "c", "in", "bus");

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Sources.Count.ShouldBe(3, "三个发送口在一条多源网络上（多源本身合法：形状一致）");
        network.Targets.Count.ShouldBe(1);

        // 宿端：**两个不同模块的源** ⇒ 两个不同对端 ⇒ 两条线；a 的两个口同角色 ⇒ 仍有 ×2 的计数
        var pairs = services.GetRolePairs("c");

        pairs.Count.ShouldBe(2, "不同模块的源是不同的对端角色 ⇒ 各自一条线");
        pairs.Select(p => p.PeerInstanceId).ShouldBe(["a", "b"], "按对端实例稳定排序");
        pairs.ShouldAllBe(p => p.LocalRoleKey == $"recv:{SampleTypeKey}");
        pairs.ShouldAllBe(p => p.PeerRoleKey == $"send:{SampleTypeKey}");
        pairs.Single(p => p.PeerInstanceId == "a").Count.ShouldBe(2, "同一模块的两个同类型口 ⇒ 一条 + ×2");
        pairs.Single(p => p.PeerInstanceId == "b").Count.ShouldBe(1);

        // 源端：a 只看到"我 → c"这一条（两个口聚合）
        var fromA = services.GetRolePairs("a").ShouldHaveSingleItem();
        fromA.PeerInstanceId.ShouldBe("c");
        fromA.Count.ShouldBe(2);
        fromA.NetworkKeys.ShouldHaveSingleItem();

        services.GetRolePairs("b").ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    // ------------------------------------------------------------ 判据 8–9：反向断言与只读

    [Fact]
    public void 判据8_角色不参与判定_口级连接仍逐口建立且角色不进入持久化()
    {
        using var temp = new TempDirectory();
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        DualPortReceiver.Reset();

        Add(runtime, "dual", "role-dual-sender", typeof(DualPortSender));
        Add(runtime, "sink", "role-dual-receiver", typeof(DualPortReceiver));

        Connect(runtime, "dual", "out1", "sink", "in1", "n1");
        Connect(runtime, "dual", "out2", "sink", "in2", "n2");

        // 角色把两个口聚合成了一个角色 / 一条对，但**底层仍是两条口级连接**
        _ = services.GetDataRoles("dual").ShouldHaveSingleItem();
        var pair = services.GetRolePairs("dual").ShouldHaveSingleItem();
        pair.Count.ShouldBe(2, "聚合是**呈现**：口级连接数照实报（两条）");
        services.GetNetworks().Count.ShouldBe(2, "两条网络各自成立，不被角色聚合合并");

        // 最强的反向断言：两个口的**绑定都在**——各自发一个值，两边都收得到
        Send(runtime, "dual", "out1", seq: 1, value: 1.5f);
        Send(runtime, "dual", "out2", seq: 2, value: 2.5f);

        WaitUntil(() => DualPortReceiver.Values.Count >= 2, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("角色聚合**不得**折叠口级连接：两个口都必须真的能投递");

        DualPortReceiver.Values.OrderBy(v => v).ShouldBe([1.5f, 2.5f]);
        services.GetDataRoles("dual").ShouldHaveSingleItem().ConnectionCount.ShouldBe(2);

        // 角色是**派生视图**：不进组合（否则"改角色"就会被误当成"改拓扑"）。
        // 判据按**属性名**判：类型标识里本来就含 "role" 字样，逐字符搜文本会误报。
        var path = temp.PathFor("composite.json");
        services.SaveComposite(path, "roles").Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var keys = new List<string>();
        CollectKeys(root, keys);

        keys.ShouldNotContain(
            k => k.Contains("role", StringComparison.OrdinalIgnoreCase),
            "角色是纯派生视图，绝不进入组合件");
    }

    [Fact]
    public void 判据9_角色视图为只读_调用前后组合快照应逐字段一致()
    {
        using var temp = new TempDirectory();
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "dual", "role-dual-sender", typeof(DualPortSender));
        Add(runtime, "sink", "role-dual-receiver", typeof(DualPortReceiver));
        Add(runtime, "legacy", "role-legacy", typeof(LegacyUndeclaredSender));
        Add(runtime, "portless", "role-portless", typeof(PortlessModule));

        Connect(runtime, "dual", "out1", "sink", "in1", "n1");
        Connect(runtime, "legacy", "out", "sink", "in2", "n2");

        var before = temp.PathFor("before.json");
        services.SaveComposite(before, "roles").Succeeded.ShouldBeTrue();

        // 读遍所有实例的角色与角色对（含零口、含未声明类型）
        foreach (var instance in services.GetInstances())
        {
            _ = services.GetDataRoles(instance.InstanceId);
            _ = services.GetRolePairs(instance.InstanceId);
        }

        var after = temp.PathFor("after.json");
        services.SaveComposite(after, "roles").Succeeded.ShouldBeTrue();

        JsonNode.DeepEquals(ReadWithoutTimestamp(before), ReadWithoutTimestamp(after))
            .ShouldBeTrue("角色视图是**纯派生**：读取不得改变任何运行时状态（R10）");
    }

    // ------------------------------------------------------------ 测试辅助

    private static JsonObject ReadWithoutTimestamp(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        _ = root.Remove("savedAt");
        return root;
    }

    /// <summary>递归收集 JSON 里**全部属性名**（判"角色不进持久化"用）。</summary>
    private static void CollectKeys(JsonNode? node, List<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    keys.Add(name);
                    CollectKeys(value, keys);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    CollectKeys(item, keys);
                }

                break;

            default:
                break;
        }
    }

    private static void Add(ModuleRuntime runtime, string instanceId, string name, Type entryType)
    {
        var definition = new ModuleDefinition(
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

        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, definition)))
            .Applied.ShouldBeTrue($"{instanceId} 应登记成功");
    }

    private static void Connect(
        ModuleRuntime runtime,
        string sourceInstance,
        string sourcePort,
        string targetInstance,
        string targetPort,
        string networkName)
    {
        var result = runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember(sourceInstance, sourcePort, PortDirection.Send),
            [new NetworkMember(targetInstance, targetPort, PortDirection.Receive)],
            networkName)));

        result.Applied.ShouldBeTrue($"{sourceInstance}.{sourcePort} → {targetInstance}.{targetPort} 应连接成功");
    }

    private static void Label(ModuleRuntime runtime, string instanceId, string portName, string label)
    {
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, 0.5, false, label))))
            .Applied.ShouldBeTrue();
    }

    /// <summary>从指定口发出一个 <see cref="Sample"/> 形状的数据报（走真实投递路径）。</summary>
    private static void Send(ModuleRuntime runtime, string instanceId, string portName, ulong seq, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var layout = ContractLayout.FromShape(Sample);
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", seq);
        writer.WriteF32("value", value);

        _ = instance!.Context.Ports.TrySend(portName, payload);
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

    /// <summary>临时目录（持久化判据需要真实落盘）。</summary>
    private sealed class TempDirectory : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "hyw-roles-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => _ = Directory.CreateDirectory(_directory);

        public string PathFor(string fileName) => Path.Combine(_directory, fileName);

        public void Dispose()
        {
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

    // ------------------------------------------------------------ 测试模块

    /// <summary>两个**同方向同类型**的发送口（判据 1 / 6 / 7 / 8 / 9）。</summary>
    [ModuleDefinition("test", "role-dual-sender", "1.0.0")]
    public sealed class DualPortSender : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("out1", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
            context.Declaration.DeclarePort(new PortDeclaration("out2", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
        }
    }

    /// <summary>同一类型、方向相反的两种口（判据 2）。</summary>
    [ModuleDefinition("test", "role-both-ways", "1.0.0")]
    public sealed class SendReceiveModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
        }
    }

    /// <summary>两个**形状相同但标识不同**的类型，各占一个发送口（判据 3）。</summary>
    [ModuleDefinition("test", "role-twin-type", "1.0.0")]
    public sealed class TwinTypeSender : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(KeyA, 1, Sample));
            context.Declaration.DeclareDataType(new DataTypeDeclaration(KeyB, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("outA", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(KeyA, 1),
            });
            context.Declaration.DeclarePort(new PortDeclaration("outB", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(KeyB, 1),
            });
        }
    }

    /// <summary>**未声明类型**的旧式模块（判据 4）——口只给形状，不给类型引用。</summary>
    [ModuleDefinition("test", "role-legacy", "1.0.0")]
    public sealed class LegacyUndeclaredSender : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Sample));
    }

    /// <summary>零数据口（判据 5）。</summary>
    [ModuleDefinition("test", "role-portless", "1.0.0")]
    public sealed class PortlessModule : IModule
    {
        public void OnRegistered(IModuleContext context) => _ = context;
    }

    /// <summary>单发送口（判据 7 的第二个源）。</summary>
    [ModuleDefinition("test", "role-sample-sender", "1.0.0")]
    public sealed class SampleSender : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
        }
    }

    /// <summary>单接收口（判据 7 的宿）。</summary>
    [ModuleDefinition("test", "role-sample-receiver", "1.0.0")]
    public sealed class SampleReceiver : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
        }
    }

    /// <summary>两个同类型接收口（判据 6 / 8 / 9）。</summary>
    [ModuleDefinition("test", "role-dual-receiver", "1.0.0")]
    public sealed class DualPortReceiver : IModule
    {
        /// <summary>两个口收到的值（判据 8 的"两个口都真的能投递"取证）。</summary>
        public static List<float> Values { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, 1, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("in1", PortDirection.Receive, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });
            context.Declaration.DeclarePort(new PortDeclaration("in2", PortDirection.Receive, Sample)
            {
                Type = new DataTypeRef(SampleTypeId, 1),
            });

            var layout = ContractLayout.FromShape(Sample);

            foreach (var portName in new[] { "in1", "in2" })
            {
                context.Ports.SetReceiveHandler(portName, payload =>
                {
                    if (PayloadReader.TryCreate(payload, layout, out var reader))
                    {
                        lock (Values)
                        {
                            Values.Add(reader.ReadF32("value"));
                        }
                    }
                });
            }
        }

        /// <summary>清空观测（静态集合跨用例残留：xUnit 不保证类内用例顺序）。</summary>
        public static void Reset()
        {
            lock (Values)
            {
                Values.Clear();
            }
        }
    }
}
