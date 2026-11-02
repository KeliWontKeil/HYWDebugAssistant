using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.Serial.Views;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpClient.Views;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.TcpServer.Views;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Modules.Udp.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
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
                harness.AddModule("manager-text-sender", "test/manager-text-sender", 400, 240);

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

                    var module = harness.Windows.Modules.Single(item => item.InstanceId == "manager-text-sender");
                    var content = (Control)ViewHost(first!).Content!;
                    module.ContentMinWidth.ShouldBe(content.MinWidth, 0.1, "内容最小宽必须来自模块视图根的 .axaml 声明");
                    module.ContentMinHeight.ShouldBe(content.MinHeight, 0.1, "内容最小高必须来自模块视图根的 .axaml 声明");
                    module.EffectiveMinHeight.ShouldBe(
                        content.MinHeight + (2 * ModuleSurfaceMetrics.FrameBorder)
                            + ModuleSurfaceMetrics.InsetTop + ModuleSurfaceMetrics.InsetBottom,
                        0.1,
                        "窗口最小高 = 内容声明 + 框架 chrome，不另抄一份字面值");

                    // 启动前已存在的窗口必须能被"补挂"（快捷键等）枚举到。
                    manager.AttachedWindows.ShouldContain(first!);
                    var attached = new List<Window>();
                    manager.AttachExistingWindows(attached.Add);
                    attached.ShouldContain(first!);

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

    [Fact]
    public async Task 外壳未显示时_SyncAll挂起_显示后就绪创建()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "startup-text-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("startup-text-sender", "test/startup-text-sender", 400, 240);

                var shell = new Window { Width = 800, Height = 600 };
                var manager = new ModuleWindowManager(harness.Windows, shell);

                try
                {
                    manager.IsShellFrameReady.ShouldBeFalse("外壳尚未显示");
                    manager.SyncAll();
                    Pump();
                    manager.WindowFor("startup-text-sender").ShouldBeNull(
                        "外框未就绪不得创建自动恢复模块的窗口");

                    shell.Show();
                    manager.RefreshShellFrame();
                    manager.SyncAll();
                    Pump();

                    manager.IsShellFrameReady.ShouldBeTrue("显示后外框成为真值");
                    manager.WindowFor("startup-text-sender").ShouldNotBeNull();
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

    [Fact]
    public async Task 新模块的初始高度来自各自视图而不是统一代码默认值()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register("serial-a", typeof(SerialModule), typeof(SerialViewFactory), 400, 0);
                harness.Register("tcp-client-a", typeof(TcpClientModule), typeof(TcpClientViewFactory), 400, 0);
                harness.Register("tcp-server-a", typeof(TcpServerModule), typeof(TcpServerViewFactory), 400, 0);
                harness.Register("udp-a", typeof(UdpModule), typeof(UdpViewFactory), 400, 0);

                harness.Services.AddModule("serial-a", "test/serial-a").Succeeded.ShouldBeTrue();
                harness.Services.AddModule("tcp-client-a", "test/tcp-client-a").Succeeded.ShouldBeTrue();
                harness.Services.AddModule("tcp-server-a", "test/tcp-server-a").Succeeded.ShouldBeTrue();
                harness.Services.AddModule("udp-a", "test/udp-a").Succeeded.ShouldBeTrue();
                harness.Windows.Refresh();

                var shell = new Window { Width = 900, Height = 700 };
                shell.Show();
                var manager = new ModuleWindowManager(harness.Windows, shell);

                try
                {
                    manager.SyncAll();
                    Pump();

                    foreach (var module in harness.Windows.Modules)
                    {
                        var window = manager.WindowFor(module.InstanceId);
                        window.ShouldNotBeNull();
                        var content = (Control)ViewHost(window!).Content!;
                        var expected = content.MinHeight + (2 * ModuleSurfaceMetrics.FrameBorder)
                            + ModuleSurfaceMetrics.InsetTop + ModuleSurfaceMetrics.InsetBottom;

                        module.Height.ShouldBe(
                            expected,
                            0.1,
                            $"{module.InstanceId} 的初始高度必须来自自己的 .axaml 内容声明，不能取统一代码默认值");
                    }
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

    [Fact]
    public async Task 关闭模块窗后_不再响应模块属性变化()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "closed-hook-text-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("closed-hook-text-sender", "test/closed-hook-text-sender", 400, 240);

                var shell = new Window { Width = 800, Height = 600 };
                shell.Show();
                var manager = new ModuleWindowManager(harness.Windows, shell);

                try
                {
                    manager.SyncAll();
                    Pump();

                    var window = manager.WindowFor("closed-hook-text-sender");
                    window.ShouldNotBeNull();
                    var lockButton = window!.GetVisualDescendants()
                        .OfType<IconButton>()
                        .Single(button => button.Name == "LockButton");
                    lockButton.Icon.ShouldBe(ShellIcons.Lock);

                    var module = harness.Windows.Modules.Single(item => item.InstanceId == "closed-hook-text-sender");
                    window.Close();
                    Pump();

                    // 关闭后若仍订阅 PropertyChanged，这一行会把图标改成 Unlock；
                    // 正确的生命周期是窗口已退订，图标保持关闭前的 Lock。
                    module.IsLocked = true;
                    Pump();
                    lockButton.Icon.ShouldBe(ShellIcons.Lock, "关闭的窗口不得继续持有模块事件");
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
