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
/// **层隔离**：贴合只是几何谓词；不同层的模块即使贴边也不成组、不参与拖动/缩放候选。
/// </summary>
public class LayerIsolationTests
{
    [Fact]
    public void 不同层的贴合模块不应成组也不应参与拖动吸附()
    {
        using var harness = new IsolationHarness();
        var windows = harness.Windows;

        var a = windows.Modules.Single(module => module.InstanceId == "a");
        var b = windows.Modules.Single(module => module.InstanceId == "b");

        SnapEngine.AreJoined(a.PhysicalRect, b.PhysicalRect).ShouldBeTrue("先用几何证明两者确实贴边");
        windows.GroupMembersOf("a").ShouldNotContain("b");
        windows.GroupMembersOf("b").ShouldNotContain("a");

        // 把 a 拖到与 b 正好贴合：拖动候选只取 a 的层，b 不参与，位置来自指针而不是吸附。
        windows.BeginDrag("a", pointerX: 50, pointerY: 50, independent: false).ShouldBeTrue();
        windows.DragTo(pointerX: 54, pointerY: 50);

        a.PixelRect.Right.ShouldBe(104);
        b.PixelRect.Left.ShouldBe(104);
        windows.GroupMembersOf("a").ShouldNotContain("b", "层不同，贴边也不能在落手后合并");

        windows.EndDrag();
    }

    [Fact]
    public void 不同层模块不应提供缩放边或尺寸候选()
    {
        using var harness = new IsolationHarness();
        var windows = harness.Windows;

        var a = windows.Modules.Single(module => module.InstanceId == "a");

        windows.BeginResize("a", ResizeHandle.Right, pointerX: 100, pointerY: 50).ShouldBeTrue();
        windows.ResizeTo(pointerX: 101, pointerY: 50);

        // b 的宽是 100；若跨层尺寸候选参与，a 会被吸回 100。a 当前层只有自己，故保持指针结果 101。
        a.Width.ShouldBe(101);
        a.PixelRect.Right.ShouldBe(101);

        windows.EndResize();
    }

    private sealed class IsolationHarness : IDisposable
    {
        public IsolationHarness()
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            var definition = new ModuleDefinition(
                ModuleId.Create("test", "layer-isolation"),
                nameof(IsolationModule),
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                typeof(IsolationModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

            Services.AddModule("a", "test/layer-isolation").Succeeded.ShouldBeTrue();
            Services.AddModule("b", "test/layer-isolation").Succeeded.ShouldBeTrue();
            Services.AddModule("c", "test/layer-isolation").Succeeded.ShouldBeTrue();

            // c 在 a 下方且与 a 重合 ⇒ a 在 1 层；b 与 a 贴边（4px）但不与 c 重合 ⇒ b 在 0 层。
            Services.UpdateModuleLayout(new ModuleLayoutView("c", 0, 0, 100, 100)).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("a", 0, 0, 100, 100)).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("b", 104, 0, 100, 100)).Succeeded.ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();
            Windows.ZOrderProvider = id => id switch
            {
                "c" => 0,
                "a" => 1,
                "b" => 2,
                _ => int.MinValue,
            };
            Windows.RefreshLayers();
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();
    }

    [ModuleDefinition("test", "layer-isolation")]
    public sealed class IsolationModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
