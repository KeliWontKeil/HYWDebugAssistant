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
        binding.Diagnostics.Published.ShouldBe(1, "**发布计数 = 每个数据报一次**（按源）");
        binding.Diagnostics.LastDataTime.ShouldNotBeNull();

        // 【ADR-0050 Q6(c）】**单源网络零附加**：不带薄旁路头（也不构造）
        runtime.Carriers.TryGet("r1", out var carrier).ShouldBeTrue();
        carrier!.Diagnostics.Headered.ShouldBe(0, "单源网络**不带**薄旁路头");
        carrier.Diagnostics.LastHeaderSequence.ShouldBe(0);
    }

    [Fact]
    public void 多源网络_应带薄旁路头且序号按源递增_单源网络零附加()
    {
        // 【ADR-0050 Q6(c)】薄旁路头**只在多源网络**上带：源标识 + **按源序号** + 接收时刻；
        // **旁路元数据、不进 payload**（字节守恒不变），由**观测面**消费（承载诊断记录最近序号/时刻）。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "s2", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "dual-receiver", typeof(DualReceiverModule));

        SetLabel(runtime, "s1", SenderModule.OutPort, "bus");
        SetLabel(runtime, "s2", SenderModule.OutPort, "bus");
        SetLabel(runtime, "r1", "in1", "bus");
        SetLabel(runtime, "r1", "in2", "bus");

        runtime.Content.TryGetInstance("s1", out var sender1).ShouldBeTrue();
        runtime.Content.TryGetInstance("s2", out var sender2).ShouldBeTrue();

        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 1f);

        // s1 发 3 个、s2 发 1 个：**序号按源各自递增**（互不干扰）
        for (var i = 0; i < 3; i++)
        {
            _ = sender1!.Context.Ports.TrySend(SenderModule.OutPort, payload);
        }

        _ = sender2!.Context.Ports.TrySend(SenderModule.OutPort, payload);

        var bindings = runtime.Bindings.Bindings.Where(b => b.Network.Name == "bus").ToList();
        bindings.Count.ShouldBe(2);
        bindings.ShouldContain(b => b.Source.InstanceId == "s1" && b.Diagnostics.Published == 3);
        bindings.ShouldContain(b => b.Source.InstanceId == "s2" && b.Diagnostics.Published == 1);

        // 每个源 1 个数据报投到 2 个宿 ⇒ Delivered 按宿计（被多播放大），Published **不被放大**
        bindings.ShouldAllBe(b => b.Diagnostics.Delivered == b.Diagnostics.Published * 2);

        WaitForHeadered(runtime, "r1", 8).ShouldBeTrue("多源网络的数据报应带薄旁路头投递（观测面记录到）");
    }

    private static bool WaitForHeadered(ModuleRuntime runtime, string instanceId, long expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (runtime.Carriers.TryGet(instanceId, out var carrier)
                && carrier is not null
                && carrier.Diagnostics.Headered >= expected)
            {
                carrier.Diagnostics.LastHeaderSequence.ShouldBeGreaterThan(0, "旁路头序号应按源递增（1 起）");
                carrier.Diagnostics.LastHeaderTimestamp.ShouldBeGreaterThan(0, "旁路头应带接收时刻（单调计数）");
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
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
    public void 对齐建网_两条独立匿名网络的身份键必须互不相同()
    {
        // 【P40 回归】用户实测崩溃：出现**第二条**自动建网链路时抛
        // "An item with the same key has already been added. Key: "（**空键**）——
        // 根因是**用网络名当唯一键**，而匿名网络的名字就是空串。
        // 本用例把不变量钉住：**每条网络都有非空且互不相同的身份键**，且诊断键与网络键一一对应。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        AddModule(runtime, "s2", "sender", typeof(SenderModule));
        AddModule(runtime, "r2", "receiver", typeof(ReceiverModule));

        // 两组**互不相邻**的接触对（各自的自动建网链路）
        Place(runtime, "s1", 0, 0, 300, 200);
        Place(runtime, "r1", 300, 0, 300, 200);
        Place(runtime, "s2", 0, 400, 300, 200);
        Place(runtime, "r2", 300, 400, 300, 200);

        var networks = services.GetNetworks();
        networks.Count.ShouldBe(2, "两条独立链路应各成一条匿名网络");
        networks.ShouldAllBe(n => n.IsAnonymous && n.Name.Length == 0, "自动建网是匿名网络（名字为空串）");

        var keys = networks.Select(n => n.Key).ToList();
        keys.ShouldAllBe(k => k.Length > 0, "身份键不得为空（旧的崩溃正是空键撞键）");
        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(2, "两条网络的身份键必须互不相同");

        var diagnostics = services.GetDiagnostics();
        diagnostics.Count.ShouldBe(2);
        diagnostics.Select(d => d.Key).ShouldAllBe(k => keys.Contains(k, StringComparer.Ordinal),
            "诊断键必须能与网络键配对（视图层就是靠它取数的）");
        diagnostics.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
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

    [Fact]
    public void 诊断_丢弃应按原因分类且停流不计入传输丢弃()
    {
        // 【ADR-0046 D8 / ADR-0050 Q5】丢弃必须分类：**停流未投递 ≠ 传输丢弃**（前者是合法状态）。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();

        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 1f);

        // ① **无网络**时发送：没有任何绑定 ⇒ 不计入任何绑定（不算"丢弃"）
        _ = sender!.Context.Ports.TrySend(SenderModule.OutPort, payload);

        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-1").ShouldBeTrue();
        var diagnostics = services.GetDiagnostics().ShouldHaveSingleItem();

        // ② **载荷非法**（长度小于契约尺寸）⇒ InvalidPayload
        _ = sender.Context.Ports.TrySend(SenderModule.OutPort, ReadOnlySpan<byte>.Empty);
        diagnostics = services.GetDiagnostics().ShouldHaveSingleItem();
        diagnostics.DropInvalidPayload.ShouldBe(1);
        diagnostics.DropStalled.ShouldBe(0);
        diagnostics.Dropped.ShouldBe(1, "总数 = 各原因之和");

        // ③ 正常投递 ⇒ Delivered（不产生任何丢弃）
        _ = sender.Context.Ports.TrySend(SenderModule.OutPort, payload);
        diagnostics = services.GetDiagnostics().ShouldHaveSingleItem();
        diagnostics.Delivered.ShouldBe(1);
        diagnostics.Dropped.ShouldBe(1);

        // ④ **停流未投递**（网络停流）：单独归类，**不计入传输丢弃**
        runtime.Apply(ChangeSet.OfOne(new HYWDebugAssistant.Core.Changes.SetModuleEnabledOperation("s1", false)));
        _ = sender.Context.Ports.TrySend(SenderModule.OutPort, payload);
        _ = sender.Context.Ports.TrySend(SenderModule.OutPort, payload);

        diagnostics = services.GetDiagnostics().ShouldHaveSingleItem();
        diagnostics.DropStalled.ShouldBe(2, "停流期间的发送记为**停流未投递**（合法状态）");

        var transportDrops = diagnostics.DropQueueFull + diagnostics.DropNoCarrier + diagnostics.DropInvalidPayload;
        transportDrops.ShouldBe(1, "**传输丢弃**只能是载荷非法那 1 次（停流的 2 次不得混入）");
        diagnostics.Dropped.ShouldBe(3, "总数 = 1（传输）+ 2（停流）");
    }

    // ------------------------------------------------------------ 多源网络（ADR-0050 M1/M2/M5/M6/M7）

    /// <summary>多源测试用接收模块（两个接收口，同契约）。</summary>
    [ModuleDefinition("test", "dual-receiver", "1.0.0")]
    public sealed class DualReceiverModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            foreach (var name in new[] { "in1", "in2" })
            {
                context.Declaration.DeclarePort(new PortDeclaration(
                    name, PortDirection.Receive, ContractShape.OfPrimitive(ContractPrimitive.F32)));
                context.Ports.SetReceiveHandler(name, _ => { });
            }
        }
    }

    [Fact]
    public void 多源网络_同名标签多个发送端应建成一条网络且逐源投递()
    {
        // 【ADR-0050 M1/M2/M7】多源 = **合法的一等组合**（旧"整条拒绝"已作废）；
        // 每个源各一条绑定 ⇒ 各源数据都能到达宿，且**计数按源归属**。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "s2", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "dual-receiver", typeof(DualReceiverModule));

        SetLabel(runtime, "s1", SenderModule.OutPort, "bus");
        SetLabel(runtime, "s2", SenderModule.OutPort, "bus");
        SetLabel(runtime, "r1", "in1", "bus");
        SetLabel(runtime, "r1", "in2", "bus");

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Name.ShouldBe("bus");
        network.Sources.Count.ShouldBe(2, "**同名标签的多个发送端同属一条多源网络**（旧行为是整条拒绝）");
        network.Sources.ShouldContain("s1.out");
        network.Sources.ShouldContain("s2.out");
        network.Targets.Count.ShouldBe(2);

        // 每个源一条绑定 ⇒ 两条诊断（**按源归属**）
        var diagnostics = services.GetDiagnostics();
        diagnostics.Count.ShouldBe(2, "多源网络 = **逐源一条绑定**（各自的契约布局与计数）");
        diagnostics.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(2);

        // 两个源各发一次 ⇒ 每个源的诊断各 +1（互不混淆）
        runtime.Content.TryGetInstance("s1", out var sender1).ShouldBeTrue();
        runtime.Content.TryGetInstance("s2", out var sender2).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 3.5f);

        _ = sender1!.Context.Ports.TrySend(SenderModule.OutPort, payload);
        _ = sender2!.Context.Ports.TrySend(SenderModule.OutPort, payload);

        services.GetDiagnostics().ShouldAllBe(d => d.Delivered == 2, "每个源各投递到 2 个宿 ⇒ 各自的 Delivered = 2");

        var keys = services.GetDiagnostics().Select(d => d.Key).ToList();
        keys.ShouldContain("s1.out→r1.in1,r1.in2");
        keys.ShouldContain("s2.out→r1.in1,r1.in2");
    }

    [Fact]
    public void 宿口指标_多源网络下必须等于各源诊断之和而不是零()
    {
        // 【用户 2026-09 裁定"统一宿口取数口径，关系到整个数据来源的一致性"】
        // **根因（旧实现）**：取数按"两次字典查找 + 猜键"——先按**网络身份键**（`源集合 → 宿集合`）查，
        // 失败再拿**口字面量**去查**逐源诊断表**（键 = `单源 → 宿子集`，是**源**键）。
        // 宿口在后一个键空间里根本不存在 ⇒ **宿口的投递/丢弃/速率静默为 0**
        // （1:1 单源单宿时两个键恰好字面相等，所以这个缺陷一直没被看见）。
        // 本用例就是那盏灯：宿口必须按**网络**把该网络各源的诊断加起来。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "s2", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "dual-receiver", typeof(DualReceiverModule));
        AddModule(runtime, "r9", "receiver", typeof(ReceiverModule));   // **未入网**的口（"全 0"的对照）

        SetLabel(runtime, "s1", SenderModule.OutPort, "bus");
        SetLabel(runtime, "s2", SenderModule.OutPort, "bus");
        SetLabel(runtime, "r1", "in1", "bus");
        SetLabel(runtime, "r1", "in2", "bus");

        // ① **不在任何网络上的口 ⇒ 全 0**（"未入网"这条文案由界面自己判定，框架只保证口径）
        var idle = services.GetPortMetrics("r9", ReceiverModule.InPort);
        idle.Delivered.ShouldBe(0);
        idle.Dropped.ShouldBe(0);
        idle.BytesPerSecond.ShouldBe(0);
        idle.NetworkKeys.ShouldBeEmpty("未入网 ⇒ 该口不参与任何「流」");

        runtime.Content.TryGetInstance("s1", out var sender1).ShouldBeTrue();
        runtime.Content.TryGetInstance("s2", out var sender2).ShouldBeTrue();
        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, 1f);
        _ = sender1!.Context.Ports.TrySend(SenderModule.OutPort, payload);
        _ = sender2!.Context.Ports.TrySend(SenderModule.OutPort, payload);

        var diagnostics = services.GetDiagnostics();
        diagnostics.Count.ShouldBe(2, "多源网络 = 逐源一条绑定");
        diagnostics.ShouldAllBe(d => d.Delivered == 2, "每个源各投到 2 个宿 ⇒ 各自的 Delivered = 2");

        // ② **宿口**：两个源各有一次投递落在本口 ⇒ 本口读到"该网络各源诊断之和"（**旧实现在这里恒为 0**）
        var sink = services.GetPortMetrics("r1", "in1");
        sink.Delivered.ShouldBeGreaterThan(0, "宿口必须按**网络**把该网络各源的诊断加起来（这正是本次修复的红灯）");
        sink.Delivered.ShouldBe(
            diagnostics.Sum(d => d.Delivered),
            "**宿口指标 = 其入网各源诊断之和**（每个源的绑定都把 in1 算在它自己的 2 个宿里 ⇒ 各 2，合计 4）");
        sink.NetworkKeys.ShouldContain(
            services.GetNetworks().ShouldHaveSingleItem().Key,
            "该口参与的网络身份键必须能对回网络视图（配对用，不参与取数）");

        // ③ **「绑定级按宿计」是框架口径的既有性质**（不是缺陷，但也别误读）：一条 `源 → 多宿` 绑定只有
        //    一份诊断 ⇒ 它的**每个宿口**都读到同一份计数（含投给兄弟宿的那些）。钉住它，免得后人"顺手修好"成
        //    另一套口径（那正是本次要消除的分叉：端口标记会与角色方块对不上）。
        var siblingSink = services.GetPortMetrics("r1", "in2");
        siblingSink.Delivered.ShouldBe(sink.Delivered, "同一条绑定的两个宿口读同一份绑定级计数（框架既有性质）");
        siblingSink.Dropped.ShouldBe(sink.Dropped);
        siblingSink.BytesPerSecond.ShouldBe(sink.BytesPerSecond);

        // ④ **发送口**：以它为源的全部绑定之和 = 它自己的那条逐源诊断（本例 = 2 个宿）
        var source = services.GetPortMetrics("s1", SenderModule.OutPort);
        source.Delivered.ShouldBe(2);
        source.Delivered.ShouldBe(
            diagnostics.Single(d => d.Key.StartsWith("s1.out", StringComparison.Ordinal)).Delivered,
            "发送口只读「以它为源」的那些流");

        // ⑤ 接口契约：实例/口不存在**不抛**（全 0），空白参数必须拒绝（与查询面其余方法一致）
        services.GetPortMetrics("nobody", "nowhere").Delivered.ShouldBe(0);
        _ = Should.Throw<ArgumentException>(() => services.GetPortMetrics(" ", "out"));

        // ⑥ **把根因本身也钉住**：多源网络下 `NetworkView.Key`（= `源集合 → 宿集合`）**不在**逐源诊断的键集合里
        //    ⇒ "先按网络键查、查不到再拿口字面量查逐源表"这条老路**必然落空**（宿口就是这么静默归零的）。
        //    有了这条，谁把修复回退成"猜键"取数，这里立刻红。
        var networkKey = services.GetNetworks().ShouldHaveSingleItem().Key;
        var diagnosticKeys = diagnostics.Select(d => d.Key).ToList();
        diagnosticKeys.ShouldNotContain(
            networkKey,
            "多源网络下网络身份键 ≠ 逐源键 —— 这正是「按网络键查逐源表」永远取不到数的原因");
        diagnosticKeys.ShouldContain("s1.out→r1.in1,r1.in2");
        diagnosticKeys.ShouldContain("s2.out→r1.in1,r1.in2");
    }

    [Fact]
    public void 端口指标_单源单宿时源口与宿口必须读到同一个速率()
    {
        // 【用户 2026-09 裁定"统一口径"，"同一根线两侧不得显示不同的数"】
        // 源口与宿口读的是**同一份**逐源诊断、**同一批**框架速率计（`_rateMeters` 按绑定身份键索引）。
        // 旧实现里接收侧走"每 (实例, 口) 队列"的速率计、发送侧走绑定速率计 ⇒ 两套口径、两侧可能不同数。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));
        Connect(runtime, "s1", SenderModule.OutPort, "r1", ReceiverModule.InPort, "link-rate").ShouldBeTrue();

        runtime.Content.TryGetInstance("s1", out var sender).ShouldBeTrue();
        var payload = new byte[sizeof(float)];

        // 先观察一次**建立基准**（RateMeter：首个观察只建基准、返回 0 —— 不猜历史速率）
        services.GetPortMetrics("s1", SenderModule.OutPort).BytesPerSecond.ShouldBe(
            0,
            "首个观察没有「上一时刻」⇒ 不得猜历史速率");

        // 基准之后才真正流过数据；窗口必须 > RateMeter.WindowSeconds(1s) 才会重算
        Thread.Sleep((int)(HYWDebugAssistant.Core.Diagnostics.RateMeter.WindowSeconds * 1000) + 150);   // 整秒桶：窗口未满不会重算
        for (var i = 0; i < 10; i++)
        {
            _ = sender!.Context.Ports.TrySend(SenderModule.OutPort, payload);
        }

        var source = services.GetPortMetrics("s1", SenderModule.OutPort);
        var sink = services.GetPortMetrics("r1", ReceiverModule.InPort);

        source.BytesPerSecond.ShouldBeGreaterThan(0, "有数据流过 ⇒ 速率必须是个正数");
        sink.BytesPerSecond.ShouldBeGreaterThan(0, "宿口同样必须给出速率（占位符只应出现在真的没数据时）");

        // **断言设计理由**：两侧读的是同一条绑定的同一个速率计 ⇒ 数值逐位相同；但它们是两次浮点求和，
        // 故用**相对容差 1e-6** 吸收求和顺序差。这个容差**不足以掩盖"两套口径"**：口径一旦分叉，
        // 差值要么是"0 vs 正数"、要么是量级差（两个速率计的**基准时刻**不同 ⇒ 一个刚建基准为 0、
        // 另一个已算出正数），两种都会被这条断言逮住 —— 既不会 flaky，也不失红灯的力气。
        ShouldShareRate(
            sink.BytesPerSecond,
            source.BytesPerSecond,
            "同一根线两侧必须读**同一个**速率口径（旧实现：接收侧走队列口径、发送侧走绑定口径）");

        // 与"逐源诊断"再对一次：宿口速率必须就是**该绑定**的搬运字节速率（同一批速率计的直接证据）
        ShouldShareRate(
            sink.BytesPerSecond,
            services.GetDiagnostics().ShouldHaveSingleItem().BusByteRate,
            "端口指标与逐源诊断必须同源（框架侧只有这一批速率计）");
    }

    /// <summary>
    /// 断言两个速率是**同一个口径**：相对容差 1e-6 只吸收浮点求和顺序差（见调用处的理由）。
    /// </summary>
    /// <param name="actual">实测值。</param>
    /// <param name="expected">对照值。</param>
    /// <param name="because">失败说明。</param>
    private static void ShouldShareRate(double actual, double expected, string because) =>
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(
            1e-6 * Math.Max(Math.Abs(actual), Math.Abs(expected)) + 1e-9,
            because);

    [Fact]
    public void 多源网络_逐对契约兼容_每个源只投递到自己兼容的宿()
    {
        // 【ADR-0050 M6/M7】兼容判定**逐 (源,宿) 对**：一个源**只**投向契约兼容的宿；
        // 多源网络里每个源各一条绑定 ⇒ **诊断按源归属**（键 = `单源 → 它的宿子集`）。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(SenderModule));            // f32 发送口
        AddModule(runtime, "su", "u64-sender", typeof(U64SenderModule));      // u64 发送口
        AddModule(runtime, "r1", "receiver", typeof(ReceiverModule));         // f32 接收口
        AddModule(runtime, "ru", "u64-receiver", typeof(U64ReceiverModule));  // u64 接收口

        foreach (var (instance, port) in new[]
                 {
                     ("s1", SenderModule.OutPort), ("su", U64SenderModule.OutPort),
                     ("r1", ReceiverModule.InPort), ("ru", U64ReceiverModule.InPort),
                 })
        {
            SetLabel(runtime, instance, port, "bus");
        }

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Sources.Count.ShouldBe(2, "两个发送端同名 ⇒ 一条多源网络");
        network.Targets.Count.ShouldBe(2, "两个接收口都是宿成员（与**任一源**兼容即入网）");

        var diagnostics = services.GetDiagnostics();
        diagnostics.Count.ShouldBe(2, "**每个源一条绑定**（各自的宿子集与计数）");

        // 逐对过滤：s1(f32) 只投 r1；su(u64) 只投 ru
        diagnostics.Select(d => d.Key).ShouldContain("s1.out→r1.in");
        diagnostics.Select(d => d.Key).ShouldContain("su.out→ru.in");

        var keys = diagnostics.Select(d => d.Key).ToList();
        keys.ShouldNotContain(k => k.Contains("r1.in,ru.in", StringComparison.Ordinal),
            "**不得跨契约投递**：f32 的源不得投向 u64 的宿");

        // 各源投一次：各自 Delivered = 1（按源归属）
        runtime.Content.TryGetInstance("s1", out var f32Sender).ShouldBeTrue();
        runtime.Content.TryGetInstance("su", out var u64Sender).ShouldBeTrue();

        var f32Payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(f32Payload, 1f);
        _ = f32Sender!.Context.Ports.TrySend(SenderModule.OutPort, f32Payload);

        var u64Payload = new byte[sizeof(ulong)];
        _ = BitConverter.TryWriteBytes(u64Payload, 7UL);
        _ = u64Sender!.Context.Ports.TrySend(U64SenderModule.OutPort, u64Payload);

        services.GetDiagnostics().ShouldAllBe(d => d.Delivered == 1, "每个源各投递 1 次（只有 1 个兼容宿）");
    }

    /// <summary>u64 发送模块（逐对兼容测试用）。</summary>
    [ModuleDefinition("test", "u64-sender", "1.0.0")]
    public sealed class U64SenderModule : IModule
    {
        /// <summary>发送口名。</summary>
        public const string OutPort = "out";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                OutPort, PortDirection.Send, ContractShape.OfPrimitive(ContractPrimitive.U64)));
    }

    /// <summary>u64 接收模块（逐对兼容测试用）。</summary>
    [ModuleDefinition("test", "u64-receiver", "1.0.0")]
    public sealed class U64ReceiverModule : IModule
    {
        /// <summary>接收口名。</summary>
        public const string InPort = "in";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, ContractShape.OfPrimitive(ContractPrimitive.U64)));
            context.Ports.SetReceiveHandler(InPort, _ => { });
        }
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
