using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **窗口层与模态作用域**：外壳固定底带，模块/面板浮动带，模态固定顶带且禁用低带。
/// </summary>
/// <remarks>
/// 真实 z 序依赖平台（Windows 用 Win32，其它平台降级），无头测试只钉"层级登记 + 模态禁用/恢复"
/// 这两条可脱离平台验证的事实；观感仍由人工验收。
/// </remarks>
[Collection("visual-snapshots")]
public class AppWindowGroupTests
{
    [Fact]
    public async Task 模态作用域_禁用低带并在释放后恢复()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var shell = new Window { Width = 400, Height = 300 };
                shell.Show();

                var module = new Window();
                var panel = new Window();
                var modal = new Window();

                var group = new AppWindowGroup(shell);

                try
                {
                    group.Track(module, AppWindowLayer.Module);
                    group.Track(panel, AppWindowLayer.Panel);
                    group.Track(modal, AppWindowLayer.Modal);

                    group.LayerOf(shell).ShouldBe(AppWindowLayer.Shell);
                    group.LayerOf(module).ShouldBe(AppWindowLayer.Module);
                    group.LayerOf(panel).ShouldBe(AppWindowLayer.Panel);
                    group.LayerOf(modal).ShouldBe(AppWindowLayer.Modal);
                    AppWindowGroup.Of(module).ShouldBeSameAs(group);

                    using (group.BeginModal(modal))
                    {
                        shell.IsEnabled.ShouldBeFalse("模态打开时外壳不可操作");
                        module.IsEnabled.ShouldBeFalse("模态打开时模块窗口不可操作");
                        panel.IsEnabled.ShouldBeFalse("模态打开时功能面板不可操作");
                        modal.IsEnabled.ShouldBeTrue("模态自己必须可用");
                    }

                    shell.IsEnabled.ShouldBeTrue("关闭模态后外壳恢复");
                    module.IsEnabled.ShouldBeTrue("关闭模态后模块恢复");
                    panel.IsEnabled.ShouldBeTrue("关闭模态后功能面板恢复");
                }
                finally
                {
                    group.Dispose();
                    shell.Close();
                }

                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 模态始终置顶_浮动组抬升也不能覆盖它()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var shell = new Window { Width = 400, Height = 300 };
                shell.Show();
                var module = new Window();
                var modal = new Window();
                var group = new AppWindowGroup(shell);

                try
                {
                    group.Track(module);
                    group.Track(modal, AppWindowLayer.Modal);

                    using (group.BeginModal(modal))
                    {
                        modal.Topmost.ShouldBeTrue("模态进入时必须是本应用最高层");

                        group.RaiseFloatingGroup([module]);
                        modal.Topmost.ShouldBeTrue("模块组抬升不能把模态的 Topmost 抢走");
                        modal.IsEnabled.ShouldBeTrue();
                    }

                    modal.Topmost.ShouldBeFalse("释放模态后恢复进入前的 Topmost");
                }
                finally
                {
                    group.Dispose();
                    shell.Close();
                }

                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 抬升整组_只动浮动带且保持组内顺序()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var shell = new Window { Width = 400, Height = 300 };
                shell.Show();

                var a = new Window();
                var b = new Window();
                var c = new Window();
                var group = new AppWindowGroup(shell);

                try
                {
                    group.Track(a);
                    group.Track(b);
                    group.Track(c);

                    group.RaiseFloatingGroup([a, b]);
                    group.FloatingOrder.ShouldBe([c, a, b], "整组连续放末尾，组内保持传入顺序");

                    group.RaiseFloatingGroup([b, c]);
                    group.FloatingOrder.ShouldBe([a, b, c], "b/c 已在末尾，顺序不变；a 仍在下面");

                    group.RaiseFloatingGroup([shell, b]);
                    group.FloatingOrder.ShouldBe([a, c, b], "外壳不得进入浮动带；b 被抬到最上");
                }
                finally
                {
                    group.Dispose();
                    shell.Close();
                }

                Pump();
            },
            CancellationToken.None);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
