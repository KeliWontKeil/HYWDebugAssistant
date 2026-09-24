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
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1–T3（M1-6）：变更原子性、建网与绑定投递、增量重建、级联清理与查询面。
/// </summary>
/// <remarks>
/// 端到端路径：实例化 → 建网 → 模块经上下文发送 → **绑定直达目标队列** → 目标专属线程处理。
/// </remarks>
public class CoreRuntimeTests
{
    // ------------------------------------------------------------ 端到端投递

    [Fact]
    public void 运行时_端到端投递_应经绑定直达目标队列并由目标线程处理()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();

        ReceiverModule.Received.Clear();

        // 模块经**上下文**发送（真实路径：Context.Ports → RuntimePortAccess → 绑定投递）
        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        BitConverter.TryWriteBytes(payload, 2.5f);
        sender!.Context.Ports.TrySend(SenderModule.OutPort, payload).ShouldBeTrue();

        WaitUntil(() => ReceiverModule.Received.Count > 0, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("目标实例应在其专属线程上收到投递");
        ReceiverModule.Received[0].ShouldBe(2.5f);

        runtime.Bindings.TryGet("link-1", out var binding).ShouldBeTrue();
        binding!.Diagnostics.Delivered.ShouldBe(1);
        binding.Diagnostics.LastDataTime.ShouldNotBeNull();
    }

    [Fact]
    public void 运行时_无绑定时发送应被丢弃且不抛异常()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();

