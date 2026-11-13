using Avalonia;
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
/// **贴壳整组拖动**：外壳与模块共用“起点 + 绝对整数位移”，可见外框不能被窗口位置冒充。
/// </summary>
/// <remarks>
/// 外壳的 <c>Window.Position</c> 与 DWM 可见外框相差系统装饰边框。本用例刻意让两者错开 8px：
/// 若实现把窗口位置写进 <c>ShellPhysicalRect</c>，外壳右边缘与模块左边缘会立刻脱开；
/// 若实现把绝对位移当增量累加，第二帧外壳会多走一次第一帧的位移。
/// </remarks>
public class ShellGroupDragTests
{
    [Fact]
    public void 贴壳组拖动_外壳按绝对位移移动且可见外框不漂移()
    {
        using var harness = new ShellDragHarness();
        var windows = harness.Windows;
        var module = windows.Modules.Single(item => item.InstanceId == "a");

        var startModuleLeft = module.PixelRect.Left;
        var startVisible = windows.ShellPhysicalRect.ShouldNotBeNull();

        var targets = new List<PixelPoint>();
        windows.ShellMoveRequested += (_, target) => targets.Add(target);

        windows.BeginDrag("a", pointerX: 100, pointerY: 100, independent: false).ShouldBeTrue();

        // 只拖 2px：外壳在组内时，旧的外壳参照线不能再把模块拉回起点。
        windows.DragTo(pointerX: 102, pointerY: 100);
        module.PixelRect.Left.ShouldBe(startModuleLeft + 2);
        targets.Count.ShouldBe(1);
        targets[^1].ShouldBe(new PixelPoint(102, 100));

        // 再拖到 +26/+14：绝对位移；旧实现会累加成 +28，外壳多走 2px。
        windows.DragTo(pointerX: 126, pointerY: 114);
        module.PixelRect.Left.ShouldBe(startModuleLeft + 26);
        targets.Count.ShouldBe(2);
        targets[^1].ShouldBe(new PixelPoint(126, 114));

        var movedVisible = windows.ShellPhysicalRect.ShouldNotBeNull();
        movedVisible.Left.ShouldBe(startVisible.Left + 26, "可见外框应随组平移，而不是被窗口位置替换");
        movedVisible.Right.ShouldBe(module.PixelRect.Left, "贴壳组移动后，外壳可见右边缘仍应贴住模块左边缘");
        movedVisible.Left.ShouldNotBe(targets[^1].X, "窗口位置（外层左上角）不能冒充可见外框原点");

        windows.EndDrag();
    }

    private sealed class ShellDragHarness : IDisposable
    {
        public ShellDragHarness()
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            var definition = new ModuleDefinition(
                ModuleId.Create("test", "shell-drag"),
                nameof(ShellDragModule),
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                typeof(ShellDragModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
            Services.AddModule("a", "test/shell-drag").Succeeded.ShouldBeTrue();

            // 外壳可见右边缘 = 108 + 500 = 608；模块左边缘正好贴住它。
            Services.UpdateModuleLayout(new ModuleLayoutView("a", 608, 108, 200, 300)).Succeeded.ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();

            // 外层窗口位置 (100,100) 与可见外框 (108,108) 刻意相差 8px（系统装饰）。
            var visible = new CanvasRect(108, 108, 500, 400);
            Windows.SetShellFrame(visible, visible);
            Windows.SetShellWindowPosition(new PixelPoint(100, 100));
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();
    }

    [ModuleDefinition("test", "shell-drag")]
    public sealed class ShellDragModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
