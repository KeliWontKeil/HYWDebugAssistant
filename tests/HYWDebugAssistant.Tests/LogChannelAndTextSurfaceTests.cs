using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **日志通道 → 显示层**的回归网〔2026-09 用户裁定 · WinForms 模型〕。
/// </summary>
/// <remarks>
/// <para>
/// 用户原话："如果不开换行或是时间戳，我认为追加数据应**直接追加在后面**，不做任何标记，保证数据的完整性" ·
/// "文本框里看到的文本是什么样，保存就是什么样，并**去除标记**"。
/// </para>
/// <para>
/// 本文件从**通道那一头**开始验（模块选哪一个通道调用由模块级用例钉住）：
/// 显示区经 <c>IViewDisplayAccess.AppendText</c>（不换行）/ <c>AppendLine</c>（通道换行）拿到数据 ⇒
/// 断言"屏幕上真的长这样"：① 不开换行时**没有人为换行**；② 开换行时换行来自**通道调用**、
/// 界面里**一个 `↵` 都不画**（它不是字符）；③ 数据里**真有的换行字符**才画 `↵`；
/// ④ 只是"获得键盘焦点"不再冻结日志（用户裁定：点一下日志框不该让日志卡住）。
/// </para>
/// <para>
/// **链路**：真实模块包（数据显示器）→ 真实运行时（显示请求进 L4 挂起表）→ UI 刷新节拍落地到显示缓冲 →
/// 真实 `ModuleWindow` 里的 `TextSurface`（文本框文本 + 绘制层算出的标记几何）。
/// </para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class LogChannelAndTextSurfaceTests : IClassFixture<HeadlessUiFixture>
{
    /// <summary>显示源名（与数据显示器模块声明的那个一致；测试不引模块程序集 ⇒ 用字面量）。</summary>
    private const string LogSource = "log";

    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具（真实窗口必须在它自己的调度线程上创建）。</param>
    public LogChannelAndTextSurfaceTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>
    /// **不开换行：追加数据直接接在后面，界面里不出现任何人为换行**（用户裁定原文）。
    /// </summary>
    [Fact]
    public void 日志_不开换行时_界面里不出现人为换行() => _ui.Run(() =>
    {
        using var harness = new LogHarness();
        var (_, surface) = harness.OpenLogWindow();

        harness.Channel.AppendText(LogSource, "AB");
        harness.Channel.AppendText(LogSource, "CD");

        harness.RefreshAndSettle();

        var text = surface.Editor.Text ?? string.Empty;

        text.ShouldBe("ABCD", "两段数据**直接接在后面**（旧实现按 96 字节定宽行切开，还会补空格）");
        text.ShouldNotContain('\n', "不开换行时**没有任何人为换行**");
        text.ShouldNotContain("↵", Case.Sensitive);
        surface.Marks.Marks.ShouldBeEmpty("没有换行字符 ⇒ 一个标记都不画");
    });

    /// <summary>
    /// **通道换行：界面里看得到换行，但一个 `↵` 都不画**。
    /// </summary>
    /// <remarks>
    /// 这是"通道换行**不是字符**"那条裁定的可执行判据：文本里必须有换行（否则两包挤在一行），
    /// 而显示层的标记数必须是 **0**。
    /// </remarks>
    [Fact]
    public void 日志_通道换行的行末不画换行标记() => _ui.Run(() =>
    {
        using var harness = new LogHarness();
        var (_, surface) = harness.OpenLogWindow();

        // 「数据包换行」= 换行由**通道调用**表达（模块那边选哪一个调用的判据见模块级用例）
        harness.Channel.AppendLine(LogSource, "AA");
        harness.Channel.AppendLine(LogSource, "BB");

        harness.RefreshAndSettle();

        TextLines(surface.Editor.Text ?? string.Empty).ShouldBe(["AA", "BB"], "两个包 ⇒ 屏幕上两行（换行由通道给）");
        surface.Marks.Marks.ShouldBeEmpty(
            "**通道换行不是字符** ⇒ 显示层绝不能给它画 `↵`（用户裁定：通道引进的换行不是字符，永远不该有 ↵）");
    });

    /// <summary>
    /// **数据里真有的换行字符才画 `↵`**：同一块文本里"通道换行"与"数据换行"必须分得清。
    /// </summary>
    /// <remarks>
    /// 构造：第一条数据自带一个换行字符（`甲\n乙`），随后又用**通道**换行，再来一条（`丙`）。
    /// 屏幕上三行（0 号行里的 `\n` 来自数据、1 号行末的 `\n` 来自通道）⇒ **只应有一个标记**，
    /// 且它落在**第一行**（第二行末尾那个是通道换行，不画）。
    /// </remarks>
    [Fact]
    public void 日志_数据里的换行字符画标记而通道换行不画() => _ui.Run(() =>
    {
        using var harness = new LogHarness();
        var (_, surface) = harness.OpenLogWindow();

        harness.Channel.AppendLine(LogSource, "甲\n乙");
        harness.Channel.AppendLine(LogSource, "丙");

        harness.RefreshAndSettle();

        TextLines(surface.Editor.Text ?? string.Empty).ShouldBe(["甲", "乙", "丙"], "数据里的换行 + 通道换行 = 三行");

        var marks = surface.Marks.Marks;

        marks.Count.ShouldBe(1, "只有**数据里真的有**的那一个换行字符画 `↵`（通道那两个不画）");
        marks[0].Text.ShouldBe(NewlineMarkOverlay.NewlineMark);
        marks.Select(m => Math.Round(m.Position.Y, 1)).Distinct().Count()
            .ShouldBe(1, "那个标记挂在**第一行**（数据换行所在的行），不是第二行（通道换行那一行）");
    });

    /// <summary>
    /// **只是"获得键盘焦点"不再冻结日志**〔2026-09 用户裁定 · 批次 3 收窄〕。
    /// </summary>
    /// <remarks>
    /// 旧判据是"有选区 **或** 有焦点"⇒ 用户点一下日志框（一个字都没选）日志就**永不刷新**，看着像卡死。
    /// 重写窗口唯一的真实副作用是"把选区抽走" ⇒ 判据收窄为**有选区**；本用例钉住"只有焦点时必须继续刷新"。
    /// </remarks>
    [Fact]
    public void 日志_只是获得焦点时仍应继续刷新() => _ui.Run(() =>
    {
        using var harness = new LogHarness();
        var (_, surface) = harness.OpenLogWindow();

        harness.Channel.AppendLine(LogSource, "第一行");
        harness.RefreshAndSettle();

        TextLines(surface.Editor.Text ?? string.Empty).ShouldBe(["第一行"]);

        // 用户点进日志框：**有焦点、但没有任何选区**
        surface.Editor.Focus();
        Settle();
        surface.Editor.IsFocused.ShouldBeTrue("本用例的前提：焦点真的在日志框里");
        (surface.Editor.SelectionStart != surface.Editor.SelectionEnd).ShouldBeFalse("且**没有选区**");

        harness.Channel.AppendLine(LogSource, "第二行");
        harness.RefreshAndSettle();

        TextLines(surface.Editor.Text ?? string.Empty).ShouldBe(
            ["第一行", "第二行"], "只是有焦点 ⇒ **必须继续刷新**（旧实现会在这里冻住，看起来像日志卡死）");
    });

    /// <summary>
    /// A5 的 `DataTemplate` **真的建出了日志那一块**：标题、只读文本面、丢弃提示三件都在，
    /// 且丢弃提示是**绑在** `DroppedText` 上的（不是写死的空块）。
    /// </summary>
    /// <remarks>
    /// 外壳视图 `TextLogView` 删掉之后，"显示区怎么摆"整块搬进了 `DataTemplate`
    /// ⇒ 模板级的东西（名字、绑定）必须由**真的走到模板**的用例盯住；名字域也从"视图自己的名字域"
    /// 变成了"模板的名字域"（`FindControl` 从视图上找不到，只能按可视树按名字找）。
    /// </remarks>
    [Fact]
    public void 日志模板_应带标题与丢弃提示且提示跟着模型显示() => _ui.Run(() =>
    {
        using var harness = new LogHarness();
        var (window, surface) = harness.OpenLogWindow();

        surface.ShowScrollBar.ShouldBeTrue("A5 那一块必须就是分页窗口用法的文本面");
        surface.IsReadOnly.ShouldBeTrue();

        var log = window.DataContext
            .ShouldBeOfType<ModuleViewViewModel>()
            .Elements.OfType<TextLogElementViewModel>().ShouldHaveSingleItem();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();

        texts.ShouldContain(t => t.Text == log.Title, "标题块必须真的画出来");

        var hint = texts.FirstOrDefault(t => string.Equals(t.Name, "DroppedHint", StringComparison.Ordinal))
            .ShouldNotBeNull("丢弃提示块必须在 A5 模板里（否则丢弃就是静默的）");
        hint.IsVisible.ShouldBeFalse("没有丢弃就**一个字都不显示**（提示块本身仍在，只是不可见）");

        // 模型说"丢了 2 行" ⇒ 界面必须跟着显示（这条绑定就是"丢弃不静默"的落点）
        log.DroppedLines = 2;
        Settle();

        hint.IsVisible.ShouldBeTrue("丢弃了就必须说出来");
        hint.Text.ShouldNotBeNull().ShouldContain("已丢弃 2 行");
    });

    /// <summary>文本框里的行（统一成 LF 切开）。</summary>
    private static string[] TextLines(string text) =>
        text is { Length: > 0 } ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : [];

    /// <summary>推进到"布局 + 一个 20 Hz 刷新节拍都跑完"（节流是真实时间的，必须给真实时间）。</summary>
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

    /// <summary>装入**真实数据显示器包**并开一个真实模块窗口的小夹具。</summary>
    private sealed class LogHarness : IDisposable
    {
        private const string MonitorId = "hyw/data-monitor/1.0.0";

        public LogHarness()
        {
            var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");
            Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

            ViewState = new ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
            _ = Services.ScanModuleSources([staged]);

            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);

            Services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();
            Windows.Refresh();

            Runtime.Content.TryGetInstance("m1", out var instance).ShouldBeTrue();
            Instance = instance!;
        }

        public ViewStateStore ViewState { get; }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public HYWDebugAssistant.Hosting.Instances.ModuleInstance Instance { get; }

        /// <summary>模块侧的显示通道（与模块自己调用的是**同一个对象**）。</summary>
        public IViewDisplayAccess Channel => Instance.Context.Display;

        /// <summary>开日志窗口（找不到显示区文本面就直接失败，不静默跳过断言）。</summary>
        /// <remarks>
        /// 外壳视图 `TextLogView` 已删 ⇒ A5 那一块**就是** `DataTemplate` 里那个 `TextSurface`
        /// （用 `ShowScrollBar` 认它：只有日志用法才开分页窗口 + 自建竖条）。
        /// </remarks>
        public (ModuleWindow Window, TextSurface Surface) OpenLogWindow()
        {
            var module = Windows.Modules.ShouldHaveSingleItem();
            var window = new ModuleWindow { DataContext = module, Controller = Windows };
            window.Show();
            Settle();

            var surface = window.GetVisualDescendants().OfType<TextSurface>()
                .Where(s => s.ShowScrollBar)
                .ShouldHaveSingleItem("A5 模板里应当**恰好一个**分页窗口用法的文本面");

            return (window, surface);
        }

        /// <summary>把挂起的显示请求落地（与生产 UI 同一个调用）并跑完一个节拍。</summary>
        public void RefreshAndSettle()
        {
            Windows.Refresh();
            Settle();
        }

        public void Dispose() => Runtime.Dispose();

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到仓库根");
        }
    }
}
