using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1/T2：模块窗口的选中态与端口标记的“多源/多宿”投影回归。
/// </summary>
/// <remarks>
/// 这两条是**纯视图模型**判据：不起窗口、不等速率窗口；真实观感仍由人工验收。
/// </remarks>
public class UiSelectionAndRateProjectionTests
{
    [Fact]
    public void 多源多收_每个发送端和接收端的端口标记都必须入网且速率有值()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "s2", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));
        CoreRuntimeTests.AddModule(runtime, "r2", "receiver", typeof(CoreRuntimeTests.ReceiverModule));

        foreach (var (id, port) in new[]
                 {
                     ("s1", CoreRuntimeTests.SenderModule.OutPort),
                     ("s2", CoreRuntimeTests.SenderModule.OutPort),
                     ("r1", CoreRuntimeTests.ReceiverModule.InPort),
                     ("r2", CoreRuntimeTests.ReceiverModule.InPort),
                 })
        {
            CoreRuntimeTests.SetLabel(runtime, id, port, "bus");
        }

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        foreach (var id in new[] { "s1", "s2", "r1", "r2" })
        {
            var module = windows.Modules.Single(m => m.InstanceId == id);
            var marker = module.Ports.ShouldHaveSingleItem();

            marker.IsConnected.ShouldBeTrue($"{id} 必须入网（多源时不能用 NetworkView.Source 只认第一个源）");
            marker.NetworkName.ShouldBe("bus");
            marker.RateText.ShouldNotBe("---", $"{id} 已连接 ⇒ 速率应是 0.00 B/s 或正值，不是“无连接”");
        }
    }

    [Fact]
    public void 清空选中后再次选中同一模块必须生效_且锁定模块仍可选中但不能拖动()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "s2", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var s1 = windows.Modules.Single(m => m.InstanceId == "s1");
        var s2 = windows.Modules.Single(m => m.InstanceId == "s2");

        windows.Select("s1");
        s1.IsSelected.ShouldBeTrue();

        windows.ClearSelection();
        windows.SelectedInstanceId.ShouldBeNull("清空选中必须也清掉唯一事实源，否则再次选中同一模块会被吞掉");
        s1.IsSelected.ShouldBeFalse();

        windows.Select("s1");
        s1.IsSelected.ShouldBeTrue("清空后再次点击同一模块的边框必须能重新选中（手柄才会出现）");

        windows.Select("s2");
        s1.IsSelected.ShouldBeFalse();
        s2.IsSelected.ShouldBeTrue();

        // 锁定只禁止拖动/缩放；**可选中**（组合范围可以包含锁定模块）。
        windows.ToggleLock("s1");
        windows.Select("s1");
        s1.IsSelected.ShouldBeTrue("锁定模块仍可被选中（只是不能拖动/缩放）");
        s2.IsSelected.ShouldBeFalse();
        windows.SelectedInstanceId.ShouldBe("s1");
        windows.BeginDrag("s1", 0, 0, independent: false).ShouldBeFalse("锁定模块不得进入拖动");

        windows.ToggleLock("s1");
        windows.Select("s1");
        s1.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void 多选_锚点保留第一个选中_移除锚点后顺延()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "s2", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var s1 = windows.Modules.Single(m => m.InstanceId == "s1");
        var s2 = windows.Modules.Single(m => m.InstanceId == "s2");

        windows.EnsureSelectionInitialized();
        windows.SelectedInstanceIds.ShouldBe(["s1", "s2"], "组合页首次进入默认全选");
        s1.IsSelected.ShouldBeTrue();
        s2.IsSelected.ShouldBeTrue();

        windows.ClearSelection();
        windows.Select("s1");
        windows.ToggleSelection("s2");

        windows.SelectedInstanceIds.ShouldBe(["s1", "s2"]);
        s1.IsSelected.ShouldBeTrue();
        s2.IsSelected.ShouldBeTrue();
        s1.IsPrimary.ShouldBeTrue("Shift 加选不抢走第一个选中的锚点");
        s2.IsPrimary.ShouldBeFalse();

        windows.RemoveFromSelection("s1");
        windows.SelectedInstanceIds.ShouldBe(["s2"]);
        s1.IsSelected.ShouldBeFalse();
        s2.IsPrimary.ShouldBeTrue("移除锚点后顺延到下一个选中项");

        windows.SelectAll();
        windows.SelectionCount.ShouldBe(2);
        s2.IsPrimary.ShouldBeTrue("全选时应保留仍在集合中的锚点");
        windows.ClearSelection();
        windows.HasSelection.ShouldBeFalse();
    }
}
