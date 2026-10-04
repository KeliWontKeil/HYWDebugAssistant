using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
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
/// **"同名标签即连"** —— 命名网络由端口标签派生（用户可见的唯一建网机制）。
/// </summary>
public class PortNetworkLabelTests
{
    /// <summary>期望的宿成员字面量（避免重复构造常量数组）。</summary>
    private static readonly string[] ReceiverIn = ["r.in"];

    /// <summary>期望的宿成员（1:N 场景）。</summary>
    private static readonly string[] ReceiverFanOut = ["r1.in", "r2.in"];

    [Fact]
    public void 同名标签_应把发送口与接收口自动连成一条命名网络()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");
        harness.Add("r", "receiver");

        harness.Services.UpdatePortNetwork("s", "out", "bus-a").Succeeded.ShouldBeTrue();
        harness.Services.GetNetworks().ShouldBeEmpty("只有发送端有标签时还不成网络");

        harness.Services.UpdatePortNetwork("r", "in", "bus-a").Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().ShouldHaveSingleItem();
        network.Name.ShouldBe("bus-a");
        network.Source.ShouldBe("s.out");
        network.Targets.ShouldBe(ReceiverIn);
        network.IsAnonymous.ShouldBeFalse();

        // 口视图应带上标签与"已连接"
        var receiver = harness.Services.GetInstances().Single(i => i.InstanceId == "r");
        receiver.Ports.Single().NetworkLabel.ShouldBe("bus-a");
        receiver.Ports.Single().IsConnected.ShouldBeTrue();
    }

    [Fact]
    public void 同名标签_应支持_1对N扇出()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");
        harness.Add("r1", "receiver");
        harness.Add("r2", "receiver");

        harness.Services.UpdatePortNetwork("s", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r2", "in", "bus").Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().ShouldHaveSingleItem();
        network.Targets.Count.ShouldBe(2);
        network.Targets.ShouldContain("r1.in");
        network.Targets.ShouldContain("r2.in");
    }

    [Fact]
    public void 不同标签_不应建网()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");
        harness.Add("r", "receiver");

        harness.Services.UpdatePortNetwork("s", "out", "a").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r", "in", "b").Succeeded.ShouldBeTrue();

        harness.Services.GetNetworks().ShouldBeEmpty();
    }

    [Fact]
    public void 清除标签_应退出网络()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");
        harness.Add("r", "receiver");
        harness.Services.UpdatePortNetwork("s", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r", "in", "bus").Succeeded.ShouldBeTrue();
        harness.Services.GetNetworks().ShouldHaveSingleItem();

        harness.Services.UpdatePortNetwork("r", "in", null).Succeeded.ShouldBeTrue();

        harness.Services.GetNetworks().ShouldBeEmpty();
        harness.Services.GetInstances().Single(i => i.InstanceId == "r").Ports.Single().NetworkLabel.ShouldBeNull();
    }

    [Fact]
    public void 多个发送端同名_应建成一条多源网络()
    {
        // 多源是一等设计：同名标签的多个发送端同属**一条多源网络**，并**逐源**建立绑定；
        // 「网络只能有一个源、否则整条不建网」的规则**已作废**。
        using var harness = new LabelHarness();
        harness.Add("s1", "sender");
        harness.Add("s2", "sender");
        harness.Add("r", "receiver");

        harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("s2", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r", "in", "bus").Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().ShouldHaveSingleItem();
        network.Name.ShouldBe("bus");
        network.Sources.Count.ShouldBe(2, "**多源 = 合法的一等组合**（同名标签的多个发送端同属一条网络）");
        network.Targets.ShouldBe(ReceiverIn);
        harness.Runtime.Issues.Recent(20)
            .ShouldNotContain(e => e.Issue.Severity == HYWDebugAssistant.Sdk.Reporting.IssueSeverity.Error,
                "多源**不再是错误**（旧行为是报 CompositionError 并整条拒绝）");
    }

    [Fact]
    public void 契约不兼容的成员_应被排除并给出原因()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");            // f32
        harness.Add("r", "receiver");          // f32
        harness.Add("w", "wide-receiver");     // f64

        harness.Services.UpdatePortNetwork("s", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r", "in", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("w", "in", "bus").Succeeded.ShouldBeTrue();

        var network = harness.Services.GetNetworks().ShouldHaveSingleItem();
        network.Targets.ShouldBe(ReceiverIn, "不兼容成员被排除（不自动转换）");
        harness.Runtime.Issues.Recent(20).ShouldContain(e => e.Issue.Message.Contains("不兼容", StringComparison.Ordinal));
    }

    // 「命名口自动从匿名直连中摘出」的用例随「加入即连」整体移除：
    // 新规则下**显式建立的网络一律不动**（用户意图优先），命名口只是不参与"对齐建网"。


    [Fact]
    public void 非法标签名_应被拒绝()
    {
        using var harness = new LabelHarness();
        harness.Add("s", "sender");

        harness.Services.UpdatePortNetwork("s", "out", "含 空格 的名字").Succeeded.ShouldBeFalse();
        harness.Services.UpdatePortNetwork("s", "out", new string('x', 65)).Succeeded.ShouldBeFalse();
        harness.Services.GetInstances().Single().Ports.Single().NetworkLabel.ShouldBeNull();
    }

    /// <summary>标签测试现场（真实运行时 + 少量测试模块）。</summary>
    private sealed class LabelHarness : IDisposable
    {
        public LabelHarness(bool alignAutoNetwork = false)
        {
            Runtime = new ModuleRuntime(
                issues: null,
                carrierOptions: null);

            // 对齐自动建网：标签测试默认关闭，避免几何对齐干扰"仅标签"的判定
            Runtime.UpdateAlignAutoNetwork(alignAutoNetwork);

            Services = new RuntimeServices(Runtime);

            Register("sender", typeof(SenderModule));
            Register("receiver", typeof(ReceiverModule));
            Register("wide-receiver", typeof(WideReceiverModule));
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public void Add(string instanceId, string kind) =>
            Services.AddModule(instanceId, $"test/{kind}/1.0.0").Succeeded.ShouldBeTrue();

        public void Dispose() => Runtime.Dispose();

        private void Register(string name, Type entryType)
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

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    [ModuleDefinition("test", "sender", "1.0.0")]
    public sealed class SenderModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    [ModuleDefinition("test", "receiver", "1.0.0")]
    public sealed class ReceiverModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    [ModuleDefinition("test", "wide-receiver", "1.0.0")]
    public sealed class WideReceiverModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F64)));
    }
}