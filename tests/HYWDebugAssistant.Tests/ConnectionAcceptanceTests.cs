using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **建网只有一条判定链路**：候选（标签 / 贴合）→ 唯一接受判定（方向 + 契约）→ 落地（成员只收被接受的对）。
/// </summary>
/// <remarks>
/// <para>
/// 判据不是"某条消息的措辞"，而是两件事：① 标签与贴合走**同一条接受判定**（预览候选与落地同一集合）；
/// ② 拒绝必须带**具体原因**（不是静默不连）。
/// </para>
/// </remarks>
public class ConnectionAcceptanceTests
{
    /// <summary>同一标识 + 同一版本的两口贴合 ⇒ 正常建网（统一判定没有把正常路径一起关掉）。</summary>
    [Fact]
    public void 贴合_同标识同形状_应建网()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s", "alpha-sender", typeof(AlphaSender));
        AddModule(runtime, "r", "alpha-receiver", typeof(AlphaReceiver));

        Place(runtime, "s", 0, 0, 300, 200);
        Place(runtime, "r", 300, 0, 300, 200);
        SetEdge(runtime, "s", "out", PortEdge.Right);
        SetEdge(runtime, "r", "in", PortEdge.Left);

        var network = runtime.Content.Networks.ShouldHaveSingleItem();
        network.Sources.ShouldHaveSingleItem().ToString().ShouldBe("s.out");
        network.Targets.ShouldHaveSingleItem().ToString().ShouldBe("r.in");

        services.PreviewConnections().Count.ShouldBe(1, "预览候选与落地一致");
    }

    /// <summary>同名标签下，不兼容的源**不进入成员**（不是"连上了但没有绑定"），其余源照常。</summary>
    [Fact]
    public void 同名标签_不兼容的源不进入成员_并给出原因()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "alpha-sender", typeof(AlphaSender));
        AddModule(runtime, "s2", "beta-sender", typeof(BetaSender));
        AddModule(runtime, "r", "alpha-receiver", typeof(AlphaReceiver));

        SetLabel(runtime, "s1", "out", "bus");
        SetLabel(runtime, "s2", "out", "bus");
        SetLabel(runtime, "r", "in", "bus");

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Sources.ShouldBe(["s1.out"], "只有被接受的那一路是成员");
        network.Targets.ShouldBe(["r.in"]);

        runtime.Issues.Recent(20).ShouldContain(
            e => e.Issue.Message.Contains("test/beta", StringComparison.Ordinal),
            "被排除的源必须带**具体原因**");
    }

    // ------------------------------------------------------------ 辅助

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name),
            name, string.Empty, [], ModuleCapability.None, [], true,
            entryType, TestViewPackages.For(entryType), string.Empty);

    private static void Place(ModuleRuntime runtime, string instanceId, double x, double y, double width, double height) =>
        runtime.Apply(ChangeSet.OfOne(new UpdateModuleLayoutOperation(
            new ModuleLayoutView(instanceId, x, y, width, height)))).Applied.ShouldBeTrue();

    private static void SetEdge(ModuleRuntime runtime, string instanceId, string portName, PortEdge edge) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, 0.5, IsAnchorFixed: null, Label: null, Edge: edge))))
            .Applied.ShouldBeTrue();

    private static void SetLabel(ModuleRuntime runtime, string instanceId, string portName, string? label) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortNetworkOperation(instanceId, portName, label))).Applied.ShouldBeTrue();

    // ------------------------------------------------------------ 测试模块（同形状、不同标识）

    /// <summary>发送：<c>test/alpha</c>（f32）。</summary>
    [ModuleDefinition("test", "alpha-sender")]
    public sealed class AlphaSender : IModule
    {
        /// <summary>输出口名。</summary>
        public const string OutPort = "out";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            Declare(context, OutPort, PortDirection.Send, "test/alpha");
    }

    /// <summary>接收：<c>test/alpha</c>（f32）。</summary>
    [ModuleDefinition("test", "alpha-receiver")]
    public sealed class AlphaReceiver : IModule
    {
        /// <summary>输入口名。</summary>
        public const string InPort = "in";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            Declare(context, InPort, PortDirection.Receive, "test/alpha");
    }

    /// <summary>发送：<c>test/beta</c>（f32；形状与 alpha 相同，标识不同）。</summary>
    [ModuleDefinition("test", "beta-sender")]
    public sealed class BetaSender : IModule
    {
        /// <summary>输出口名。</summary>
        public const string OutPort = "out";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            Declare(context, OutPort, PortDirection.Send, "test/beta");
    }

    /// <summary>声明"数据类型 + 带类型引用的口"（形状恒为 f32，只有标识/方向不同）。</summary>
    private static void Declare(IModuleContext context, string portName, PortDirection direction, string typeId)
    {
        var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(typeId, shape));
        context.Declaration.DeclarePort(new PortDeclaration(portName, direction, shape)
        {
            TypeId = typeId,
        });
    }
}
