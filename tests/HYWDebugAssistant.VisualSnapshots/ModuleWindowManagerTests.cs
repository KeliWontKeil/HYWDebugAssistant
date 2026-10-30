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
/// **模块窗口管理器**在窗口被直接关闭后的行为：重新同步必须由新窗口接管，且内容不能是空壳。
/// </summary>
[Collection("visual-snapshots")]
public class ModuleWindowManagerTests
{
    /// <summary>
    /// 关闭一扇模块窗口后再次同步：管理器必须忘掉这扇窗，由新窗口接管并重新挂上模块视图。
    /// </summary>
    [Fact]
    public async Task 窗口关闭后重新同步_由新窗口接管且内容非空()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "manager-text-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("manager-text-sender", "test/manager-text-sender/1.0.0", 400, 240);

                var shell = new Window { Width = 800, Height = 600 };
                shell.Show();

                var manager = new ModuleWindowManager(harness.Windows, shell);

                try
                {
                    manager.SyncAll();
                    Pump();

                    var first = manager.WindowFor("manager-text-sender");
                    first.ShouldNotBeNull();
                    ViewHost(first!).Content.ShouldNotBeNull();

                    first!.Close();
                    Pump();

                    manager.SyncAll();
                    Pump();

                    var second = manager.WindowFor("manager-text-sender");
                    second.ShouldNotBeNull();
                    second!.ShouldNotBeSameAs(first);
                    second.IsVisible.ShouldBeTrue();
                    ViewHost(second).Content.ShouldNotBeNull();
                }
                finally
                {
                    manager.Dispose();
                    shell.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>窗口内容区里的模块视图宿主（每个窗口只有一个）。</summary>
    private static ModuleViewHost ViewHost(Window window) =>
        window.GetVisualDescendants().OfType<ModuleViewHost>().Single();

    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
