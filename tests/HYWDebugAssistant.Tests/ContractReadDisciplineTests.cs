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
/// T1：**接收方的读取纪律**——「兼容」不等于「能按自己的契约整块读」。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>兼容只保证"接收方声明的字段集**覆盖**发送方"；版本升级（尾部追加）时到达的数据报长度 =
///     **发送方**的契约尺寸，尾部字段**根本不在**；</item>
///   <item>因此接收方必须**按到达长度读**（<see cref="PayloadReader.TryCreate"/>）：硬构造会抛异常，
///     而**实测后果**是该模块的投递循环整体退出（表现为"模块莫名不再收数"）；</item>
///   <item>本类同时把**多源同名标签混入不同版本**这一情形钉成回归：
///     **同一个宿口上可以出现不同长度的数据报**，只读公共前缀才是安全用法。</item>
/// </list>
/// </remarks>
public class ContractReadDisciplineTests
{
    private static readonly ContractShape V1 = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));            // 12B

    private static readonly ContractShape V2 = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)),
        ("tag", ContractShape.OfPrimitive(ContractPrimitive.U32)));              // 16B（尾部追加）

    private const string TypeId = "test/read-discipline";

    // ------------------------------------------------------------ SDK 原语

    [Fact]
    public void 读取器_长度不足应安全失败_长度足够应读出前缀字段()
    {
        var v2Layout = ContractLayout.FromShape(V2);
        var datagram = new byte[V1.SizeInBytes];                                  // 12B（旧版本发出来的）
        var writer = new PayloadWriter(datagram, ContractLayout.FromShape(V1));
        writer.WriteU64("seq", 42);
        writer.WriteF32("value", 1.5f);

        // 用**自己的** 16B 契约硬构造 ⇒ 抛异常（这是模块最容易踩的写法）
        Should.Throw<ArgumentException>(() => _ = new PayloadReader(datagram, v2Layout));

        // 安全用法：长度不足 ⇒ false（不抛）；长度足够 ⇒ 读得到
        PayloadReader.TryCreate(datagram, v2Layout, out _).ShouldBeFalse("数据报比本口契约短 ⇒ 安全失败");

        var v1Layout = ContractLayout.FromShape(V1);
        PayloadReader.TryCreate(datagram, v1Layout, out var reader).ShouldBeTrue();
        reader.ReadU64("seq").ShouldBe(42UL);
        reader.ReadF32("value").ShouldBe(1.5f);

        // 真正 16B 的数据报用 16B 契约当然没问题
        var full = new byte[V2.SizeInBytes];
        new PayloadWriter(full, v2Layout).WriteF32("value", 2.5f);
        PayloadReader.TryCreate(full, v2Layout, out var fullReader).ShouldBeTrue();
        fullReader.ReadF32("value").ShouldBe(2.5f);
    }

    // ------------------------------------------------------------ 端到端：形状不一致的处理

    [Fact]
    public void 形状不一致_应拒绝直连_且不产生任何投递()
    {
        // v1 源（12B）→ v2 接收方（16B，尾部追加）**不兼容**：
        // 版本升级按破坏性变更处理 ⇒ 拒绝直连（这是「不让接收方拿到短于自己契约的数据报」的根治办法）
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        PrefixReceiver.Reset();

        Add(runtime, "s1", "discipline-sender-v1", typeof(SenderV1));
        Add(runtime, "r2", "discipline-receiver", typeof(PrefixReceiver));

        var result = runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember("s1", "out", PortDirection.Send),
            [new NetworkMember("r2", "in", PortDirection.Receive)],
            "link")));

        result.Applied.ShouldBeFalse("形状不一致（12B vs 16B）必须拒绝直连");
        result.Failures.ShouldContain(f => f.Contains("形状不一致", StringComparison.Ordinal));

        SendV1(runtime, "s1", seq: 1, value: 1.5f);
        Thread.Sleep(150);

        PrefixReceiver.Lengths.ShouldBeEmpty("未建网 ⇒ 不应有任何投递");
    }

    [Fact]
    public void 多源同口混入不同版本_应只保留形状一致的那一路_同一口上永不出现两种长度()
    {
        // **同名标签但版本/形状不同**的两个发布者：
        // 形状不一致的那一路**不建立该 (源,宿) 对的投递**（其余源照常），
        // ⇒ 同一宿口上**永远不会**混入不同长度的数据报。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        PrefixReceiver.Reset();

        Add(runtime, "s1", "discipline-sender-v1", typeof(SenderV1));   // 12B
        Add(runtime, "s2", "discipline-sender-v2", typeof(SenderV2));   // 16B
        Add(runtime, "r2", "discipline-receiver", typeof(PrefixReceiver));   // 声明 v2（16B）

        foreach (var (instance, port) in new[] { ("s1", "out"), ("s2", "out"), ("r2", "in") })
        {
            runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
                instance, new Core.Api.PortGeometryView(port, 0.5, false, "bus")))).Applied.ShouldBeTrue();
        }

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Sources.Count.ShouldBe(2, "同名标签多发送端仍是一条多源网络（多源本身仍合法）");
        network.Targets.Count.ShouldBe(1);

        SendV1(runtime, "s1", seq: 1, value: 1.5f);
        SendV2(runtime, "s2", seq: 2, value: 9.5f);

        WaitUntil(() => PrefixReceiver.Lengths.Count >= 1, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("形状一致的那一路（v2）应正常送达");

        Thread.Sleep(200);   // 给"不该到达"的那一路足够时间暴露问题

        PrefixReceiver.Lengths.ShouldAllBe(length => length == V2.SizeInBytes,
            "同一宿口上只应出现**一种长度**（形状不一致的源不建立该对投递）");
        PrefixReceiver.Values.ShouldBe([9.5f], "只有形状一致的那一路能到达");

        // v1 那一路：**逐对排除** + Issue（不是整条网络拒绝，也不是静默丢弃）
        runtime.Issues.Recent(20).ShouldContain(e =>
            e.Issue.Message.Contains("契约兼容", StringComparison.Ordinal));
        runtime.Issues.Recent(20).ShouldNotContain(e =>
            e.Issue.Message.Contains("异常退出", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 测试辅助

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

    private static void SendV1(ModuleRuntime runtime, string instanceId, ulong seq, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();
        var layout = ContractLayout.FromShape(V1);
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", seq);
        writer.WriteF32("value", value);
        _ = instance!.Context.Ports.TrySend("out", payload);
    }

    private static void SendV2(ModuleRuntime runtime, string instanceId, ulong seq, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();
        var layout = ContractLayout.FromShape(V2);
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", seq);
        writer.WriteF32("value", value);
        writer.WriteU32("tag", 7);
        _ = instance!.Context.Ports.TrySend("out", payload);
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

        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, definition))).Applied.ShouldBeTrue();
    }

    /// <summary>v1 发送模块（声明 <c>test/read-discipline@1</c> = 12B）。</summary>
    [ModuleDefinition("test", "discipline-sender-v1", "1.0.0")]
    public sealed class SenderV1 : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 1, V1));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, V1)
            {
                Type = new DataTypeRef(TypeId, 1),
            });
        }
    }

    /// <summary>v2 发送模块（同标识**尾部追加** <c>tag:u32</c> ⇒ 16B）。</summary>
    [ModuleDefinition("test", "discipline-sender-v2", "1.0.0")]
    public sealed class SenderV2 : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 2, V2));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, V2)
            {
                Type = new DataTypeRef(TypeId, 2),
            });
        }
    }

    /// <summary>
    /// 声明 **v2（16B）** 但**按到达长度读公共前缀**的接收模块 —— 正确的接收方写法。
    /// </summary>
    [ModuleDefinition("test", "discipline-receiver", "1.0.0")]
    public sealed class PrefixReceiver : IModule
    {
        /// <summary>每次收到的**数据报实际长度**（用于断言"长度 = 发送方契约尺寸"）。</summary>
        public static List<int> Lengths { get; } = [];

        /// <summary>解出的值（只读公共前缀字段）。</summary>
        public static List<float> Values { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 2, V2));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, V2)
            {
                Type = new DataTypeRef(TypeId, 2),
            });

            context.Ports.SetReceiveHandler("in", payload =>
            {
                Lengths.Add(payload.Length);

                // 只读**一定存在**的公共前缀字段（seq/value 在 v1/v2 都在）
                if (PayloadReader.TryCreate(payload, ContractLayout.FromShape(V1), out var reader))
                {
                    Values.Add(reader.ReadF32("value"));
                }
            });
        }

        /// <summary>清空观测（静态集合跨用例残留：xUnit 不保证类内用例顺序）。</summary>
        public static void Reset()
        {
            Lengths.Clear();
            Values.Clear();
        }
    }
}
