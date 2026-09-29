using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **A5 文本日志的"分页窗口"口径**〔2026-09 用户裁定 B 案：只读 `TextBox` + 分页窗口〕。
/// </summary>
/// <remarks>
/// <para>界面只把**一屏多**的行喂给文本框（实测 ≈ 0.47 ms/行；20 000 行整段塞进去要十几秒），
/// 更早的内容靠自建滚动条换窗口 —— 本文件盯的就是"这确实是个窗口，而不是把全量塞进去"，
/// 外加几条用户裁定的交互语义：跟随尾部、向上回看不抢位置、选区/焦点期间不重写、到顶丢弃的提示。</para>
/// <para>**技法**：与视觉测试同一套路 —— 真实 `App` + 真实窗口 + 真实布局（`Settle()` 里补上
/// 20 Hz 节拍所需的真实时间）。正文与竖条**就是共用控件 `TextSurface` 自己的**（`TextLogView` 外壳已删），
/// 所以这里直接以日志视图模型为数据源构造同一个文本面，不需要运行时。</para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class TextLogWindowTests : IClassFixture<HeadlessUiFixture>
{
    /// <summary>宿主窗口宽度：要能放下好几屏，好让"窗口 &lt; 全量"这件事可观察。</summary>
    private const double HostWidth = 420;

    /// <summary>宿主窗口高度（同上）。</summary>
    private const double HostHeight = 320;

    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具（与视觉测试同集合 ⇒ 禁止并行）。</param>
    public TextLogWindowTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>窗口真的是窗口：缓冲里是**全量**行，文本框只拿到**最后 `WindowSize` 行**。</summary>
    [Fact]
    public void 文本日志_只把最后WindowSize行喂给只读文本框() => _ui.Run(() =>
    {
        var log = new TextLogElementViewModel("日志", dataSource: null);
        using var host = Open(log);

        const int total = 60;
        for (var i = 0; i < total; i++)
        {
            log.Append($"line-{i:D4}");
        }

        Settle();

        host.Editor.IsReadOnly.ShouldBeTrue("只读文本框：可选中、可复制，但改不了");
        log.Lines.Count.ShouldBe(total, "**缓冲保留全量**（回看、丢弃提示、复制全部都靠它）");
        host.Surface.WindowSize.ShouldBeGreaterThan(0, "窗口行数由布局后的视口算出");

        var shown = TextLines(host.Editor);
        shown.Length.ShouldBeLessThan(total, "全量 60 行 >> 一屏 ⇒ 文本框里不该是全量");
        shown.Length.ShouldBe(Math.Min(host.Surface.WindowSize, total), "窗口 = 视口放得下的行数 × 2（一屏 + 一页余量）");
        shown[^1].ShouldBe("line-0059", "窗口停在末尾");
        shown.ShouldNotContain(x => string.Equals(x, "line-0000", StringComparison.Ordinal), "最早的行不在窗口里：它在缓冲里，靠滚动条回看");
    });

    /// <summary>**跟随尾部**：每次追加（跑完一个节拍）后，窗口的最后一行就是最新那行。</summary>
    [Fact]
    public void 文本日志_追加后应跟到尾部() => _ui.Run(() =>
    {
        var log = new TextLogElementViewModel("日志", dataSource: null);
        using var host = Open(log);

        for (var i = 0; i < 30; i++)
        {
            log.Append($"line-{i:D4}");
        }

        Settle();
        TextLines(host.Editor)[^1].ShouldBe("line-0029", "首批内容到达后就跟到尾");

        // 逐条追加：**每一次**都要看到最新行（节流到 20 Hz 也不能漏掉"跟到底"这件事）
        for (var i = 30; i < 40; i++)
        {
            log.Append($"line-{i:D4}");
            Settle();
            TextLines(host.Editor)[^1].ShouldBe($"line-{i:D4}");
        }

        host.Surface.IsFollowingTail.ShouldBeTrue();
    });

    /// <summary>**不抢回看位置**：滚动条拖到上面之后追加不得改动 `WindowStart`；拖回最底 ⇒ 恢复跟随。</summary>
    [Fact]
    public void 文本日志_向上回看后追加不应抢位置_拖回底部恢复跟随() => _ui.Run(() =>
    {
        var log = new TextLogElementViewModel("日志", dataSource: null);
        using var host = Open(log);

        for (var i = 0; i < 60; i++)
        {
            log.Append($"line-{i:D4}");
        }

        Settle();

        // 拖到最上 = 用户明确的"回看历史"意图
        var beforeDrag = Diag(host, log, "拖动前");
        host.Bar.Value = 0;
        host.Surface.IsFollowingTail.ShouldBeFalse($"{beforeDrag} → {Diag(host, log, "刚拖动滚动条")}");
        Settle();

        host.Surface.WindowStart.ShouldBe(0);
        host.Surface.IsFollowingTail.ShouldBeFalse(Diag(host, log, "拖动并跑完一拍之后"));
        TextLines(host.Editor)[0].ShouldBe("line-0000", "窗口起点在最上面，最早的行走进视野");

        log.Append("line-0060");
        Settle();

        host.Surface.WindowStart.ShouldBe(0, "用户正在上面回看：追加**不得**动他的位置");
        TextLines(host.Editor)[0].ShouldBe("line-0000");
        TextLines(host.Editor).ShouldNotContain("line-0060", "回看期间窗口内容不该被新行顶走");

        // 拖回最底 = 恢复跟随
        host.Bar.Value = host.Bar.Maximum;
        Settle();

        host.Surface.IsFollowingTail.ShouldBeTrue("回到底部 ⇒ 恢复跟随");
        TextLines(host.Editor)[^1].ShouldBe("line-0060", "恢复跟随后最新行又出现了");
    });

    /// <summary>
    /// **有选区时不重写**（用户 2026-09 裁定）：窗口内容原封不动，数据继续进缓冲；选区一收就补齐并回到尾部。
    /// </summary>
    [Fact]
    public void 文本日志_有选区时追加不得重写窗口_选区收起后补齐并恢复跟随() => _ui.Run(() =>
    {
        var log = new TextLogElementViewModel("日志", dataSource: null);
        using var host = Open(log);

        for (var i = 0; i < 30; i++)
        {
            log.Append($"line-{i:D4}");
        }

        Settle();

        var before = host.Editor.Text;

        host.Editor.SelectionStart = 0;
        host.Editor.SelectionEnd = 5;
        Dispatcher.UIThread.RunJobs();

        log.Append("line-0030");
        Settle();

        host.Editor.Text.ShouldBe(before, "有选区时**绝不重写**（否则用户正在选的文字会被抽走）");
        log.Lines.Count.ShouldBe(31, "但数据照旧进缓冲（窗口只是**暂时**不刷新）");

        // 选区收起 = 交互结束 ⇒ 把攒下的内容一次补齐，并按裁定回到跟随尾部
        host.Editor.SelectionEnd = 0;
        Settle();

        TextLines(host.Editor)[^1].ShouldBe("line-0030", "选区收起后补齐了暂停期间攒下的内容");
        host.Surface.IsFollowingTail.ShouldBeTrue("交互结束 ⇒ 回到跟随尾部");
    });

    /// <summary>
    /// 到顶丢最旧时**仍然明说**（A5 的老语义，换实现不该丢）。
    /// </summary>
    /// <remarks>
    /// 这里盯的是**语义**（缓冲真的丢了、丢了几行、窗口里只剩还在的那几行、模型给出的提示文案正确）；
    /// "那句提示真的画在界面上"由模板级的用例盯（`LogChannelAndTextSurfaceTests`，
    /// 那里挂的是真实模块窗口 ⇒ 真的走到 A5 的 `DataTemplate`）。
    /// </remarks>
    [Fact]
    public void 文本日志_超出容量时丢弃提示仍在() => _ui.Run(() =>
    {
        // 容量压到 3 行，好让"丢弃"在几步内真的发生（生产容量是 20 000）
        var log = new TextLogElementViewModel("日志", dataSource: null, buffer: new DisplayBuffer(capacity: 3));
        using var host = Open(log);

        for (var i = 0; i < 5; i++)
        {
            log.Append($"line-{i}");
        }

        Settle();

        log.DroppedLines.ShouldBe(2);
        TextLines(host.Editor).ShouldBe(["line-2", "line-3", "line-4"], "窗口只留缓冲里还在的那几行");
        log.DroppedText.ShouldNotBeNull("丢弃提示必须有文案").ShouldContain("已丢弃 2 行");
    });

    /// <summary>把无头环境推进到"布局 + 一个 20 Hz 节拍都跑完"。</summary>
    /// <remarks>
    /// 只跑 `RunJobs` 不够：布局/渲染由渲染帧驱动，而刷新节流是**真实时间**的 `DispatcherTimer`
    /// ⇒ 必须给出真实时间（`Thread.Sleep`）再抽干队列，否则窗口永远停在上一拍。
    /// </remarks>
    private static void Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>失败信息里的现场快照（窗口行数/起点、滚动条范围、视口与视图高度）。</summary>
    private static string Diag(LogHost host, TextLogElementViewModel log, string stage) =>
        $"阶段={stage}：size={host.Surface.WindowSize} start={host.Surface.WindowStart} max={host.Bar.Maximum} "
        + $"value={host.Bar.Value} lines={log.Lines.Count} editorH={host.Editor.Bounds.Height:0.#} "
        + $"surfaceH={host.Surface.Bounds.Height:0.#}";

    /// <summary>文本框里的行（统一成 LF 切开；只读文本框可能把换行规范化）。</summary>
    private static string[] TextLines(TextBox editor) =>
        editor.Text is { Length: > 0 } text ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : [];

    /// <summary>
    /// 把**文本日志那一套用法**挂进真实窗口并跑完首帧（找不到关键控件就直接失败，而不是静默跳过断言）。
    /// </summary>
    /// <remarks>
    /// 属性组合与 A5 的 `DataTemplate` 逐项一致（只读、多行、分页窗口 + 自建竖条、画 `↵`）——
    /// 外壳视图 `TextLogView` 已删，日志就是"同一个文本面的一组属性"。
    /// </remarks>
    private static LogHost Open(TextLogElementViewModel log)
    {
        var surface = new TextSurface
        {
            IsReadOnly = true,
            IsMultiline = true,
            ShowScrollBar = true,
            ShowNewlineMarks = true,
            Lines = log.Lines,
        };

        var window = new Window
        {
            Width = HostWidth,
            Height = HostHeight,
            Content = surface,
        };

        window.Show();
        Settle();

        return new LogHost(window, surface, surface.Editor, surface.ScrollBar);
    }

    /// <summary>测试宿主：窗口 + 文本面 + 两个关键控件（`Dispose` 关窗并抽干队列）。</summary>
    private sealed class LogHost(Window window, TextSurface surface, TextBox editor, ScrollBar bar) : IDisposable
    {
        /// <summary>宿主窗口。</summary>
        public Window Window { get; } = window;

        /// <summary>被检文本面（日志用法）。</summary>
        public TextSurface Surface { get; } = surface;

        /// <summary>只读文本框（日志正文）。</summary>
        public TextBox Editor { get; } = editor;

        /// <summary>自建竖向滚动条。</summary>
        public ScrollBar Bar { get; } = bar;

        /// <inheritdoc />
        public void Dispose()
        {
            Window.Close();
            Settle();
        }
    }
}
