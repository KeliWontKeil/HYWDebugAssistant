using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **主窗口是跨层锚点**：任意层的模块都能贴外壳；选中/组遍历按模块层，外壳整体移动跨层。
/// </summary>
public class ModuleLayerIntegrationTests
{
    [Fact]
    public void 外壳贴壳组应按层选择且整体移动跨层()
    {
        using var harness = new LayerHarness();
        var windows = harness.Windows;

        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");
        var c = windows.Modules.Single(module => module.InstanceId == "c");

        windows.RefreshLayers();

        // c 在 b 下方，b 压在 c 上 ⇒ b 升到 1 层；a/c 并排不重合 ⇒ 0 层。
        a.Layer.ShouldBe(0);
        c.Layer.ShouldBe(0);
        b.Layer.ShouldBe(1);

        // 从 a 出发的组只按 0 层走：外壳 + a + c，不串到 1 层的 b。
        var fromA = windows.GroupMembersOf("a");
        fromA.ShouldContain("a");
        fromA.ShouldContain("c");
        fromA.ShouldNotContain("b");

        // 从 b 出发只走 1 层：外壳 + b，不带走 0 层的 a/c。
        var fromB = windows.GroupMembersOf("b");
        fromB.ShouldContain("b");
        fromB.ShouldNotContain("a");
        fromB.ShouldNotContain("c");

        // 改变叠放顺序：b 移到 c 下方后，重合关系的上下层互换。
        windows.ZOrderProvider = id => id switch
        {
            "a" => 0,
            "b" => 1,
            "c" => 2,
            _ => int.MinValue,
        };
        windows.RefreshLayers();

        b.Layer.ShouldBe(0, "b 现在在 c 下方");
        c.Layer.ShouldBe(1, "c 现在压在 b 上");

        // 外壳移动是跨层的：所有贴壳模块（a/b/c）都跟着走。
        var beforeA = a.PixelRect.Left;
        var beforeB = b.PixelRect.Left;
        var beforeC = c.PixelRect.Left;
        var shell = windows.ShellPhysicalRect.ShouldNotBeNull();

        windows.TranslateShellGroup(10, 0, shell);

        a.PixelRect.Left.ShouldBe(beforeA + 10);
        b.PixelRect.Left.ShouldBe(beforeB + 10);
        c.PixelRect.Left.ShouldBe(beforeC + 10);
    }

    [Fact]
    public void 拖贴壳组不应带走其他层的贴壳组()
    {
        using var harness = new LayerHarness();
        var windows = harness.Windows;

        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");
        var c = windows.Modules.Single(module => module.InstanceId == "c");

        var startA = a.PixelRect.Left;
        var startB = b.PixelRect.Left;
        var startC = c.PixelRect.Left;

        windows.BeginDrag("a", pointerX: 50, pointerY: 50, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 60, pointerY: 50);

        a.PixelRect.Left.ShouldBe(startA + 10);
        c.PixelRect.Left.ShouldBe(startC + 10, "同层贴壳组随外壳移动");
        b.PixelRect.Left.ShouldBe(startB, "其他层的贴壳组不得被拖走");

        windows.EndDrag();
    }

    private sealed class LayerHarness : IDisposable
    {
        public LayerHarness()
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            var definition = new ModuleDefinition(
                ModuleId.Create("test", "layer"),
                nameof(LayerModule),
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                typeof(LayerModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

            Services.AddModule("a", "test/layer").Succeeded.ShouldBeTrue();
            Services.AddModule("b", "test/layer").Succeeded.ShouldBeTrue();
            Services.AddModule("c", "test/layer").Succeeded.ShouldBeTrue();

            Services.UpdateModuleLayout(new ModuleLayoutView("a", 0, 0, 100, 100)).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("b", 0, 200, 100, 100)).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("c", 0, 250, 100, 100)).Succeeded.ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();

            // 外壳右边缘 = 0；三个模块都贴壳；a 与 c 不重合，b 压住 c。
            var shell = new CanvasRect(-100, 0, 100, 400);
            Windows.SetShellFrame(shell, shell);
            Windows.SetShellWindowPosition(new Avalonia.PixelPoint(-100, 0));
            Windows.ZOrderProvider = id => id switch
            {
                "a" => 0,
                "c" => 1,
                "b" => 2,
                _ => int.MinValue,
            };
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();
    }

    [ModuleDefinition("test", "layer")]
    public sealed class LayerModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
