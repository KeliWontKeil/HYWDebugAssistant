using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T4（M1-8）：组合件持久化——round-trip 无损、未知字段保真、缺失/损坏/拒绝加载、
/// 重锚与悬空、活动面与布局、配置恢复次序。
/// </summary>
public class CompositePersistenceTests
{
    /// <summary>配置类型在文件中的键（= 配置类型完整名）。</summary>
    private static readonly string ConfigTypeName = typeof(GeneratorConfig).FullName!;

    /// <summary>预期成员字面量。</summary>
    private static readonly string[] SinkInMember = ["s1.in"];

    /// <summary>预期悬空成员字面量。</summary>
    private static readonly string[] RenamedPortMember = ["s1.renamed-port"];

    // ------------------------------------------------------------ 无损往返（AC-4）

    [Fact]
    public void 往返_保存加载再保存应逐字段等价()    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();

        // 几何覆盖必须在建网**之前**：`UpdatePortGeometry` 是**整条替换**，Label 传 null 会清掉网络名
        // （产品侧 UI 读回全套字段，见 `ModuleWindowsViewModel.TryWriteAnchorGeometry`）。
        first.Services.UpdatePortGeometry("g1", new PortGeometryView(GeneratorModule.OutPort, 0.25, true)).Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");
        first.Services.UpdateConfiguration("g1", new GeneratorConfig { PeriodMs = 250, Gain = 2.5 }).Succeeded.ShouldBeTrue();
        first.Services.UpdateModuleLayout(new ModuleLayoutView("g1", 40, 60, 260, 140)).Succeeded.ShouldBeTrue();

        var path = first.PathFor("composite.json");
        first.Services.SaveComposite(path, "demo").Succeeded.ShouldBeTrue();

        // 加载进**全新**现场（同定义集）
        using var second = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        var instances = second.Services.GetInstances();
        instances.Count.ShouldBe(2);
        instances.ShouldNotContain(i => i.IsPlaceholder);

        var generator = instances.Single(i => i.InstanceId == "g1");
        generator.ModuleId.ShouldBe("test/gen");
        generator.State.ShouldBe("Active");
        generator.Ports.Single().AnchorOffset.ShouldBe(0.25);
        generator.Ports.Single().IsAnchorFixed.ShouldBeTrue();

        var networks = second.Services.GetNetworks();
        networks.Count.ShouldBe(1);
        networks[0].Name.ShouldBe("link-1");
        networks[0].Targets.ShouldBe(SinkInMember);
        networks[0].UnresolvedMembers.ShouldBeEmpty();

        var layout = second.Services.GetLayout().Single(l => l.InstanceId == "g1");
        layout.X.ShouldBe(40);
        layout.Y.ShouldBe(60);
        layout.Width.ShouldBe(260);
        layout.Height.ShouldBe(140);

        // 加载后**可运行**：数据经绑定直达目标
        SinkModule.Received.Clear();
        second.SendFrom("g1", GeneratorModule.OutPort, 1.5f).ShouldBeTrue();
        WaitUntil(() => SinkModule.Received.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue("加载后的网络应可投递");
        SinkModule.Received[0].ShouldBe(1.5f);

        // 再保存 → 除 savedAt 外逐字段等价
        var secondPath = second.PathFor("composite-2.json");
        second.Services.SaveComposite(secondPath, "demo").Succeeded.ShouldBeTrue();

        var before = ReadWithoutTimestamp(path);
        var after = ReadWithoutTimestamp(secondPath);
        JsonNode.DeepEquals(before, after).ShouldBeTrue($"往返应无损：\n前：{before}\n后：{after}");
    }

    [Fact]
    public void 往返_未知字段应在根_实例_网络_布局_配置各层保留()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        var path = harness.PathFor("external.json");

        File.WriteAllText(path, $$"""
        {
          "name": "external",
          "futureRootField": { "x": [1, 2, 3] },
          "instances": [
            {
              "instanceId": "g1",
              "moduleId": "test/gen",
              "enabled": true,
              "futureInstanceField": "keep-me",
              "configuration": {
                "{{typeof(GeneratorConfig).FullName}}": { "PeriodMs": 77, "Gain": 1.5, "futureConfigField": true }
              },
              "ports": { "out": { "offset": 0.3, "locked": false, "futurePortField": 9 } }
            }
          ],
          "networks": [
            { "name": "n1", "sources": ["g1.out"], "targets": [], "futureNetworkField": "n" }
          ],
          "layout": {
            "g1": { "x": 5, "y": 6, "width": 200, "height": 100, "viewVisible": true, "futureLayoutField": "L" }
          }
        }
        """);

