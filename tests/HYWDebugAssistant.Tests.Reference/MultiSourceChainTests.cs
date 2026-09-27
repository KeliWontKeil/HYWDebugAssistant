using HYWDebugAssistant.Modules.Reference.Fuser;
using HYWDebugAssistant.Modules.Reference.ManualSender;
using HYWDebugAssistant.Modules.Reference.Monitor;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// M1-18（**ADR-0050 落地**）：**多源参考链路** —— 两个发布者 → 多源融合器 → 信息显示模块。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>这条链验证"多源是**合法的一等组合**"：两个发送端各自成网进入融合器的**两个口**，
///     融合器自己决定合并策略（框架**永不仲裁**，ADR-0050 M3/Q4），再作为**新的发布者**把结果发给下游；</item>
///   <item>来源**用口区分**（M4：来源不进数据面）——这是应用层看到"值从哪来"的推荐形态；</item>
///   <item>三个网络（两条入网 + 一条出网）都靠**端口网络标签**建立，与手工验收的操作完全一致。</item>
/// </list>
/// </remarks>
public class MultiSourceChainTests
{
    [Fact]
    public void 多源链路_两路输入应按策略融合并转发给下游()
    {
        using var composition = new ManualChainTests.Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("s1", "hyw/reference-manual-sender/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("s2", "hyw/reference-manual-sender/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("fuser", "hyw/reference-fuser/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", "hyw/reference-monitor/1.0.0").Succeeded.ShouldBeTrue();

        // 三条网络：s1→fuser.in1（"a"）· s2→fuser.in2（"b"）· fuser.out→monitor.in（"bus"）
        composition.Services.UpdatePortNetwork("s1", ManualSenderModule.OutputPort, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FuserModule.InputPort1, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("s2", ManualSenderModule.OutputPort, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FuserModule.InputPort2, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FuserModule.OutputPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", MonitorModule.InputPort, "bus").Succeeded.ShouldBeTrue();

        composition.Services.GetNetworks().Count.ShouldBe(3, "两条入网 + 一条出网");

        // 【ADR-0052】真实组合里，角色与"角色间数据流"应经公开查询面暴露：
        // 融合器**自己声明**一块"两入一出"的功能分块（框架不派生角色）；`m/n` 由网络成员关系算出。
        var fuse = composition.Services.GetRoles("fuser").ShouldHaveSingleItem();
        fuse.RoleId.ShouldBe("fuse");
        fuse.Inputs.Count.ShouldBe(2, "两路输入属于**同一块**功能（合成一路是一件事）");
        fuse.Outputs.ShouldBe([FuserModule.OutputPort]);
        fuse.InboundRoleCount.ShouldBe(2, "两路输入分别来自 s1、s2 的角色 ⇒ 2 个对端角色");
        fuse.OutboundRoleCount.ShouldBe(1, "输出去往 monitor 的角色 ⇒ 1");

        var flows = composition.Services.GetRoleFlows("fuser");
        flows.Count.ShouldBe(3, "两条入流 + 一条出流（按「本端角色, 方向, 对端实例, 对端角色」聚合）");
        flows.ShouldAllBe(f => f.LocalRoleKey == "fuser/fuse");

        var outFlow = flows.Where(f => f.LocalIsSend).ShouldHaveSingleItem();
        outFlow.PeerInstanceId.ShouldBe("monitor");
        outFlow.PeerRoleKey.ShouldBe("monitor/show");
        outFlow.PortPairCount.ShouldBe(1, "一个口对 ⇒ 1");
        outFlow.NetworkKeys.ShouldHaveSingleItem(
            "网络身份（框架派生的连接身份）只用于取指标——**不在界面上显示网络名**");

        var inFlows = flows.Where(f => !f.LocalIsSend).ToList();
        inFlows.Count.ShouldBe(2, "**多源**：两个发布者各自一条（来源用实例/角色区分，不靠网络名）");
        inFlows.Select(f => f.PeerInstanceId).OrderBy(x => x, StringComparer.Ordinal).ShouldBe(["s1", "s2"]);

        // 内部处理顺序是另一回事：参考模块都是单块 ⇒ **零声明零线**（框架不猜）
        composition.Services.GetRoleOrder("fuser").ShouldBeEmpty();

        // 纯源/纯汇模块：各一个角色、没有内部顺序
        composition.Services.GetRoles("s1").ShouldHaveSingleItem()
            .OutboundRoleCount.ShouldBe(1, "手动发送只有一个输出口、连到一个对端角色");
        composition.Services.GetRoles("s1").ShouldHaveSingleItem()
            .InboundRoleCount.ShouldBeNull("没有输入口 ⇒ null（界面写 `--`）");
        composition.Services.GetRoleOrder("s1").ShouldBeEmpty();
        composition.Services.GetRoleOrder("monitor").ShouldBeEmpty();

        // ① 第 1 路发 5.0
        Send(composition, "s1", "5.0");

        composition.WaitForCount("fuser", FuserModule.Count1Source, atLeast: 1)
            .ShouldBeTrue("融合器应在**第 1 个口**上收到数据（来源用口区分）");
        composition.WaitUntilLatest("fuser", FuserModule.FusedSource, 5.0)
            .ShouldBeTrue("Latest 策略下第 1 路的到达即成为当前融合值");

        // ② 第 2 路发 1.5 ⇒ Latest：后到的赢（不比较时刻，跨口无时间序 —— M4b）
        Send(composition, "s2", "1.5");

        composition.WaitForCount("fuser", FuserModule.Count2Source, atLeast: 1)
            .ShouldBeTrue("融合器的**第 2 个口**也应收到数据（多源：两个发布者各走各的口）");
        composition.WaitUntilLatest("fuser", FuserModule.FusedSource, 1.5)
            .ShouldBeTrue("Latest = 后到的覆盖先到的");

        // ③ 切到平均策略：两路最近值取算术平均（(5.0 + 3.5) / 2 = 4.25）
        composition.Services.UpdateConfiguration("fuser", new FuserConfig
        {
            Mode = FusionMode.Average,
            ForwardMerged = true,
            PublishToView = true,
        }).Succeeded.ShouldBeTrue();

        Send(composition, "s2", "3.5");

        composition.WaitUntilLatest("fuser", FuserModule.FusedSource, 4.25)
            .ShouldBeTrue("Average = 两路最近值的算术平均");

        // ④ 融合结果**作为新的发布者**继续向下游转发（fuser.out → monitor.in）
        composition.WaitForCount("monitor", MonitorModule.CountSource, atLeast: 3)
            .ShouldBeTrue("下游应收到**融合后**的数据单元（转发开关默认开）");
        composition.WaitUntilLatest("monitor", MonitorModule.LastSource, 4.25)
            .ShouldBeTrue("下游看到的最后值 = 融合值");

        // ⑤ **角色维度的速率/投递必须真的取到框架诊断**【ADR-0053】：曾经按"逐源键"建表、用**网络键**查
        //    ⇒ 永远查不到 ⇒ 界面上角色的速率**恒显示占位符**（用户实测："角色本身速率无法显示，一直是---"）。
        var fuserRole = composition.Services.GetRoles("fuser").ShouldHaveSingleItem();
        fuserRole.RateBytesPerSecond.ShouldBeGreaterThan(
            0, "融合器角色的速率 = 它产出侧网络的搬运字节速率之和（必须 > 0，否则说明键空间又对不上了）");
        fuserRole.Delivered.ShouldBeGreaterThan(0, "投递数同样来自框架诊断（按网络聚合）");

        composition.Services.GetRoles("monitor").ShouldHaveSingleItem()
            .RateBytesPerSecond.ShouldBeGreaterThan(0, "纯汇角色取**输入侧**网络 ⇒ 同样必须 > 0");
    }

    [Fact]
    public void 多源融合器_固定优先级策略应忽略第2路()
    {
        using var composition = new ManualChainTests.Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("s1", "hyw/reference-manual-sender/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("s2", "hyw/reference-manual-sender/1.0.0").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("fuser", "hyw/reference-fuser/1.0.0").Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("s1", ManualSenderModule.OutputPort, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FuserModule.InputPort1, "a").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("s2", ManualSenderModule.OutputPort, "b").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("fuser", FuserModule.InputPort2, "b").Succeeded.ShouldBeTrue();

        composition.Services.UpdateConfiguration("fuser", new FuserConfig
        {
            Mode = FusionMode.Priority,
        }).Succeeded.ShouldBeTrue();

        Send(composition, "s1", "8.0");
        composition.WaitUntilLatest("fuser", FuserModule.FusedSource, 8.0).ShouldBeTrue();

        Send(composition, "s2", "1.0");
        composition.WaitForCount("fuser", FuserModule.Count2Source, atLeast: 1)
            .ShouldBeTrue("第 2 路确实收到了数据");

        // Priority：第 2 路虽然到达，但第 1 路优先 ⇒ 融合值不变
        Thread.Sleep(150);
        composition.Latest("fuser", FuserModule.FusedSource)
            .ShouldBe(8.0, 0.0001, "Priority = 只要第 1 路有数据就用第 1 路");
    }

    private static void Send(ManualChainTests.Composition composition, string instanceId, string value)
    {
        composition.Submit(instanceId, ManualSenderModule.ValueInput, value).Succeeded.ShouldBeTrue();
        composition.Services.SendCommand(instanceId, ManualSenderModule.SendCommand).Succeeded.ShouldBeTrue();
    }
}
