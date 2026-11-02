using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Tests.Reference.Stubs;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **多源链路判据**（载体 = 两个正式发送器 + 融合测试桩）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>这条链验证"**多源是合法的一等组合**"：两个发布者各自成网进入下游的**两个口**，
///     下游模块自己决定合并策略（框架**永不仲裁**），再作为**新的发布者**把结果发给更下游；</item>
///   <item>来源**用口区分**（M4：来源不进数据面）；</item>
///   <item><b>载体</b>：两端的发布者用**正式模块**（`text-sender` / `hex-sender`），中间的「两入一出」
///     用**本项目内的测试桩**（框架能力「两入一出 + 多源 + 角色分块」必须继续被覆盖 ⇒ 由桩承载）。</item>
/// </list>
/// </remarks>
public class MultiSourceChainTests
{
    [Fact]
    public void 多源链路_两路输入应按策略融合并转发给下游()
    {
        using var composition = new BusinessChainTests.Composition();
        composition.InstallBusinessPackages();
        composition.Register("fusion-stub", typeof(FusionStubModule));

        composition.Services.AddModule("s1", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("s2", HexSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("fuser", "test/fusion-stub").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        // 三条网络：s1→fuser.in1（"a"）· s2→fuser.in2（"b"）· fuser.out→monitor.in（"bus"）
        composition.Services.UpdatePortNetwork("s1", TextSenderModule.OutPort, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FusionStubModule.InputPort1, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("s2", HexSenderModule.OutPort, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FusionStubModule.InputPort2, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FusionStubModule.OutputPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        composition.Services.GetNetworks().Count.ShouldBe(3, "两条入网 + 一条出网");

        // 角色与「角色间数据流」经公开查询面暴露：桩**自己声明**一块「两入一出」的功能分块
        var fuse = composition.Services.GetRoles("fuser").ShouldHaveSingleItem();
        fuse.RoleId.ShouldBe(FusionStubModule.RoleId);
        fuse.Inputs.Count.ShouldBe(2, "两路输入属于**同一块**功能（合成一路是一件事）");
        fuse.Outputs.ShouldBe([FusionStubModule.OutputPort]);
        fuse.InboundRoleCount.ShouldBe(2, "两路输入分别来自 s1、s2 的角色 ⇒ 2 个对端角色");
        fuse.OutboundRoleCount.ShouldBe(1, "输出去往 monitor 的角色 ⇒ 1");

        var flows = composition.Services.GetRoleFlows("fuser");
        flows.Count.ShouldBe(3, "两条入流 + 一条出流");
        flows.ShouldAllBe(f => f.LocalRoleKey == "fuser/fuse");

        var outFlow = flows.Where(f => f.LocalIsSend).ShouldHaveSingleItem();
        outFlow.PeerInstanceId.ShouldBe("monitor");
        outFlow.PortPairCount.ShouldBe(1, "一个口对 ⇒ 1");

        var inFlows = flows.Where(f => !f.LocalIsSend).ToList();
        inFlows.Count.ShouldBe(2, "**多源**：两个发布者各自一条（来源用实例/角色区分，不靠网络名）");
        inFlows.Select(f => f.PeerInstanceId).OrderBy(x => x, StringComparer.Ordinal).ShouldBe(["s1", "s2"]);

        // 内部处理顺序是另一回事：桩是单块 ⇒ **零声明零线**（框架不猜）
        composition.Services.GetRoleOrder("fuser").ShouldBeEmpty();
        composition.Services.GetRoles("s1").ShouldHaveSingleItem().InboundRoleCount.ShouldBeNull("没有输入口 ⇒ null");
        composition.Services.GetRoleOrder("monitor").ShouldBeEmpty();

        // 显示器：每包一行，便于断言转发内容
        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // ① 第 1 路发 5.0
        SendText(composition, "s1", "5.0");

        composition.WaitForCount("fuser", FusionStubModule.Count1Source, atLeast: 1)
            .ShouldBeTrue("融合器应在**第 1 个口**上收到数据（来源用口区分）");
        composition.WaitUntilLatest("fuser", FusionStubModule.FusedSource, 5.0)
            .ShouldBeTrue("Latest 策略下第 1 路的到达即成为当前融合值");

        // ② 第 2 路发 1.5 ⇒ Latest：后到的赢
        SendHex(composition, "s2", "31 2E 35");   // "1.5"

        composition.WaitForCount("fuser", FusionStubModule.Count2Source, atLeast: 1)
            .ShouldBeTrue("融合器的**第 2 个口**也应收到数据（多源：两个发布者各走各的口）");
        composition.WaitUntilLatest("fuser", FusionStubModule.FusedSource, 1.5)
            .ShouldBeTrue("Latest = 后到的覆盖先到的");

        // ③ 切到平均策略：(5.0 + 3.5) / 2 = 4.25
        composition.Services.UpdateConfiguration("fuser", new FusionStubConfig
        {
            Mode = FusionStrategy.Average,
            ForwardMerged = true,
        }).Succeeded.ShouldBeTrue();

        SendHex(composition, "s2", "33 2E 35");   // "3.5"

        composition.WaitUntilLatest("fuser", FusionStubModule.FusedSource, 4.25)
            .ShouldBeTrue("Average = 两路最近值的算术平均");

        // ④ 融合结果**作为新的发布者**继续向下游转发（fuser.out → monitor.in）
        composition.WaitForDisplayCount("monitor", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue("下游应收到**融合后**的数据单元（转发开关默认开）");
        composition.WaitUntilDisplayText("monitor", DataMonitorModule.LogSource, "4.25")
            .ShouldBeTrue("下游看到的显示内容 = 融合值");

        // ⑤ **角色维度的速率/投递必须真的取到框架诊断**
        // ⚠ 速率口径 = **整秒桶**（上一个 ≥1 秒窗口的积累量）⇒ 断言前必须**等满一个窗口**，
        //    否则读到的是"首个窗口未满"的 0（这不是回归，是口径本身）。
        composition.Services.GetRoles("fuser");   // 先观察一次，建立窗口基准
        Thread.Sleep(1200);
        var fuserRole = composition.Services.GetRoles("fuser").ShouldHaveSingleItem();
        fuserRole.PayloadRateBytesPerSecond.ShouldBeGreaterThan(
            0, "融合器角色的速率 = 它产出侧网络的**有效载荷字节速率**之和（必须 > 0，否则说明键空间又对不上了）");
        fuserRole.Delivered.ShouldBeGreaterThan(0, "投递数同样来自框架诊断（按网络聚合）");
        composition.Services.GetRoles("monitor").ShouldHaveSingleItem()
            .PayloadRateBytesPerSecond.ShouldBeGreaterThan(0, "纯汇角色取**输入侧**网络 ⇒ 同样必须 > 0");
    }

    [Fact]
    public void 多源融合_固定优先级策略应忽略第2路()
    {
        using var composition = new BusinessChainTests.Composition();
        composition.InstallBusinessPackages();
        composition.Register("fusion-stub", typeof(FusionStubModule));

        composition.Services.AddModule("s1", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("s2", HexSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("fuser", "test/fusion-stub").Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("s1", TextSenderModule.OutPort, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FusionStubModule.InputPort1, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("s2", HexSenderModule.OutPort, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FusionStubModule.InputPort2, "b").Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("fuser", new FusionStubConfig
        {
            Mode = FusionStrategy.Priority,
        }).Succeeded.ShouldBeTrue();

        SendText(composition, "s1", "8.0");
        composition.WaitUntilLatest("fuser", FusionStubModule.FusedSource, 8.0).ShouldBeTrue();

        SendHex(composition, "s2", "31 2E 30");   // "1.0"
        composition.WaitForCount("fuser", FusionStubModule.Count2Source, atLeast: 1)
            .ShouldBeTrue("第 2 路确实收到了数据");

        // Priority：第 2 路虽然到达，但第 1 路优先 ⇒ 融合值不变
        Thread.Sleep(150);
        composition.Latest("fuser", FusionStubModule.FusedSource)
            .ShouldBe(8.0, 0.0001, "Priority = 只要第 1 路有数据就用第 1 路");
    }

    private const string TextSenderModuleId = "hyw/text-sender";

    private const string HexSenderModuleId = "hyw/hex-sender";

    private const string MonitorModuleId = "hyw/data-monitor";

    /// <summary>文本发送器发一段文本（输入 + 发送命令 —— 与界面同一条路径）。</summary>
    private static void SendText(BusinessChainTests.Composition composition, string instanceId, string text)
    {
        composition.Submit(instanceId, TextSenderModule.TextSourceOf(0), text).Succeeded.ShouldBeTrue();
        composition.Services.SendCommand(instanceId, TextSenderModule.SendCommand).Succeeded.ShouldBeTrue();
    }

    /// <summary>HEX 发送器发一段十六进制（等价于同一段文本的字节）。</summary>
    private static void SendHex(BusinessChainTests.Composition composition, string instanceId, string hex)
    {
        composition.Submit(instanceId, HexSenderModule.HexSourceOf(0), hex).Succeeded.ShouldBeTrue();
        composition.Services.SendCommand(instanceId, HexSenderModule.SendCommand).Succeeded.ShouldBeTrue();
    }
}