        harness.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        // 配置已生效（已知字段）
        harness.Runtime.Content.TryGetInstance("g1", out var instance).ShouldBeTrue();
        instance!.Context.Configuration.Read<GeneratorConfig>().PeriodMs.ShouldBe(77);

        var savedPath = harness.PathFor("saved.json");
        harness.Services.SaveComposite(savedPath, "external").Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(savedPath))!.AsObject();
        root["futureRootField"]!["x"]!.AsArray().Count.ShouldBe(3, "根层未知字段应保留");

        var savedInstance = root["instances"]!.AsArray()[0]!.AsObject();
        savedInstance["futureInstanceField"]!.GetValue<string>().ShouldBe("keep-me", "实例层未知字段应保留");
        savedInstance["ports"]!["out"]!["futurePortField"]!.GetValue<int>().ShouldBe(9, "端口层未知字段应保留");

        var savedConfig = savedInstance["configuration"]![ConfigTypeName]!.AsObject();
        savedConfig["futureConfigField"]!.GetValue<bool>().ShouldBeTrue("配置对象内的未知字段应保留");
        savedConfig["PeriodMs"]!.GetValue<int>().ShouldBe(77, "已知配置字段按当前值写回");

        var savedNetwork = root["networks"]!.AsArray()[0]!.AsObject();
        savedNetwork["futureNetworkField"]!.GetValue<string>().ShouldBe("n", "网络层未知字段应保留");

        root["layout"]!["g1"]!["futureLayoutField"]!.GetValue<string>().ShouldBe("L", "布局层未知字段应保留");
    }

    // ------------------------------------------------------------ 缺失定义（占位）与重锚

    [Fact]
    public void 加载_缺失模块定义应占位并保留配置与网络成员()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");
        var path = first.PathFor("composite.json");
        first.Services.SaveComposite(path, "demo").Succeeded.ShouldBeTrue();

        // 加载侧**没有 sink 包**
        using var second = new Harness(("gen", typeof(GeneratorModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        var instances = second.Services.GetInstances();
        instances.Count.ShouldBe(2);

        var placeholder = instances.Single(i => i.InstanceId == "s1");
        placeholder.IsPlaceholder.ShouldBeTrue();
        placeholder.State.ShouldBe(RuntimeStateNames.Placeholder);
        placeholder.Ports.ShouldBeEmpty("占位实例不实例化、不建口");

        // 占位**不改动**其他部分：gen 照常运行，网络保留声明
        instances.Single(i => i.InstanceId == "g1").State.ShouldBe("Active");
        second.Runtime.Content.PlaceholderCount.ShouldBe(1);

        var network = second.Services.GetNetworks().Single();
        network.Source.ShouldBe("g1.out");
        network.UnresolvedMembers.ShouldBe(SinkInMember, "缺失实例的网络成员应悬空保留");

        second.Runtime.Issues.Recent(20).ShouldNotBeEmpty("占位保留必须登记一条事实");

        // 重新保存：占位与悬空成员都不丢（否则用户的配置会被"加载一次"吃掉）
        var resaved = second.PathFor("resaved.json");
        second.Services.SaveComposite(resaved, "demo").Succeeded.ShouldBeTrue();
        var root = JsonNode.Parse(File.ReadAllText(resaved))!.AsObject();
        root["instances"]!.AsArray().Count.ShouldBe(2);
        root["networks"]!.AsArray()[0]!["unresolved"]!.AsArray()[0]!.GetValue<string>().ShouldBe("s1.in");
    }

    [Fact]
    public void 占位_指定替代模块后应实例化并重新锚定悬空成员()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");
        first.Services.UpdateConfiguration("g1", new GeneratorConfig { PeriodMs = 111, Gain = 3 }).Succeeded.ShouldBeTrue();
        var path = first.PathFor("composite.json");
        first.Services.SaveComposite(path, "demo").Succeeded.ShouldBeTrue();

        using var second = new Harness(("gen", typeof(GeneratorModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();
        second.Services.GetInstances().Single(i => i.InstanceId == "s1").IsPlaceholder.ShouldBeTrue();

        // 补齐模块包（注册 sink）→ 手动指定替代版本
        second.Register("sink", typeof(SinkModule));
        var result = second.Services.ResolvePlaceholder("s1", "test/sink");

        result.Succeeded.ShouldBeTrue(result.Failures.Count > 0 ? result.Failures[0] : null);

        second.Services.GetInstances().ShouldNotContain(i => i.IsPlaceholder);
        second.Runtime.Content.InstanceCount.ShouldBe(2);

        var network = second.Services.GetNetworks().Single();
        network.UnresolvedMembers.ShouldBeEmpty("占位解析后悬空成员应自动重新锚定");
        network.IsStalled.ShouldBeFalse();

        SinkModule.Received.Clear();
        second.SendFrom("g1", GeneratorModule.OutPort, 4.25f).ShouldBeTrue();
        WaitUntil(() => SinkModule.Received.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue("重锚后应恢复投递");
        SinkModule.Received[0].ShouldBe(4.25f);
    }

    // ------------------------------------------------------------ 异常加载

    [Fact]
    public void 加载_单条记录损坏应跳过该条并保留其余()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        var path = harness.PathFor("partial.json");

        File.WriteAllText(path, """
        {
          "name": "partial",
          "instances": [
            { "instanceId": "broken", "enabled": true },
            { "instanceId": "g1", "moduleId": "test/gen", "enabled": true }
          ],
          "networks": []
        }
        """);

        var result = harness.Services.LoadComposite(path);

        result.Succeeded.ShouldBeTrue("损坏 → **尽量部分恢复**，不放弃整次加载");
        harness.Services.GetInstances().ShouldHaveSingleItem().InstanceId.ShouldBe("g1");
        harness.Runtime.Issues.Recent(20).ShouldNotBeEmpty("跳过损坏实例必须登记一条事实");
    }

    [Fact]
    public void 加载_不可解析应拒绝且原文件不变()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        var path = harness.PathFor("broken.json");
        const string broken = "{ this is not json ";
        File.WriteAllText(path, broken);

        var result = harness.Services.LoadComposite(path);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldHaveSingleItem();
        File.ReadAllText(path).ShouldBe(broken, "拒绝加载**绝不改写**用户文件");
        harness.Runtime.Content.InstanceCount.ShouldBe(0);    }


    [Fact]
    public void 加载_合法组通道配置的结构深度不应被误拒()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workspace-depth-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // 根 → instances[] → 实例 → configuration → groups[] → 组 → channels[] → 通道 → 字段
            const string document = """
            {
              "name": "workspace",
              "instances": [
                {
                  "instanceId": "d1",
                  "moduleId": "hyw/waveform-display",
                  "configuration": {
                    "Groups": [
                      { "GroupId": 1, "Channels": [ { "Name": "CH1", "Visible": true } ] }
                    ]
                  }
                }
              ],
              "networks": [],
              "layout": {}
            }
            """;
            File.WriteAllText(path, document);

            CompositeStore.TryRead(path, out var root, out var outcome, out var error).ShouldBeTrue();
            outcome.ShouldBe(CompositeReadOutcome.Success);
            error.ShouldBeNull();
            root.ShouldNotBeNull();
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
    public void 加载_超过结构深度上限应拒绝()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        var path = harness.PathFor("deep.json");

        // 65 层嵌套（上限 64）
        var deep = "1";
        for (var index = 0; index < 65; index++)
        {
            deep = "{\"a\":" + deep + "}";
        }

        File.WriteAllText(path, """
        {
          "instances": [],
          "networks": [],
          "deep":
        """ + deep + "}");

        var result = harness.Services.LoadComposite(path);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldHaveSingleItem();
    }

    [Fact]
    public void 加载_超过文件大小上限应拒绝()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        var path = harness.PathFor("huge.json");

        // 16 MB + 1 字节（内容无关：先判大小，再判语法）
        using (var stream = File.Create(path))
        {
            stream.SetLength(CompositeStore.MaxFileSizeBytes + 1);
        }

        var result = harness.Services.LoadComposite(path);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldHaveSingleItem();
    }

    // ------------------------------------------------------------ 重锚、活动面、配置

    [Fact]
    public void 加载_口消失应产生悬空成员且不建立绑定()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        var path = harness.PathFor("dangling.json");

        File.WriteAllText(path, """
        {
          "instances": [
            { "instanceId": "g1", "moduleId": "test/gen", "enabled": true },
            { "instanceId": "s1", "moduleId": "test/sink", "enabled": true }
          ],
          "networks": [
            { "name": "link-1", "sources": ["g1.out"], "targets": ["s1.renamed-port"] }
          ]
        }
        """);

        harness.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().Single();
        network.Targets.ShouldBeEmpty();
        network.UnresolvedMembers.ShouldBe(RenamedPortMember);
        network.Sources.ShouldBe(["g1.out"], "源口在场 ⇒ 源**锚定**（宿悬空不影响源的在场）");

        // 宿全悬空 ⇒ **该源没有兼容宿 = 该源不可用**（不建绑定、报 Issue）；
        // 但 **不是停流**（停流专指"全部源未锚定"），网络与声明照常保留。
        network.IsStalled.ShouldBeFalse("宿悬空**不是**停流");
        harness.Runtime.Bindings.Bindings.ShouldBeEmpty("没有可投递的宿 ⇒ 不建立绑定");
        harness.Runtime.Issues.Recent(20).ShouldNotBeEmpty("悬空宿必须登记一条事实");

        // 悬空成员不进目标列表 → 发送被丢弃但不抛异常
        harness.SendFrom("g1", GeneratorModule.OutPort, 1f).ShouldBeFalse();
        harness.Runtime.Issues.Recent(20).ShouldNotBeEmpty("悬空宿成员必须登记一条事实");
    }

    // ------------------------------------------------------------ 多源网络



    [Fact]
    public void 加载_多源网络_部分源未锚定应不停流且逐源报Issue()
    {
        // **部分源不可用 ≠ 停流**：其余源照常投递，只对该源报 Issue；
        // 未锚定的源**保留声明**（进 UnresolvedSources），补齐模块后自动重锚。
        using var harness = new Harness(("sink", typeof(SinkModule)));
        var path = harness.PathFor("multi-partial.json");

        File.WriteAllText(path, """
        {
          "instances": [
            { "instanceId": "g1", "moduleId": "test/gen", "enabled": true },
            { "instanceId": "g2", "moduleId": "test/gen", "enabled": true },
            { "instanceId": "s1", "moduleId": "test/sink", "enabled": true }
          ],
          "networks": [
            { "name": "bus", "isAutoGenerated": false, "sources": ["g1.out", "g2.out"], "targets": ["s1.in"] }
          ]
        }
        """);

        // 加载侧**没有 gen 包** ⇒ g1/g2 都成占位（无口）
        harness.Services.LoadComposite(path).Succeeded.ShouldBeTrue();
        harness.Services.GetInstances().Where(i => i.InstanceId is "g1" or "g2")
            .ShouldAllBe(i => i.IsPlaceholder);

        // 只补齐 g1 → g1 锚定、g2 仍悬空
        harness.Register("gen", typeof(GeneratorModule));
        var resolve1 = harness.Services.ResolvePlaceholder("g1", "test/gen");
        resolve1.Succeeded.ShouldBeTrue(resolve1.Failures.Count > 0 ? resolve1.Failures[0] : null);

        var network = harness.Services.GetNetworks().Single(n => n.Name == "bus");
        network.Sources.Count.ShouldBe(2, "**声明保留**：未锚定的源也在视图的源集合里（在场在前、悬空在后）");
        network.Sources.ShouldContain("g2.out");
        network.IsStalled.ShouldBeFalse("**部分源未锚定不是停流**（全部源未锚定才是）");

        var bindings = harness.Runtime.Bindings.Bindings.Where(b => b.Network.Name == "bus").ToList();
        bindings.Count.ShouldBe(1, "只有锚定的那个源建立绑定");
        bindings[0].Source.PortName.ShouldBe(GeneratorModule.OutPort);
        bindings[0].Source.InstanceId.ShouldBe("g1");

        harness.Runtime.Issues.Recent(30).ShouldNotBeEmpty("未锚定源必须登记一条事实");

        harness.SendFrom("g1", GeneratorModule.OutPort, 2f).ShouldBeTrue("**其余源照常投递**");

        // 补齐 g2 → 第二个源自动重锚，两个源各成一条绑定
        harness.Services.ResolvePlaceholder("g2", "test/gen").Succeeded.ShouldBeTrue();

        harness.Runtime.Bindings.Bindings.Count(b => b.Network.Name == "bus").ShouldBe(2, "补齐后逐源重新锚定");
        harness.SendFrom("g2", GeneratorModule.OutPort, 3f).ShouldBeTrue();
    }

    [Fact]
    public void 加载_多源网络_全部源未锚定时应停流且保留声明()
    {
        // **停流 = 全部源未锚定**（合法降级态）：声明保留、不建绑定、不投递。
        using var harness = new Harness(("sink", typeof(SinkModule)));
        var path = harness.PathFor("multi-stalled.json");

        File.WriteAllText(path, """
        {
          "instances": [
            { "instanceId": "g1", "moduleId": "test/gen", "enabled": true },
            { "instanceId": "g2", "moduleId": "test/gen", "enabled": true },
            { "instanceId": "s1", "moduleId": "test/sink", "enabled": true }
          ],
          "networks": [
            { "name": "bus", "isAutoGenerated": false, "sources": ["g1.out", "g2.out"], "targets": ["s1.in"] }
          ]
        }
        """);

        harness.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().Single(n => n.Name == "bus");
        network.IsStalled.ShouldBeTrue("**全部源未锚定 ⇒ 停流**");
        network.Sources.ShouldBe(["g1.out", "g2.out"], "停流也**保留全部源声明**");
        network.Targets.ShouldBeEmpty();
        network.UnresolvedMembers.ShouldBe(SinkInMember, "源未锚定 ⇒ 宿也无从锚定（声明保留）");

        harness.Runtime.Bindings.Bindings.ShouldNotContain(b => b.Network.Name == "bus", "停流 = 不建绑定");
        harness.Runtime.Issues.Recent(30).ShouldNotBeEmpty("停流必须登记一条事实");

        // 补齐两个源 → 自动重锚，恢复投递
        harness.Register("gen", typeof(GeneratorModule));
        var resolveA = harness.Services.ResolvePlaceholder("g1", "test/gen");
        resolveA.Succeeded.ShouldBeTrue(resolveA.Failures.Count > 0 ? resolveA.Failures[0] : null);
        var resolveB = harness.Services.ResolvePlaceholder("g2", "test/gen");
        resolveB.Succeeded.ShouldBeTrue(resolveB.Failures.Count > 0 ? resolveB.Failures[0] : null);

        harness.Services.GetNetworks().Single(n => n.Name == "bus").IsStalled.ShouldBeFalse();
        harness.Services.GetNetworks().Single(n => n.Name == "bus").Sources
            .ShouldBe(["g1.out", "g2.out"], "两个源都回到场内");
        harness.Runtime.Bindings.Bindings.Count(b => b.Network.Name == "bus").ShouldBe(2);
        harness.SendFrom("g1", GeneratorModule.OutPort, 1.5f).ShouldBeTrue();
    }

    [Fact]
    public void 加载_禁用实例应保持禁用且停流()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");
        first.Services.SetModuleEnabled("s1", false).Succeeded.ShouldBeTrue();

        var path = first.PathFor("disabled.json");
        first.Services.SaveComposite(path, "disabled").Succeeded.ShouldBeTrue();

        using var second = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        var sink = second.Services.GetInstances().Single(i => i.InstanceId == "s1");
        sink.State.ShouldBe(ModuleInstanceState.Disabled.ToString(), "活动面（启用/禁用）随组合件保存");

        second.Runtime.Bindings.TryGet("link-1", out var binding).ShouldBeTrue();
        binding!.IsSuspended.ShouldBeTrue("禁用 = 停流，绑定保留");
        second.SendFrom("g1", GeneratorModule.OutPort, 1f).ShouldBeFalse();
    }

    [Fact]
    public void 加载_配置应在激活前恢复()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        first.Services.UpdateConfiguration("g1", new GeneratorConfig { PeriodMs = 321 }).Succeeded.ShouldBeTrue();

        var path = first.PathFor("config.json");
        first.Services.SaveComposite(path, "config").Succeeded.ShouldBeTrue();

        SinkModule.Received.Clear();
        GeneratorModule.ActivatedPeriods.Clear();
        using var second = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        // 加载流程：实例化 → **恢复配置** → 激活（OnActivated 读到的是文件里的值）
        second.Runtime.Content.TryGetInstance("g1", out var generator).ShouldBeTrue();
        generator!.Context.Configuration.Read<GeneratorConfig>().PeriodMs.ShouldBe(321);
        GeneratorModule.ActivatedPeriods.ShouldContain(321, "配置必须在 OnActivated **之前**恢复");
    }

    [Fact]
    public void 加载_不应触发自动建网()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        // 组合件里**没有**网络（R 项关闭时保存的现场）
        var path = first.PathFor("no-network.json");
        first.Services.SaveComposite(path, "no-network").Succeeded.ShouldBeTrue();

        // 加载侧**开启**快速建网：加载期间的网络应完全来自文件（不得复制出自动连线）
        using var second = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        second.Runtime.UpdateAlignAutoNetwork(true);
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        second.Services.GetNetworks().ShouldBeEmpty("加载不参与自动建网：网络只来自文件");
    }

    // ------------------------------------------------------------ 布局（公共面）

    [Fact]
    public void 布局_经变更入口更新并可查询_非法值应整体拒绝()
    {
        using var harness = new Harness(("gen", typeof(GeneratorModule)));
        harness.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();

        harness.Services.GetLayout().ShouldBeEmpty("未摆放过的实例不在布局中");

        harness.Services.UpdateModuleLayout(new ModuleLayoutView("g1", 10, 20, 240, 120)).Succeeded.ShouldBeTrue();
        var layout = harness.Services.GetLayout().ShouldHaveSingleItem();
        layout.X.ShouldBe(10);
        layout.IsViewVisible.ShouldBeTrue();

        harness.Services.UpdatePortGeometry("g1", new PortGeometryView(GeneratorModule.OutPort, 0.75, true)).Succeeded.ShouldBeTrue();
        var port = harness.Services.GetInstances().Single().Ports.Single();
        port.AnchorOffset.ShouldBe(0.75);
        port.IsAnchorFixed.ShouldBeTrue();

        // 非法值：整体拒绝 + 零副作用
        harness.Services.UpdateModuleLayout(new ModuleLayoutView("g1", double.NaN, 0, 240, 120)).Succeeded.ShouldBeFalse();
        harness.Services.UpdateModuleLayout(new ModuleLayoutView("g1", 0, 0, 0, 120)).Succeeded.ShouldBeFalse();
        harness.Services.UpdateModuleLayout(new ModuleLayoutView("ghost", 0, 0, 240, 120)).Succeeded.ShouldBeFalse();
        harness.Services.UpdatePortGeometry("g1", new PortGeometryView(GeneratorModule.OutPort, 1.5)).Succeeded.ShouldBeFalse();

        harness.Services.GetLayout().ShouldHaveSingleItem().X.ShouldBe(10);
        harness.Services.GetInstances().Single().Ports.Single().AnchorOffset.ShouldBe(0.75);

        // 实例移除 → 布局级联清理
        harness.Services.RemoveModule("g1").Succeeded.ShouldBeTrue();
        harness.Services.GetLayout().ShouldBeEmpty();
    }

    // ------------------------------------------------------------ 端口锚点「固定」三态（G5 · C5-7）

    [Fact]
    public void 端口锚点固定_未设置即框架固定_用户设置后覆盖_且解锁false也要落盘()
    {
        using var first = new Harness(
            ("gen", typeof(GeneratorModule)),
            ("draggable-anchor", typeof(DraggableAnchorModule)));

        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("a1", "test/draggable-anchor").Succeeded.ShouldBeTrue();

        // ① **未设置**（组合件里没有 `locked`）⇒ 框架固定项：**锚点默认固定**（保守默认）。
        //    声明面已无「可拖」这一项（可拖动性不是声明能力），两个口都按固定解析。
        Anchor(first, "g1").IsAnchorFixed.ShouldBeTrue("未设置 ⇒ 框架固定项（默认固定）");
        Anchor(first, "a1").IsAnchorFixed.ShouldBeTrue("声明面没有可拖这一项 ⇒ 默认固定");

        // ② 用户**解锁**（显式 false）—— 三态里的"具体值"，必须覆盖框架缺省
        first.Services.UpdatePortGeometry(
            "g1",
            new PortGeometryView(GeneratorModule.OutPort, 0.5, IsAnchorFixed: false)).Succeeded.ShouldBeTrue();
        Anchor(first, "g1").IsAnchorFixed.ShouldBeFalse("用户设过 ⇒ 以用户为准");

        var path = first.PathFor("anchor.json");
        first.Services.SaveComposite(path, "anchor").Succeeded.ShouldBeTrue();

        // ③ `false` 必须**真的写进文件**：不写就等于把用户的解锁丢掉（重载后又变成固定）
        var locked = ReadWithoutTimestamp(path)["instances"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(n => (string?)n["instanceId"] == "g1")["ports"]!
            .AsObject()[GeneratorModule.OutPort]!
            .AsObject()["locked"];
        locked.ShouldNotBeNull("显式解锁（false）必须落盘");
        locked!.GetValue<bool>().ShouldBeFalse();

        using var second = new Harness(
            ("gen", typeof(GeneratorModule)),
            ("draggable-anchor", typeof(DraggableAnchorModule)));
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();

        Anchor(second, "g1").IsAnchorFixed.ShouldBeFalse("重载后用户的解锁必须还在");
        Anchor(second, "a1").IsAnchorFixed.ShouldBeTrue("没设置过的口是框架固定项（默认固定）");

        // ④ 反向：**默认值不写文件**（缺省不写）—— 没动过的口不该在组合件里留下 `locked`
        AnchorGeometry(second, "a1").IsAnchorFixed.ShouldBeNull("用户没设置过 ⇒ 三态保持 null（不写文件）");
    }

    /// <summary>取某实例唯一端口的**查询面**（已解析的有效固定态）。</summary>
    private static PortView Anchor(Harness harness, string instanceId) => PortOf(harness, instanceId);

    /// <summary>取某实例唯一端口的**查询面**（含已解析的有效边 / 覆盖值）。</summary>
    private static PortView PortOf(Harness harness, string instanceId) =>
        harness.Services.GetInstances().Single(i => i.InstanceId == instanceId).Ports.Single();

    /// <summary>取某实例唯一端口的**几何**（三态原始值，用来断言"缺省不写"）。</summary>
    private static PortGeometryView AnchorGeometry(Harness harness, string instanceId) =>
        harness.Runtime.Content.Layout.GetPort(instanceId, Anchor(harness, instanceId).Name);

    // ------------------------------------------------------------ 加载 = 加载一批模块

    [Fact]
    public void 加载_同一文件两次_冲突ID应重映射且同名网络合并()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");

        var path = first.PathFor("twice.json");
        first.Services.SaveComposite(path, "twice").Succeeded.ShouldBeTrue();

        using var second = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));

        // 第一次：空现场，ID 原样
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();
        second.Services.GetInstances().Count.ShouldBe(2);
        second.Services.GetInstances().ShouldContain(instance => instance.InstanceId == "g1");
        second.Services.GetInstances().ShouldContain(instance => instance.InstanceId == "s1");

        // 第二次：冲突 ID 生成新 ID；同名网络成员合并，不出现两条同名网络
        second.Services.LoadComposite(path).Succeeded.ShouldBeTrue();
        second.Services.GetInstances().Count.ShouldBe(4);
        second.Services.GetInstances().ShouldContain(instance => instance.InstanceId == "g1-2");
        second.Services.GetInstances().ShouldContain(instance => instance.InstanceId == "s1-2");

        var networks = second.Services.GetNetworks();
        networks.Count.ShouldBe(1, "同名网络必须合并为一条（与标签同名即连一致）");
        networks[0].Name.ShouldBe("link-1");
        networks[0].Sources.Count.ShouldBe(2);
        networks[0].Targets.Count.ShouldBe(2);
    }

    [Fact]
    public void 重启快照_保存后恢复并删除文件()
    {
        using var first = new Harness(("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        first.Services.AddModule("g1", "test/gen").Succeeded.ShouldBeTrue();
        first.Services.AddModule("s1", "test/sink").Succeeded.ShouldBeTrue();
        TestLink.Link(first.Services, new PortRef("g1", GeneratorModule.OutPort), new PortRef("s1", SinkModule.InPort), "link-1");

        first.Services.SaveRestartSnapshot().Succeeded.ShouldBeTrue();
        File.Exists(first.RestartPath).ShouldBeTrue();

        using var second = new Harness(first.RestartPath, ("gen", typeof(GeneratorModule)), ("sink", typeof(SinkModule)));
        second.Services.RestoreRestartSnapshotIfAny().Succeeded.ShouldBeTrue();

        second.Services.GetInstances().Count.ShouldBe(2);
        second.Services.GetNetworks().Single().Name.ShouldBe("link-1");
        File.Exists(first.RestartPath).ShouldBeFalse("恢复成功后快照应删除");
    }

    // ------------------------------------------------------------ 测试辅助

    private static JsonObject ReadWithoutTimestamp(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        _ = root.Remove("savedAt");
        return root;
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

    /// <summary>测试现场：运行时 + 公开面服务 + 临时目录。</summary>
    private sealed class Harness : IDisposable
    {
        private readonly string _directory;

        public Harness(params (string Name, Type EntryType)[] modules)
            : this(restartPath: null, modules)
        {
        }

        public Harness(string? restartPath, params (string Name, Type EntryType)[] modules)
        {
            _directory = Path.Combine(Path.GetTempPath(), "hyw-composite-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
            RestartPath = restartPath ?? Path.Combine(_directory, "restart.json");

            Runtime = new ModuleRuntime();
            Runtime.UpdateAlignAutoNetwork(false);
            Services = new RuntimeServices(Runtime, restartSnapshotPath: RestartPath);

            foreach (var (name, entryType) in modules)
            {
                Register(name, entryType);
            }
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public string RestartPath { get; }

        public void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                entryType,
                packageDirectory: string.Empty,
                assemblyPath: string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        public string PathFor(string fileName) => Path.Combine(_directory, fileName);

        /// <summary>以 "g1" 实例的发送口发出一个 f32（走真实投递路径）。</summary>
        public bool SendFrom(string instanceId, string port, float value)
        {
            Runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();
            var payload = new byte[sizeof(float)];
            BitConverter.TryWriteBytes(payload, value);
            return instance!.Context.Ports.TrySend(port, payload);
        }

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

    /// <summary>测试模块：发送口（f32）+ 可持久化配置（在 <c>OnActivated</c> 记录读到的配置以验证恢复次序）。</summary>
    [ModuleDefinition("test", "gen")]
    public sealed class GeneratorModule : IModule
    {
        public const string OutPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        /// <summary>激活时读到的配置值（验证"恢复配置在激活之前"）。</summary>
        public static List<int> ActivatedPeriods { get; } = [];

        private IModuleContext? _context;

        public void OnRegistered(IModuleContext context)
        {
            _context = context;
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape));
        }

        public void OnActivated() => ActivatedPeriods.Add(_context!.Configuration.Read<GeneratorConfig>().PeriodMs);
    }

    /// <summary>测试模块：接收口（f32），把收到的值收集到静态列表。</summary>
    [ModuleDefinition("test", "sink")]
    public sealed class SinkModule : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        public static List<float> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape));
            context.Ports.SetReceiveHandler(InPort, payload => Received.Add(BitConverter.ToSingle(payload)));
        }
    }

    /// <summary>测试配置（模块自有类型：持久化走反射式序列化）。</summary>
    public sealed class GeneratorConfig
    {
        public int PeriodMs { get; set; } = 100;

        public double Gain { get; set; } = 1.0;
    }

    /// <summary>
    /// 测试模块：口的边、偏移都写在代码声明里 —— 用来对照"声明值即事实源"。
    /// **可拖动性不在声明面**：锚点默认固定，只能由用户显式解锁。
    /// </summary>
    [ModuleDefinition("test", "draggable-anchor")]
    public sealed class DraggableAnchorModule : IModule
    {
        /// <summary>口名。</summary>
        public const string OutPort = "out";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                OutPort,
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32))
            {
                // 几何的**唯一来源是代码声明**：这条口的默认位置写在这里。
                Edge = PortEdge.Top,
                AnchorOffset = 0.25,
            });
    }
}
