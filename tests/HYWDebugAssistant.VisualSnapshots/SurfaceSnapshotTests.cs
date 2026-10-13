using System.Runtime.InteropServices;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Controls;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **模块内容表面（新机制）的"真出像素"自检**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它为什么存在</b>：内容机制换成了"声明树 → 排布树 → 控件树"，无头 VM 用例只能证明
///     "视图模型里有元素表"，证明不了**新表面真的把控件画出来了**。模板/资源没挂上、面板没量出高度、
///     宿主缓存写错时**一律不报错、只是什么都不画**。</item>
///   <item><b>判据</b>：① `ModuleArea` 里真的长出了 <see cref="ModuleSurfacePanel"/>；
///     ② 声明里写了的控件类型真的出现。</item>
///   <item><b>与 <c>TabStripSnapshotTests</c> 同一 collection</b>：真实渲染栈是进程级配置。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class SurfaceSnapshotTests
{
    /// <summary>本类快照的独立输出目录（不与别的快照类共用 —— 那些用例开头会清空自己的目录）。</summary>
    private static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-surface-snapshot");

    /// <summary>
    /// 给每条用例一份自己的产物目录。
    /// </summary>
    /// <param name="name">用例短名（目录名后缀）。</param>
    /// <returns>目录绝对路径（已清空并建好）。</returns>
    /// <remarks>
    /// **不能共用一个目录**：两条用例并行跑时会互相 `Delete(recursive: true)` 掉对方刚写下的快照。
    /// </remarks>
    private static string OutputDirectoryFor(string name)
    {
        var path = Path.Combine(OutputDirectory, name);

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        _ = Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task 声明面_应真的渲染出新机制的控件()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var output = OutputDirectoryFor("surface-sender");

        var file = Path.Combine(output, "surface-sender.png");
        var hasPanel = false;
        var numericValue = string.Empty;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.AddSender("sender", x: 40, y: 40);

                var module = harness.Windows.Modules.ShouldHaveSingleItem();
                var window = harness.OpenWindow(module);
                Pump();

                var descendants = window.GetVisualDescendants().ToList();

                hasPanel = descendants.OfType<ModuleSurfacePanel>().Any();

                // 声明面写的是 `numeric` + `data: "value"` ⇒ 这个控件得真的在树上，且读数位有占位符
                var numeric = descendants.OfType<SurfaceNumericControl>().FirstOrDefault();
                if (numeric is not null)
                {
                    numericValue = DescendantTexts(numeric);
                }

                _ = Render(window, file);
            },
            CancellationToken.None);

        File.Exists(file).ShouldBeTrue($"应留下快照：{file}");

        hasPanel.ShouldBeTrue("新的内容表面面板必须真的在视觉树里（换了内容宿主却没长出来 = 接线断）");
        numericValue.Contains('-').ShouldBeTrue(
            $"没有数据源时读数位置必须留下占位符（不是空白）：{numericValue}");
    }

    [Fact]
    public async Task 声明面_应真的渲染出日志_且命令按钮能拿到框架结论()
    {
        // 夹具包本身必须先是一份**读得动**的声明 —— 读不动时宿主整份忽略，界面就什么都不显示。
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();
        ViewDeclarationReader.TryRead(OverlaySnapshotTests.SnapshotViewPackages.Panel, schema, out var document, out var warnings, out var readError)
            .ShouldBeTrue($"{readError}（警告：{string.Join("；", warnings)}）");
        document.ShouldNotBeNull();

        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var output = OutputDirectoryFor("surface-panel");

        var file = Path.Combine(output, "surface-panel.png");
        var logText = string.Empty;
        var resultText = string.Empty;
        var numericValue = string.Empty;
        var hasLog = false;
        var hasCommand = false;
        var foundDeclarationOnly = false;
        var declarationOnlyText = string.Empty;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.AddPanel("panel", x: 40, y: 40);
                harness.AppendDisplayLine("panel", "log", "第一行来自测试");

                var module = harness.Windows.Modules.ShouldHaveSingleItem();

                var window = harness.OpenWindow(module);

                // 显示缓冲的落地在刷新里 ⇒ 先推一轮，控件建起来时数据已经在缓冲里
                harness.Windows.Refresh();
                Pump();

                var descendants = window.GetVisualDescendants().ToList();

                // ② 数值读数要吃到夹具周期发布的 42.5：**活动线程是异步的** ⇒ 轮询等它写出第一笔。
                // 这一步同时证明"取数链真的接上了"——没有活动线程时数值会一直是占位符（下面的断言会红）。
                var source = harness.Services
                    .GetSubscriptionSources()
                    .FirstOrDefault(s => string.Equals(s.DataSourceName, "value", StringComparison.Ordinal));

                source.ShouldNotBeNull("夹具包的 `value` 数据源必须出现在订阅源表里");
                for (var attempt = 0; attempt < 100 && source.Written == 0; attempt++)
                {
                    Thread.Sleep(10);
                    harness.Windows.Refresh();
                    Pump();
                }

                var numericProbe = window.GetVisualDescendants().OfType<SurfaceNumericControl>().FirstOrDefault();

                // 直接逼一次刷新，看数值控件是否**自己**读得到（把"刷新没到控件"与"取数失败"分开）
                numericProbe?.Refresh();

                source.Written.ShouldBeGreaterThan(
                    0L, "夹具的周期活动线程必须真的把数据发出来（承载线程没起 ⇒ 整个取数链无从验证）");

                // ① 折叠区里的文本面：展开后必须真的把追加的行画出来（输出面与输入面是同一个控件）
                var expander = descendants.OfType<Expander>().FirstOrDefault();
                expander.ShouldNotBeNull("声明里的 `collapsible` 必须变成 Expander");

                expander.IsExpanded = true;
                Pump();
                harness.Windows.Refresh();
                Pump();

                var log = window.GetVisualDescendants()
                    .OfType<SurfaceTextBoxControl>()
                    .FirstOrDefault(box => box.Node.ReadOnly);
                if (log is not null)
                {
                    hasLog = true;
                    logText = DescendantTexts(log);
                }

                // ② 数值读数吃到了夹具周期发布的 42.5
                var numeric = window.GetVisualDescendants().OfType<SurfaceNumericControl>().FirstOrDefault();
                if (numeric is not null)
                {
                    numericValue = DescendantTexts(numeric);
                }

                // ③ 命令按钮：真点一下 ⇒ 框架的结论必须回填到状态角
                var command = window.GetVisualDescendants().OfType<SurfaceCommandButtonControl>().FirstOrDefault();
                if (command is not null)
                {
                    hasCommand = true;
                    command.Invoke();
                    resultText = DescendantTexts(command);
                }

                // ④ 未渲染的 kind 必须走占位控件（不许画一个"看起来能用"的壳）。
                // 声明面里 `rendered: false` 的有四种，夹具只放了 `statusLight` 一个 ⇒ 就查它：
                var placeholder = window.GetVisualDescendants()
                    .OfType<SurfaceDeclarationOnlyControl>()
                    .FirstOrDefault(control => string.Equals(control.Node.Kind, "statusLight", StringComparison.Ordinal));

                // 视觉树只有 UI 线程能读 ⇒ 要断言的字在这里抄出来（出去再读 TextBlock 会抛"线程拥有者不符"）
                if (placeholder is not null)
                {
                    foundDeclarationOnly = true;
                    declarationOnlyText = DescendantTexts(placeholder);
                }

                _ = Render(window, file);
            },
            CancellationToken.None);

        hasLog.ShouldBeTrue("展开后的只读文本面必须变成 SurfaceTextBoxControl");
        logText.Contains("第一行来自测试", StringComparison.Ordinal)
            .ShouldBeTrue("显示缓冲里追加的行必须真的画到日志控件上");
        numericValue.Contains("42.5", StringComparison.Ordinal).ShouldBeTrue(
            $"数值读数必须吃到模块发布的数据（说明取数链真的接上了）：{numericValue}");
        hasCommand.ShouldBeTrue("声明里的 `commandButton` 必须变成 SurfaceCommandButtonControl");
        resultText.Contains("已投递命令", StringComparison.Ordinal).ShouldBeFalse(
            "按下按钮**不许**弹「底层触发了什么」这类系统回执 —— 用户要知道的是产生了什么结果："
            + $"实际画出来的是「{resultText}」");
        foundDeclarationOnly.ShouldBeTrue(
            "声明 `rendered: false` 的 kind 必须走占位控件 —— 目录说它不渲染，界面就不许画出一个看起来能用的壳");
        declarationOnlyText.Contains('灯').ShouldBeTrue(
            "占位控件也要把它声明的标题画出来（只画一个空框等于没告诉用户这里原本有什么）");
    }

    /// <summary>
    /// 把一个控件（含子控件）里所有非空文本按视觉顺序拼起来 —— 无头断言"画出来的字对不对"用。
    /// </summary>
    /// <remarks>
    /// **不加分隔符**：数值控件把数字拆成"整数部分 + 小数部分"两块 TextBlock，中间插空格会拼出
    /// "4 2 . 5" 这种假错。所以判据用 `Contains`，而不是整串相等。
    /// <para>
    /// 也要抄 `TextBox` 的正文：输入框与日志面走的是同一个 <c>TextSurface</c>，
    /// 里面的正文住在内层文本框里（不再是一块 <see cref="TextBlock"/>）。
    /// </para>
    /// </remarks>
    private static string DescendantTexts(Visual root) =>
        string.Concat(
            root.GetVisualDescendants()
                .Select(visual => visual switch
                {
                    TextBlock block => block.Text,
                    TextBox box => box.Text,
                    _ => null,
                })
                .Where(text => !string.IsNullOrEmpty(text)));

    /// <summary>
    /// 控件目录必须登记**声明面认得的每一种 kind**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条用例守的是一个**静默缺陷**：把描述挂在偏类（<c>partial</c>）的跨文件静态字段上时，
    /// 偏类的静态字段**跨文件初始化顺序未定义** —— 目录建表时另外两个文件里的字段还是
    /// <see langword="null"/>，按类型反射枚举把它们**跳过且不报错**：
    /// 19 条只进 4 条（<c>group</c>/<c>tabs</c>/<c>collapsible</c>/<c>container</c>）。
    /// 后果不是"少几种控件"，而是 <c>numeric</c> 被当成未知 kind、<c>textBox</c> 的块轴下限 88 不生效
    /// （读数与排布全错），而**没有任何一条报错**。
    /// </para>
    /// <para>
    /// 目录是 <c>internal</c>（渲染侧词汇表不出程序集），这里按名字取它 —— 用例要守的是
    /// "两个程序集里的词汇表基数相等"，这本身就是跨程序集的事。
    /// </para>
    /// </remarks>
    [Fact]
    public void 控件目录_必须登记声明面认得的每一种kind()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var catalogType = typeof(ModuleSurfacePanel).Assembly
            .GetType("HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog.ControlCatalog", throwOnError: true);
        catalogType.ShouldNotBeNull("渲染侧的控件目录类型必须存在");

        var kinds = catalogType!
            .GetProperty("Kinds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetValue(null) as System.Collections.IEnumerable;

        var catalogKinds = kinds?.Cast<object>().Select(k => k?.ToString() ?? string.Empty).ToList() ?? [];
        catalogKinds.ShouldBe(
            [.. schema.Kinds.Keys.Order(StringComparer.Ordinal)],
            ignoreOrder: true,
            "控件目录与包声明 schema 是同一份词汇表的两处投影，缺一条就是「少一种控件」或「一种 kind 静默失效」");
    }

    /// <summary>
    /// 控件目录的**渲染账位**必须与声明面 schema 的 <c>rendered</c> **逐 kind 同口径**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这条用例消的是**双事实源**：目录里每条描述记着一个渲染账位（"这一种本轮画不画"），
    /// 而同一个事实的权威在包声明 schema 的 <c>rendered</c> 字段。两处在两个程序集里，
    /// 方向禁令又不许 <c>Ui</c> 在运行期去读 <c>Hosting</c> 的 schema ⇒ 只能靠用例把口径钉死。
    /// </para>
    /// <para>
    /// **不钉死的后果全是静默的**：两边不一致时，要么"声明说没有、界面却有"（<c>rendered:false</c>
    /// 的 kind 长出了真控件），要么"声明说有、界面却给占位"（用户看到的是「尚未渲染」的牌子）。
    /// 两条都不会抛异常、都不会写日志。
    /// </para>
    /// </remarks>
    [Fact]
    public void 控件目录的渲染账位_必须与声明面的rendered逐kind同口径()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var descriptors = CatalogDescriptors();
        descriptors.Count.ShouldBe(
            schema!.Kinds.Count,
            "控件目录与声明面 schema 必须一个 kind 一条描述（缺一条就是「一种 kind 静默失效」）");

        var mismatched = new List<string>();
        foreach (var (kind, descriptor) in descriptors)
        {
            if (!schema.Kinds.TryGetValue(kind, out var spec))
            {
                mismatched.Add($"{kind}（目录有、声明面没有）");
                continue;
            }

            var catalogSaysRendered =
                (bool)descriptor.GetType().GetProperty("IsRendered")!.GetValue(descriptor)!;
            if (catalogSaysRendered != spec.Rendered)
            {
                mismatched.Add(
                    $"{kind}（目录说 {(catalogSaysRendered ? "已渲染" : "仅登记")}、声明面 rendered={spec.Rendered}）");
            }
        }

        mismatched.ShouldBeEmpty(
            "渲染账位只有一处权威（包声明 schema 的 rendered）—— 目录那一份是本地账位，必须与它同口径");
    }

    /// <summary>按名字取目录里的全部描述（kind → 描述对象）。</summary>
    /// <remarks>目录是 <c>internal</c>，而"两处同口径"是跨程序集的事，只能按名字取。</remarks>
    private static Dictionary<string, object> CatalogDescriptors()
    {
        var catalogType = typeof(ModuleSurfacePanel).Assembly
            .GetType("HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog.ControlCatalog", throwOnError: true);

        var kinds = catalogType!
            .GetProperty("Kinds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetValue(null) as System.Collections.IEnumerable;

        var find = catalogType.GetMethod(
            "Find",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kind in (kinds?.Cast<object>() ?? []).Select(k => k?.ToString() ?? string.Empty))
        {
            var descriptor = find!.Invoke(null, [kind]);
            descriptor.ShouldNotBeNull($"kind「{kind}」在目录里必须有描述");
            map[kind] = descriptor!;
        }

        return map;
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static byte[] Render(Window window, string file)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);

#pragma warning disable CS0618   // 快照工具：旧的 Save(string) 足够
        bitmap.Save(file);
#pragma warning restore CS0618

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

        return buffer;
    }
}
