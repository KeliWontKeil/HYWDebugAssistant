using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1（ADR-0046 D2/D6）：**数据类型注册表 + 兼容三层判据 + 解码器注册**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>同标识同版本 → 兼容（且要求两边机械镜像一致）；</item>
///   <item>同标识不同版本 → **版本规则**（接收方 = 发送方的**尾部追加**则兼容）；</item>
///   <item>**不同标识 → 框架无资格判定 ⇒ 默认不兼容**（结构相同也不认）+ 显式确认可强连；</item>
///   <item>未声明类型 → **回退结构比较**（旧模块照常）；</item>
///   <item>解码器随类型注册：**模块提供、框架只回调**（D1 ③）。</item>
/// </list>
/// </remarks>
public class DataTypeCompatibilityTests
{
    // ------------------------------------------------------------ 三层判据

    [Fact]
    public void 同标识同版本_应兼容并可投递()
    {
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));
        AddModule(runtime, "r1", "typed-receiver-v1", typeof(TypedReceiverV1Module));

        Connect(runtime, "s1", "out", "r1", "in", "bus").ShouldBeTrue("同标识同版本 ⇒ 兼容");

        SendFrom(runtime, "s1", 42UL, 2.5f).ShouldBeTrue();

        WaitUntil(() => TypedReceiverV1Module.Received.Count > 0, TimeSpan.FromSeconds(2))
            .ShouldBeTrue("兼容连接应能投递");
        TypedReceiverV1Module.Received[0].ShouldBe((2.5f));
    }

    [Fact]
    public void 同标识不同版本_应不兼容_版本升级按破坏性变更处理()
    {
        // 【ADR-0051】**版本不同 = 不是同一种数据报**：直连拒绝（不再有"尾部追加 = 兼容"）。
        // 用**形状完全相同、仅版本不同**的一对，才能单独验证"版本"这一条规则
        // （形状本身不同时，先被形状检查拦下——那也是对的，但测不到版本规则）。
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));
        AddModule(runtime, "r2", "typed-receiver-same-shape-v2", typeof(TypedReceiverSameShapeV2Module));

        var result = TryConnect(runtime, "s1", "out", "r2", "in", "bus");

        result.Applied.ShouldBeFalse("同标识不同版本 ⇒ 不兼容（版本升级属破坏性变更）");
        result.Failures.ShouldContain(f => f.Contains("版本不同", StringComparison.Ordinal));
    }

    [Fact]
    public void 各自声明的同形状类型_应判兼容_形状比较必须是结构比较()
    {
        // **防隐形陷阱**：`ContractShape` 是 record，但结构形状的字段集合做的是**引用比较**
        // ⇒ 若用 `==` 判形状，两个模块各自 new 出"字面完全一样"的形状会被判不相等，
        // 表现为"只有引用同一个静态实例才连得上"。本用例钉住"结构比较"这一语义。
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "sa", "typed-sender-independent", typeof(IndependentShapeSenderModule));
        AddModule(runtime, "rb", "typed-receiver-independent", typeof(IndependentShapeReceiverModule));

        Connect(runtime, "sa", "out", "rb", "in", "bus")
            .ShouldBeTrue("两个模块**各自构造**的同一形状必须判兼容（结构比较，不是引用比较）");

        SendFromIndependent(runtime, "sa", 3.25f);
        WaitUntil(() => IndependentShapeReceiverModule.Received.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        IndependentShapeReceiverModule.Received[0].ShouldBe(3.25f);
    }

    [Fact]
    public void 同标识不同版本_改已有字段类型应不兼容()
    {
        // v2' = 把 value 由 f32 改成 f64（不是尾部追加）⇒ 不兼容（否则接收方按错误类型解读）
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));
        AddModule(runtime, "rb", "typed-receiver-broken", typeof(TypedReceiverBrokenUpgradeModule));

        var result = TryConnect(runtime, "s1", "out", "rb", "in", "bus");

        result.Applied.ShouldBeFalse("改已有字段类型不属兼容升级");
        result.Failures.ShouldContain(f => f.Contains("契约不兼容", StringComparison.Ordinal));
    }

    [Fact]
    public void 不同标识_结构相同也应默认不兼容_显式确认后可强连()
    {
        // 【D6 第三层】**框架无资格判定"不同标识是否同一种量"** ⇒ 默认不兼容（即便结构完全相同）
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));
        AddModule(runtime, "ro", "typed-receiver-other-id", typeof(TypedReceiverOtherIdModule));

        var rejected = TryConnect(runtime, "s1", "out", "ro", "in", "bus");
        rejected.Applied.ShouldBeFalse("不同标识默认不兼容");
        rejected.Failures.ShouldContain(f => f.Contains("数据类型标识不同", StringComparison.Ordinal));

        // 显式确认强连 ⇒ 放行（结构仍必须覆盖）
        Connect(runtime, "s1", "out", "ro", "in", "forced", allowUnverified: true)
            .ShouldBeTrue("用户显式确认后可强连");

        SendFrom(runtime, "s1", 9UL, 3.5f).ShouldBeTrue(Diagnose(runtime, "s1"));
        WaitUntil(() => TypedReceiverOtherIdModule.Received.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue();
    }

    [Fact]
    public void 未声明类型_应回退结构比较_旧模块照常()
    {
        // 向后兼容：不声明类型的端口仍按"传递语义 + 形状子集"判定
        using var runtime = new ModuleRuntime();

        AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));

        Connect(runtime, "s1", "out", "r1", "in", "bus").ShouldBeTrue("双方未声明类型 ⇒ 回退结构比较");

        // 一边声明、一边未声明 ⇒ **同样回退结构比较**（不因"没声明"而拒绝）；
        // 形状一致（{seq:u64, value:f32}）⇒ 兼容；形状不同则按结构规则拒绝（不猜类型）
        AddModule(runtime, "r2", "typed-receiver-v1", typeof(TypedReceiverV1Module));
        AddModule(runtime, "sl", "legacy-struct-sender", typeof(LegacyStructSenderModule));

        Connect(runtime, "sl", "out", "r2", "in", "legacy").ShouldBeTrue("单边声明类型 ⇒ 回退结构比较");
        Connect(runtime, "s1", "out", "r2", "in", "shape-mismatch").ShouldBeFalse(
            "f32 无法覆盖 {seq:u64, value:f32}（结构规则照常生效，不因对方有类型而放宽）");
    }

    // ------------------------------------------------------------ 声明的机械镜像

    [Fact]
    public void 口引用未声明类型_应回退结构并报Issue()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));
        AddModule(runtime, "rx", "typed-sender-dangling-ref", typeof(DanglingRefSenderModule));

        var instance = runtime.Content.Instances.Single(i => i.InstanceId == "rx");
        instance.DataTypes.ShouldBeEmpty("引用未声明类型 ⇒ 机械镜像里没有它");
        runtime.Issues.Recent(20).ShouldContain(e =>
            e.Issue.Message.Contains("引用了未声明的数据类型", StringComparison.Ordinal));

        // 两边都回退结构 ⇒ 形状一致即兼容
        Connect(runtime, "s1", "out", "rx", "out2", "x").ShouldBeFalse("方向不合法（两个发送口）");

        AddModule(runtime, "r1", "typed-receiver-v1", typeof(TypedReceiverV1Module));
        Connect(runtime, "rx", "out2", "r1", "in", "y").ShouldBeTrue("回退结构：形状一致即兼容");
    }

    [Fact]
    public void 重复声明同标识同版本_应报Issue并忽略后者()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "dup", "typed-dup", typeof(DuplicateTypeModule));

        var instance = runtime.Content.Instances.Single(i => i.InstanceId == "dup");
        instance.DataTypes.Count.ShouldBe(1, "同（标识, 版本）只留第一次声明");
        runtime.Issues.Recent(20).ShouldContain(e =>
            e.Issue.Message.Contains("数据类型重复声明", StringComparison.Ordinal));
    }

    [Fact]
    public void 计量块与解码器_应随类型注册并被框架持有()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s1", "typed-sender-v1", typeof(TypedSenderV1Module));

        var instance = runtime.Content.Instances.Single(i => i.InstanceId == "s1");
        var type = instance.FindDataType(new DataTypeRef(TypedSenderV1Module.TypeId, 1));

        type.ShouldNotBeNull("机械镜像应能按（标识, 版本）查到类型");
        type!.SizeInBytes.ShouldBe(12, "尺寸由形状机械给出（尺寸属传输）");
        type.Metering.SamplesPerUnit.ShouldBe(1, "计量块缺省值");
        type.Metering.ChannelCount.ShouldBe(1);
        type.Metering.NominalRateHz.ShouldBe(1000, "显式声明的标称速率");

        // 解码器：**由模块提供**，框架只回调
        type.Decoder.ShouldNotBeNull("发送模块应注册解码器");

        var sample = new byte[TypedSenderV1Module.Layout.SizeInBytes];
        _ = BitConverter.TryWriteBytes(sample.AsSpan(8), 1.5f);
        type.Decoder!(sample, out var value).ShouldBeTrue("按声明的原语解码（小端）");
        value.ShouldBe(1.5);

        // 尺寸不足 ⇒ 解码失败（不猜）
        type.Decoder(new byte[2], out _).ShouldBeFalse("样本长度不足 = 解码失败，不回退推断");
    }

    // ------------------------------------------------------------ 解码器注册（D1 ③）

    [Fact]
    public void 数据源引用已声明类型_框架应交出模块的解码器()
    {
        // 【ADR-0046 D1 ③】呈现期解码**由模块提供**：数据源引用类型 ⇒ 框架把该类型的解码器句柄交出去
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        AddModule(runtime, "ui1", "typed-view-module", typeof(TypedViewModule));

        var sources = services.GetSubscriptionSources();
        sources.Count.ShouldBe(3, "三个数据源各一个订阅缓冲");

        var typed = sources.Single(s => s.DataSourceName == "value");
        typed.Decoder.ShouldNotBeNull("引用已声明类型 ⇒ 解码器由模块提供");
        typed.DataType.ShouldBe(new DataTypeRef(TypedViewModule.TypeId, 1), "视图应暴露类型身份");

        // 回调一次：模块解出的是**它自己的语义**（此处 = 原语 f32 的字节）
        var sample = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(sample, 2.5f);
        typed.Decoder!(sample, out var decoded).ShouldBeTrue();
        decoded.ShouldBe(2.5);

        // 旧形态（只声明原语、不引用类型）⇒ 不给句柄（呈现层走参考实现回退）
        var legacy = sources.Single(s => s.DataSourceName == "legacy");
        legacy.Decoder.ShouldBeNull("未引用类型 ⇒ 无模块解码器（UI 走参考实现回退）");
        legacy.ValueType.ShouldBe(ContractPrimitive.F32);

        // 引用了**未声明**的类型 ⇒ 同样不给句柄（不猜）
        var dangling = sources.Single(s => s.DataSourceName == "dangling");
        dangling.Decoder.ShouldBeNull("引用未声明类型 ⇒ 无解码器（呈现层显示占位，不猜）");
    }

    // ------------------------------------------------------------ 四档速率 / 三类丢弃 / 水位（R4-3）

    [Fact]
    public void 四档速率_应由框架机械给出且各档含义不同()
    {
        // 【ADR-0046 D8】**四档并列**且**分母不同**：单元（按源）· 搬运字节（按宿）· 有效载荷字节 · 样本/通道样本
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        AddModule(runtime, "s1", "metered-sender", typeof(MeteredSenderModule));
        AddModule(runtime, "r1", "metered-receiver", typeof(MeteredReceiverModule));
        Connect(runtime, "s1", "out", "r1", "in", "bus").ShouldBeTrue();

          // 第一次观察只建基准（速率 0），随后发一批数据，再观察一次得到速率。
          // 间隔要**大于速率计的整秒窗口**（否则窗口未满按设计返回上次速率 —— 见 `RateMeter.WindowSeconds`）。
        _ = services.GetDiagnostics();
        Thread.Sleep((int)(Core.Diagnostics.RateMeter.WindowSeconds * 1000) + 80);

        for (var i = 0; i < 50; i++)
        {
            SendMetered(runtime, "s1", (ulong)i);
        }

        WaitUntil(() => MeteredReceiverModule.Received.Count >= 50, TimeSpan.FromSeconds(2)).ShouldBeTrue();

        var diagnostic = services.GetDiagnostics().ShouldHaveSingleItem();

        diagnostic.Key.ShouldBe("s1.out→r1.in");
        diagnostic.Published.ShouldBe(50, "**发布计数 = 每个数据报一次**（按源）");
        diagnostic.Delivered.ShouldBe(50, "单宿：投递次数与发布数相同");
        diagnostic.UnitRateHz.ShouldBeGreaterThan(0, "单元速率（按源）应由框架给出");
        diagnostic.NominalRateHz.ShouldBe(2000, "类型声明里的标称速率应被带出来（只用于「实测 vs 标称」）");

        // 各档**分母不同**：
        // 搬运字节（按宿）= 投递数 × **契约尺寸**（16B，含自描述与填充）
        // 有效载荷字节     = 发布数 × **类型声明的 PayloadSizeInBytes**（4B，业务数据）
        var unit = diagnostic.UnitRateHz;
        diagnostic.BusByteRate.ShouldBe(unit * MeteredSenderModule.ContractSize, 0.001, "搬运字节 = 单元速率 × 契约尺寸");
        diagnostic.PayloadByteRate.ShouldBe(unit * 4, 0.001, "有效载荷字节 = 单元速率 × 声明的载荷字节");

        // 样本档 = 单元速率 × SamplesPerUnit；通道样本档再 × ChannelCount
        diagnostic.SampleRateHz.ShouldBe(unit * 8, 0.001, "每单元 8 个样本");
        diagnostic.ChannelSampleRateHz.ShouldBe(unit * 8 * 4, 0.001, "每样本 4 通道");
    }

    /// <summary>
    /// **有效载荷字节速率：逐单元读"有效长度字段"**（2026-09 用户裁定"速率只要有效数据，截断的不应该算在
    /// 有效数据里面，整个数据包大小速率是底层协议，用户想知道的肯定是有效速率"）。
    /// </summary>
    /// <remarks>
    /// 契约 18B、每个数据报只带 **3 字节**业务数据 ⇒ 两档必须**各说各的数**：
    /// 搬运字节 = 单元速率 × 18，有效载荷字节 = 单元速率 × **3**。
    /// 若框架退回"按静态尺寸猜"（= 契约尺寸），第二档会等于第一档 ⇒ 本用例红。
    /// </remarks>
    [Fact]
    public void 有效载荷字节_应逐单元读有效长度字段()
    {
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        AddModule(runtime, "v1", "variable-length-sender", typeof(VariableLengthSenderModule));
        AddModule(runtime, "vr", "variable-length-receiver", typeof(VariableLengthReceiverModule));
        Connect(runtime, "v1", "out", "vr", "in", "bus").ShouldBeTrue();

        VariableLengthReceiverModule.Received.Clear();

        // 首次观察只建基准；等满一个整秒窗口再放数据（`RateMeter.WindowSeconds`）
        _ = services.GetDiagnostics();
        Thread.Sleep((int)(Core.Diagnostics.RateMeter.WindowSeconds * 1000) + 80);

        for (var i = 0; i < 50; i++)
        {
            SendVariable(runtime, "v1", payloadLength: 3);
        }

        WaitUntil(() => VariableLengthReceiverModule.Received.Count >= 50, TimeSpan.FromSeconds(2)).ShouldBeTrue();

        var diagnostic = services.GetDiagnostics().ShouldHaveSingleItem();
        var unit = diagnostic.UnitRateHz;

        diagnostic.Published.ShouldBe(50);
        diagnostic.BusByteRate.ShouldBe(
            unit * VariableLengthSenderModule.Shape.SizeInBytes,
            0.001,
            "搬运字节速率仍按**契约尺寸**（18B，含补零）算");
        diagnostic.PayloadByteRate.ShouldBe(
            unit * 3,
            0.001,
            "有效载荷字节速率必须按**每个单元真实的 len**（3B）算 —— 这正是用户要的「有效速率」");
        diagnostic.PayloadByteRate.ShouldBeLessThan(
            diagnostic.BusByteRate / 4,
            "两者必须差出量级（18 vs 3），否则说明有效长度字段没被读");
    }

    /// <summary>
    /// **有效长度超出契约尺寸 ⇒ 夹到契约尺寸**（不让一个坏字段把速率算到天上去）。
    /// </summary>
    /// <remarks>
    /// 载荷字段是**数据**，模块写错/链路损坏都可能给出 9999 ⇒ 上限必须由框架（它才知道契约尺寸）夹住。
    /// 夹取发生在**累计之前**（`Binding.PayloadBytesOfUnit`），所以速率与"长度 ≤ 契约尺寸"的既有不变量一致。
    /// </remarks>
    [Fact]
    public void 有效长度超过契约尺寸应被夹住()
    {
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        AddModule(runtime, "v2", "variable-length-sender", typeof(VariableLengthSenderModule));
        AddModule(runtime, "vr2", "variable-length-receiver", typeof(VariableLengthReceiverModule));
        Connect(runtime, "v2", "out", "vr2", "in", "bus").ShouldBeTrue();

        VariableLengthReceiverModule.Received.Clear();
        _ = services.GetDiagnostics();
        Thread.Sleep((int)(Core.Diagnostics.RateMeter.WindowSeconds * 1000) + 80);

        for (var i = 0; i < 20; i++)
        {
            SendVariable(runtime, "v2", payloadLength: 9999);
        }

        WaitUntil(() => VariableLengthReceiverModule.Received.Count >= 20, TimeSpan.FromSeconds(2)).ShouldBeTrue();

        var diagnostic = services.GetDiagnostics().ShouldHaveSingleItem();
        var contractSize = VariableLengthSenderModule.Shape.SizeInBytes;

        diagnostic.PayloadByteRate.ShouldBe(
            diagnostic.UnitRateHz * contractSize,
            0.001,
            "越界的有效长度必须夹到契约尺寸（不是放任它把速率算大）");
    }

    [Fact]
    public void 队列水位与三类丢弃_应按口与按原因分别可查()
    {
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        // 容量**按口计**（ADR-0050 Q2）：把每口容量压到 8，便于稳定制造"队满"
        runtime.Carriers.UpdateOptions(new HYWDebugAssistant.DataPlane.Carriers.CarrierOptions { QueueCapacity = 8 });

        AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        AddModule(runtime, "slow", "slow-receiver", typeof(SlowReceiverModule));
        Connect(runtime, "s1", "out", "slow", "in", "bus").ShouldBeTrue();

        // 塞一批：接收端很慢 ⇒ 队列满 ⇒ **传输丢弃**
        for (var i = 0; i < 40; i++)
        {
            SendF32(runtime, "s1", i);
        }

        var depths = services.GetQueueDepths();
        var queue = depths.ShouldHaveSingleItem();

        queue.InstanceId.ShouldBe("slow");
        queue.PortName.ShouldBe("in");
        queue.Capacity.ShouldBe(8, "容量**按口计**（ADR-0050 Q2）");
        queue.Depth.ShouldBeLessThanOrEqualTo(queue.Capacity);
        queue.Enqueued.ShouldBeGreaterThan(0);
        queue.Dropped.ShouldBeGreaterThan(0, "慢消费必然触发队满丢弃");
        queue.Fill.ShouldBeInRange(0, 1);
        queue.BusByteRate.ShouldBeGreaterThanOrEqualTo(0);

        var diagnostic = services.GetDiagnostics().ShouldHaveSingleItem();

        // **三类丢弃分开**：传输丢弃 = 队列满 + 无承载 + 载荷非法；停流未投递单列且**不混入**
        diagnostic.DropTransport.ShouldBe(
            diagnostic.DropQueueFull + diagnostic.DropNoCarrier + diagnostic.DropInvalidPayload,
            "传输丢弃 = 三类原因之和");
        diagnostic.DropQueueFull.ShouldBeGreaterThan(0);
        diagnostic.DropStalled.ShouldBe(0, "本用例没有停流 ⇒ 停流未投递必须为 0（不得混算）");
    }

    [Fact]
    public void 停流期间的发送_应只计入停流未投递而非传输丢弃()
    {
        using var runtime = new ModuleRuntime();
        var services = new Core.Services.RuntimeServices(runtime);

        AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));
        Connect(runtime, "s1", "out", "r1", "in", "bus").ShouldBeTrue();

        runtime.Apply(ChangeSet.OfOne(new SetModuleEnabledOperation("r1", Enabled: false)))
            .Applied.ShouldBeTrue("禁用接收端 = 绑定停流");

        _ = SendF32(runtime, "s1", 1);

        var diagnostic = services.GetDiagnostics().ShouldHaveSingleItem();
        diagnostic.DropStalled.ShouldBe(1, "停流未投递是**合法状态**");
        diagnostic.DropTransport.ShouldBe(0, "停流**不得**计入传输丢弃");
        diagnostic.Delivered.ShouldBe(0);
    }

    // ------------------------------------------------------------ 测试辅助

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(
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

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static bool Connect(
        ModuleRuntime runtime, string sourceInstance, string sourcePort, string targetInstance, string targetPort, string name,
        bool allowUnverified = false) =>
        TryConnect(runtime, sourceInstance, sourcePort, targetInstance, targetPort, name, allowUnverified).Applied;

    private static Core.Changes.ChangeSetResult TryConnect(
        ModuleRuntime runtime, string sourceInstance, string sourcePort, string targetInstance, string targetPort, string name,
        bool allowUnverified = false) =>
        runtime.Apply(ChangeSet.OfOne(new ConnectNetworkOperation(
            new NetworkMember(sourceInstance, sourcePort, PortDirection.Send),
            [new NetworkMember(targetInstance, targetPort, PortDirection.Receive)],
            name,
            allowUnverified)));

    private static string Diagnose(ModuleRuntime runtime, string instanceId)
    {
        var bindings = string.Join(
            " | ",
            runtime.Bindings.Bindings.Select(b =>
                $"{b.Source}→{string.Join(",", b.Targets)}: suspended={b.IsSuspended} stalled={b.Network.IsStalled} published={b.Diagnostics.Published} delivered={b.Diagnostics.Delivered} dropped={b.Diagnostics.Dropped}"));

        var carriers = string.Join(
            " | ",
            runtime.Carriers.Carriers.Select(c => $"{c.InstanceId}: [{string.Join(",", c.PortNames)}]"));

        return $"bindings={runtime.Bindings.Bindings.Count} {bindings} ## carriers: {carriers} ## instance={instanceId}";
    }

    private static bool SendFrom(ModuleRuntime runtime, string instanceId, ulong seq, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var layout = TypedSenderV1Module.Layout;
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", seq);
        writer.WriteF32("value", value);

        return instance!.Context.Ports.TrySend("out", payload);
    }

    /// <summary>发一个"独立形状"模块的数据报（<c>{seq:u64, value:f32}</c>）。</summary>
    private static bool SendFromIndependent(ModuleRuntime runtime, string instanceId, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var layout = IndependentShapeSenderModule.Layout;
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", 1);
        writer.WriteF32("value", value);

        return instance!.Context.Ports.TrySend("out", payload);
    }

    /// <summary>发一个"计量用"数据报（16B：seq:u64 + value:f32 + tag:u32，声明载荷 4B）。</summary>
    private static bool SendMetered(ModuleRuntime runtime, string instanceId, ulong seq)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var layout = MeteredSenderModule.Layout;
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU64("seq", seq);
        writer.WriteF32("value", seq);
        writer.WriteU32("tag", 7);

        return instance!.Context.Ports.TrySend("out", payload);
    }

    /// <summary>
    /// 发一个"**变长有效载荷**"数据报（18B：<c>len:u16</c> + <c>data:bytes[16]</c>），
    /// <c>len</c> 由调用方给（可以故意给越界值，验证框架夹取）。
    /// </summary>
    /// <param name="runtime">运行时。</param>
    /// <param name="instanceId">发送实例。</param>
    /// <param name="payloadLength">写进 `len` 字段的"有效字节数"（**不校验** —— 校验是被测对象的事）。</param>
    private static bool SendVariable(ModuleRuntime runtime, string instanceId, ushort payloadLength)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var layout = VariableLengthSenderModule.Layout;
        var payload = new byte[layout.SizeInBytes];
        var writer = new PayloadWriter(payload, layout);
        writer.WriteU16("len", payloadLength);
        writer.WriteBytes("data", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);

        return instance!.Context.Ports.TrySend("out", payload);
    }

    private static bool SendF32(ModuleRuntime runtime, string instanceId, float value)
    {
        runtime.Content.TryGetInstance(instanceId, out var instance).ShouldBeTrue();

        var payload = new byte[sizeof(float)];
        _ = BitConverter.TryWriteBytes(payload, value);
        return instance!.Context.Ports.TrySend("out", payload);
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

    // ------------------------------------------------------------ 测试模块（类型化）

    /// <summary>类型化发送模块：声明 <c>test/payload@v1</c> = <c>{seq:u64, value:f32}</c>（12B）。</summary>
    [ModuleDefinition("test", "typed-sender-v1", "1.0.0")]
    public sealed class TypedSenderV1Module : IModule
    {
        public const string OutPort = "out";
        public const string TypeId = "test/payload";

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 1, Shape)
            {
                Metering = new MeteringBlock { NominalRateHz = 1000, SamplesPerUnit = 1, ChannelCount = 1 },
                Decoder = (ReadOnlySpan<byte> sample, out double decoded) =>
                    sample.Length >= TypedSenderV1Module.Layout.SizeInBytes
                        ? PrimitiveSampleDecoder.TryDecode(ContractPrimitive.F32, sample[8..], out decoded)
                        : Fail(out decoded),
                SemanticTags = ["V", "unit=V"],
            });

            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
            {
                Type = new DataTypeRef(TypeId, 1),
            });
        }

        private static bool Fail(out double value)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>未声明类型但形状相同的发送模块（验证"单边声明 ⇒ 回退结构比较"）。</summary>
    [ModuleDefinition("test", "legacy-struct-sender", "1.0.0")]
    public sealed class LegacyStructSenderModule : IModule
    {
        public const string OutPort = "out";

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                OutPort, PortDirection.Send, TypedSenderV1Module.Shape));
    }

    /// <summary>**各自构造同一形状**的发送模块（<c>{seq:u64, value:f32}</c>；用于验证形状比较是结构比较）。</summary>
    [ModuleDefinition("test", "typed-sender-independent", "1.0.0")]
    public sealed class IndependentShapeSenderModule : IModule
    {
        public const string OutPort = "out";

        /// <summary>**刻意不与接收模块共享实例**：同一个字面量、各自构造（record 的 `==` 会判不相等）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/independent", 1, Shape));
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
            {
                Type = new DataTypeRef("test/independent", 1),
            });
        }
    }

    /// <summary>与之对应：**另外 new 一个相同结构**的接收模块。</summary>
    [ModuleDefinition("test", "typed-receiver-independent", "1.0.0")]
    public sealed class IndependentShapeReceiverModule : IModule
    {
        /// <summary>**刻意独立构造**（与发送模块不共享实例）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        public static List<float> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/independent", 1, Shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Shape)
            {
                Type = new DataTypeRef("test/independent", 1),
            });

            context.Ports.SetReceiveHandler("in", payload =>
                Received.Add(new PayloadReader(payload, IndependentShapeSenderModule.Layout).ReadF32("value")));
        }
    }

    /// <summary>类型化接收模块：声明**同一个** <c>test/payload@v1</c>。</summary>
    [ModuleDefinition("test", "typed-receiver-v1", "1.0.0")]
    public sealed class TypedReceiverV1Module : IModule
    {
        public const string InPort = "in";

        public static List<float> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                TypedSenderV1Module.TypeId, 1, TypedSenderV1Module.Shape));

            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, TypedSenderV1Module.Shape)
            {
                Type = new DataTypeRef(TypedSenderV1Module.TypeId, 1),
            });

            context.Ports.SetReceiveHandler(InPort, payload =>
                Received.Add(new PayloadReader(payload, TypedSenderV1Module.Layout).ReadF32("value")));
        }
    }

    /// <summary>声明 <c>test/payload@v2</c> 但**形状与 v1 完全相同**的接收模块（单独验证"版本规则"）。</summary>
    [ModuleDefinition("test", "typed-receiver-same-shape-v2", "1.0.0")]
    public sealed class TypedReceiverSameShapeV2Module : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                TypedSenderV1Module.TypeId, 2, TypedSenderV1Module.Shape));

            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, TypedSenderV1Module.Shape)
            {
                Type = new DataTypeRef(TypedSenderV1Module.TypeId, 2),
            });
        }
    }

    /// <summary>类型化接收模块：声明 <c>test/payload@v2</c> = v1 + 尾部 <c>tag:u8</c>（13B）。</summary>    [ModuleDefinition("test", "typed-receiver-v2", "1.0.0")]
    public sealed class TypedReceiverV2Module : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)),
            ("tag", ContractShape.OfPrimitive(ContractPrimitive.U8)));

        public static List<float> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypedSenderV1Module.TypeId, 2, Shape));

            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
            {
                Type = new DataTypeRef(TypedSenderV1Module.TypeId, 2),
            });

            // 收方按**自己的**布局读前缀字段（v1 数据报 ≤ v2 布局）
            context.Ports.SetReceiveHandler(InPort, payload =>
                Received.Add(new PayloadReader(payload, TypedSenderV1Module.Layout).ReadF32("value")));
        }
    }

    /// <summary>坏升级：v2 把已有字段 <c>value</c> 由 f32 改成 f64（**不是尾部追加**）。</summary>
    [ModuleDefinition("test", "typed-receiver-broken", "1.0.0")]
    public sealed class TypedReceiverBrokenUpgradeModule : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F64)));

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypedSenderV1Module.TypeId, 2, Shape));
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
            {
                Type = new DataTypeRef(TypedSenderV1Module.TypeId, 2),
            });
        }
    }

    /// <summary>不同标识但**结构完全相同**（用于验证"框架无资格判定 ⇒ 默认不兼容"）。</summary>
    [ModuleDefinition("test", "typed-receiver-other-id", "1.0.0")]
    public sealed class TypedReceiverOtherIdModule : IModule
    {
        public const string InPort = "in";
        public const string OtherTypeId = "other/payload";

        public static List<float> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                OtherTypeId, 1, TypedSenderV1Module.Shape));

            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, TypedSenderV1Module.Shape)
            {
                Type = new DataTypeRef(OtherTypeId, 1),
            });

            context.Ports.SetReceiveHandler(InPort, payload =>
                Received.Add(new PayloadReader(payload, TypedSenderV1Module.Layout).ReadF32("value")));
        }
    }

    /// <summary>口引用了**自己没声明**的类型（框架只能回退结构判定，并报 Issue）。</summary>
    [ModuleDefinition("test", "typed-sender-dangling-ref", "1.0.0")]
    public sealed class DanglingRefSenderModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out2", PortDirection.Send, TypedSenderV1Module.Shape)
            {
                Type = new DataTypeRef("nobody/declared-this", 1),
            });
    }

    /// <summary>重复声明同一（标识, 版本）。</summary>
    [ModuleDefinition("test", "typed-dup", "1.0.0")]
    public sealed class DuplicateTypeModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("dup/type", 1, TypedSenderV1Module.Shape));
            context.Declaration.DeclareDataType(new DataTypeDeclaration("dup/type", 1, TypedSenderV1Module.Shape));
        }
    }

    /// <summary>
    /// 三个数据源：**引用已声明类型** / **只声明原语（旧形态）** / **引用未声明类型**。
    /// </summary>
    [ModuleDefinition("test", "typed-view-module", "1.0.0")]
    public sealed class TypedViewModule : IModule
    {
        public const string TypeId = "test/sample";

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                TypeId, 1, ContractShape.OfPrimitive(ContractPrimitive.F32))
            {
                Decoder = PrimitiveSampleDecoder.For(ContractPrimitive.F32),
            });

            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.Numeric, "value")],
                [
                    new ViewDataSource("value", valueType: ContractPrimitive.F32)
                    {
                        Type = new DataTypeRef(TypeId, 1),
                    },
                    new ViewDataSource("legacy", valueType: ContractPrimitive.F32),
                    new ViewDataSource("dangling", valueType: ContractPrimitive.F32)
                    {
                        Type = new DataTypeRef("nobody/declared-this", 1),
                    },
                ]));
        }
    }

    /// <summary>
    /// 计量用发送模块：契约 16B，但**声明的有效载荷只有 4B**、每单元 **8 样本 × 4 通道**、标称 **2000 Hz**。
    /// </summary>
    /// <remarks>刻意让四档**各不相同**，否则"四档"就退化成同一个数（测不出分母差异）。</remarks>
    [ModuleDefinition("test", "metered-sender", "1.0.0")]
    public sealed class MeteredSenderModule : IModule
    {
        public const string OutPort = "out";
        public const string TypeId = "test/metered";

        public const int ContractSize = 16;

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)),
            ("tag", ContractShape.OfPrimitive(ContractPrimitive.U32)));

        public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

        public void OnRegistered(IModuleContext context)
        {
            Shape.SizeInBytes.ShouldBe(ContractSize);

            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 1, Shape)
            {
                Metering = new MeteringBlock
                {
                    PayloadSizeInBytes = 4,
                    SamplesPerUnit = 8,
                    ChannelCount = 4,
                    NominalRateHz = 2000,
                },
            });

            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
            {
                Type = new DataTypeRef(TypeId, 1),
            });
        }
    }

    /// <summary>接收 <c>test/metered</c> 的模块（同标识同版本 ⇒ 兼容）。</summary>
    [ModuleDefinition("test", "metered-receiver", "1.0.0")]
    public sealed class MeteredReceiverModule : IModule
    {
        public const string InPort = "in";

        public static List<ulong> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                MeteredSenderModule.TypeId, 1, MeteredSenderModule.Shape));

            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, MeteredSenderModule.Shape)
            {
                Type = new DataTypeRef(MeteredSenderModule.TypeId, 1),
            });

            context.Ports.SetReceiveHandler(InPort, payload =>
                Received.Add(new PayloadReader(payload, MeteredSenderModule.Layout).ReadU64("seq")));
        }
    }

    /// <summary>
    /// **变长有效载荷**的发送模块：契约 <c>{ len:u16, data:bytes[16] }</c>（18B，尾部补零），
    /// 计量块**声明有效长度字段 = `len`** ⇒ 有效载荷字节速率必须逐单元读它。
    /// </summary>
    /// <remarks>这是 `RawBytes`（文本/HEX 发送器）的最小复刻：**同一个"契约定长、载荷变长"问题**。</remarks>
    [ModuleDefinition("test", "variable-length-sender", "1.0.0")]
    public sealed class VariableLengthSenderModule : IModule
    {
        public const string OutPort = "out";
        public const string TypeId = "test/variable";
        public const int DataCapacity = 16;

        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
            ("data", ContractShape.OfBytes(DataCapacity)));

        public static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

        public void OnRegistered(IModuleContext context)
        {
            // 契约尺寸 = 2 + 16 = 18（发 3 字节业务数据也搬满 18 字节）
            Shape.SizeInBytes.ShouldBe(18);

            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 1, Shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "len" },
            });

            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
            {
                Type = new DataTypeRef(TypeId, 1),
            });
        }
    }

    /// <summary>接收 <c>test/variable</c> 的模块（同标识同版本 ⇒ 兼容）。</summary>
    [ModuleDefinition("test", "variable-length-receiver", "1.0.0")]
    public sealed class VariableLengthReceiverModule : IModule
    {
        public const string InPort = "in";

        public static List<ushort> Received { get; } = [];

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(
                VariableLengthSenderModule.TypeId, 1, VariableLengthSenderModule.Shape));

            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, VariableLengthSenderModule.Shape)
            {
                Type = new DataTypeRef(VariableLengthSenderModule.TypeId, 1),
            });

            context.Ports.SetReceiveHandler(InPort, payload =>
                Received.Add(new PayloadReader(payload, VariableLengthSenderModule.Layout).ReadU16("len")));
        }
    }

    /// <summary>很慢的接收端（每单元 20ms）——用于把队列打满以验证水位与"队满"丢弃。</summary>
    [ModuleDefinition("test", "slow-receiver", "1.0.0")]
    public sealed class SlowReceiverModule : IModule
    {
        public const string InPort = "in";

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, ContractShape.OfPrimitive(ContractPrimitive.F32)));

            context.Ports.SetReceiveHandler(InPort, _ => Thread.Sleep(20));
        }
    }
}
