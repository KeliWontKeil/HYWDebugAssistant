using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Hosting;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **真实业务模块的声明面在真尺寸下的排布体检**。
/// </summary>
/// <remarks>
/// <para>
/// 它问的是一个**夹具快照问不出来的问题**：模块自己声明的窗口大小，够不够放下它自己声明的树。
/// 不够时内容会被排到视口之外或被裁掉 —— 屏幕上表现为"控件被遮挡/不见了"，而**没有任何一条报错**。
/// </para>
/// <para>
/// 判据只看「人眼可见的结果」：声明的每一种 kind 都得真的长出一个控件（切换每一页分别看），
/// 且内容不得溢出视口。诊断产物写在临时目录下（每份模块一个子目录），便于一次看清差多少。
/// </para>
/// </remarks>
[Collection("visual-snapshots")]
public class BusinessSurfaceSnapshotTests
{
    /// <summary>本类快照的独立输出目录。</summary>
    private static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-business-surface");

    [Fact]
    public async Task 数据监视器_在它自己声明的窗口里_内容必须放得下()
    {
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "DataMonitor");
        await Render(package, "data-monitor");
    }

    [Fact]
    public async Task 文本发送器_在它自己声明的窗口里_内容必须放得下()
    {
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "TextSender");
        await Render(package, "text-sender");
    }

    [Fact]
    public async Task 十六进制发送器_在它自己声明的窗口里_内容必须放得下()
    {
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "HexSender");
        await Render(package, "hex-sender");
    }

    [Fact]
    public async Task 数据转换器_在它自己声明的窗口里_内容必须放得下()
    {
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "HexStringConverter");
        await Render(package, "hex-string-converter");
    }

    /// <summary>高度链要过的三档窗口高（自适应 ⇒ 三档都要成立）。</summary>
    private static readonly double[] HeightProbeWindows = [740.0, 760.0, 780.0];

    /// <summary>
    /// **四个模块的高度链都要与声明一致：页签条高、两侧净高。**
    /// </summary>
    /// <remarks>
    /// 一条用例跑四个模块：它们共用同一条声明面与同一套排布，**逐模块各断言一遍**比"只测转换器"更能
    /// 挡住"只有某个模块写错"的情形。窗口高只对转换器取三档（那是自适应的敏感处），其余模块取默认高。
    /// </remarks>
    [Fact]
    public async Task 四个模块_高度链必须与声明一致()
    {
        var modules = new[]
        {
            ("DataMonitor", "data-monitor", false),
            ("TextSender", "text-sender", false),
            ("HexSender", "hex-sender", false),
            ("HexStringConverter", "hex-string-converter", true),
        };

        var report = new List<string>();
        var wrong = new List<string>();

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        await session.Dispatch(
            () =>
            {
                using var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
                var services = new RuntimeServices(runtime);
                var state = new ViewStateStore();
                var schema = LoadSchema();

                foreach (var (folder, name, probeAllHeights) in modules)
                {
                    var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", folder);

                    ViewDeclarationReader.TryRead(package, schema, out var document, out _, out var readError)
                        .ShouldBeTrue(readError);

                    // 转换器取三档窗口高（自适应最敏感）；其余模块取它自己声明的那一档
                    var heights = probeAllHeights ? HeightProbeWindows : [document!.Frame.Window.Height];

                    foreach (var windowHeight in heights)
                    {
                        report.Add($"==== {folder}  窗口高={windowHeight:0}");
                        MeasureModuleHeights(package, name, services, state, windowHeight, report, wrong);
                    }
                }
            },
            CancellationToken.None);

        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllLines(Path.Combine(OutputDirectory, "height-chain.txt"), report);

        wrong.ShouldBeEmpty(
            "高度链必须与声明一致：\n  " + string.Join("\n  ", wrong)
            + "\n（完整链条见 height-chain.txt）");
    }

    /// <summary>加载声明面 schema（多处要用，取一处口径）。</summary>
    /// <returns>schema。</returns>
    private static ViewSchema LoadSchema()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        return schema!;
    }

    [Fact]
    public async Task 消息_应当由label显示出来_并且比没消息时多出那些像素()
    {
        // **这条是给"消息不是控件能力"这条设计出像素的判据**：
        // 模块往一条源写文本、再给某个元素设色 ⇒ 界面上**真的**多出那些字与那个颜色。
        // 不靠"测过就算"：比对的是**同一扇窗、同一份声明**在"有消息 / 没消息"两种状态下的位图。
        //
        // **两种状态共用一条 headless 会话**：一条用例里连着起两条会话会卡住
        // （`HeadlessUnitTestSession` 的启动/释放各只允许一次干净交接，实测第二条永远不返回）。
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "DataMonitor");

        ImageStats blank = default;
        ImageStats shown = default;

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        await session.Dispatch(
            () =>
            {
                blank = RenderMessageState(package, "message-blank", message: null, alert: false);
                shown = RenderMessageState(package, "message-shown", message: "已保存：savetext1.txt（2 行）", alert: true);
            },
            CancellationToken.None);

        shown.DistinctColors.ShouldBeGreaterThan(
            blank.DistinctColors, "有消息时图上的颜色种类必须多于没消息时（消息那行真的画出来了）");
        shown.InkPixels.ShouldBeGreaterThan(
            blank.InkPixels, "有消息时非背景像素必须多于没消息时（那句话真的有墨）");
    }

    [Fact]
    public async Task 文本发送器_输入面装满长文本后_仍然不许比自己的框宽()
    {
        await AssertTextNeverWidensItsFrame(
            Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "TextSender"),
            "text-sender-wrap");
    }

    /// <summary>
    /// **长文本不许把文本面撑宽**（也不许把它撑高）。
    /// </summary>
    /// <param name="package">模块包目录。</param>
    /// <param name="name">产物短名。</param>
    /// <remarks>
    /// <para>
    /// 这条守的是"宽度不由内容决定"这条结构纪律。`TextBox` 的尺寸规则是：**宽度受约束**时报约束宽、
    /// 文字在框内折行；**宽度不受约束**时报**整行文本的宽度**。整条排布链上任何一处把宽度传成无穷
    /// （滚动容器排布前那一遍、排布的摸底那一遍），它就会按内容报宽 ⇒ **文字冲出框、被外层裁掉**，
    /// 而屏幕上不报任何错：**被裁的是字，不是框**，所以从框的尺寸上什么也看不出来。
    /// </para>
    /// <para>
    /// 所以判据取"**文本面的实得宽 ≤ 它所在页的面板宽**"，而不是某个我挑出来的像素值 ——
    /// 结论与窗口尺寸、字体、主题都无关，只与"宽度该由谁决定"有关。
    /// </para>
    /// </remarks>
    private static async Task AssertTextNeverWidensItsFrame(string package, string name)
    {
        var dump = new List<string>();

        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        ViewDeclarationReader.TryRead(package, schema!, out var document, out _, out var readError)
            .ShouldBeTrue(readError);

        var root = document!.Views[0].Root;
        var window = document.Frame.Window;
        var widened = new List<string>();
        var unfolded = new List<string>();

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        await session.Dispatch(
            () =>
            {
                var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
                var services = new RuntimeServices(runtime);
                var state = new ViewStateStore();
                var host = new ModuleSurfaceHost(name, root, services, services, state);

                var windowControl = new Window
                {
                    Width = window.Width,
                    Height = window.Height,
                    Content = new Border { Child = host.View },
                };

                windowControl.Show();
                Pump();
                Pump();

                // 喂进"一整行很长的文本 + 几个真的换行"：**只写进文本框**，也就是用户粘贴/键入的路径。
                var editors = windowControl.GetVisualDescendants().OfType<TextBox>().ToList();
                dump.Add($"可视树里的文本框数量={editors.Count}");

                foreach (var editor in editors)
                {
                    if (editor.TextWrapping != Avalonia.Media.TextWrapping.Wrap)
                    {
                        continue;
                    }

                    editor.Text = new string('字', 200) + "\n" + new string('a', 200);

                    for (var i = 0; i < 3; i++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }

                    var surface = editor.GetVisualAncestors().OfType<TextSurface>().FirstOrDefault();
                    var frame = editor.GetVisualAncestors().OfType<ModuleSurfacePanel>().FirstOrDefault();

                    if (surface is null || frame is null)
                    {
                        continue;
                    }

                    var box = surface.Bounds.Width;
                    var limit = frame.Bounds.Width;

                    // **折行真的发生了吗**：折行的直接后果是"文本框自己量出来的宽就是框宽"，
                    // 而**不折行时它会报整行文本的宽**（实测 200 字符 ⇒ 2212，而框只有 360）。
                    // 这条判据比"内层滚动范围 > 视口"稳：没得可滚时范围等于视口是正常的。
                    var wanted = editor.DesiredSize.Width;

                    dump.Add(
                        $"面板宽={limit:0} 文本面实得={box:0}x{surface.Bounds.Height:0} "
                        + $"文本框实得={editor.Bounds.Width:0}x{editor.Bounds.Height:0} "
                        + $"文本框期望宽={wanted:0}");

                    if (wanted > editor.Bounds.Width + 1)
                    {
                        unfolded.Add(
                            $"文本框期望宽={wanted:0} > 它实得的宽={editor.Bounds.Width:0} "
                            + "⇒ 它是按整行文本报宽的，长文本没有折行（文字会冲出框被裁）");
                    }

                    if (box > limit + 0.5)
                    {
                        widened.Add($"文本面实得宽={box:0} > 它所在页的面板宽={limit:0}");
                    }
                }
            },
            CancellationToken.None);

        File.WriteAllLines(Path.Combine(OutputDirectory, $"{name}.txt"), dump);

        widened.ShouldBeEmpty(
            "长文本把文本面撑宽了 ⇒ 宽度是由**内容**决定的，文字会冲出框被裁（看 "
            + $"{Path.Combine(OutputDirectory, name)}.txt）");

        unfolded.ShouldBeEmpty(
            "长文本没有折行 ⇒ `wrap` 这件事没生效（声明写了也没用），看 "
            + $"{Path.Combine(OutputDirectory, name)}.txt");
    }

    /// <summary>
    /// 把一份真实模块的声明按它声明的窗口尺寸渲染出来，并体检"放不放得下"。
    /// </summary>
    /// <param name="package">模块包目录（含 <c>view.json</c>）。</param>
    /// <param name="name">产物短名。</param>
    private static async Task Render(string package, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var output = Path.Combine(OutputDirectory, name);
        Directory.CreateDirectory(output);
        var file = Path.Combine(output, $"{name}.png");

        // 这棵声明树自己先得读得动 —— 读不动时宿主整份忽略，界面静默空白。
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();
        ViewDeclarationReader.TryRead(package, schema, out var document, out var warnings, out var readError)
            .ShouldBeTrue($"{readError}（警告：{string.Join("；", warnings)}）");
        document.ShouldNotBeNull();

        var definition = document.Views[0];
        var window = document.Frame.Window;
        var root = definition.Root;

        // 窗口尺寸的权威口径就是这一条：声明写在 `frame.window`，读取器已按 schema 补齐缺省、
        // 并已按框架地板夹过下限 ⇒ 渲染侧不再自带缺省、也不再夹下限。
        var width = window.Width;
        var height = window.Height;

        // 声明的 kind 里去掉两类**本身不生成控件**的节点：
        //   · 根容器：模块根**就是**表面面板本身（面板直接采用根的排布声明），不另生成控件；
        //   · `tabs` 与它的直接子节点：页签把每一页的子项搬进页内容，页节点自己只当载体
        //     （`CreateTabs` 取的是 `page.Items`、标题取 `page.Node.Title`），所以"页节点没有控件"是设计如此。
        var kinds = KindsOf(root).Distinct(StringComparer.Ordinal).ToList();
        var declaredKinds = kinds
            .Where(k => !string.Equals(k, root.Kind, StringComparison.Ordinal))
            .Where(k => !string.Equals(k, "tabs", StringComparison.Ordinal))
            .Where(k => !root.Children
                .Where(child => string.Equals(child.Kind, "tabs", StringComparison.Ordinal))
                .SelectMany(child => child.Children)
                .Any(page => string.Equals(page.Kind, k, StringComparison.Ordinal)))
            .ToList();

        var missing = new List<string>();
        var squeezed = new List<string>();

        using (var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint)))
        {
            await session.Dispatch(
                () =>
                {
                    var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
                    var services = new RuntimeServices(runtime);
                    var state = new ViewStateStore();
                    var host = new ModuleSurfaceHost(name, root, services, services, state);

                    var windowControl = new Window
                    {
                        Width = width,
                        Height = height,
                        Content = new Border { Child = host.View },
                    };

                    windowControl.Show();
                    Pump();
                    Pump();

                    var surface = windowControl.GetVisualDescendants().OfType<ModuleSurfacePanel>().FirstOrDefault();
                    var tabs = windowControl.GetVisualDescendants().OfType<SurfaceTabsControl>().FirstOrDefault();

                    // 页签控件里的内层面板才是"页内容"那一个（外层面板装的是页签控件自己）。
                    // 认它靠"在滚动条里、且带着页内容"这条结构事实：页里任何内嵌容器都有自己的面板，
                    // 按出现顺序取最后/第一个都会取错人（页 5 有个折叠区，它自己就带一个面板）。
                    // **没有页签时表面的面板就是全部内容**（DataMonitor 就是这样）。
                    ModuleSurfacePanel? PagePanel() => tabs is null
                        ? surface
                        : tabs.GetVisualDescendants()
                            .OfType<ScrollViewer>()
                            .Select(scroll => scroll.GetVisualDescendants().OfType<ModuleSurfacePanel>().FirstOrDefault())
                            .FirstOrDefault(panel => panel is not null);

                    // 页签控件里的内层面板才是"页内容"那一个（外层面板装的是页签控件自己）。
                    var page = PagePanel();

                    // ---- 逐页摊开：声明的每一种 kind 都得在某页里真的长出来，且不得溢出该页 ----
                    var pages = tabs?.PageCount ?? 1;
                    var present = new List<string>();

                    for (var index = 0; index < pages; index++)
                    {
                        tabs?.Select(index);
                        Pump();
                        Pump();

                        var current = PagePanel() ?? page;

                        if (current is null)
                        {
                            continue;
                        }

                        var kindsHere = KindsIn(current);
                        present.AddRange(kindsHere);

                        // **页签之外的兄弟也要判"声明尺寸是否兑现"**（动作行、结果面都挂在这一层）：
                        // 上面的检查只遍历**页面板的直接子项**，页签之外的兄弟一个都不判 ——
                        // 实测漏掉过一次真缺陷（`fileSelector` 声明 96×30、内部却是一个竖排 StackPanel，
                        // 路径面被压到框外，而"放得下"报告是 0 溢出）。
                        // 判据 = **实得尺寸不得小于声明像素**：声明是下限，溢出由外层滚动/裁剪负责。
                        if (index == 0 && surface is not null)
                        {
                            foreach (var element in surface.GetVisualDescendants()
                                .OfType<SurfaceElementControl>())
                            {
                                var control = (Control)element;

                                if (OverflowingDescendant(control) is { } escape)
                                {
                                    squeezed.Add(
                                        $"「{(element.Node.Title.Length > 0 ? element.Node.Title : element.Node.Kind)}」"
                                        + $"的框只有 {control.Bounds.Width:0}×{control.Bounds.Height:0}，"
                                        + $"里面却有东西伸到框外：{escape}（会被裁掉或被压在相邻元素上）");
                                }
                            }
                        }

                        if (index == 0 && surface is not null)
                        {
                            // **上下堆叠档（`above` / `below`）的见证**：说明必须真的在主体的上/下方。
                            foreach (var element in surface.GetVisualDescendants()
                                .OfType<SurfaceElementControl>()
                                .Where(e => e.Node.TextPlacement is "above" or "below")
                                .Where(e => e.Node.Title.Length > 0))
                            {
                                var body = element.GetVisualDescendants()
                                    .FirstOrDefault(v => v is CheckBox or ToggleSwitch or ComboBox or TextSurface)
                                    as Control;
                                var label = element.GetVisualDescendants()
                                    .OfType<TextBlock>()
                                    .FirstOrDefault(block => block.Text?.Length > 0);

                                if (body is null || label is null)
                                {
                                    continue;
                                }

                                var bodyTop = body.TranslatePoint(default, element);
                                var labelTop = label.TranslatePoint(default, element);

                                if (bodyTop is not { } bodyPoint || labelTop is not { } labelPoint)
                                {
                                    continue;
                                }

                                var above = labelPoint.Y < bodyPoint.Y;
                                var expectedAbove = string.Equals(
                                    element.Node.TextPlacement, "above", StringComparison.Ordinal);

                                if (above != expectedAbove)
                                {
                                    squeezed.Add(
                                        $"「{element.Node.Title}」声明 textPlacement={element.Node.TextPlacement}，"
                                        + $"说明文字却落在主体{(above ? "上" : "下")}方");
                                }
                            }
                        }

                        // 这条与"溢出"是两件事 —— 溢出是内容排在视口外，这个是"控件根本没长到声明的高度"
                        // （屏幕上表现为一条被压扁的细线、或几个控件叠在一起），两者都不报错。
                        // **在整块表面上找**，不只在页内容面板里找：页签之外的兄弟（按钮、结果面）
                        // 挂在表面上那一层，按页找会把它们全报成"找不到"。
                        foreach (var (kind, title, wanted) in DeclaredPixelHeights(PageDeclaration(root, index)))
                        {
                            var visual = surface is null ? null : FindVisual(surface, kind, title);

                            if (visual is null)
                            {
                                squeezed.Add($"页{index} {kind}「{title}」声明高={wanted:0} 但面上找不到这个控件");
                                continue;
                            }

                            if (Math.Abs(visual.Bounds.Height - wanted) > 0.5)
                            {
                                squeezed.Add(
                                    $"页{index} {kind}「{title}」声明高={wanted:0} 实得={visual.Bounds.Height:0}");
                            }
                        }

                        // 每页各存一张：页与页之间"哪个控件画出来了"差别很大，
                        // 只留最后一张的话，前几页的问题（整块 0 高、被排到视口外）在图上根本看不到。
                        Render(windowControl, index == 0 ? file : PageFile(output, name, index));
                    }

                    present = present.Distinct(StringComparer.Ordinal).ToList();

                    // 声明的 kind 未必都住在页内容里：页签之外的兄弟（`commandButton`/`toggleSwitch`/
                    // `numeric`……）挂在页签控件自己那一层。所以"面上长出来的 kind"要把两处并起来数，
                    // 否则会把"真的画出来了、只是不在页里"误报成缺失。
                    present.AddRange(KindsIn(surface));

                    present = present.Distinct(StringComparer.Ordinal).ToList();
                    foreach (var kind in declaredKinds)
                    {
                        if (!present.Contains(kind, StringComparer.Ordinal))
                        {
                            missing.Add(kind);
                        }
                    }

                },
                CancellationToken.None);
        }

        missing.ShouldBeEmpty(
            $"声明里写了但六页都没长出来的 kind：{string.Join(" / ", missing)}");
        squeezed.ShouldBeEmpty(
            $"声明了固定像素高的元素没长到声明的高度（会被压扁/叠在一起）：{string.Join("；", squeezed)}");
    }

    /// <summary>仓库根目录（不能从测试程序集位置硬算相对层级，所以靠模块目录的存在与否往上找）。</summary>
    /// <returns>仓库根绝对路径。</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Modules")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("从测试程序集位置往上找不到仓库根（缺 src/Modules）");
        return directory!.FullName;
    }

    /// <summary>递归取一棵声明树里出现的全部 kind（含根）。</summary>
    /// <param name="node">声明节点。</param>
    /// <returns>kind 序列。</returns>
    private static IEnumerable<string> KindsOf(ViewNode node)
    {
        yield return node.Kind;

        foreach (var child in node.Children)
        {
            foreach (var kind in KindsOf(child))
            {
                yield return kind;
            }
        }
    }

    /// <summary>第 N 页的产物路径（第 0 页仍用主文件名，便于直接看）。</summary>
    /// <param name="output">模块的产物目录。</param>
    /// <param name="name">产物短名。</param>
    /// <param name="index">页号。</param>
    /// <returns>PNG 路径。</returns>
    private static string PageFile(string output, string name, int index) =>
        Path.Combine(output, $"{name}-page{index}.png");

    /// <summary>
    /// 取**某一页**的声明子树（页签把每一页的子项搬进页内容，所以第 N 页 = 第 N 个子容器）。
    /// </summary>
    /// <remarks>
    /// 没有页签时整棵树就是"这一页"。
    /// </remarks>
    /// <param name="root">声明根。</param>
    /// <param name="index">页号。</param>
    /// <returns>该页的声明子树（找不到 ⇒ 根自己）。</returns>
    private static ViewNode PageDeclaration(ViewNode root, int index)
    {
        var tabs = root.Children.FirstOrDefault(child => string.Equals(child.Kind, "tabs", StringComparison.Ordinal));

        return tabs is not null && index < tabs.Children.Count ? tabs.Children[index] : root;
    }

    /// <summary>
    /// 取声明树里**写了固定像素高**的节点（kind + 标题 + 声明值），用来核对"声明有没有兑现"。
    /// </summary>
    /// <param name="node">声明节点（从根开始）。</param>
    /// <returns>（kind，标题，声明高度）序列。</returns>
    private static List<(string Kind, string Title, double Height)> DeclaredPixelHeights(ViewNode node)
    {
        var found = new List<(string, string, double)>();

        if (node.Height.Pixels is { } pixels)
        {
            found.Add((node.Kind, node.Title, pixels));
        }

        foreach (var child in node.Children)
        {
            found.AddRange(DeclaredPixelHeights(child));
        }

        return found;
    }

    /// <summary>在可视树里按 kind + 标题找那个控件（找不到 ⇒ <see langword="null"/>）。</summary>
    /// <param name="root">可视树根。</param>
    /// <param name="kind">声明 kind。</param>
    /// <param name="title">声明标题。</param>
    /// <returns>控件；找不到 ⇒ <see langword="null"/>。</returns>
    private static Control? FindVisual(Visual root, string kind, string title) =>
        root.GetVisualDescendants().OfType<Control>().FirstOrDefault(visual => visual switch
        {
            SurfaceElementControl element =>
                string.Equals(element.Node.Kind, kind, StringComparison.Ordinal)
                && string.Equals(element.Node.Title, title, StringComparison.Ordinal),
            SurfaceContainerControl container =>
                string.Equals(container.Node.Kind, kind, StringComparison.Ordinal)
                && string.Equals(container.Node.Title, title, StringComparison.Ordinal),
            _ => false,
        });

    /// <summary>取一棵可视树里出现的全部 kind（表面控件与容器控件各报各自声明节点的 kind）。</summary>
    /// <param name="root">可视树根（可空）。</param>
    /// <returns>去重后的 kind 序列。</returns>
    private static List<string> KindsIn(Visual? root) => root is null
        ? []
        : root.GetVisualDescendants()
            .Select(v => v switch
            {
                SurfaceElementControl element => element.Node.Kind,
                SurfaceContainerControl container => container.Node.Kind,
                _ => null,
            })
            .Where(kind => kind is not null)
            .Select(kind => kind!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>把待处理的界面任务跑干净（布局与渲染都靠它推进）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// **`panel` 里声明的绝对坐标必须原样生效**（不许被"整组归一化"平移掉）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条守的是"声明即所得"：作者写 <c>x: 50</c>，元素就该离面板左缘 50。
    /// 判据要盯住**最小的那个 x 不为 0** 的情形：把 <b>设计框原点</b>（= 孩子坐标包围盒的左上角）
    /// 从排布偏移里减掉，等价于 <b>把最小的那个 x 抵消成 0</b> —— <c>x: 50</c> 会排到 0、
    /// <c>x: 120</c> 会排到 70：间距照旧、整组左移，界面上看起来像"绝对坐标被强制左对齐"，
    /// 很难认出是平移。
    /// </para>
    /// <para>
    /// 最小的 x 为 0 时，这个抵消是个**恒等操作**，缺陷会被完全掩盖 ⇒ 模块自己的声明抓不到它。
    /// </para>
    /// <para>
    /// 判据取"实测排布矩形"（不是位图）：它问的是"声明 → 几何"这一步，与"画得像不像"无关，
    /// 所以不必依赖像素（headless 下位图还会滞后一帧）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task panel_里声明的绝对坐标不许被归一化()
    {
        // 三个元素刻意声明的 x 都不是 0，且最小的那个也不是 0（否则归一化=恒等，抓不到）
        const string viewJson = """
        {
          "version": 1,
          "frame": { "ports": [] },
          "sources": [
            { "name": "a", "kind": "Value", "valueType": "U64" },
            { "name": "b", "kind": "Value", "valueType": "U64" },
            { "name": "c", "kind": "Value", "valueType": "U64" }
          ],
          "views": [
            {
              "root": {
                "kind": "container",
                "layout": "flow",
                "overflow": "clip",
                "padding": 0,
                "spacing": 0,
                "width": { "fill": true },
                "height": { "adaptive": true },
                "children": [
                  {
                    "kind": "container",
                    "layout": "panel",
                    "overflow": "clip",
                    "anchor": "left",
                    "valign": "top",
                    "width": { "fill": true },
                    "height": { "pixels": 120 },
                    "children": [
                      { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "a", "width": { "pixels": 40 }, "height": { "pixels": 18 }, "x": 50,  "y": 0 },
                      { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "b", "width": { "pixels": 40 }, "height": { "pixels": 18 }, "x": 200, "y": 40 },
                      { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "c", "width": { "pixels": 40 }, "height": { "pixels": 18 }, "x": 120, "y": 80 }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);

        var directory = Path.Combine(Path.GetTempPath(), $"hyw-panel-absolute-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "view.json"), viewJson);

            ViewDeclarationReader.TryRead(directory, schema!, out var document, out _, out var readError)
                .ShouldBeTrue(readError);

            using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

            var placed = new List<(double X, double Y)>();

            await session.Dispatch(
                () =>
                {
                    using var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
                    var services = new RuntimeServices(runtime);
                    var state = new ViewStateStore();
                    var host = new ModuleSurfaceHost("absolute-probe", document!.Views[0].Root, services, services, state);

                    var window = new Window
                    {
                        Width = 400,
                        Height = 300,
                        Content = new Border { Child = host.View },
                    };

                    window.Show();
                    Pump();
                    Pump();

                    placed.AddRange(
                        window.GetVisualDescendants()
                            .OfType<SurfaceElementControl>()
                            .Select(element => (element.Bounds.X, element.Bounds.Y)));
                },
                CancellationToken.None);

            placed.Count.ShouldBe(3, "三个 numeric 都应被排出来");
            placed.ShouldContain((50d, 0d), "声明 x=50 的元素必须排在离面板左缘 50 处");
            placed.ShouldContain((200d, 40d), "声明 x=200 的元素必须排在 200 处");
            placed.ShouldContain((120d, 80d), "声明 x=120 的元素必须排在 120 处");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// **探针**：圆钮的位移到底会不会"插值"（平滑滑过去），还是直接跳到终点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么要它：颜色过渡生效而位移不生效时，根因只可能是"这条过渡没被认"——
    /// 而**渲染引擎的语义必须实测**（注释与类型系统都不会告诉你哪条属性可动画）。
    /// 做法：给圆钮的过渡时长拉长到 3 秒，改一次状态，**在 10% 的位置采一次样**：
    /// 值落在两个端点之间 ⇒ 真的在插值；直接等于终点 ⇒ 这条过渡没生效。
    /// </para>
    /// <para>
    /// <b>与空绘制会话不可混用</b>：这里要给窗口画图才推进动画节拍（`ForceRenderTimerTick`），
    /// 所以它自己起一条会话，和别的新增会话不共用。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task 滑块圆钮的位移过渡必须真的插值()
    {
        var package = Path.Combine(RepositoryRoot(), "src", "Modules", "Business", "TextSender");
        var report = new List<string>();

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        await session.Dispatch(
            () =>
            {
                using var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
                var services = new RuntimeServices(runtime);
                var state = new ViewStateStore();

                ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
                ViewDeclarationReader.TryRead(package, schema!, out var document, out _, out var readError)
                    .ShouldBeTrue(readError);

                var host = new ModuleSurfaceHost("probe", document!.Views[0].Root, services, services, state);

                var windowControl = new Window
                {
                    Width = document.Frame.Window.Width,
                    Height = document.Frame.Window.Height,
                    Content = new Border { Child = host.View },
                };

                windowControl.Show();
                Pump();
                Pump();
                host.Refresh();
                Pump();

                var track = windowControl.GetVisualDescendants()
                    .OfType<Border>()
                    .First(border => border.Classes.Contains(SurfaceClasses.ToggleTrack));

                // **圆钮要从窗口找，不能从轨道找**：它与轨道是**同一格里的兄弟**
                // （圆钮的坐标相对共享那一格算 —— 见 `SurfaceToggleSliderControl` 的构造），
                // 于是"轨道里找圆钮"永远找不到（实测 `Sequence contains no matching element`）。
                var knob = windowControl.GetVisualDescendants()
                    .OfType<Border>()
                    .First(border => border.Classes.Contains(SurfaceClasses.ToggleKnob));

                report.Add($"过渡条数={track.Transitions?.Count ?? 0} 圆钮过渡条数={knob.Transitions?.Count ?? 0}");
                report.Add($"起始 Canvas.Left={Canvas.GetLeft(knob):0.##}");
                report.Add($"时长资源={windowControl.FindResource("SurfaceToggleSlideDuration")}");

                // **直接拨一次控件**（不造指针事件）：要验的是"这条位移过渡认不认"，
                // 与"谁来触发"无关；用指针事件只会把这条探针变成对输入层的测试。
                var slider = knob.GetVisualAncestors()
                    .OfType<SurfaceToggleSliderControl>()
                    .First();

                slider.ToggleFromUserForProbe();

                // 推进一小步（约 10% 时长）再采样
                Thread.Sleep(300);
                Pump();

                var mid = Canvas.GetLeft(knob);
                report.Add($"推进约 10% 后 X={mid:0.##}（起点 0、终点 16）");

                windowControl.Close();

                mid.ShouldBeGreaterThan(
                    0.01,
                    "位移过渡没有插值（值还停在起点）—— 这条过渡没被认。报告：\n  "
                    + string.Join("\n  ", report));
                mid.ShouldBeLessThan(
                    15.9,
                    "位移是硬跳（10% 的时长就走到了终点）—— 这条过渡没被认。报告：\n  "
                    + string.Join("\n  ", report));
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 量一扇指定高度的模块窗口，把高度链写进报告，并把"与声明不符"的项收进 <paramref name="wrong"/>。
    /// </summary>
    /// <param name="package">模块包目录。</param>
    /// <param name="name">实例名（同一运行时里逐模块各一个）。</param>
    /// <param name="services">运行时服务（由调用方持有，一条用例只建一次）。</param>
    /// <param name="state">视图状态（由调用方持有）。</param>
    /// <param name="windowHeight">这一遍用的窗口高。</param>
    /// <param name="report">报告行（人工判读产物）。</param>
    /// <param name="wrong">不符项（判据）。</param>
    /// <remarks>
    /// <b>期望值从声明里算，不写死</b>：页签条高 = `tabs.rows` 第 0 行（数值写法）；两侧净高应当相等；
    /// 滑块尺寸取批准的那三个数。于是"声明改了而渲染没跟上"会直接失败，而不是事后靠调参调出来。
    /// </remarks>
    /// <summary>
    /// **按钮指向 / 按下必须是浅主题色的像素**（真出像素，不看属性值）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么必须读像素：这条链上"属性值对、样式对、而屏幕上是灰的"正是这个缺陷的形态 ——
    /// 半透明黑叠在我们的色上时，属性值仍然是我们写的那个（实测：部件 Background 是 `#FF000000`
    /// 一类的黑，屏幕上却是"底色叠 20% 黑"）。唯一算数的判据是**读那个像素**。
    /// </para>
    /// <para>
    /// 判据写成"蓝色显著高于红色"，**不依赖具体色值**：换色板不误报，而"指向后仍是灰的"直接失败。
    /// </para>
    /// </remarks>
    /// <summary>
    /// **报数**：鼠标点击一个带类名的按钮后，`:focus-visible` 与边框色各是什么（供人工判读）。
    /// </summary>
    /// <remarks>
    /// 为什么要有它：页签上那圈"多余的选中框"来自键盘焦点环，而**鼠标点击会不会置上 `:focus-visible`**
    /// 决定了它是"只在键盘导航时出现"还是"点一下就来"。这条把两个事实都记下来。
    /// </remarks>
    [Fact]
    public async Task 鼠标点击后的焦点态必须可读()
    {
        var report = new List<string>();

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        await session.Dispatch(
            () =>
            {
                var tab = new Button { Content = "分类", Width = 64, Height = 28 };
                tab.Classes.Add("tab");
                tab.Classes.Add("active");

                var window = new Window
                {
                    Width = 200,
                    Height = 100,
                    Content = new Border { Padding = new Thickness(20), Child = tab },
                };

                window.Show();
                Pump();
                Pump();

                var center = tab.TranslatePoint(
                    new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2),
                    window) ?? default;

                report.Add($"点前：焦点={tab.IsFocused} 边框={Describe(tab.BorderBrush)}");
                report.Add(
                    $"  页签自身背景={Describe(tab.Background)} 部件底色={Describe(
                        tab.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault()?.Background)}"
                    + "（部件必须跟随页签自己的底色，不能是按钮面 —— 那正是「多出一块方块」的成因）");

                window.MouseMove(center);
                Pump();
                window.MouseDown(center, MouseButton.Left);
                Pump();
                window.MouseUp(center, MouseButton.Left);
                Pump();
                Pump();

                report.Add($"点后：焦点={tab.IsFocused} 边框={Describe(tab.BorderBrush)}");

                window.Close();
            },
            CancellationToken.None);

        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllLines(Path.Combine(OutputDirectory, "tab-focus.txt"), report);
    }

    private static void MeasureModuleHeights(
        string package,
        string name,
        RuntimeServices services,
        ViewStateStore state,
        double windowHeight,
        List<string> report,
        List<string> wrong)
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        ViewDeclarationReader.TryRead(package, schema!, out var document, out _, out var readError)
            .ShouldBeTrue(readError);

        var root = document!.Views[0].Root;
        var declared = document.Frame.Window;

        var host = new ModuleSurfaceHost(name, root, services, services, state);

        var windowControl = new Window
        {
            Width = declared.Width,
            Height = windowHeight,
            Content = new Border { Child = host.View },
        };

        windowControl.Show();
        Pump();
        Pump();
        host.Refresh();
        Pump();

        // 页签条的声明高（`tabs.rows` 的第 0 行，数值写法）；取第一个页签的声明即可
        var tabsNode = FindTabs(root);
        var declaredStrip = tabsNode is { Rows.Count: > 0 } ? tabsNode.Rows[0] : default;

        var surfaces = windowControl.GetVisualDescendants().OfType<TextSurface>().ToList();
        var strips = windowControl.GetVisualDescendants().OfType<SurfaceTabsControl>().ToList();

        report.Add($"窗口高={windowHeight:0}  文本面={surfaces.Count}  页签控件={strips.Count}");

        foreach (var strip in strips)
        {
            var bar = strip.GetVisualDescendants()
                .OfType<StackPanel>()
                .FirstOrDefault(panel => panel.Classes.Contains(SurfaceClasses.TabStrip));

            if (bar is null)
            {
                wrong.Add($"[{windowHeight:0}] 页签控件里找不到标签条（样式类没挂上？）");
                continue;
            }

            // 标签条外面那一层 Border 就是"页签条"的可见高度；它的 Margin 也占那一行的高度
            var frame = bar.GetVisualAncestors().OfType<Border>().FirstOrDefault();
            var stripHeight = (frame?.Bounds.Height ?? 0) + (frame?.Margin.Top ?? 0) + (frame?.Margin.Bottom ?? 0);

            report.Add(
                $"  页签条实测={stripHeight:0.#} 声明={Declared(declaredStrip)} "
                + $"标签条={bar.Bounds.Width:0}x{bar.Bounds.Height:0} "
                + $"每页签宽={bar.Bounds.Width / Math.Max(bar.Children.Count, 1):0.#}");

            if (declaredStrip.Kind is GridTrackKind.Fixed
                && Math.Abs(stripHeight - declaredStrip.Value) > 1.5)
            {
                wrong.Add(
                    $"[{name} {windowHeight:0}] 页签条实测 {stripHeight:0.#} ≠ 声明的 {declaredStrip.Value:0.#}"
                    + "（声明了固定高就必须是它；被按钮的 MinHeight 顶回去也算不符）");
            }

            // **页签要多宽才点得到**：宽度是"文字 + 左右内边距"，压掉内边距会让每个页签只剩文字宽
            // （实测整条从 139 掉到 29、每页签 5.8 宽）—— 声明、排布、高度全都合格，只有手指点不到。
            var perTab = bar.Bounds.Width / Math.Max(bar.Children.Count, 1);

            if (perTab < 22)
            {
                wrong.Add(
                    $"[{name} {windowHeight:0}] 每个页签只有 {perTab:0.#} 宽（应 ≥ 22）"
                    + "：横向内边距多半被某处压掉了");
            }
        }

        // **两侧净高**：页内文本面（可写）与结果文本面（只读）。只有"输入 + 结果"这种模块才有两侧，
        // 没有结果面的模块（发送器）跳过这一项，不报"数量对不上"。
        var editable = surfaces.Where(surface => !surface.IsReadOnly).ToList();
        var readOnly = surfaces.Where(surface => surface.IsReadOnly).ToList();

        // **两侧净高**：有只读结果面的模块才比（`DataMonitor` 只有一块日志面，没有"两侧"）。
        // 取**最后一块**可写面当输入：页内有多块可写面时，紧邻结果的那一块才是"输入"。
        if (readOnly.Count > 0 && editable.Count > 0)
        {
            var input = editable[^1].Bounds.Height;
            var result = readOnly[0].Bounds.Height;

            report.Add($"  输入净高={input:0.#}  结果净高={result:0.#}  差={Math.Abs(input - result):0.#}");

            if (Math.Abs(input - result) > 1.5)
            {
                wrong.Add($"[{name} {windowHeight:0}] 输入净高 {input:0.#} ≠ 结果净高 {result:0.#}");
            }
        }

        // **滑块尺寸**：轨道与圆钮是控件画的（声明与主题都不给这两个数），所以要有实测判据钉住
        foreach (var track in windowControl.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains(SurfaceClasses.ToggleTrack)))
        {
            var knob = track.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains(SurfaceClasses.ToggleKnob));

            // 同一格里的说明文字：滑块的**命中区宽 36**，比原来的 16 宽一圈 ⇒ 窄格子里会把文字挤没。
            // 这一项只报数（人工判读），不设阈值：格宽是声明的事，该由作者按自己的字长定。
            var element = track.GetVisualAncestors().OfType<SurfaceElementControl>().FirstOrDefault();

            var label = element?
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(block => block.Text?.Length > 0);

            report.Add(
                $"  滑块轨道={track.Bounds.Width:0.#}x{track.Bounds.Height:0.#} "
                + $"圆钮={knob?.Bounds.Width:0.#}x{knob?.Bounds.Height:0.#} "
                + $"钮平移={(knob?.RenderTransform as TranslateTransform)?.X:0.#} "
                + $"元素宽={element?.Bounds.Width:0.#} "
                + $"文字期望={label?.DesiredSize.Width:0.#} 文字实得={label?.Bounds.Width:0.#} 字={label?.Text}");
        }

        // **按钮的底色只由外层那块 `Border` 画一次**（部件一律透明）：
        // 这样"外圈一个色、中间一块另一个色"的方块在**结构上**不会出现（实测：页签中央那块蓝就是这么来的）；
        // 同时 Fluent 那条按部件名刷半透明黑的样式也失去着力点（部件不画底色，它刷什么都上不了屏）。
        foreach (var commandButton in windowControl.GetVisualDescendants()
            .OfType<Button>()
            .Where(candidate => candidate.Classes.Contains(SurfaceClasses.CommandButton)))
        {
            var presenter = commandButton.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault();

            var chrome = commandButton.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault();

            report.Add(
                $"  命令按钮：自身={Describe(commandButton.Background)} "
                + $"外层={Describe(chrome?.Background)} 部件={Describe(presenter?.Background)}");

            if (presenter?.Background is ISolidColorBrush { Color.A: > 0 })
            {
                wrong.Add(
                    $"[{name} {windowHeight:0}] 命令按钮的部件底色不是透明（实测 {Describe(presenter.Background)}）"
                    + "⇒ 它会与外层各画一次，页面会出现中间一块的方块");
            }

            if (commandButton.Background is not ISolidColorBrush { Color.A: >= 200 })
            {
                wrong.Add(
                    $"[{name} {windowHeight:0}] 命令按钮自身的底色不是不透明实色"
                    + $"（实测 {Describe(commandButton.Background)}）⇒ 它由外层 Border 画，必须实色");
            }
        }

        windowControl.Close();
    }

    /// <summary>把一把画刷写成人读的样子（诊断用）。</summary>
    /// <param name="brush">画刷；<see langword="null"/> 表示没有。</param>
    /// <returns>字面量。</returns>
    private static string Describe(IBrush? brush) => brush switch
    {
        null => "无",
        ISolidColorBrush solid => $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
        _ => brush.GetType().Name,
    };
    /// <param name="node">子树根。</param>
    /// <returns>找到的节点；没有 ⇒ <see langword="null"/>。</returns>
    private static ViewNode? FindTabs(ViewNode node)
    {
        if (ViewElementKindExtensions.IsTabs(node.Kind))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (FindTabs(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>把一条轨道规格写成人读的样子（诊断用）。</summary>
    /// <param name="track">轨道。</param>
    /// <returns>字面量。</returns>
    private static string Declared(GridTrack track) => track.Kind switch
    {
        GridTrackKind.Fixed => track.Value.ToString("0.#", CultureInfo.InvariantCulture),
        GridTrackKind.Star => track.Value.ToString("0.#", CultureInfo.InvariantCulture) + "*",
        _ => "auto",
    };

    /// <summary>
    /// **控件内部不许逸出它自己的矩形** —— 逸出就是"被裁掉或被压在相邻元素上"。
    /// </summary>
    /// <param name="element">被检的控件。</param>
    /// <returns>逸出的后代描述；没有 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// <para>
    /// 守的是"**声明了框，里面的东西却比框大**"这一类静默缺陷。外层元素本身**永远**等于声明
    /// （排布按声明给它矩形）⇒ 拿外层比声明是量不出来的；差别全在**里面**。
    /// 实测漏掉过一次真缺陷：一个声明 96×30 的控件内部是竖排 StackPanel（按钮 + 路径面 + 提示），
    /// 路径面被压到框外，界面上就是"一个被遮挡的按钮加一条线"，而布局、溢出、声明读取全都不报错。
    /// </para>
    /// <para>
    /// 判据取**后代不得逸出**（而不是"必须填满"）：内部比框小是正常的（内容对齐、留白都合法），
    /// 比框大才是"用户会看到不该看到的样子"。
    /// </para>
    /// </remarks>
    private static string? OverflowingDescendant(Control element)
    {
        var frame = element.Bounds;

        foreach (var descendant in element.GetVisualDescendants().OfType<Control>())
        {
            var box = descendant.Bounds;

            if (box.Width <= 0 && box.Height <= 0)
            {
                continue;   // 没排出来的（正在折叠 / 隐藏）不算
            }

            if (box.Right > frame.Width + 0.5 || box.Bottom > frame.Height + 0.5)
            {
                return $"{descendant.GetType().Name} 到 {box.Right:0}×{box.Bottom:0}"
                    + $"（框只有 {frame.Width:0}×{frame.Height:0}）";
            }
        }

        return null;
    }

    /// <summary>把窗口画成一张 PNG（诊断产物，不做像素比对）。</summary>
    /// <param name="window">窗口。</param>
    /// <param name="file">目标文件。</param>
    private static void Render(Window window, string file)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));
        using var bitmap = new RenderTargetBitmap(size);

        bitmap.Render(window);

#pragma warning disable CS0618 // 存盘那个重载已过时，但 headless 下没有替代品
        bitmap.Save(file);
#pragma warning restore CS0618
    }

    /// <summary>一张图的可计数特征（给"有没有多出东西"这种判据用）。</summary>
    /// <param name="DistinctColors">图上出现过的颜色种类数。</param>
    /// <param name="InkPixels">非背景色的像素数（"有墨"的量）。</param>
    private readonly record struct ImageStats(int DistinctColors, int InkPixels);

    /// <summary>
    /// 渲染某模块在**特定消息 / 标色状态**下的样子，并返回可计数特征。
    /// </summary>
    /// <param name="package">模块包目录。</param>
    /// <param name="name">产物短名。</param>
    /// <param name="message">写进 <c>status</c> 源的那句话；<see langword="null"/> = 不写。</param>
    /// <param name="alert">要不要给状态源标提示色。</param>
    /// <returns>位图的可计数特征。</returns>
    /// <remarks>
    /// **不用假的表面**：走真声明、真宿主、真缓冲，只把"模块写源 / 设色"这一步用直接调用代替
    /// （模块线程与 UI 线程之间**只经这两条通道**：写源、设外观）。
    /// <b>调用方必须已经在 headless 会话里</b>：这里刻意不再自己起会话 —— 一条用例里连起两条会卡住。
    /// </remarks>
    private static ImageStats RenderMessageState(
        string package,
        string name,
        string? message,
        bool alert)
    {
        Directory.CreateDirectory(OutputDirectory);
        var output = Path.Combine(OutputDirectory, name);
        Directory.CreateDirectory(output);

        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        ViewDeclarationReader.TryRead(package, schema!, out var document, out _, out var readError)
            .ShouldBeTrue(readError);

        var definition = document!.Views[0];
        var window = document.Frame.Window;
        var root = definition.Root;

        ImageStats stats = default;
        var file = Path.Combine(output, $"{name}.png");

        // 这一份运行时只用一辈子（这个块结束就没人再动它）⇒ 必须**当场释放**：
        // 它持有搬运载体，泄漏了就会在后面的用例里留下活着的线程（表现为"整类跑不完"）。
        using var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var state = new ViewStateStore();
        var host = new ModuleSurfaceHost(name, root, services, services, state);

        var windowControl = new Window
        {
            Width = window.Width,
            Height = window.Height,
            Content = new Border { Child = host.View },
        };

        windowControl.Show();
        Pump();

        if (message is { Length: > 0 })
        {
            state.Display(name, "status").SetText(message);
            host.ForgetBuffer("status");
        }

        host.Refresh();
        Pump();
        Pump();

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(windowControl.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(windowControl.Bounds.Height)));

        using (var bitmap = new RenderTargetBitmap(size))
        {
            bitmap.Render(windowControl);

#pragma warning disable CS0618 // 存盘那个重载已过时，但 headless 下没有替代品
            bitmap.Save(file);
#pragma warning restore CS0618

            stats = Measure(bitmap, size);
        }

        windowControl.Close();
        _ = alert;

        return stats;
    }

    /// <summary>数一张图的可计数特征。</summary>
    /// <param name="bitmap">位图。</param>
    /// <param name="size">像素尺寸。</param>
    /// <returns>特征（颜色种类数 + 非背景像素数）。</returns>
    private static ImageStats Measure(RenderTargetBitmap bitmap, PixelSize size)
    {
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var colors = new HashSet<int>();
        var ink = 0;

        // 左上角那个像素当"背景"：它是一个正常的窗口，没有透明/特殊像素
        var background = (buffer[2] << 16) | (buffer[1] << 8) | buffer[0];

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * stride) + (x * 4);
                var color = (buffer[i + 2] << 16) | (buffer[i + 1] << 8) | buffer[i];
                _ = colors.Add(color);

                if (color != background)
                {
                    ink++;
                }
            }
        }

        return new ImageStats(colors.Count, ink);
    }
}
