using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **已拼接模块的整组选择 + 置顶请求**（纯视图模型；不建窗口、不渲染）。
/// </summary>
public class ModuleGroupSelectionTests
{
    [Fact]
    public void 相邻两块_点击应选中整组并以被点者为锚点()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "b", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        Place(windows, "a", 0, 0);
        Place(windows, "b", 359, 0);

        var requests = new List<ModuleGroupRaiseRequest>();
        windows.GroupRaiseRequested += (_, request) => requests.Add(request);

        windows.SelectGroup("a");

        windows.SelectedInstanceIds.ShouldBe(["a", "b"]);
        windows.SelectedInstanceId.ShouldBe("a");
        var request = requests.ShouldHaveSingleItem();
        request.InstanceIds.ShouldBe(["a", "b"]);
        request.Primary.ShouldBe("a");
    }

    [Fact]
    public void 间距超过容差_只选被点者()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "b", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        Place(windows, "a", 0, 0);
        Place(windows, "b", 400, 0);

        var requests = new List<ModuleGroupRaiseRequest>();
        windows.GroupRaiseRequested += (_, request) => requests.Add(request);

        windows.SelectGroup("a");

        windows.SelectedInstanceIds.ShouldBe(["a"]);
        requests.ShouldHaveSingleItem().InstanceIds.ShouldBe(["a"]);
    }

    [Fact]
    public void 组内不可见成员应被剔除()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "b", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        Place(windows, "a", 0, 0);
        Place(windows, "b", 359, 0);
        windows.Modules.Single(module => module.InstanceId == "b").IsViewVisible = false;

        windows.SelectGroup("a");

        windows.SelectedInstanceIds.ShouldBe(["a"]);
    }

    [Fact]
    public void 点击不存在的实例_不抛也不触发抬窗()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        windows.Select("a");

        var raised = false;
        windows.GroupRaiseRequested += (_, _) => raised = true;

        Should.NotThrow(() => windows.SelectGroup("missing"));
        raised.ShouldBeFalse();
        windows.SelectedInstanceIds.ShouldBeEmpty();
    }

    /// <summary>组合页的普通选择/批量选择不得触发抬窗（只有窗口点击路径触发）。</summary>
    [Fact]
    public void 非点击路径的选择不应触发抬窗()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "a", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();

        var raised = false;
        windows.GroupRaiseRequested += (_, _) => raised = true;

        windows.Select("a");
        windows.SetSelection(["a"]);
        windows.ToggleSelection("a");

        raised.ShouldBeFalse();
    }

    private static void Place(ModuleWindowsViewModel windows, string instanceId, double x, double y)
    {
        var module = windows.Modules.Single(candidate => candidate.InstanceId == instanceId);
        module.X = x;
        module.Y = y;
        module.Width = 359;
        module.Height = 240;
    }
}
