using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **显示文本的增量路径**：每帧只应用"新增的已封口行 + 当前行后缀 + 头部裁剪"。
/// </summary>
/// <remarks>
/// 走的是真实缓冲（<see cref="DisplayBuffer"/>）+ 真实控件，而不是快照替身：
/// 这条路径是"高频长时间显示不把界面拖死"的关键，替身测不到它。
/// </remarks>
[Collection("visual-snapshots")]
public class TextSurfaceDeltaTests
{
    [Fact]
    public async Task 长缓冲_只为可见窗口持有布局()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new DeltaContext();
                var surface = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display, Width = 400 };
                var host = new ModuleViewHost(context, surface);
                var window = new Window
                {
                    Width = 520,
                    Height = 320,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = surface.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                var packet = new string('A', 1000);

                for (var i = 0; i < 200; i++)
                {
                    context.Buffer.AppendText(packet);
                }

                host.Refresh();
                Pump();

                view.VisualLineCount.ShouldBeGreaterThan(1000, "20 万字符在 400px 宽下应折成上千个视觉行");
                view.TextLength.ShouldBe(context.Buffer.TotalChars);

                (view.LiveLayoutCount).ShouldBeLessThan(
                    400,
                    "布局应当只为可见窗口附近的行创建；与总行数成正比就说明又退回了“每行缓存一份 TextLayout”");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 增量路径_长单行追加应只重排尾部且内容与缓冲一致()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new DeltaContext();
                var surface = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display, Width = 320 };
                var host = new ModuleViewHost(context, surface);
                var window = new Window
                {
                    Width = 420,
                    Height = 220,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = surface.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();

                // ① 同一条逻辑行上分很多帧追加：只应重排尾部视觉行，不能重排整条。
                context.Buffer.AppendText(new string('a', 300));
                host.Refresh();
                Pump();

                for (var frame = 0; frame < 60; frame++)
                {
                    var before = view.LayoutBuildCount;
                    context.Buffer.AppendText(new string('b', 120));
                    host.Refresh();
                    Pump();

                    (view.LayoutBuildCount - before).ShouldBeLessThanOrEqualTo(
                        8,
                        "追加只应重排尾部；与已显示的总长度成正比就说明退回了全文重建");
                }

                view.Text.ShouldBe(new string('a', 300) + new string('b', 120 * 60));

                // ② 封口当前行 + 开一条新行：两条逻辑行按通道换行分开。
                context.Buffer.AppendLine(string.Empty);
                context.Buffer.AppendText("second");
                host.Refresh();
                Pump();

                view.Text.ShouldBe(
                    new string('a', 300) + new string('b', 120 * 60) + Environment.NewLine + "second");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 增量路径_容量到顶应同步删头且只剩最新内容()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new DeltaContext(capacity: 5);
                var surface = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display };
                var host = new ModuleViewHost(context, surface);
                var window = new Window
                {
                    Width = 320,
                    Height = 200,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = surface.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();

                for (var index = 0; index < 20; index++)
                {
                    context.Buffer.AppendLine($"line-{index}");
                    host.Refresh();
                    Pump();
                }

                context.Buffer.DroppedCount.ShouldBe(15);
                view.Text.ShouldBe(string.Join(Environment.NewLine, Enumerable.Range(15, 5).Select(i => $"line-{i}")));

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public void 帧时钟间隔应跟随刷新率设置()
    {
        try
        {
            UiFrameClock.SetRate(30);
            UiFrameClock.Interval.TotalMilliseconds.ShouldBe(1000.0 / 30, 0.5);

            UiFrameClock.SetRate(120);
            UiFrameClock.Interval.TotalMilliseconds.ShouldBe(1000.0 / 120, 0.5);

            UiFrameClock.SetRate(0);
            UiFrameClock.Interval.ShouldBe(TimeSpan.FromSeconds(1), "下限夹到 1 Hz");

            UiFrameClock.SetRate(10_000);
            UiFrameClock.Interval.TotalMilliseconds.ShouldBe(1000.0 / 240, 0.5, "上限夹到 240 Hz");
        }
        finally
        {
            UiFrameClock.SetRate(60);
        }
    }

    [Fact]
    public async Task 关闭满缓冲窗口_不得触发整表重排()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new DeltaContext(capacity: 20_000);
                var surface = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display, Width = 400 };
                var host = new ModuleViewHost(context, surface);
                var window = new Window
                {
                    Width = 520,
                    Height = 320,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                for (var i = 0; i < 20_000; i++)
                {
                    context.Buffer.AppendLine(new string('x', 80));
                }

                host.Refresh();
                Pump();

                var view = surface.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                var builds = view.LayoutBuildCount;
                var visuals = view.VisualLineCount;

                // 离开可视树时样式会退回默认；那不能变成"按新字体把满缓冲重排一遍"。
                window.Content = null;
                Pump();

                (view.LayoutBuildCount - builds).ShouldBe(
                    0,
                    "离开可视树不得触发任何重排：满缓冲重排一次就是秒级卡顿");
                view.VisualLineCount.ShouldBe(visuals);

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>以真实显示缓冲为数据源的视图上下文（增量接口直连缓冲）。</summary>
    private sealed class DeltaContext(int capacity = DisplayBuffer.DefaultCapacity, int maxChars = 0) : IModuleViewContext
    {
        public DisplayBuffer Buffer { get; } = maxChars > 0 ? new(capacity, maxChars) : new(capacity);

        public string InstanceId => "delta";

        public string MultilineSubmitKey => "CtrlEnter";

        public double? ReadLatest(string source) => null;

        public IReadOnlyList<double> ReadSeries(string source) => [];

        public TextBufferSnapshot ReadText(string source) =>
            new(Buffer.Snapshot(), Buffer.DroppedCount, Buffer.Capacity, 0, Buffer.Revision);

        public DisplayDelta ReadTextDelta(string source, in DisplayCursor cursor, List<string> sealedLines) =>
            Buffer.ReadDelta(cursor, sealedLines);

        public bool SubmitInput(string source, string text) => true;

        public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

        public ConfigFieldView? ReadField(string fieldName) => null;

        public CommandOutcome UpdateField(string fieldName, string value) => CommandOutcome.Ok;

        public ViewAppearance Appearance(string source) => ViewAppearance.None;

        public string? ReadInputText(string source) => null;

        public void WriteInputText(string source, string text)
        {
        }

        public int? SelectedPage(string stateKey) => null;

        public void SetSelectedPage(string stateKey, int page)
        {
        }
    }
}
