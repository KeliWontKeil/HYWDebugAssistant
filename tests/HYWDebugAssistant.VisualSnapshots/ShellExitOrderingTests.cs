using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **关闭顺序的平台不变量**：`OwnerWindowOnly` 下外壳先决定；取消时子窗一个都不关，
/// 放行后子窗才随外壳关闭（模块视图也只能在那时摘除）。
/// </summary>
[Collection("visual-snapshots")]
public sealed class ShellExitOrderingTests
{
    /// <summary>普通 owner/child：owner 的 <c>Closing</c> 被取消时子窗必须保持可见。</summary>
    [Fact]
    public async Task 外壳取消关闭_子窗保持可见_放行后一并关闭()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var owner = new Window
                {
                    Width = 320,
                    Height = 240,
                    ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly,
                };
                var child = new Window { Width = 200, Height = 120 };
                var cancelled = true;
                owner.Closing += (_, e) => e.Cancel = cancelled;

                owner.Show();
                child.Show(owner);
                Pump();

                owner.Close();
                Pump();

                child.IsVisible.ShouldBeTrue("外壳的 Closing 被取消时子窗必须保持可见");

                cancelled = false;
                owner.Close();
                Pump();

                child.IsVisible.ShouldBeFalse("外壳放行关闭后子窗必须一并关闭");
            },
            CancellationToken.None);
    }

    /// <summary>模块窗口回归：取消退出时模块窗与视图都还在；确认退出后一起关掉并摘视图。</summary>
    [Fact]
    public async Task 取消退出_模块窗口与视图保持_确认后一并关闭()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "exit-module",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("exit-module", "test/exit-module", 400, 240);

                var shell = new Window
                {
                    Width = 800,
                    Height = 600,
                    ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly,
                };
                shell.Show();

                var manager = new ModuleWindowManager(harness.Windows, shell);
                var cancelled = true;
                shell.Closing += (_, e) => e.Cancel = cancelled;

                try
                {
                    manager.SyncAll();
                    Pump();

                    var window = manager.WindowFor("exit-module");
                    window.ShouldNotBeNull();
                    var surface = window!.GetVisualDescendants().OfType<ModuleSurfaceView>().Single();
                    window.GetVisualDescendants().OfType<ModuleViewHost>().Single().Content.ShouldNotBeNull();
                    surface.Content.ShouldNotBeNull();

                    shell.Close();
                    Pump();

                    var afterCancel = manager.WindowFor("exit-module");
                    afterCancel.ShouldNotBeNull("取消关闭后模块窗口必须还在管理器里");
                    afterCancel!.IsVisible.ShouldBeTrue("取消关闭后模块窗口必须仍然可见");
                    surface.Content.ShouldNotBeNull("取消关闭后模块视图不得被摘除");

                    cancelled = false;
                    shell.Close();
                    Pump();

                    manager.WindowFor("exit-module").ShouldBeNull("外壳关闭后模块窗口应被关闭并移出管理器");
                    surface.Content.ShouldBeNull("窗口真的关闭之后视图才从内容区摘除");
                }
                finally
                {
                    manager.Dispose();

                    if (shell.IsVisible)
                    {
                        shell.Close();
                    }

                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>直接关闭模块窗口：视图必须在 <c>Closed</c> 路径上摘除。</summary>
    [Fact]
    public async Task 直接关闭模块窗口_视图从内容区摘除()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "close-module",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("close-module", "test/close-module", 400, 240);

                var module = harness.Windows.Modules.Single(item => item.InstanceId == "close-module");
                var window = harness.OpenWindow(module);
                Pump();

                var surface = window.GetVisualDescendants().OfType<ModuleSurfaceView>().Single();
                surface.Content.ShouldNotBeNull();

                window.Close();
                Pump();

                surface.Content.ShouldBeNull("窗口真的关闭之后视图必须从内容区摘除");
            },
            CancellationToken.None);
    }

    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
