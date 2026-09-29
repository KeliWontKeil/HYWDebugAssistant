using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **四个基础模块的真实排布验收**〔2026-09 用户反馈："缩放模块时文本框只有宽度随动、高度却不产生变化"·
/// "显示文本框会随着行数增加而变长？应该是显示滚动条而不是变布局"·"模块默认大小不对，信息被遮蔽和覆盖"〕。
/// </summary>
/// <remarks>
/// 用**真实模块包**（`artifacts/modules`）+ 真实窗口 + 真实布局，逐条钉住三件事：
/// <list type="number">
///   <item><b>默认尺寸下内容排得下</b>：元素面板的**实占高度不低于最小布局高度** ⇒ 不需要滚动就能看全（不遮蔽）；</item>
///   <item><b>内容增长不改变布局</b>：往显示区灌几百行，显示区**自己的高度不变**（滚动条接管）；</item>
///   <item><b>高度随窗口拉伸</b>：把窗口拉高，多行输入 / 显示区**高度跟着变**（不只是宽度）。</item>
/// </list>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class BusinessModulesLayoutTests : IClassFixture<HeadlessUiFixture>
{
    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具。</param>
    public BusinessModulesLayoutTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>
    /// 四个模块在其**声明的默认尺寸**下都应把内容排下：**不用滚动 · 不被覆盖层压住**。
    /// </summary>
    /// <remarks>
    /// 三条断言（2026-09 用户反馈"上下边框存在元素遮盖问题（窗口高度和内容比例不合适）"·
    /// "**模块本体出现滚动条是比例不正确的表现**"）：
    /// <list type="number">
    ///   <item>内容最小高 ≤ 视口高（排得下）；</item>
    ///   <item>**没有滚动条**：`Extent ≤ Viewport`（用户口径：滚动是框架能力，但默认尺寸下不该出现）；</item>
    ///   <item>**不被压住**：内容底边在"左下运行指示"与"端口标记带"的顶边**之上**（`ModuleContentView` 的上/下预留带保证）。</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void 四模块_默认尺寸下内容应排得下() => _ui.Run(() =>
    {
        using var harness = new RealModulesHarness();
        var measurements = new List<string>();
        var failures = new List<string>();

        foreach (var (instanceId, moduleId) in RealModulesHarness.All)
        {
            harness.Place(instanceId, moduleId);
            var module = harness.Windows.Modules.Single(m => m.InstanceId == instanceId);
            var window = harness.OpenWindow(module);
            Settle();

            var panel = PanelOf(window);
            panel.ShouldNotBeNull($"{instanceId}：内容面板应存在");

            // 内容视口 = 内容区 ScrollViewer 的视口（窗口减去边框与滚动条）
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
            var viewport = scroll.Viewport.Height;

            var panelBottom = BottomOf(panel!, window);

            // 运行指示（左下角）：tree 里另有**端口标记内的连接指示灯**也用 `StateIndicator` ⇒ 取**最靠下**那个
            var stateTop = window.GetVisualDescendants().OfType<StateIndicator>()
                .Select(si => TopOf(si, window))
                .DefaultIfEmpty(double.PositiveInfinity)
                .Max();

            // 端口标记：只对**下边**的标记断言"内容不被压住"（上边的标记由顶部预留带负责，见另一条断言）
            var half = window.Bounds.Height / 2;
            var bottomMarkers = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("portMarker"))
                .Select(b => TopOf(b, window))
                .Where(top => top > half)
                .ToList();
            var markerTop = bottomMarkers.Count == 0 ? double.PositiveInfinity : bottomMarkers.Min();

            // 顶部：内容顶边必须在上边标记之下
            var panelTop = TopOf(panel!, window);
            var topMarkers = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("portMarker"))
                .Select(b => new { Top = TopOf(b, window), Bottom = BottomOf(b, window) })
                .Where(m => m.Top <= half)
                .ToList();
            var markerBottom = topMarkers.Count == 0 ? double.NegativeInfinity : topMarkers.Max(m => m.Bottom);

            measurements.Add(
                $"[{instanceId}] 窗口={module.Width:0}x{module.Height:0} 内容最小高={panel!.DesiredSize.Height:0.#} 视口高={viewport:0.#}"
                + $" extent={scroll.Extent.Height:0.#} 内容顶={panelTop:0.#} 内容底={panelBottom:0.#} 状态行顶={stateTop:0.#}"
                + $" 下边标记顶={markerTop:0.#} 上边标记底={markerBottom:0.#}");

            // 四条判据（**全部先量后判**：四个模块各自的数一起报出来，免得"改一个看一个"）
            if (panel.DesiredSize.Height > viewport + 0.5)
            {
                failures.Add($"{moduleId}：默认尺寸 {module.Width}×{module.Height} 下内容**排不下**"
                    + $"（内容最小高 {panel.DesiredSize.Height:0.#} > 视口 {viewport:0.#}）");
            }

            if (scroll.Extent.Height > viewport + 0.5)
            {
                failures.Add($"{moduleId}：默认尺寸下**出现了滚动条**（Extent {scroll.Extent.Height:0.#} > Viewport {viewport:0.#}）"
                    + " —— 用户口径：\"模块本体出现滚动条是比例不正确的表现\"");
            }

            if (panelBottom > stateTop + 0.5)
            {
                failures.Add($"{moduleId}：内容底边 {panelBottom:0.#} 被左下角运行指示（顶 {stateTop:0.#}）压住");
            }

            if (panelBottom > markerTop + 0.5)
            {
                failures.Add($"{moduleId}：内容底边 {panelBottom:0.#} 被**下边**端口标记带（顶 {markerTop:0.#}）压住");
            }

            if (panelTop < markerBottom - 0.5)
            {
                failures.Add($"{moduleId}：内容顶边 {panelTop:0.#} 被**上边**端口标记带（底 {markerBottom:0.#}）压住");
            }

            window.Close();   // 不留活窗口：同一进程里的用例共享负载（实测：留着会让时序敏感的用例变飘）
            Settle();

            harness.Remove(instanceId);
        }

        System.IO.File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "hyw-layout-probe.txt"),
            string.Join(Environment.NewLine, measurements) + Environment.NewLine);

        failures.ShouldBeEmpty(string.Join("；\n", failures) + "\n实测：\n" + string.Join("\n", measurements));
    });

    /// <summary>
    /// **多行输入框必须填满"面板分配给它的元素高度"**（2026-09 用户实测："模块的文本框仍然有问题…
    /// 我放大模块后就正确了"）。
    /// </summary>
    /// <remarks>
    /// 填满型（多行输入 / A5 日志）由 `ModuleElementPanel` 分到"剩余高度"，但**模板内部还得把这份高度交给文本框**：
    /// 模板根的行定义若是 `Auto,Auto,Auto`，文本框只会拿到自己的最小高（60）⇒ 面板给的高度被浪费在空白里，
    /// 表现就是"框比该有的矮、放大窗口才看着对"。⇒ 模板里文本框那一行必须是 `*`。
    /// </remarks>
    [Fact]
    public void 多行输入框_应拿到面板分给它的剩余高度() => _ui.Run(() =>
    {
        using var harness = new RealModulesHarness();
        harness.Place("t1", RealModulesHarness.TextSender);
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var window = harness.OpenWindow(module);
        Settle();

        var input = module.Elements.OfType<TextInputElementViewModel>()
            .First(e => e.IsMultiline && e.Source.StartsWith("text", StringComparison.Ordinal));
        var box = window.GetVisualDescendants().OfType<TextBox>()
            .Single(b => ReferenceEquals(b.DataContext, input));

        // 模板根 = 该元素模板的根（DataContext = 该输入元素、且是 box 的祖先）
        var root = window.GetVisualDescendants().OfType<Panel>()
            .Where(p => ReferenceEquals(p.DataContext, input))
            .OrderBy(p => p.Bounds.Width * p.Bounds.Height)
            .FirstOrDefault();

        root.ShouldNotBeNull("输入元素的模板根应存在");

        System.IO.File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "hyw-layout-probe.txt"),
            $"[input-fill] 根={root!.Bounds.Width:0.#}×{root.Bounds.Height:0.#} 框={box.Bounds.Width:0.#}×{box.Bounds.Height:0.#}"
            + $" 框最小高={box.MinHeight:0.#} 行定义={(root as Grid)?.RowDefinitions}{Environment.NewLine}");

        box.Bounds.Height.ShouldBeGreaterThan(
            box.MinHeight + 20,
            $"默认尺寸下多行输入框必须**明显高于最小高**（最小 {box.MinHeight:0.#}，实际 {box.Bounds.Height:0.#}）"
            + " —— 只在模板里给它 `Auto` 行时，它永远只有最小高（用户：「放大模块后就正确了」）");

        window.Close();
        Settle();
    });

    /// <summary>显示区高度**不随行数变化**（内容增长 ⇒ 出内部滚动条，而不是把布局撑长）。</summary>
    [Fact]
    public void 显示区_行数增长不应改变布局() => _ui.Run(() =>
    {
        using var harness = new RealModulesHarness();
        harness.Place("m1", RealModulesHarness.Monitor);
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var window = harness.OpenWindow(module);
        Settle();

        var log = module.Elements.OfType<TextLogElementViewModel>().Single();
        var view = LogSurfaceOf(window);
        var before = view!.Bounds.Height;

        before.ShouldBeGreaterThan(0);

        // 灌 500 行（真实显示路径：模块 → 显示源 → 面板把行写进共享缓冲）
        for (var i = 0; i < 500; i++)
        {
            log.Append($"line-{i:D4}");
        }

        Settle();

        System.IO.File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "hyw-layout-probe.txt"),
            $"[log] before={before:0.##} after={view.Bounds.Height:0.##} panelDesired={PanelOf(window)?.DesiredSize.Height:0.##} panelBounds={PanelOf(window)?.Bounds.Height:0.##} windowH={window.Bounds.Height:0.##}{Environment.NewLine}");

        view.Bounds.Height.ShouldBe(before, "显示区高度由**布局**决定，不随行数增长（增长的应是内部滚动范围）");

        // 内部真的出现滚动（内容高度 > 视口）：分页窗口的正文仍由文本框自己的 `ScrollViewer` 承载
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        scroll.ShouldNotBeNull();
        scroll!.Extent.Height.ShouldBeGreaterThanOrEqualTo(scroll.Viewport.Height, "行多了应能滚动（窗口内容高于视口）");

        window.Close();
        Settle();
    });

    /// <summary>
    /// **"清空显示"要真的清掉且不会回来**（2026-09 用户实测："清空按钮无用，只会使窗口闪一下"）。
    /// </summary>
    /// <remarks>
    /// 旧实现两条错：① **先追加、后清空**（同一帧里把旧内容追加进视图模型，下一帧又从框架缓冲读回来
    /// ⇒ 用户看到"闪一下又全回来了"）；② 每个显示元素各取一次挂起请求 ⇒ 源名不匹配时**把请求丢掉**。
    /// 现在：本帧一次取完 + **清空优先**（同时清掉框架持有的 `DisplayBuffer`）。
    /// </remarks>
    [Fact]
    public void 清空显示_应清掉框架缓冲且后续帧不会又回来() => _ui.Run(() =>
    {
        using var harness = new RealModulesHarness();
        harness.Place("m1", RealModulesHarness.Monitor);
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        _ = harness.OpenWindow(module);
        Settle();

        var log = module.Elements.OfType<TextLogElementViewModel>().ShouldHaveSingleItem();

        for (var i = 0; i < 5; i++)
        {
            log.Append($"line-{i}");
        }

        Settle();
        log.Lines.Count.ShouldBe(5, "先灌 5 行（真实路径：显示缓冲 → 视图模型）");

        // 真实路径：命令 → 模块（**自己的线程**）→ `IModuleContext.Display.Clear` → 挂起表 → UI 刷新节拍执行
        harness.Services.SendCommand("m1", "clear").Succeeded.ShouldBeTrue();

        WaitUntil(
            () =>
            {
                harness.Windows.Refresh();
                Settle();
                return CurrentLog(module).Lines.Count == 0;
            },
            TimeSpan.FromSeconds(2)).ShouldBeTrue("「清空」必须真的把显示内容清掉（用户实测：只会闪一下）");

        // 再走几帧：旧内容**不得**被重新读回来
        for (var i = 0; i < 3; i++)
        {
            harness.Windows.Refresh();
            Settle();
        }

        CurrentLog(module).Lines.Count.ShouldBe(0, "清空后不得在后续帧又冒出来（这正是「闪一下」的成因）");

        harness.Remove("m1");
    });

    /// <summary>把窗口拉高：多行输入与显示区**高度跟着变**（不再只有宽度随动）。</summary>
    [Fact]
    public void 拉高窗口_多行输入与显示区的高度应跟着变() => _ui.Run(() =>
    {
        using var harness = new RealModulesHarness();
        harness.Place("t1", RealModulesHarness.TextSender);
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var window = harness.OpenWindow(module);
        Settle();

        var input = module.Elements.OfType<TextInputElementViewModel>()
            .First(e => e.IsMultiline && e.Source.StartsWith("text", StringComparison.Ordinal));

        var boxBefore = InputBoxOf(window);
        var heightBefore = boxBefore!.Bounds.Height;

        heightBefore.ShouldBeGreaterThan(0);

        // 把**窗口**拉高 120 DIP（生产里由窗口管理器改窗口；只改视图模型的 Height 不会动窗口）
        window.Height += 120;
        Settle();

        var panel2 = PanelOf(window);
        var items = window.GetVisualDescendants().OfType<ItemsControl>().First();
        var scroll2 = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        var box2 = InputBoxOf(window);
        System.IO.File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "hyw-layout-probe.txt"),
            $"[input] before={heightBefore:0.##} after={box2?.Bounds.Height:0.##} boxParent={(box2?.Parent as Control)?.Bounds.Height:0.##}"
            + $" windowH={window.Bounds.Height:0.##} viewport={scroll2.Viewport.Height:0.##} itemsMinH={items.MinHeight:0.##}"
            + $" panelBounds={panel2?.Bounds.Height:0.##} panelDesired={panel2?.DesiredSize.Height:0.##}{Environment.NewLine}");

        var boxAfter = InputBoxOf(window);
        boxAfter!.Bounds.Height.ShouldBeGreaterThan(
            heightBefore,
            $"{input.Source}：窗口拉高后多行输入框应跟着变高（用户反馈：此前只有宽度随动）");

        window.Close();
        Settle();
    });

    /// <summary>
    /// **字体设置项真的作用到资源 token**（2026-09 用户裁定："字体在设置里显式可调…默认中文宋体、英文 Times New Roman"）。
    /// </summary>
    /// <remarks>
    /// 断言的是**资源键**（`FontUi` / `FontData`）—— 视图一律用 `DynamicResource` 取它，
    /// 所以"token 被换掉" ⟺ "界面文字/文本框字体跟着换"（无需逐个控件断言）。
    /// </remarks>
    [Fact]
    public void 字体设置_应作用到资源token() => _ui.Run(() =>
    {
        var settings = new ApplicationSettings
        {
            UiFontFamily = "Times New Roman, 宋体",
            DataFontFamily = "Consolas",
        };

        HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(settings);
        Settle();

        Avalonia.Application.Current.ShouldNotBeNull();

        Avalonia.Application.Current!.TryFindResource("FontData", out var data).ShouldBeTrue("FontData token 应存在");
        (data as Avalonia.Media.FontFamily)?.Name.ShouldContain("Consolas", customMessage: "数据字体应被设置项换掉");

        Avalonia.Application.Current.TryFindResource("FontUi", out var ui).ShouldBeTrue("FontUi token 应存在");
        (ui as Avalonia.Media.FontFamily)?.Name.ShouldContain("Times New Roman", customMessage: "界面字体应被设置项换掉");

        // 还原默认（避免影响同一无头会话里的其他用例）
        HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(new ApplicationSettings());
        Settle();
    });

    /// <summary>
    /// **只改字体（颜色全部"跟随主题"）时字体也必须生效**（2026-09 用户实测缺陷："字体修改不生效"）。
    /// </summary>
    /// <remarks>
    /// 成因：`ThemeApplier.ApplyCore` 原先在"颜色覆盖为空"时**直接 return**，而字体应用写在它**之后**
    /// ⇒ 只要用户没设主题色/背景色/方向色（例如把主题色选成"跟随主题"），**字体改动被整段跳过**。
    /// 本用例专门造这个组合（颜色全空 + 只给字体）⇒ **去掉修复即红**。
    /// </remarks>
    [Fact]
    public void 字体设置_颜色全跟随主题时字体也必须生效() => _ui.Run(() =>
    {
        var settings = new ApplicationSettings
        {
            AccentColor = string.Empty,
            BackgroundMode = "Theme",
            BackgroundColor = string.Empty,
            DirectionSendColor = string.Empty,
            DirectionReceiveColor = string.Empty,
            UiFontFamily = "Georgia",
            DataFontFamily = "Consolas",
        };

        HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(settings);
        Settle();

        Avalonia.Application.Current.ShouldNotBeNull();
        Avalonia.Application.Current!.TryFindResource("FontData", out var data).ShouldBeTrue("FontData token 应存在");
        (data as Avalonia.Media.FontFamily)?.Name.ShouldContain(
            "Consolas", customMessage: "颜色没有覆盖时，字体改动**同样**必须被应用（用户实测：字体修改不生效）");

        // 还原默认
        HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(new ApplicationSettings());
        Settle();
    });

    /// <summary>当前窗口里的**显示区视图模型**（元素表可能被重建 ⇒ 每次重新取，不缓存实例）。</summary>
    private static TextLogElementViewModel CurrentLog(ModuleViewViewModel module) =>
        module.Elements.OfType<TextLogElementViewModel>().ShouldHaveSingleItem();

    /// <summary>轮询等待条件成立（模块活动体在**自己的线程**上跑，需要给它时间）。</summary>
    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>取窗口里的**内容排布面板**。</summary>
    private static ModuleElementPanel? PanelOf(ModuleWindow window) =>
        window.GetVisualDescendants().OfType<ModuleElementPanel>().FirstOrDefault();

    /// <summary>某控件**相对于窗口**的顶边（无该控件 ⇒ 无穷大，比较自然通过）。</summary>
    /// <remarks>逐级累加父级 `Bounds.Y`（`TranslatePoint` 在本测试项目的引用面里不可用 ⇒ 自己走一遍）。</remarks>
    private static double TopOf(Avalonia.Visual? control, ModuleWindow window)
    {
        if (control is null)
        {
            return double.PositiveInfinity;
        }

        var y = 0.0;

        for (Avalonia.Visual? current = control; current is not null && !ReferenceEquals(current, window); current = current.GetVisualParent())
        {
            y += current.Bounds.Y;
        }

        return y;
    }

    /// <summary>某控件**相对于窗口**的底边。</summary>
    private static double BottomOf(Avalonia.Visual? control, ModuleWindow window) =>
        control is null ? double.NegativeInfinity : TopOf(control, window) + control.Bounds.Height;

    /// <summary>
    /// 取窗口里的**显示区文本面**（A5）—— 外壳视图 `TextLogView` 已删，
    /// A5 那一块就是 `DataTemplate` 里那个开了分页窗口的 `TextSurface`。
    /// </summary>
    private static TextSurface? LogSurfaceOf(ModuleWindow window) =>
        window.GetVisualDescendants().OfType<TextSurface>().FirstOrDefault(s => s.ShowScrollBar);

    /// <summary>取窗口里第一个文本框（多行输入用例只放一个多行输入 + 一个单行间隔输入 ⇒ 取多行那个）。</summary>
    private static TextBox? InputBoxOf(ModuleWindow window) =>
        window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(b => b.AcceptsReturn);

    /// <summary>装入**真实模块包**的小夹具（与集成用例同一路径：扫描 → 加入 → 开窗）。</summary>
    private sealed class RealModulesHarness : IDisposable
    {
        public const string TextSender = "hyw/text-sender/1.0.0";
        public const string HexSender = "hyw/hex-sender/1.0.0";
        public const string Monitor = "hyw/data-monitor/1.0.0";
        public const string Converter = "hyw/hex-string-converter/1.0.0";

        /// <summary>四个模块（实例 id → 模块包 id）。</summary>
        public static readonly (string InstanceId, string ModuleId)[] All =
        [
            ("t1", TextSender),
            ("h1", HexSender),
            ("m1", Monitor),
            ("c1", Converter),
        ];

        public RealModulesHarness()
        {
            var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");
            Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

            ViewState = new ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
            _ = Services.ScanModuleSources([staged]);

            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);
        }

        public ViewStateStore ViewState { get; }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        /// <summary>
        /// 加入一个模块（**不写布局记录**）—— 这样窗口尺寸取**模块声明的首选尺寸**
        /// （写了布局就会被 320×220 盖掉，测的就不是"模块默认大小"了 —— 第一次就踩了这个）。
        /// </summary>
        public void Place(string instanceId, string moduleId)
        {
            Services.AddModule(instanceId, moduleId).Succeeded.ShouldBeTrue();
            Windows.Refresh();
        }

        /// <summary>移除（每个用例里连续验多个模块时保持现场干净）。</summary>
        public void Remove(string instanceId)
        {
            _ = Services.RemoveModule(instanceId);
            Windows.Refresh();
        }

        /// <summary>
        /// 开窗（与生产代码同一赋值顺序）+ **按模块声明尺寸设窗口大小**。
        /// </summary>
        /// <remarks>
        /// 生产里这步由 `ModuleWindowManager.ApplyGeometry` 做（**本轮刚修**：此前"没有布局记录就直接返回"
        /// ⇒ 模块声明的 `PreferredWidth/Height` 从未生效，窗口一律 320×220 —— 那正是用户说的
        /// "模块默认大小不对，信息被遮蔽和覆盖"）。无头环境缩放 = 1 ⇒ 模型（物理像素）与 DIP 数值相同。
        /// </remarks>
        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow { DataContext = module, Controller = Windows };
            window.Width = Math.Max(ModuleViewViewModel.MinWidth, module.Width);
            window.Height = Math.Max(ModuleViewViewModel.MinHeight, module.Height);
            window.Show();
            Settle();
            return window;
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
