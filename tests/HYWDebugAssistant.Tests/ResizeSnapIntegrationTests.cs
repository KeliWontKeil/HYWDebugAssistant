using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **缩放边吸附的物理像素口径**：125%/跨屏缩放时，吸附边与目标边必须落在同一个整数物理像素上；
/// Alt 关闭吸附与抓取偏移也在这里钉住。
/// </summary>
public class ResizeSnapIntegrationTests
{
    [Fact]
    public void 百分之百二十五缩放_右边应精确贴到另一窗口左边()
    {
        using var harness = new ResizeHarness(
            a: new ModuleLayoutView("a", 0, 0, 200, 100) { Scale = 1.25 },
            b: new ModuleLayoutView("b", 300, 0, 200, 100) { Scale = 1.25 });

        var windows = harness.Windows;
        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");

        windows.BeginResize("a", ResizeHandle.Right, pointerX: 250, pointerY: 50).ShouldBeTrue();
        windows.ResizeTo(pointerX: 372, pointerY: 50);

        a.PixelRect.Right.ShouldBe(b.PixelRect.Left);
        a.PixelRect.Right.ShouldBe(375);

        windows.EndResize();
    }

    [Fact]
    public void 跨缩放窗口_按物理像素贴合()
    {
        // a 在 125% 屏、b 在 100% 屏：DIP 宽度不等于物理宽度，但吸附必须对齐物理边。
        using var harness = new ResizeHarness(
            a: new ModuleLayoutView("a", 0, 0, 200, 100) { Scale = 1.25 },
            b: new ModuleLayoutView("b", 400, 0, 100, 100) { Scale = 1 });

        var windows = harness.Windows;
        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");

        windows.BeginResize("a", ResizeHandle.Right, pointerX: 250, pointerY: 50).ShouldBeTrue();
        windows.ResizeTo(pointerX: 398, pointerY: 50);

        a.PixelRect.Right.ShouldBe(400);
        a.PixelRect.Right.ShouldBe(b.PixelRect.Left);

        windows.EndResize();
    }

    [Fact]
    public void 关闭吸附时按指针移动且不产生辅助线()
    {
        using var harness = new ResizeHarness(
            a: new ModuleLayoutView("a", 0, 0, 200, 100),
            b: new ModuleLayoutView("b", 300, 0, 200, 100));

        var windows = harness.Windows;
        var a = windows.Modules.Single(module => module.InstanceId == "a");

        windows.BeginResize("a", ResizeHandle.Right, pointerX: 200, pointerY: 50).ShouldBeTrue();
        windows.ResizeTo(pointerX: 298, pointerY: 50, snapEnabled: false);

        a.Width.ShouldBe(298);
        a.Guides.ShouldBeEmpty();

        windows.EndResize();
    }

    [Fact]
    public void 抓取点偏移不应让边第一次移动就跳到指针()
    {
        using var harness = new ResizeHarness(
            a: new ModuleLayoutView("a", 0, 0, 200, 100),
            b: new ModuleLayoutView("b", 300, 0, 200, 100));

        var windows = harness.Windows;
        var a = windows.Modules.Single(module => module.InstanceId == "a");

        // 抓在右边内侧 2px：移动 10px 时右边应移动 10px，而不是直接跳到指针 208。
        windows.BeginResize("a", ResizeHandle.Right, pointerX: 198, pointerY: 50).ShouldBeTrue();
        windows.ResizeTo(pointerX: 208, pointerY: 50);

        a.Width.ShouldBe(210);

        windows.EndResize();
    }

    private sealed class ResizeHarness : IDisposable
    {
        public ResizeHarness(ModuleLayoutView a, ModuleLayoutView b)
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            var definition = new ModuleDefinition(
                ModuleId.Create("test", "resize-snap"),
                nameof(ResizeModule),
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                typeof(ResizeModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
            Services.AddModule("a", "test/resize-snap").Succeeded.ShouldBeTrue();
            Services.AddModule("b", "test/resize-snap").Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(a).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(b).Succeeded.ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();
    }

    [ModuleDefinition("test", "resize-snap")]
    public sealed class ResizeModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
