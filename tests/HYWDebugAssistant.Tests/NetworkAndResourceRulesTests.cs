using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
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
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T2（契约兼容矩阵 + 快速建网 R0–R6 表驱动）与 T3（资源生命周期）——`docs/architecture/06` §6.5。
/// </summary>
public class NetworkAndResourceRulesTests
{
    // ------------------------------------------------------------ 网络名校验

    /// <summary>网络名校验：**按枚举判原因**（文案不进协议/控制流）。</summary>
    [Theory]
    [InlineData(null, NetworkNameError.Empty)]
    [InlineData("", NetworkNameError.Empty)]
    [InlineData("   ", NetworkNameError.Empty)]
    [InlineData("bad name", NetworkNameError.ContainsWhitespace)]
    [InlineData("ok", NetworkNameError.None)]
    public void 网络名_应按枚举判原因(string? name, NetworkNameError expected)
    {
        NetworkNaming.TryValidateName(name, out var error)
            .ShouldBe(expected == NetworkNameError.None);
        error.ShouldBe(expected);
    }

    /// <summary>长度边界：上限内接受，越界拒绝（原因 = <see cref="NetworkNameError.TooLong"/>）。</summary>
    [Theory]
    [InlineData(NetworkNaming.MaxNameLength, true)]
    [InlineData(NetworkNaming.MaxNameLength + 1, false)]
    public void 网络名_长度边界应被拒或接受(int length, bool expected)
    {
        var name = new string('a', length);

        NetworkNaming.TryValidateName(name, out var error).ShouldBe(expected);
        error.ShouldBe(expected ? NetworkNameError.None : NetworkNameError.TooLong);
    }

    // ------------------------------------------------------------ T2 · 契约兼容矩阵

    /// <summary>契约兼容判定：**形状完全一致**（不得用「接收 ⊇ 发送」的子集判定）。</summary>
    public static TheoryData<string, ContractShape, ContractShape, TransferSemantics, TransferSemantics, bool> CompatibilityCases => new()
    {
        // 单原语：类型与尺寸必须一致
        { "f32 → f32", F32, F32, TransferSemantics.Stream, TransferSemantics.Stream, true },
        { "f32 → f64", F32, F64, TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "u64 → u64", U64, U64, TransferSemantics.Stream, TransferSemantics.Stream, true },
        { "bytes[4] → bytes[4]", ContractShape.OfBytes(4), ContractShape.OfBytes(4), TransferSemantics.Stream, TransferSemantics.Stream, true },
        { "bytes[4] → bytes[8]", ContractShape.OfBytes(4), ContractShape.OfBytes(8), TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "f32[4] → f32[4]", ContractShape.OfArray(ContractPrimitive.F32, 4), ContractShape.OfArray(ContractPrimitive.F32, 4), TransferSemantics.Stream, TransferSemantics.Stream, true },
        { "f32[4] → f32[2]", ContractShape.OfArray(ContractPrimitive.F32, 4), ContractShape.OfArray(ContractPrimitive.F32, 2), TransferSemantics.Stream, TransferSemantics.Stream, false },

        // 结构：**字段名 + 类型 + 顺序 + 数量全同**才兼容（子集判定已作废）
        { "{seq,value} → {seq,value}", Struct, Struct, TransferSemantics.Stream, TransferSemantics.Stream, true },
        { "{seq,value} → {seq,value,extra}", Struct, StructWithExtra, TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "{seq,value} → {value}", Struct, ValueOnly, TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "{seq,value} → {seq:f64,value:f32}", Struct, StructWithWideSeq, TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "{seq,value} → {value,seq}（顺序不同）", Struct, StructReordered, TransferSemantics.Stream, TransferSemantics.Stream, false },
        { "{seq,value} → {a:u64,b:f32}（同名尺寸不同名）", Struct, StructRenamed, TransferSemantics.Stream, TransferSemantics.Stream, false },

        // **各自构造、结构相同**也必须判兼容（record 的 `==` 对集合成员是引用比较，不能拿来判形状）
        { "独立构造的同形状", Struct, StructAgain, TransferSemantics.Stream, TransferSemantics.Stream, true },

        // 传递语义：跨语义不判兼容（模块内部组帧或插入转换模块）
        { "语义不一致", F32, F32, TransferSemantics.Stream, TransferSemantics.Message, false },
        { "语义一致（消息）", F32, F32, TransferSemantics.Message, TransferSemantics.Message, true },
    };

