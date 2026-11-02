using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Design;
using NSubstitute;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>拓扑面板：网络列表 + 单网络诊断详情。</summary>
public class TopologyViewModelTests
{
    [Fact]
    public void 刷新_应按网络分组并默认选中异常网络()
    {
        var topology = new TopologyViewModel(EmptyRuntime.Instance);
        topology.Refresh();

        topology.NetworkCount.ShouldBe(2);
        topology.AbnormalCount.ShouldBe(1);
        topology.StalledCount.ShouldBe(1);
        topology.UnresolvedCount.ShouldBe(1);
        topology.Networks.Count.ShouldBe(2);
        topology.SelectedNetwork.ShouldNotBeNull("异常网络应默认选中");
        topology.SelectedNetwork!.HasStalled.ShouldBeTrue();

        var stalled = topology.Networks.First(network => network.IsProblem);
        stalled.Sources.Count.ShouldBe(1);

        stalled.Targets.Count.ShouldBe(1);

        stalled.Targets[0].IsUnresolved.ShouldBeTrue();


        var normal = topology.Networks.First(network => !network.IsProblem);
        normal.Sources[0].Delivered.ShouldBe(1234);
        normal.Sources[0].DeliveredText.ShouldNotBeNullOrWhiteSpace();

    }

    [Fact]
    public void 搜索与状态过滤_应只保留匹配网络且保持选中()
    {
        var topology = new TopologyViewModel(EmptyRuntime.Instance);
        topology.Refresh();

        var selectedKey = topology.SelectedNetwork!.Key;

        topology.SearchText = "bus1";
        topology.Networks.Count.ShouldBe(1);
        topology.Networks[0].DisplayName.ShouldBe("bus1");

        topology.SearchText = string.Empty;
        topology.StateFilter = TopologyViewModel.Stalled;
        topology.Networks.Count.ShouldBe(1);
        topology.Networks[0].HasStalled.ShouldBeTrue();
        topology.SelectedNetwork!.HasStalled.ShouldBeTrue();

        topology.StateFilter = TopologyViewModel.All;
        topology.Networks.Count.ShouldBe(2);

        // 全量恢复后，仍存在的原选中项不会被强行改成别的网络。
        topology.SelectedNetwork!.Key.ShouldBe(selectedKey);
    }

    [Fact]
    public void 状态过滤_异常应同时覆盖停流与悬空()
    {
        var topology = new TopologyViewModel(EmptyRuntime.Instance);
        topology.Refresh();

        topology.StateFilter = TopologyViewModel.Abnormal;
        topology.Networks.Count.ShouldBe(1);
        topology.Networks[0].IsProblem.ShouldBeTrue();
    }

    // ------------------------------------------------------------ 队列压力（R6）

    [Fact]
    public void 源丢弃分项_零值不出现且顺序固定()
    {
        var query = Query(Network("s1.out", "m1.in"));
        query.GetPortMetrics("s1", "out").Returns(new PortMetricsView(10, 5, 0)
        {
            DropQueueFull = 5,
            DropQueueFullEvicted = 3,
            DropQueueFullRejected = 0,
            DropNoCarrier = 2,
        });

        var topology = new TopologyViewModel(query);
        topology.Refresh();

        var source = topology.Networks[0].Sources[0];
        source.DropBreakdownText.ShouldBe(
            Text("topology.drop.evicted", 3) + " · " + Text("topology.drop.noCarrier", 2),
            "固定顺序：挤旧 → 拒新 → 无承载 → 承载已停 → 载荷非法 → 停流；零值不出现");
        source.MetricsText.Contains(source.DropBreakdownText, StringComparison.Ordinal)
            .ShouldBeTrue("复制/详情文本里也带分项");

        // 全零 ⇒ 空串
        var calm = Query(Network("s1.out", "m1.in"));
        calm.GetPortMetrics("s1", "out").Returns(new PortMetricsView(0, 0, 0));
        var calmTopology = new TopologyViewModel(calm);
        calmTopology.Refresh();
        calmTopology.Networks[0].Sources[0].DropBreakdownText.ShouldBeEmpty("没有丢弃就没有这一串");
    }

    [Fact]
    public void 宿队列_有压力才显示且饱和带后缀()
    {
        var query = Query(Network("s1.out", "m1.in"));
        query.GetQueueDepths().Returns(
        [
            new PortQueueDepthView("m1", "in", Depth: 3, Capacity: 8, Enqueued: 10, Dropped: 2)
            {
                Saturated = true,
                DroppedEvictedUnits = 2,
                ExpiredUnits = 1,
                DiscardedOnStopUnits = 4,
                ModuleInternalDroppedUnits = 5,
            },
        ]);

        var topology = new TopologyViewModel(query);
        topology.Refresh();

        var target = topology.Networks[0].Targets[0];
        target.QueueText.ShouldBe(Text("topology.queue.state", 3, 8) + Text("topology.queue.saturatedSuffix"));
        target.IsSaturated.ShouldBeTrue();
        target.QueueDetailText.ShouldBe(
            Text("topology.drop.expired", 1)
            + " · " + Text("topology.drop.discardedOnStop", 4)
            + " · " + Text("topology.drop.moduleInternal", 5));
        target.DetailText.Contains(target.QueueDetailText, StringComparison.Ordinal)
            .ShouldBeTrue("复制/详情文本里带队列轴分项");

        // 没有队列条目 ⇒ 不显示队列文本
        var quiet = Query(Network("s1.out", "m1.in"));
        quiet.GetQueueDepths().Returns([]);
        var quietTopology = new TopologyViewModel(quiet);
        quietTopology.Refresh();
        quietTopology.Networks[0].Targets[0].QueueText.ShouldBeEmpty();
        quietTopology.Networks[0].Targets[0].IsSaturated.ShouldBeFalse();
    }

    [Fact]
    public void 网络诊断_有饱和宿时改用饱和句()
    {
        var saturated = Query(Network("s1.out", "m1.in"));
        saturated.GetQueueDepths().Returns(
        [
            new PortQueueDepthView("m1", "in", Depth: 5, Capacity: 8, Enqueued: 9, Dropped: 0)
            {
                Saturated = true,
            },
        ]);

        var topology = new TopologyViewModel(saturated);
        topology.Refresh();
        topology.Networks[0].DiagnosticText.ShouldBe(Text("topology.diagnostic.saturated"));

        var calm = new TopologyViewModel(Query(Network("s1.out", "m1.in")));
        calm.Refresh();
        calm.Networks[0].DiagnosticText.ShouldBe(
            Text("topology.diagnostic.normal"),
            "没有饱和宿 ⇒ 仍是既有的「正常」句");
    }

    // ------------------------------------------------------------ 辅助

    private static string Text(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TopologyViewModel).Assembly, key, args);

    /// <summary>一条单源单宿的命名网络。</summary>
    private static NetworkView Network(string source, string target) =>
        new("bus1", IsAnonymous: false, IsStalled: false, source, [target])
        {
            Key = $"{source}→{target}",
            Sources = [source],
        };

    /// <summary>可脚本化的查询面：网络/实例/队列水位由用例给，其余给安全默认值。</summary>
    private static IRuntimeQuery Query(NetworkView network)
    {
        var query = Substitute.For<IRuntimeQuery>();
        query.GetNetworks().Returns([network]);
        query.GetInstances().Returns([]);
        query.GetQueueDepths().Returns([]);
        query.GetPortMetrics(Arg.Any<string>(), Arg.Any<string>()).Returns(new PortMetricsView(0, 0, 0));
        return query;
    }
}
