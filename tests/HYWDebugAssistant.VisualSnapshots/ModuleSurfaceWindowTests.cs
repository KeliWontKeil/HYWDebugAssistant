using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **真实模块窗口**的内容预留带：模块视图必须落在端口标记以内，不能压住窗口边缘。
/// </summary>
[Collection("visual-snapshots")]
public class ModuleSurfaceWindowTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>构造测试。</summary>
    public ModuleSurfaceWindowTests(ITestOutputHelper output) => _output = output;

    /// <summary>文本发送器真实窗口：内容原点 ≥ (20,22)，底边 ≤ 窗高−24。</summary>
    [Fact]
    public async Task 文本发送器真实窗口_内容不压端口标记()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "real-text-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);

                harness.AddModule("real-text-sender", "test/real-text-sender", 400, 240);

                var module = harness.Windows.Modules.Single(item => item.InstanceId == "real-text-sender");
                var window = harness.OpenWindow(module);
                window.Width = 400;
                window.Height = 240;
                Pump();

                var host = window.GetVisualDescendants().OfType<ModuleViewHost>().Single();
                var view = (Control)host.Content!;
                view.Bounds.Width.ShouldBe(
                    Math.Max(host.Bounds.Width, view.MinWidth),
                    0.5,
                    "模块视图至少填满宿主；更大的 MinWidth 是模块自己的布局选择");

                var origin = host.TranslatePoint(new Point(0, 0), window);
                origin.ShouldNotBeNull();
                origin!.Value.X.ShouldBeGreaterThanOrEqualTo(ModuleSurfaceMetrics.InsetLeft, "左侧要留出端口标记带");
                origin.Value.Y.ShouldBeGreaterThanOrEqualTo(ModuleSurfaceMetrics.InsetTop, "上方要留出端口标记带");

                var bottom = host.TranslatePoint(new Point(host.Bounds.Width, host.Bounds.Height), window);
                bottom.ShouldNotBeNull();
                bottom!.Value.Y.ShouldBeLessThanOrEqualTo(
                    window.Height - ModuleSurfaceMetrics.InsetBottom,
                    "底边要给运行指示/端口标记留位");

                var marker = window.GetVisualDescendants()
                    .OfType<Border>()
                    .First(item => item.Classes.Contains("portMarker"));
                var markerTop = marker.TranslatePoint(new Point(0, 0), window);
                markerTop.ShouldNotBeNull();
                _output.WriteLine($"host bottom={bottom.Value.Y}, marker top={markerTop!.Value.Y}, marker height={marker.Bounds.Height}");
                markerTop.Value.Y.ShouldBeGreaterThanOrEqualTo(bottom.Value.Y, "端口标记不能压进内容区");

                var path = Path.Combine(Path.GetTempPath(), "hyw-real-window-textsender.png");
                var bitmap = new RenderTargetBitmap(new PixelSize(400, 240), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
                _output.WriteLine(path);

                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **真实显示缓冲 → 真实窗口 → 增量显示控件**：追加的日志必须经 revision/快照路径出现在窗口里。
    /// </summary>
    [Fact]
    public async Task 数据监视器真实窗口_追加日志应进入增量显示控件()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "data-monitor",
                    typeof(DataMonitorModule),
                    typeof(DataMonitorViewFactory),
                    windowWidth: 400,
                    windowHeight: 352);

                harness.AddModule("dm", "test/data-monitor", 400, 352);

                var module = harness.Windows.Modules.Single(item => item.InstanceId == "dm");
                var window = harness.OpenWindow(module);
                Pump();

                var firstLine = "第一条";
                var secondLine = "第二条";
                harness.AppendDisplayLine("dm", DataMonitorModule.LogSource, firstLine);
                harness.Windows.Refresh();
                Pump();

                var view = window.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                view.Text.ShouldContain(firstLine);
                view.VisualLineCount.ShouldBeGreaterThan(0);

                harness.AppendDisplayLine("dm", DataMonitorModule.LogSource, secondLine);
                harness.Windows.Refresh();
                Pump();

                view.Text.ShouldContain(firstLine);
                view.Text.ShouldContain(secondLine);

                var path = Path.Combine(Path.GetTempPath(), "hyw-data-monitor-incremental.png");
                using var bitmap = new RenderTargetBitmap(new PixelSize(400, 352), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
                _output.WriteLine(path);

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 隐藏后再「显示全部」：缓存的模块视图必须先脱离旧窗口，不能成为第二个父级的子控件。
    /// </summary>
    [Fact]
    public async Task 隐藏后重新显示_同一个模块视图不得重复挂载()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "hide-show-text-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);

                harness.AddModule("hide-show", "test/hide-show-text-sender", 400, 240);
                var module = harness.Windows.Modules.Single(item => item.InstanceId == "hide-show");

                var first = harness.OpenWindow(module);
                Pump();
                first.GetVisualDescendants().OfType<ModuleViewHost>().ShouldHaveSingleItem();

                // 关闭路径由窗口自身的 Closed 事件摘掉缓存视图（管理器只负责关窗）。
                first.Close();
                Pump();

                var second = harness.OpenWindow(module);
                Pump();
                second.GetVisualDescendants().OfType<ModuleViewHost>().ShouldHaveSingleItem()
                    .Content.ShouldNotBeNull();

                second.Close();
                Pump();
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