    [Theory]
    [MemberData(nameof(CompatibilityCases))]
    public void 契约兼容矩阵(
        string description,
        ContractShape sender,
        ContractShape receiver,
        TransferSemantics senderSemantics,
        TransferSemantics receiverSemantics,
        bool expected)
    {
        // **唯一入口**：未声明类型时传 null（规则自动回退为形状比较），不再有"只比形状"的独立重载。
        var result = ContractCompatibility.Check(
            sender, senderSemantics, senderTypeId: null, receiver, receiverSemantics, receiverTypeId: null);

        result.IsCompatible.ShouldBe(expected, $"{description}：{result.Reason}");

        if (!expected)
        {
            result.Reason.ShouldNotBeNullOrWhiteSpace("不兼容必须给出**可读原因**（UI 要明示差异）");
        }
    }

    // ------------------------------------------------------------ T2 · 快速建网 R0–R6

    // 建网只由「**接触且对齐**」或「**端口标签同名**」判定：加入模块**不**自动连线
    // （隐式连线反直觉，不利学习与探索）。

    [Fact]
    public void 移除实例_应释放承载与订阅且不影响其他实例()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime, new Core.Settings.SettingsService(runtime, TempSettingsPath()));

        Register(services, typeof(ViewSenderModule));
        Register(services, typeof(Receiver32Module));
        services.AddModule("s1", "test/view-sender").Succeeded.ShouldBeTrue();
        services.AddModule("s2", "test/view-sender").Succeeded.ShouldBeTrue();
        services.AddModule("r1", "test/receiver32").Succeeded.ShouldBeTrue();
        TestLink.Link(services, new PortRef("s1", "out"), new PortRef("r1", "in"), "link-1");

        runtime.Carriers.Carriers.Count.ShouldBe(3, "每个实例一个承载（专属线程）");
        runtime.Subscriptions.Count.ShouldBe(3, "每个实例一套订阅缓冲");
        var diagnosticSources = services.GetSubscriptionSources();
        diagnosticSources.ShouldContain(
            s => s.InstanceId == "s1" && s.DataSourceName == "value",
            "声明里的数据源应建立订阅缓冲");

        // 移除 s1：级联清理网络 + 释放承载与订阅
        services.RemoveModule("s1").Succeeded.ShouldBeTrue();

        runtime.Carriers.Carriers.Count.ShouldBe(2, "被移除实例的承载应停止并释放");
        runtime.Subscriptions.Count.ShouldBe(2, "订阅缓冲随之释放");
        runtime.Subscriptions.ShouldNotContain(s => s.InstanceId == "s1");
        runtime.Bindings.Bindings.ShouldBeEmpty("s1 参与的网络应级联清理");
        services.GetLayout().ShouldNotContain(l => l.InstanceId == "s1", "布局随之清理");

        // 其他实例不受影响
        runtime.Content.Instances.Single(i => i.InstanceId == "s2").State.ShouldBe(ModuleInstanceState.Active);
        runtime.Issues.Recent(50).ShouldNotContain(
            i => i.ObjectPath.Contains("s2", StringComparison.Ordinal),
            "其余实例不应因为另一个实例被移除而报问题");
    }

    [Fact]
    public void 移除全部实例_应回到空现场()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime, new Core.Settings.SettingsService(runtime, TempSettingsPath()));

        Register(services, typeof(ViewSenderModule));
        services.AddModule("s1", "test/view-sender").Succeeded.ShouldBeTrue();
        services.AddModule("s2", "test/view-sender").Succeeded.ShouldBeTrue();

        services.RemoveModule("s1").Succeeded.ShouldBeTrue();
        services.RemoveModule("s2").Succeeded.ShouldBeTrue();

        runtime.Carriers.Carriers.ShouldBeEmpty();
        runtime.Subscriptions.ShouldBeEmpty();
        runtime.Content.InstanceCount.ShouldBe(0);
        runtime.Bindings.Bindings.ShouldBeEmpty();

        // 空现场仍然可用：再次加入不受影响
        services.AddModule("s3", "test/view-sender").Succeeded.ShouldBeTrue();
        runtime.Carriers.Carriers.Count.ShouldBe(1);
    }

    // ------------------------------------------------------------ 同一实例自回环（显式 / 命名两条入口）

    [Fact]
    public void 同一实例的发送口与接收口_可形成命名自回环并投递到自己的接收口()
    {
        using var runtime = new ModuleRuntime();
        CoreRuntimeTests.AddModule(runtime, "self", "self-loop", typeof(CoreRuntimeTests.SelfLoopModule));

        CoreRuntimeTests.SelfLoopModule.Received.Clear();
        CoreRuntimeTests.SetLabel(runtime, "self", CoreRuntimeTests.SelfLoopModule.OutPort, "1");
        CoreRuntimeTests.SetLabel(runtime, "self", CoreRuntimeTests.SelfLoopModule.InPort, "1");

        // 同名标签在同一实例上形成源 → 宿：网络包含两个成员（不再被"同实例"排除）。
        var network = runtime.Content.Networks.ShouldHaveSingleItem();
        network.Name.ShouldBe("1");
        network.Sources.ShouldBe([new NetworkMember("self", "out", PortDirection.Send)]);
        network.Targets.ShouldBe([new NetworkMember("self", "in", PortDirection.Receive)]);

        var binding = runtime.Bindings.Bindings.ShouldHaveSingleItem();
        binding.Source.ShouldBe(new NetworkMember("self", "out", PortDirection.Send));
        binding.Targets.ShouldBe([new NetworkMember("self", "in", PortDirection.Receive)]);

        runtime.Content.TryGetInstance("self", out var instance).ShouldBeTrue();
        instance!.Context.Ports.TrySend("out", BitConverter.GetBytes(3.5f))
            .ShouldBeTrue("自回环是合法绑定：发送应被自己的接收口队列接受");

        SpinWait.SpinUntil(() => CoreRuntimeTests.SelfLoopModule.Received.Count == 1, 2000)
            .ShouldBeTrue("模块内自回环必须真的把数据投递到自己的接收口");
        CoreRuntimeTests.SelfLoopModule.Received.ShouldHaveSingleItem().ShouldBe(3.5f);

        runtime.Issues.Recent(50).ShouldNotContain(
            entry => entry.Issue.Category == IssueCategory.InterfaceError,
            "自回环是合法拓扑：不得报 networkExcluded / contractIncompatible 之类的排除问题");
    }

    [Fact]
    public void 同一实例不同口_可建成自回环()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "self", "self-loop", typeof(CoreRuntimeTests.SelfLoopModule));

        // **建网的唯一入口是标签 / 贴合**（显式 Connect API 已删除）：同名标签即连。
        TestLink.Link(services, new PortRef("self", "out"), new PortRef("self", "in"), "loopback");

        runtime.Content.Networks.ShouldHaveSingleItem().Name.ShouldBe("loopback");
        runtime.Bindings.Bindings.ShouldHaveSingleItem();

        runtime.Issues.Recent(50).ShouldNotContain(
            entry => entry.Issue.Category == IssueCategory.InterfaceError,
            "自回环同样不得产生「排除成员」「契约不兼容」问题");
    }

    // ------------------------------------------------------------ 辅助

    private static readonly ContractShape F32 = ContractShape.OfPrimitive(ContractPrimitive.F32);
    private static readonly ContractShape F64 = ContractShape.OfPrimitive(ContractPrimitive.F64);
    private static readonly ContractShape U64 = ContractShape.OfPrimitive(ContractPrimitive.U64);

    private static readonly ContractShape Struct =
        ContractShape.OfStruct(("seq", U64), ("value", F32));

    private static readonly ContractShape StructWithExtra =
        ContractShape.OfStruct(("seq", U64), ("value", F32), ("extra", F32));

    private static readonly ContractShape ValueOnly =
        ContractShape.OfStruct(("value", F32));

    private static readonly ContractShape StructWithWideSeq =
        ContractShape.OfStruct(("seq", F64), ("value", F32));

    /// <summary>字段顺序调换（尺寸相同但顺序不同 ⇒ 不兼容）。</summary>
    private static readonly ContractShape StructReordered =
        ContractShape.OfStruct(("value", F32), ("seq", U64));

    /// <summary>尺寸与类型都相同、**字段名不同** ⇒ 不兼容（框架不猜"是不是同一个量"）。</summary>
    private static readonly ContractShape StructRenamed =
        ContractShape.OfStruct(("a", U64), ("b", F32));

    /// <summary>与 <see cref="Struct"/> **结构相同但独立构造**（验证形状比较是结构比较，不是引用比较）。</summary>
    private static readonly ContractShape StructAgain =
        ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    /// <summary>期望的宿成员字面量（避免重复构造常量数组）。</summary>
    private static readonly string[] ReceiverInMember = ["r1.in"];

    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "hyw-rules-" + Guid.NewGuid().ToString("N") + ".json");

    private static string ModuleIdOf(Type entryType)
    {
        var attribute = (ModuleDefinitionAttribute)Attribute.GetCustomAttribute(entryType, typeof(ModuleDefinitionAttribute))!;
        return attribute.Id.ToString();
    }

    private static void Register(RuntimeServices services, Type entryType)
    {
        var attribute = (ModuleDefinitionAttribute)Attribute.GetCustomAttribute(entryType, typeof(ModuleDefinitionAttribute))!;

        var definition = new ModuleDefinition(
            attribute.Id,
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [], true,
            entryType,
            TestViewPackages.For(entryType),
            string.Empty);

        if (!services.Registry.TryResolve(definition.Id, out _))
        {
            services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    // ------------------------------------------------------------ 测试模块

    /// <summary>f32 发送口。</summary>
    [ModuleDefinition("test", "sender32")]
    public sealed class Sender32Module : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, F32));
    }

    /// <summary>f64 发送口（契约与 f32 接收口不匹配）。</summary>
    [ModuleDefinition("test", "sender64")]
    public sealed class Sender64Module : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, F64));
    }

    /// <summary>f32 接收口。</summary>
    [ModuleDefinition("test", "receiver32")]
    public sealed class Receiver32Module : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, F32));
            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }

    /// <summary>f64 接收口。</summary>
    [ModuleDefinition("test", "receiver64")]
    public sealed class Receiver64Module : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, F64));
    }

    /// <summary>结构发送口 <c>{seq: u64, value: f32}</c>。</summary>
    [ModuleDefinition("test", "struct-sender")]
    public sealed class StructSenderModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Struct));
    }

    /// <summary>结构接收口（同形状）。</summary>
    [ModuleDefinition("test", "struct-receiver")]
    public sealed class StructReceiverModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Struct));
            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }

    /// <summary>资源生命周期测试用的发送模块（**无界面**：界面声明不再由代码给出）。</summary>
    [ModuleDefinition("test", "view-sender")]
    public sealed class ViewSenderModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, F32));

            // 订阅缓冲只为**声明过的**显示源建立：这条源就是本用例要观察的那一条。
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
                "value",
                HYWDebugAssistant.Sdk.Views.ViewDataSourceKind.Value,
                ContractPrimitive.F32));
        }
    }
}