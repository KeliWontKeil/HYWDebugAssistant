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
/// **组拖动的量化不变量**：整组共用一个整数物理位移，公共边不被窗口独立取整拉开。
/// </summary>
/// <remarks>
/// 用真实控制器 + 真实布局（125% 缩放、窗口起点落在整数像素），只断言“相对像素关系”，
/// 不钉窗口的当前宽高/位置字面量。
/// </remarks>
public class ModuleDragGeometryTests
{
    [Fact]
    public void 组拖动应保持公共边并能与另一组拼接()
    {
        using var harness = new DragHarness();
        var windows = harness.Windows;

        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");
        var c = windows.Modules.Single(module => module.InstanceId == "c");
        var d = windows.Modules.Single(module => module.InstanceId == "d");

        var startA = a.PixelRect;
        var startB = b.PixelRect;
        var startC = c.PixelRect;
        startA.Right.ShouldBe(startB.Left, "起始现场必须真的贴合（否则用例测不到缝）");
        startB.Right.ShouldBeLessThan(startC.Left, "另一个组起始时必须与 a/b 分开");

        windows.BeginDrag("a", pointerX: 100, pointerY: 50, independent: false).ShouldBeTrue();

        // 指针移动 121×7 物理像素：b 的右边缘距 c 的左边缘还差 4px；
        // 组吸附应把这 4px 吸掉，而不是只看被拖的 a。
        windows.DragTo(pointerX: 221, pointerY: 57);

        var movedA = a.PixelRect;
        var movedB = b.PixelRect;
        var movedC = c.PixelRect;

        movedA.Left.ShouldBe(startA.Left + 125);
        movedA.Top.ShouldBe(startA.Top + 7);
        movedB.Left.ShouldBe(startB.Left + 125);
        movedB.Top.ShouldBe(startB.Top + 7);
        movedA.Right.ShouldBe(movedB.Left, "整组位移必须保持公共边（不能出现 1px 缝）");
        movedB.Right.ShouldBe(movedC.Left, "组的远端边缘应吸附到另一组，形成可合并的贴合");
        movedC.Left.ShouldBe(startC.Left, "未被拖的组必须留在原处");

        windows.GroupMembersOf("a").Count.ShouldBe(4, "贴合后两组应成为同一个位置派生的拼接组");

        windows.EndDrag();
    }

    private sealed class DragHarness : IDisposable
    {
        public DragHarness()
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            var definition = new ModuleDefinition(
                ModuleId.Create("test", "drag"),
                nameof(DragModule),
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                typeof(DragModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

            Services.AddModule("a", "test/drag").Succeeded.ShouldBeTrue();
            Services.AddModule("b", "test/drag").Succeeded.ShouldBeTrue();
            Services.AddModule("c", "test/drag").Succeeded.ShouldBeTrue();
            Services.AddModule("d", "test/drag").Succeeded.ShouldBeTrue();

            // 125%：DIP 与物理像素不再一一对应，正是旧实现出现缝的现场。
            Services.UpdateModuleLayout(new ModuleLayoutView("a", 0, 0, 200, 100) { Scale = 1.25 }).Succeeded
                .ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("b", 200, 0, 200, 100) { Scale = 1.25 }).Succeeded
                .ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("c", 500, 0, 200, 100) { Scale = 1.25 }).Succeeded
                .ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("d", 700, 0, 200, 100) { Scale = 1.25 }).Succeeded
                .ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();
    }

    [ModuleDefinition("test", "drag")]
    public sealed class DragModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