        var payload = new byte[sizeof(float)];
        sender!.Context.Ports.TrySend(SenderModule.OutPort, payload).ShouldBeFalse();
    }

    // ------------------------------------------------------------ 变更原子性

    [Fact]
    public void 变更集_任一操作失败应整体拒绝且零副作用()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));

        // 复合变更：第一项合法（加入 r1），第二项失败（实例不存在）
        var result = runtime.Apply(ChangeSet.Of(
            new AddModuleOperation("r1", TestDefinition("receiver", typeof(ReceiverModule))),
            new RemoveModuleOperation("not-exist")));

        result.Applied.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();
        result.Failures[0].ShouldContain("实例不存在");

        // 零副作用：r1 未加入，也**未启动任何承载**（连线程都不建）
        runtime.Content.TryGetInstance("r1", out _).ShouldBeFalse();
        runtime.Content.InstanceCount.ShouldBe(1);
        runtime.Carriers.Carriers.Count.ShouldBe(1);
    }

    [Fact]
    public void 变更集_重复实例ID应被拒绝()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));

        var result = runtime.Apply(ChangeSet.OfOne(
            new AddModuleOperation("s1", TestDefinition("sender", typeof(SenderModule)))));

        result.Applied.ShouldBeFalse();
        result.Failures[0].ShouldContain("已存在");
    }

    // ------------------------------------------------------------ 建网校验

    [Fact]
    public void 建网_契约不兼容应拒绝并说明差异()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "w1", "wide-receiver", typeof(WideReceiverModule));

        // f32（4 字节）→ f64（8 字节）：不兼容
        var result = runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember("s1", SenderModule.OutPort, PortDirection.Send),
            [new NetworkMember("w1", WideReceiverModule.InPort, PortDirection.Receive)])));

        result.Applied.ShouldBeFalse();
        result.Failures[0].ShouldContain("契约不兼容");
        runtime.Bindings.Bindings.ShouldBeEmpty();
    }

    [Fact]
    public void 建网_方向不合法应被拒绝()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        // 拿接收口当源
        var result = runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember("r1", ReceiverModule.InPort, PortDirection.Receive),
            [new NetworkMember("r1", ReceiverModule.InPort, PortDirection.Receive)])));

        result.Applied.ShouldBeFalse();
        result.Failures.ShouldContain(f => f.Contains("源必须是发送口"));
    }

    [Fact]
    public void 建网_命名冲突应被拒绝()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();

        var result = runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember("s1", SenderModule.OutPort, PortDirection.Send),
            [new NetworkMember("r1", ReceiverModule.InPort, PortDirection.Receive)],
            "link-1")));

        result.Applied.ShouldBeFalse();
        result.Failures[0].ShouldContain("已被占用");
    }

    // ------------------------------------------------------------ 增量重建与停流

    [Fact]
    public void 增量重建_删除网络后模块状态与实例保留()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();

        runtime.Content.TryGetInstance("r1", out var before).ShouldBeTrue();

        runtime.Apply(ChangeSet.OfOne(new DisconnectNetworkOperation("link-1"))).Applied.ShouldBeTrue();

        runtime.Bindings.Bindings.ShouldBeEmpty();
        runtime.Content.Networks.ShouldBeEmpty();

        // 模块实例、状态与配置保留（插拔线不重置模块）
        runtime.Content.TryGetInstance("r1", out var after).ShouldBeTrue();
        after.ShouldBeSameAs(before);
        after!.State.ShouldBe(ModuleInstanceState.Active);
    }

    [Fact]
    public void 禁用_应暂停数据流并保留拓扑_启用后恢复()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();

        runtime.Apply(ChangeSet.OfOne(new SetModuleEnabledOperation("r1", false))).Applied.ShouldBeTrue();

        runtime.Content.TryGetInstance("r1", out var disabled).ShouldBeTrue();
        disabled!.State.ShouldBe(ModuleInstanceState.Disabled);
        runtime.Bindings.TryGet("link-1", out var binding).ShouldBeTrue();
        binding!.IsSuspended.ShouldBeTrue("禁用 = 停流，但绑定保留");

        // 停流期间发送被丢弃（不阻塞发送方）
        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        sender!.Context.Ports.TrySend(SenderModule.OutPort, payload).ShouldBeFalse();
        binding.Diagnostics.Dropped.ShouldBeGreaterThanOrEqualTo(1);

        runtime.Apply(ChangeSet.OfOne(new SetModuleEnabledOperation("r1", true))).Applied.ShouldBeTrue();
        runtime.Content.TryGetInstance("r1", out var enabled).ShouldBeTrue();
        enabled!.State.ShouldBe(ModuleInstanceState.Active);
        binding.IsSuspended.ShouldBeFalse();
    }

    [Fact]
    public void 移除实例_应级联清理网络与绑定且不影响其他实例()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();

        runtime.Apply(ChangeSet.OfOne(new RemoveModuleOperation("s1"))).Applied.ShouldBeTrue();

        runtime.Content.Networks.ShouldBeEmpty("被移除实例参与的网络应级联清理");
        runtime.Bindings.Bindings.ShouldBeEmpty();
        runtime.Content.TryGetInstance("s1", out _).ShouldBeFalse();

        runtime.Content.TryGetInstance("r1", out var receiver).ShouldBeTrue();
        receiver!.State.ShouldBe(ModuleInstanceState.Active, "移除其他实例不应影响本实例");
    }
    // ------------------------------------------------------------ 对齐建网（ADR-0037："接触且对齐"）

    [Fact]
    public void 对齐建网_接触且标记对齐时应自动连上()
    {
        using var runtime = new ModuleRuntime();   // 对齐自动建网默认开启

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        runtime.Content.Networks.ShouldBeEmpty("仅加入模块**不再**自动连线");

        // 两个模块**接触**（右边缘贴左边缘）、端口标记都在边缘中点（偏移 0.5）→ 对齐
        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 300, 0, 300, 200);

        var network = runtime.Content.Networks.ShouldHaveSingleItem();
        network.IsAutoGenerated.ShouldBeTrue("派生网络（由框架按几何推导）");
        network.Name.Length.ShouldBe(0, "对齐网络是匿名直连；名字只由标签产生");
        network.Source.InstanceId.ShouldBe("s1");
        network.Targets[0].InstanceId.ShouldBe("r1");

        // 数据可流
        ReceiverModule.Received.Clear();
        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 1.25f);
        sender!.Context.Ports.TrySend(SenderModule.OutPort, payload).ShouldBeTrue();
        WaitUntil(() => ReceiverModule.Received.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        ReceiverModule.Received[0].ShouldBe(1.25f);
    }

    [Fact]
    public void 对齐建网_未接触或未对齐时不得建网()
    {
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        // ① 边缘不接触（相隔 40 px）
        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 340, 0, 300, 200);
        runtime.Content.Networks.ShouldBeEmpty("不接触 → 不建网");

        // ② 接触但标记未对齐（接收口偏移 0.5 vs 发送口偏移 0.05 → 相差 90 px > 12 px 容差）
        Place(runtime, "r1", 300, 0, 300, 200);
        MovePort(runtime, "s1", "out", 0.05);
        runtime.Content.Networks.ShouldBeEmpty("接触但标记未对齐 → 不建网（严防误建）");
    }

    [Fact]
    public void 对齐建网_拖开应自动断开()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 300, 0, 300, 200);
        runtime.Content.Networks.Count.ShouldBe(1);

        // 拖开 → 几何不再满足 → 派生网络消失（对齐网络由几何派生）
        Place(runtime, "r1", 600, 400, 300, 200);
        runtime.Content.Networks.ShouldBeEmpty("拖开即自动断开");
    }

    [Fact]
    public void 对齐建网_打了标签的口不参与()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 300, 0, 300, 200);

        // 给发送口打标签：它只参与命名网络，不再参与对齐建网
        SetLabel(runtime, "s1", "out", "bus");
        runtime.Content.Networks.ShouldBeEmpty("命名口退出对齐网络；此时无同名接收端 → 无网络");

        // 接收口填同名标签 → 命名网络建立
        SetLabel(runtime, "r1", "in", "bus");
        var network = runtime.Content.Networks.ShouldHaveSingleItem();
        network.Name.ShouldBe("bus");
        network.IsNamed.ShouldBeTrue();
    }

    [Fact]
    public void 对齐建网_契约不兼容时不得建网()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));           // f32
        AddModule(runtime, "r1", "wide-receiver", typeof(WideReceiverModule)); // f64

        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 300, 0, 300, 200);

        runtime.Content.Networks.ShouldBeEmpty("契约不兼容（f32 发送 → f64 接收）→ 不建网");
    }

    [Fact]
    public void 显式建立的网络_不得被推导误删()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        // 显式建网（不依赖几何）
        Connect(runtime, "s1", "out", "r1", "in", "link-manual").ShouldBeTrue();
        runtime.Content.Networks.Count(n => !n.IsAutoGenerated).ShouldBe(1);

        // 触发一次推导（布局提交）：显式网络必须留存
        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 900, 900, 300, 200);

        runtime.Content.Networks.Count(n => !n.IsAutoGenerated).ShouldBe(1, "显式网络不参与推导，一律不动");
    }

    // ------------------------------------------------------------ 查询面

    [Fact]
    public void 查询面_应反映实例网络与诊断()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));

        // 用**标签**建一条命名网络（新的建网方式）
        SetLabel(runtime, "s1", "out", "bus");
        SetLabel(runtime, "r1", "in", "bus");

        var services = new RuntimeServices(runtime);

        var instances = services.GetInstances();
        instances.Count.ShouldBe(2);
        var senderView = instances.Single(i => i.InstanceId == "s1");
        senderView.ModuleId.ShouldBe("test/sender/1.0.0");
        senderView.State.ShouldBe("Active");
        senderView.Ports.Count.ShouldBe(1);
        senderView.Ports[0].Direction.ShouldBe("Send");
        senderView.Ports[0].NetworkLabel.ShouldBe("bus");
        senderView.Ports[0].IsConnected.ShouldBeTrue("命名网络已建立");

        var networks = services.GetNetworks();
        networks.Count.ShouldBe(1);
        networks[0].IsAutoGenerated.ShouldBeTrue();
        networks[0].Name.ShouldBe("bus");
        networks[0].Source.ShouldBe("s1.out");

        var diagnostics = services.GetDiagnostics();
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Delivered.ShouldBe(0);
    }

    // ------------------------------------------------------------ 建网测试辅助

    /// <summary>摆放模块（写入布局）并触发一次推导（等价于"拖动结束提交"）。</summary>
    private static void Place(ModuleRuntime runtime, string instanceId, double x, double y, double width, double height) =>
        runtime.Apply(ChangeSet.OfOne(new UpdateModuleLayoutOperation(
            new ModuleLayoutView(instanceId, x, y, width, height)))).Applied.ShouldBeTrue();

    /// <summary>调整端口几何（边缘偏移）并触发一次推导。</summary>
    private static void MovePort(ModuleRuntime runtime, string instanceId, string portName, double offset) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, offset)))).Applied.ShouldBeTrue();

    /// <summary>给端口设置网络标签（走真实变更入口 → 触发推导）。</summary>
    private static void SetLabel(ModuleRuntime runtime, string instanceId, string portName, string? label) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortNetworkOperation(instanceId, portName, label))).Applied.ShouldBeTrue();

    // ------------------------------------------------------------ 测试辅助

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name, "1.0.0"),
            name, string.Empty, [], ModuleCapability.None, [], null, true,
            entryType, string.Empty, string.Empty);

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static bool Connect(
        ModuleRuntime runtime, string sourceInstance, string sourcePort, string targetInstance, string targetPort, string name) =>
        runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember(sourceInstance, sourcePort, PortDirection.Send),
            [new NetworkMember(targetInstance, targetPort, PortDirection.Receive)],
            name))).Applied;

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

    /// <summary>测试模块：发送口（f32）。</summary>
    [ModuleDefinition("test", "sender", "1.0.0")]
    public sealed class SenderModule : IModule
    {
        public const string OutPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape));
    }

    /// <summary>测试模块：接收口（f32），把收到的值收集到静态列表。</summary>
    [ModuleDefinition("test", "receiver", "1.0.0")]
    public sealed class ReceiverModule : IModule
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

    /// <summary>测试模块：接收口（f64）——用于验证契约不兼容的拒绝路径。</summary>
    [ModuleDefinition("test", "wide-receiver", "1.0.0")]
    public sealed class WideReceiverModule : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F64);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape));
    }
}