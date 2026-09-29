using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **视图状态回读 + 显示文本缓冲**的回归网〔2026-09 用户裁定 · `decisions.md` 7.28 / 7.31〕。
/// </summary>
/// <remarks>
/// <para>
/// 覆盖三件事（每一件都曾是"做不到"或"会丢"的）：
/// <list type="number">
///   <item><b>模块能回读</b>：当前选中页 · 输入框**未提交**的文本 · 显示区保留的文本 ——
///     断言走的是**模块真正拿到的那一个对象**（`instance.Context.ViewState`），不是另造的替身；</item>
///   <item><b>切页不再丢编辑</b>：用户在第 1 页敲的字，切到第 2 页再切回来**还在**（此前 `Rebuild` 会丢）；</item>
///   <item><b>显示缓冲"不清空就一直在"</b>：20 000 行容量、到顶丢最旧并计数、只读文本框里能滚回看
///     （并**实测**追加耗时 —— 容量是"实测后再调"的口径）。</item>
/// </list>
/// </para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class ViewStateReadbackTests : IClassFixture<HeadlessUiFixture>
{
    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具（真实窗口必须在它自己的调度线程上创建）。</param>
    public ViewStateReadbackTests(HeadlessUiFixture ui) => _ui = ui;
    /// <summary>
    /// 选中页 + **未提交的输入文本** + 显示行的**端到端**回读（真实窗口 + 真实运行时）。
    /// </summary>
    [Fact]
    public void 视图状态_切页与未提交文本都应能被模块读到_且显示行可读() => _ui.Run(() =>
    {
        using var harness = new VsHarness();
        harness.Place("vs-1");
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        _ = harness.OpenWindow(module);
        VisualHarnessSettle();

        harness.Runtime.Content.TryGetInstance("vs-1", out var instance).ShouldBeTrue();
        var view = instance!.Context.ViewState;

        // ---- ① 输入框当前文本（未提交）：直接从界面写（等价于用户打字）----
        var firstInput = module.Elements.OfType<TextInputElementViewModel>().Single();
        firstInput.Source.ShouldBe("page1");
        firstInput.Text = "用户正在编辑的内容";

        view.GetInputText("page1").ShouldBe("用户正在编辑的内容", "模块应能读到**尚未提交**的输入文本（所见即所发）");
        view.GetInputText("page2").ShouldBeNull("另一页没写过 ⇒ 未知");

        // ---- ② 切页：选中页写进视图状态（模块可感知）----
        var strip = module.Elements.OfType<TabStripElementViewModel>().Single();
        strip.Pages[1].SelectCommand.Execute(null);
        VisualHarnessSettle();

        view.GetSelectedPage(VsModule.PagesKey).ShouldBe(1, "切到第 2 页后，模块读到的选中页应是 1");
        module.Elements.OfType<TextInputElementViewModel>().Single().Source.ShouldBe("page2", "只渲染当前页");

        // ---- ③ 切回第 1 页：**未提交的编辑不丢**（此前 Rebuild 会丢）----
        strip.Pages[0].SelectCommand.Execute(null);
        VisualHarnessSettle();

        module.Elements.OfType<TextInputElementViewModel>().Single().Text
            .ShouldBe("用户正在编辑的内容", "切页重建元素表后，未提交的文本必须还在（对齐 WinForms：控件活着，数据就在）");
        view.GetSelectedPage(VsModule.PagesKey).ShouldBe(0);

        // ---- ④ 显示区文本：模块读到的是**缓冲的全部内容** ----
        module.Elements.OfType<TextLogElementViewModel>().Single().Append("第一行");
        module.Elements.OfType<TextLogElementViewModel>().Single().Append("第二行");

        view.GetDisplayLines("log").ShouldBe(["第一行", "第二行"], "模块回读的就是显示区那份缓冲（保存显示内容用）");
    });

    /// <summary>
    /// **显示缓冲"不清空就一直在"的策略**：容量 20 000 行、到顶丢最旧、丢弃要计数（纯视图模型级）。
    /// </summary>
    [Fact]
    public void 显示缓冲_容量应到顶丢最旧并计数()
    {
        using var harness = new VsHarness();
        harness.Place("vs-1");

        var log = harness.Windows.Modules.ShouldHaveSingleItem()
            .Elements.OfType<TextLogElementViewModel>().Single();

        log.Buffer.Capacity.ShouldBe(20_000, "容量 = 用户裁定的 20 000 行（decisions.md 7.31）");

        const int total = 20_500;

        for (var i = 0; i < total; i++)
        {
            log.Append($"line-{i:D6}");
        }

        log.Count.ShouldBe(20_000, "到顶后保留最近的 20 000 行");
        log.Lines[0].ShouldBe("line-000500", "丢的是**最旧**的");
        log.Lines[^1].ShouldBe("line-020499");
        log.DroppedLines.ShouldBe(500, "丢弃要**计数**（界面据此提示，不静默丢）");
        log.DroppedText.ShouldNotBeNull();

        // 模块回读的也是同一份（保存 = 全量，不只是可见那几行）
        harness.Runtime.Content.TryGetInstance("vs-1", out var instance).ShouldBeTrue();
        instance!.Context.ViewState.GetDisplayLines("log").Count.ShouldBe(20_000);

        // **清空是唯一会丢内容的入口**（WinForms 的 `Clear()` 语义）
        log.Clear();
        log.Count.ShouldBe(0);
        log.DroppedLines.ShouldBe(0);
        log.DroppedText.ShouldBeNull();
    }

    /// <summary>
    /// **只读文本框 + 分页窗口**真的把缓冲内容画出来了（可滚动回看、只读可复制），并**实测**追加+布局耗时。
    /// </summary>
    /// <remarks>
    /// 规模取 **2 000 行**（不是 20 000）：容量上限的性能口径用一次性的实测单独记录（见下），
    /// 常驻用例保持在这个量级 —— 否则单条用例就要十几秒（实测过）。
    /// <para>**2026-09 换实现**（用户裁定 B 案）：文本框里只放**一屏多**（窗口），全量留在缓冲里靠滚动条回看 ——
    /// 这正是"20 000 行也不卡"的根据（实测 ≈ 0.47 ms/行，整段塞进文本框要十几秒）。</para>
    /// </remarks>
    [Fact]
    public void 只读文本框_应显示缓冲内容且可滚动回看() => _ui.Run(() =>
    {
        using var harness = new VsHarness();
        harness.Place("vs-1");
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var window = harness.OpenWindow(module);
        VisualHarnessSettle();

        var log = module.Elements.OfType<TextLogElementViewModel>().Single();
        // 外壳视图 `TextLogView` 已删 ⇒ 日志正文与竖条就是 A5 模板里那个文本面自己的
        var logSurface = window.GetVisualDescendants().OfType<TextSurface>().Single(s => s.ShowScrollBar);
        var editor = logSurface.Editor;
        var bar = logSurface.ScrollBar;

        const int total = 2_000;
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < total; i++)
        {
            log.Append($"line-{i:D6}");
        }

        SettleWithTick();
        watch.Stop();

        editor.IsReadOnly.ShouldBeTrue("只读：可选中复制，但改不了");

        // **全量在缓冲里**："最早的也在"（回看得到，不是只留最近几行）
        log.Lines.Count.ShouldBe(total);
        log.Lines[0].ShouldBe("line-000000");
        log.Lines[^1].ShouldBe("line-001999");

        // **文本框里只有一屏多**（窗口）：条数 = WindowSize，最后一行是最新行
        var shown = EditorLines(editor);
        shown.Length.ShouldBe(Math.Min(logSurface.WindowSize, total), "窗口 = 视口放得下的行数 × 2（一屏 + 一页余量）");
        shown.Length.ShouldBeLessThan(total, "2 000 行不该整段塞进文本框（那正是被实测否决的做法）");
        shown[^1].ShouldBe("line-001999", "自动跟到尾：最新行在窗口里");

        // **滚动回看**（"显示只显示放得下的、之前的翻滚动条也可以看"的真实断言）
        bar.Value = 0;
        SettleWithTick();
        EditorLines(editor)[0].ShouldBe("line-000000", "往回滚应能看到**最早**的那一行（旧的没被丢掉，也不是'只留最近几行'）");

        // 拖回最底 ⇒ 恢复跟随
        bar.Value = bar.Maximum;
        SettleWithTick();
        EditorLines(editor)[^1].ShouldBe("line-001999", "回到底部后恢复跟随");

        var record = $"[实测] 追加 {total} 行 = {watch.ElapsedMilliseconds} ms；文本框窗口 {logSurface.WindowSize} 行";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "hyw-viewstate-perf.txt"), record);
        Console.WriteLine(record);
    });
    /// <summary>
    /// **模块请求"清空显示源" ⇒ 框架执行**（模块不留副本 ⇒ 只能请求；`decisions.md` 7.31 的"清空"落点）。
    /// </summary>
    [Fact]
    public void 模块请求清空显示源_界面缓冲应真的被清空() => _ui.Run(() =>
    {
        using var harness = new VsHarness();
        harness.Place("vs-1");
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        _ = harness.OpenWindow(module);
        VisualHarnessSettle();

        var log = module.Elements.OfType<TextLogElementViewModel>().Single();
        log.Append("第一行");
        log.Append("第二行");
        VisualHarnessSettle();

        log.Count.ShouldBe(2);

        // 模块侧：请求清空（框架里这条请求进 L4 的挂起表）
        harness.Runtime.Content.TryGetInstance("vs-1", out var instance).ShouldBeTrue();
        instance!.Context.Display.Clear("log");

        // UI 在刷新节拍里取走并执行
        harness.Windows.Refresh();
        VisualHarnessSettle();

        log.Count.ShouldBe(0, "清空是**唯一**会丢显示内容的入口（WinForms 的 Clear() 语义）");
        log.Lines.ShouldBeEmpty();
        instance.Context.ViewState.GetDisplayLines("log").ShouldBeEmpty();
    });

    /// <summary>存储本身的边界：未知项 ⇒ null / 空；实例移除 ⇒ 全清（"不持久化、关闭即丢"）。</summary>
    [Fact]
    public void 视图状态存储_未知项应安全_移除实例应清空()
    {
        var store = new ViewStateStore();
        var reader = store.ReaderFor("i-1");

        reader.GetSelectedPage("nope").ShouldBeNull();
        reader.GetInputText("nope").ShouldBeNull();
        reader.GetDisplayLines("nope").ShouldBeEmpty();

        store.SetSelectedPage("i-1", VsModule.PagesKey, 2);
        store.SetInputText("i-1", "page1", "abc");
        store.Display("i-1", "log").Append("x");

        reader.GetSelectedPage(VsModule.PagesKey).ShouldBe(2);
        reader.GetInputText("page1").ShouldBe("abc");
        reader.GetDisplayLines("log").ShouldBe(["x"]);

        // 另一实例读不到（按实例隔离）
        store.ReaderFor("i-2").GetSelectedPage(VsModule.PagesKey).ShouldBeNull();

        store.RemoveInstance("i-1");
        reader.GetSelectedPage(VsModule.PagesKey).ShouldBeNull();
        reader.GetInputText("page1").ShouldBeNull();
        reader.GetDisplayLines("log").ShouldBeEmpty();
    }

    /// <summary>显示缓冲：容量/丢最旧/清空/事件（纯 VM 级，不依赖窗口）。</summary>
    [Fact]
    public void 显示缓冲_容量与事件应自洽()
    {
        var buffer = new DisplayBuffer(capacity: 3);
        var appended = new List<string>();
        var trimmed = new List<int>();
        var cleared = 0;

        buffer.Appended += appended.Add;
        buffer.Trimmed += trimmed.Add;
        buffer.Cleared += () => cleared++;

        buffer.Append("a");
        buffer.Append("   ");   // 空白行忽略（与既有 A5 行为一致）
        buffer.Append("b");
        buffer.Append("c");
        buffer.Append("d");

        buffer.Snapshot().ShouldBe(["b", "c", "d"], "到顶后丢最旧");
        buffer.DroppedCount.ShouldBe(1);
        appended.ShouldBe(["a", "b", "c", "d"]);
        trimmed.ShouldBe([1]);

        buffer.Clear();
        buffer.Snapshot().ShouldBeEmpty();
        buffer.DroppedCount.ShouldBe(0);
        cleared.ShouldBe(1);
    }

    private static void VisualHarnessSettle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// 推进到"布局 + 一个 20 Hz 刷新节拍都跑完"。
    /// </summary>
    /// <remarks>
    /// `VisualHarnessSettle` 只抽干队列：日志窗口的刷新节流是**真实时间**的（50 ms），
    /// 不给真实时间的话窗口会停在上一拍（实测：无头会话里 `DispatcherTimer` 根本不会自己走）。
    /// </remarks>
    private static void SettleWithTick()
    {
        for (var i = 0; i < 3; i++)
        {
            VisualHarnessSettle();
            Thread.Sleep(25);
        }

        VisualHarnessSettle();
    }

    /// <summary>只读文本框里的行（统一成 LF 切开）。</summary>
    private static string[] EditorLines(TextBox editor) =>
        editor.Text is { Length: > 0 } text ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : [];

    /// <summary>本类的小夹具：**带视图状态存储**的运行时 + 一个声明了 `StateKey` 的多页模块。</summary>
    private sealed class VsHarness : IDisposable
    {
        public VsHarness()
        {
            ViewState = new ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
            var definition = new ModuleDefinition(
                ModuleId.Create("test", "visual-viewstate", "1.0.0"),
                typeof(VsModule).Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                typeof(VsModule),
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);
        }

        public ViewStateStore ViewState { get; }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Place(string instanceId)
        {
            Services.AddModule(instanceId, "test/visual-viewstate/1.0.0").Succeeded.ShouldBeTrue();
            Windows.Refresh();

            Services.UpdateModuleLayout(new ModuleLayoutView(
                instanceId,
                0,
                0,
                ModuleViewViewModel.DefaultWidth,
                ModuleViewViewModel.DefaultHeight)).Succeeded.ShouldBeTrue();

            Windows.Refresh();
        }

        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow
            {
                DataContext = module,
                Controller = Windows,
            };

            window.Show();
            VisualHarnessSettle();
            return window;
        }

        public void Dispose() => Runtime.Dispose();
    }

    /// <summary>
    /// 夹具模块：**两页输入（声明 `StateKey`）+ 一个文本日志** —— 覆盖回读的三样。
    /// </summary>
    [ModuleDefinition("test", "visual-viewstate", "1.0.0")]
    public sealed class VsModule : IModule
    {
        /// <summary>标签容器的状态键（模块靠它回读"当前选中页"）。</summary>
        public const string PagesKey = "pages";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(
                        ViewElementKind.TabPages,
                        title: "消息",
                        children:
                        [
                            new ViewElement(
                                ViewElementKind.Group,
                                title: "第 1 页",
                                children: [new ViewElement(ViewElementKind.TextInput, "page1", "输入1")]),
                            new ViewElement(
                                ViewElementKind.Group,
                                title: "第 2 页",
                                children: [new ViewElement(ViewElementKind.TextInput, "page2", "输入2")]),
                        ])
                    {
                        StateKey = PagesKey,
                    },
                    new ViewElement(ViewElementKind.TextLog, "log", "显示"),
                ],
                dataSources:
                [
                    new ViewDataSource("page1"),
                    new ViewDataSource("page2"),
                    new ViewDataSource("log", kind: ViewDataSourceKind.Text),
                ]));
    }
}
