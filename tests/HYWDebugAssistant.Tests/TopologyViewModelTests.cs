using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Design;
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
        stalled.Sources[0].DisplayName.ShouldContain("旧发送器");
        stalled.Targets.Count.ShouldBe(1);
        stalled.Targets[0].DisplayName.ShouldContain("旧数据显示器");
        stalled.Targets[0].IsUnresolved.ShouldBeTrue();
        stalled.DiagnosticText.ShouldContain("停流");

        var normal = topology.Networks.First(network => !network.IsProblem);
        normal.Sources[0].Delivered.ShouldBe(1234);
        normal.Sources[0].DeliveredText.ShouldBe("1,234");
        normal.DiagnosticText.ShouldContain("正常");
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
}
