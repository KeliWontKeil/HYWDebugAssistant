using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 加载组合后的窗口位置：新窗口必须直接消费已保存布局，不能被初摆位置覆盖。
/// </summary>
public class ModuleWindowLayoutLoadTests
{
    [Fact]
    public void 自动摆放_应贴现有模块下沿_而不是按整块声明高度跳()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "existing", "sender", typeof(CoreRuntimeTests.SenderModule));
        services.UpdateModuleLayout(new ModuleLayoutView("existing", 0, 0, 300, 200)).Succeeded.ShouldBeTrue();

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        CoreRuntimeTests.AddModule(runtime, "new", "sender", typeof(CoreRuntimeTests.SenderModule));
        windows.Refresh();

        var fresh = windows.Modules.Single(module => module.InstanceId == "new");
        fresh.Y.ShouldBe(212, "现有模块下沿 200 + 间距 12；旧实现会再按候选高度整块跳过");
    }

    [Fact]
    public void 自动摆放_工作区连起始行都放不下时不得横向跑出巨大间隔()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var windows = new ModuleWindowsViewModel(services, services);

        // 外壳贴在工作区下沿，模块高度放不进这一列：旧实现会每轮循环都 x += 宽 + 间距。
        windows.SetShellFrame(new CanvasRect(100, 1040, 300, 40), new CanvasRect(100, 1040, 300, 40));
        windows.SetWorkAreas([new CanvasRect(0, 0, 1920, 1040)]);
        windows.WorkArea = new CanvasRect(0, 0, 1920, 1040);

        CoreRuntimeTests.AddModule(runtime, "edge", "sender", typeof(CoreRuntimeTests.SenderModule));
        windows.Refresh();

        var fresh = windows.Modules.Single(module => module.InstanceId == "edge");
        var shellRight = windows.ShellRect!.Value.Right;
        fresh.X.ShouldBeGreaterThan(shellRight, "新模块仍从外壳右侧开始");
        fresh.X.ShouldBeLessThan(shellRight + 200, "放不下一列时不得反复横向跳列");
    }

    [Fact]
    public void 自动摆放_换列应按实际宽度而不是声明宽度步进()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var definition = new ModuleDefinition(
            ModuleId.Create("test", "placement"),
            "placement",
            "placement",
            [],
            ModuleCapability.None,
            [], true,
            typeof(CoreRuntimeTests.SenderModule),
            string.Empty,
            string.Empty,
            viewFactory: null,
            windowWidth: 400,
            windowHeight: 228);
        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

        services.AddModule("old", "test/placement").Succeeded.ShouldBeTrue();
        // 实际宽度 = 内容 360 + 左右 chrome 42；声明宽度是 400。
        services.UpdateModuleLayout(new ModuleLayoutView("old", 408, 0, 402, 228)).Succeeded.ShouldBeTrue();

        var windows = new ModuleWindowsViewModel(services, services);
        windows.SetShellFrame(new CanvasRect(0, 0, 396, 858), new CanvasRect(0, 0, 396, 858));
        windows.SetWorkAreas([new CanvasRect(0, 0, 1920, 300)]);
        windows.WorkArea = new CanvasRect(0, 0, 1920, 300);
        windows.Refresh();

        var oldModule = windows.Modules.Single(module => module.InstanceId == "old");
        services.AddModule("new", "test/placement").Succeeded.ShouldBeTrue();
        windows.Refresh();

        var fresh = windows.Modules.Single(module => module.InstanceId == "new");
        fresh.X.ShouldBeGreaterThan(oldModule.Rect.Right, "新列必须在旧列实际右边界右侧");
        fresh.X.ShouldBeLessThan(
            oldModule.Rect.Right + 400,
            "换列只能跨一列；按声明宽度步进会因约 10 DIP 的缝被判重叠，多跳一列");
    }

    [Fact]
    public void 组合整组_与现有模块相交时应整体平移_保持组内相对位置()
    {
        using var firstRuntime = new ModuleRuntime();
        var firstServices = new RuntimeServices(firstRuntime);
        CoreRuntimeTests.AddModule(firstRuntime, "g1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(firstRuntime, "g2", "sender", typeof(CoreRuntimeTests.SenderModule));
        firstServices.UpdateModuleLayout(new ModuleLayoutView("g1", 0, 0, 300, 200)).Succeeded.ShouldBeTrue();
        firstServices.UpdateModuleLayout(new ModuleLayoutView("g2", 0, 200, 300, 200)).Succeeded.ShouldBeTrue();

        var path = Path.Combine(Path.GetTempPath(), "hyw-group-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            firstServices.SaveComposite(path, "group", ["g1", "g2"]).Succeeded.ShouldBeTrue();

            using var secondRuntime = new ModuleRuntime();
            var secondServices = new RuntimeServices(secondRuntime);
            CoreRuntimeTests.AddModule(secondRuntime, "existing", "sender", typeof(CoreRuntimeTests.SenderModule));
            CoreRuntimeTests.AddModule(secondRuntime, "g1", "sender", typeof(CoreRuntimeTests.SenderModule));
            CoreRuntimeTests.AddModule(secondRuntime, "g2", "sender", typeof(CoreRuntimeTests.SenderModule));
            secondServices.RemoveModule("g1").Succeeded.ShouldBeTrue();
            secondServices.RemoveModule("g2").Succeeded.ShouldBeTrue();
            secondServices.UpdateModuleLayout(new ModuleLayoutView("existing", 0, 0, 300, 200)).Succeeded.ShouldBeTrue();

            // 先让现有模块进入 UI，再加载组合（真实界面顺序）；否则首次同步会把两者都当新实例。
            var windows = new ModuleWindowsViewModel(secondServices, secondServices);
            windows.Refresh();

            secondServices.LoadComposite(path).Succeeded.ShouldBeTrue();
            windows.Refresh();

            var g1 = windows.Modules.Single(module => module.InstanceId == "g1");
            var g2 = windows.Modules.Single(module => module.InstanceId == "g2");
            (g2.Y - g1.Y).ShouldBe(200, "整组平移必须保持组内相对位置");
            g1.Y.ShouldBeGreaterThanOrEqualTo(212, "整组应避开现有模块下沿");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
    [Fact]
    public void 加载组合_窗口位置应等于文件中的布局()
    {
        using var firstRuntime = new ModuleRuntime();
        var firstServices = new RuntimeServices(firstRuntime);
        CoreRuntimeTests.AddModule(firstRuntime, "g1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(firstRuntime, "g2", "sender", typeof(CoreRuntimeTests.SenderModule));

        firstServices.UpdateModuleLayout(new ModuleLayoutView("g1", 40, 60, 300, 200)).Succeeded.ShouldBeTrue();
        firstServices.UpdateModuleLayout(new ModuleLayoutView("g2", 400, 80, 300, 200)).Succeeded.ShouldBeTrue();
        firstServices.GetLayout().Single(layout => layout.InstanceId == "g1").X.ShouldBe(40);

        var path = Path.Combine(Path.GetTempPath(), "hyw-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            firstServices.SaveComposite(path, "layout", ["g1", "g2"]).Succeeded.ShouldBeTrue();

            using var secondRuntime = new ModuleRuntime();
            var secondServices = new RuntimeServices(secondRuntime);
            CoreRuntimeTests.AddModule(secondRuntime, "g1", "sender", typeof(CoreRuntimeTests.SenderModule));
            CoreRuntimeTests.AddModule(secondRuntime, "g2", "sender", typeof(CoreRuntimeTests.SenderModule));
            secondServices.RemoveModule("g1").Succeeded.ShouldBeTrue();
            secondServices.RemoveModule("g2").Succeeded.ShouldBeTrue();
            secondServices.LoadComposite(path).Succeeded.ShouldBeTrue();
            secondServices.GetLayout().Single(layout => layout.InstanceId == "g1").X.ShouldBe(40);

            var windows = new ModuleWindowsViewModel(secondServices, secondServices);
            windows.Refresh();

            var g1 = windows.Modules.Single(module => module.InstanceId == "g1");
            var g2 = windows.Modules.Single(module => module.InstanceId == "g2");
            g1.X.ShouldBe(40);
            g1.Y.ShouldBe(60);
            g2.X.ShouldBe(400);
            g2.Y.ShouldBe(80);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
