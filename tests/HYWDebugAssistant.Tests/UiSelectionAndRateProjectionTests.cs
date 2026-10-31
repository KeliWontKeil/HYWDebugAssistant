using Avalonia;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.Geometry;
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
    public void 窗口管理列表_发现屏幕外并可召回_且行对象稳定()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.SetWorkAreas([new CanvasRect(0, 0, 800, 600)]);
        windows.Refresh();

        var module = windows.Modules.Single(item => item.InstanceId == "s1");
        var row = windows.Overview.Single(item => item.InstanceId == "s1");

        module.Width = 320;
        module.Height = 200;
        module.X = 5000;
        module.Y = 5000;
        windows.SetWorkAreas([new CanvasRect(0, 0, 800, 600)]);

        row.IsOffScreen.ShouldBeTrue($"完全在屏幕外的模块必须被识别出来（area={windows.WorkAreas.Count}, rect={module.Rect}, moduleOff={module.IsOffScreen}）");

        row.RecallCommand.Execute(null);

        row.IsOffScreen.ShouldBeFalse("行内召回必须把屏幕外窗口钳回可见区域");
        module.Rect.Right.ShouldBeLessThanOrEqualTo(800 + 0.5);
        module.Rect.Bottom.ShouldBeLessThanOrEqualTo(600 + 0.5);

        windows.Refresh();
        windows.Overview.Single(item => item.InstanceId == "s1").ShouldBeSameAs(row, "刷新不得重建行对象");

        windows.HideWindow("s1");
        windows.Refresh();
        row.IsViewVisible.ShouldBeFalse();

        row.ShowCommand.Execute(null);
        windows.Refresh();
        row.IsViewVisible.ShouldBeTrue("行内「显示」必须走与模块窗口相同的显示入口");
    }

    [Fact]
    public void 吸附预览_只保留新接触且与拖动组相关的端口对()
    {
        ConnectionCandidateView[] candidates =
        [
            new("a.out", "b.in"),   // 与新接触，涉及拖动组 ⇒ 保留
            new("b.out", "c.in"),   // 静止模块之间 ⇒ 不保留
            new("a.out", "c.in"),   // 已有连接 ⇒ 不保留
        ];
        var existing = new HashSet<(string Source, string Target)>
        {
            ("a.out", "c.in"),
        };

        var filtered = DockPreviewPolicy.Filter(candidates, existing, ["a"]);

        filtered.ShouldHaveSingleItem().ShouldBe(candidates[0]);
    }

    [Fact]
    public void 跨缩放拖动_组内成员施加同一物理位移()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "b", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var a = windows.Modules.Single(item => item.InstanceId == "a");
        var b = windows.Modules.Single(item => item.InstanceId == "b");

        a.Width = 200;
        a.Height = 100;
        b.Width = 200;
        b.Height = 100;
        a.X = 100;
        a.Y = 100;
        b.Y = 100;

        // 物理贴合：A 右缘 300；B 在 150% 屏上 ⇒ DIP X = 300 / 1.5 = 200。
        windows.SetModuleScaling("a", 1.0);
        windows.SetModuleScaling("b", 1.5);
        b.X = 200;

        windows.GroupMembersOf("a").ShouldContain("b", "物理贴合的模块必须属于同一组");

        var aBefore = a.PhysicalRect;
        var bBefore = b.PhysicalRect;

        windows.BeginDrag("a", aBefore.X + 10, aBefore.Y + 10, independent: false).ShouldBeTrue();
        windows.DragTo(aBefore.X + 30, aBefore.Y + 10);

        (a.PhysicalRect.Right - aBefore.Right).ShouldBe(20, 0.001);
        (b.PhysicalRect.Left - bBefore.Left).ShouldBe(20, 0.001);
        a.PhysicalRect.Right.ShouldBe(b.PhysicalRect.Left, 0.001);

        windows.EndDrag();
    }

    [Fact]
    public void 外壳组拖动_发绝对物理目标_不按帧累加取整()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var a = windows.Modules.Single(item => item.InstanceId == "a");
        a.Width = 200;
        a.Height = 100;
        a.Y = 0;

        // 外壳右缘 100；模块在 150% 屏上，物理左缘也贴到 100。
        windows.SetModuleScaling("a", 1.5);
        a.X = 100 / 1.5;
        windows.SetShellFrame(new CanvasRect(0, 0, 100, 200), new CanvasRect(0, 0, 100, 200));

        PixelPoint? target = null;
        windows.ShellMoveRequested += (_, value) => target = value;

        var start = a.PhysicalRect;
        windows.BeginDrag("a", start.X + 10, start.Y + 10, independent: false).ShouldBeTrue();

        // 每帧只前进 0.6 px：若按帧取整累加会得到 +10，而绝对目标应得到 +6。
        for (var step = 0.6; step <= 6.0; step += 0.6)
        {
            windows.DragTo(start.X + 10 + step, start.Y + 10, snapEnabled: false);
        }

        target.ShouldNotBeNull();
        target.Value.X.ShouldBe(6, "绝对物理目标只取整一次");
        target.Value.Y.ShouldBe(0);

        windows.CancelDrag();
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

    [Fact]
    public void 自回环标记_只看同实例同角色_同实例不同角色与跨实例都不是自环()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        CoreRuntimeTests.AddModule(runtime, "self", "role-single", typeof(RoleModelTests.SingleRoleModule));
        CoreRuntimeTests.AddModule(runtime, "two", "role-double", typeof(RoleModelTests.DoubleRoleModule));
        CoreRuntimeTests.AddModule(runtime, "peer", "role-sink-a", typeof(RoleModelTests.SinkA));

        // 同一个角色的输出口与输入口同名标签 ⇒ 自回环。
        CoreRuntimeTests.SetLabel(runtime, "self", "out", "1");
        CoreRuntimeTests.SetLabel(runtime, "self", "in", "1");

        // 同一实例的**两个不同角色**相连接（不是自环，线画在两个方块之间）。
        CoreRuntimeTests.SetLabel(runtime, "two", "out1", "2");
        CoreRuntimeTests.SetLabel(runtime, "two", "in2", "2");

        // 跨实例连接（更不是自环）。
        CoreRuntimeTests.SetLabel(runtime, "two", "out2", "3");
        CoreRuntimeTests.SetLabel(runtime, "peer", "in", "3");

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var self = windows.Modules.Single(m => m.InstanceId == "self");
        var selfLoop = self.RoleLinks.ShouldHaveSingleItem();
        selfLoop.IsSelfLoop.ShouldBeTrue("同实例 + 同角色 ⇒ 自回环");
        selfLoop.PeerInstanceId.ShouldBe("self");
        selfLoop.LocalRoleKey.ShouldBe(selfLoop.PeerRoleKey, "自环的判据是**同一个角色**");
        selfLoop.PeerAdjacent.ShouldBeTrue("自环的两端都在本方块上 ⇒ 不画未拼接断口");

        var two = windows.Modules.Single(m => m.InstanceId == "two");
        two.RoleLinks.Single(l => l.PeerInstanceId == "two" && l.PeerRoleKey == "two/second")
            .IsSelfLoop.ShouldBeFalse("同实例但不同角色 ⇒ 普通线（不是自环）");

        var cross = windows.Modules.Single(m => m.InstanceId == "peer").RoleLinks.ShouldHaveSingleItem();
        cross.IsSelfLoop.ShouldBeFalse("跨实例 ⇒ 不是自环");
    }
}
