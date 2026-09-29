using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **只读"文本日志"用法的文本面**（`TextSurface` + `ShowScrollBar`）的三条外观/行为判据。
/// </summary>
/// <remarks>
/// <para>
/// 每次都是**真实 `App` + 真实窗口 + 真实布局 + 真实主题**：这三条缺陷里有两条（标记错位、滚动条观感）
/// 只在"主题模板真的挂上、布局真的跑过"之后才存在，纯 VM 级断言一律看不见。
/// </para>
/// <para><b>盯的三件事</b>（都来自 2026-09 用户实测反馈）：</para>
/// <list type="number">
///   <item><b>`↵` 必须跟着文本框**内部滚动**走</b>：只读框的正文由模板里的 `ScrollViewer` 承载，
///     它一滚呈现器就平移 —— 不订阅它，标记就留在旧坐标上（用户："每次重新把焦点移上去后，
///     会刷新一次换行标记位置"）；</item>
///   <item><b>竖条"没得可滚就收起、有时才是普通细条"</b>：此前 `AllowAutoHide=False` 把 `IsExpanded`
///     顶成 `true` ⇒ 主题画出**整根灰轨道 + 上下箭头按钮**（用户："风格非常奇怪且无用"）；</item>
///   <item><b>只读面不许画聚焦粗框</b>：`Fluent` 的聚焦态直接给模板部件 `Border#PART_BorderElement`
///     加粗到 2 px（用户截图里那个深色圆角粗框）。</item>
/// </list>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class TextSurfaceLogSurfaceTests : IClassFixture<HeadlessUiFixture>
{
    /// <summary>宿主窗口宽度（要能放下好几屏）。</summary>
    private const double HostWidth = 420;

    /// <summary>宿主窗口高度（同上）。</summary>
    private const double HostHeight = 240;

    /// <summary>文本框内部滚动的步长（DIP）：**不是行高整数倍**，免得"图案自映射"把断言变成恒真。</summary>
    private const double ScrollStep = 40;

    /// <summary>一行的显示高度（DIP）—— 与文本面样式里的 `LineHeight` 一致（"滚出视口"的判据按它算）。</summary>
    private const double LineHeightDip = 14;

    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具（与视觉测试同集合 ⇒ 禁止并行）。</param>
    public TextSurfaceLogSurfaceTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>
    /// **`↵` 跟着文本框内部滚动走**（用户实测缺陷 ①）。
    /// </summary>
    /// <remarks>
    /// 构造：60 条数据，其中**只有一条**含真实换行字符（`ABCDEFGH\n尾`）⇒ 全局**只有一个** `↵`
    /// （通道换行不画）；把窗口拖到缓冲头，此时那一行在第一屏里，标记可见、位置已知。
    /// 然后**只动文本框内部的滚动偏移**（不动窗口内容、不动文本）⇒ 标记必须跟着上移**同样的距离**。
    /// <para>
    /// **为什么这一个标记就够**：多标记时"各标记间距恰好是行高"⇒ 位移若正好是行高整数倍，
    /// 图案会映射到自己身上，"没重算"也能蒙混过关。单标记 + 非整数倍位移把这条路堵死。
    /// </para>
    /// </remarks>
    [Fact]
    public void 只读文本面_换行标记应跟着文本框内部滚动走() => _ui.Run(() =>
    {
        using var host = Open(LogLines(60, markedLine: 3));

        var surface = host.Surface;

        // 用户把竖条拖到最上（回看意图）⇒ 窗口从缓冲头开始、文本框内部滚回顶部
        surface.ScrollBar.Value = 0;
        Settle();

        surface.WindowStart.ShouldBe(0, "本用例前提：窗口停在缓冲头（那一行才在第一屏里）");
        surface.IsFollowingTail.ShouldBeFalse();

        var scroll = InnerScroll(surface);
        scroll.Offset.Y.ShouldBe(0, "本用例前提：文本框内部在顶部");

        // **先把基线钉住**：显式要求重绘一次 —— 这正是用户说的"重新把焦点移上去后就刷新一次"。
        // 这样本用例只盯**一件事**：之后的滚动有没有触发重算（不把"换窗口那次也要重算"混进来）。
        surface.Marks.InvalidateVisual();
        Settle();

        var before = surface.Marks.Marks
            .ShouldHaveSingleItem("基线：只有**数据里真的有**的那一个换行字符画 `↵`（通道换行不画）");
        before.Text.ShouldBe(NewlineMarkOverlay.NewlineMark);
        before.Position.X.ShouldBeGreaterThan(0, "`↵` 挂在**行末**（行首也有 X>0，但行末必须明显靠右）");

        // **只滚文本框内部**（不动窗口、不动文本）：就是用户拖内层滚动条/光标带着滚的那一下
        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + ScrollStep);
        Settle();

        scroll.Offset.Y.ShouldBe(ScrollStep, "前置条件：内部滚动真的发生了");

        var after = surface.Marks.Marks
            .ShouldHaveSingleItem("滚动之后标记仍在视口内（它本来就看得见）");

        after.Text.ShouldBe(NewlineMarkOverlay.NewlineMark);
        after.Position.X.ShouldBe(before.Position.X, "标记仍挂在**同一行的行末**：只有 Y 该跟着滚动走");

        Math.Abs(after.Position.Y - (before.Position.Y - ScrollStep)).ShouldBeLessThan(
            0.75,
            $"文本框内部滚了 {ScrollStep} ⇒ 标记必须跟着上移 {ScrollStep}（实际 {before.Position.Y:0.##} → {after.Position.Y:0.##}）。"
            + "不重算的表现就是「标记留在旧坐标上，直到别的重绘（如焦点变化）才刷一次」");

        // **再滚到那一行离开视口**：标记必须消失。
        // 这一条是"位置真的按**当前**平移现算"最硬的判据 —— 任何"缓存上一帧几何"的实现都会把
        // 旧坐标上的标记永远留着（那正是用户看到的"错位"），而只断言"位移等于滚动量"时，
        // 一个恰好把整层平移到同一位置的错误实现也可能蒙混过去。
        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + (ScrollStep * 4));
        Settle();

        scroll.Offset.Y.ShouldBeGreaterThan(
            3 * LineHeightDip,
            "前置条件：那一行（第 4 行）真的滚出视口了");
        surface.Marks.Marks.ShouldBeEmpty("承载那个换行字符的行已经不在视口里 ⇒ 一个 `↵` 都不该画");
    });

    /// <summary>
    /// **分页窗口换内容 ⇒ 标记按"当前窗口"重算**（用户实测缺陷 ① 的另一半）。
    /// </summary>
    /// <remarks>
    /// 换窗口是**换掉了整段文本**（不只是滚动）：窗口里那一行在不在，决定了 `↵` 有没有、有几个
    /// ⇒ 标记是**当前窗口内容**的产物。留着上一页的标记 = 用户说的"错位"。
    /// 这里**不手动重绘**：换内容本身就该触发重算（与"把焦点移上去才刷一次"的旧症状相反）。
    /// </remarks>
    [Fact]
    public void 只读文本面_换窗口内容后标记应按当前窗口重算() => _ui.Run(() =>
    {
        using var host = Open(LogLines(60, markedLine: 3));

        var surface = host.Surface;
        var bar = surface.ScrollBar;

        // 窗口停在缓冲头：那一行在窗口里 ⇒ 恰好一个 `↵`（**不手动重绘**，就看换内容这条链通不通）
        bar.Value = 0;
        Settle();

        surface.WindowStart.ShouldBe(0, "本用例前提：窗口停在缓冲头（那一行才在窗口里）");
        var atHead = surface.Marks.Marks
            .ShouldHaveSingleItem("换窗口内容本身就该让标记重算（不该等到「把焦点移上去」才刷一次）");

        // 窗口换到缓冲尾：那一行已经不在窗口里 ⇒ 标记必须消失
        bar.Value = bar.Maximum;
        Settle();

        surface.WindowStart.ShouldBeGreaterThan(3, "本用例前提：窗口起点已经越过那一行");
        surface.Marks.Marks.ShouldBeEmpty("标记是**当前窗口内容**的产物：那一行不在窗口里就一个都不该画");

        // 换回缓冲头：标记必须回到**同一处**（同一个 X/Y）
        bar.Value = 0;
        Settle();

        var again = surface.Marks.Marks.ShouldHaveSingleItem("换回来 ⇒ 那一个 `↵` 必须回来");
        again.Position.X.ShouldBe(
            atHead.Position.X,
            "换回同一窗口 ⇒ 标记必须落在**同一处**（位置是当前窗口内容 + 当前平移的纯函数）");
        again.Position.Y.ShouldBe(atHead.Position.Y);
    });

    /// <summary>
    /// **A5（只读面）连续换行 ⇒ 标记合并到上一行末尾**〔2026-09 用户裁定 7.45：
    /// "↵ 应该在文本末尾而不是另起一行"，抱怨原文是"多了一个奇怪的自动换行"（标记自己占了一行）〕。
    /// </summary>
    /// <remarks>
    /// 这一条与输入框的 ¶ 口径**成对存在**（同一条数据、两种面各有各的裁定）：
    /// 输入框那半边钉在 `ModuleWindowVisualTests.多行输入_纯换行的空行自己也应显示换行标记`。
    /// 判据：`甲\n\n乙` 的两个换行符 ⇒ **两个标记、同一个 Y**（都在上一行末尾）。
    /// </remarks>
    [Fact]
    public void 只读文本面_连续换行的标记应并到上一行末尾() => _ui.Run(() =>
    {
        // 数据里**真的**有一个连续换行（`甲\n\n乙`）—— 通道换行不参与（永不标）
        var lines = new List<string> { "line-0000", "甲\n\n乙", "line-0002" };
        using var host = Open(lines);

        host.Surface.WindowStart.ShouldBe(0, "本用例前提：窗口从缓冲头开始（那三行都在窗口里）");

        var marks = host.Surface.Marks.Marks;

        marks.Count.ShouldBe(2, "`甲\\n\\n乙` 里有**两个真实换行字符** ⇒ 两个标记（一个都不能少）");
        marks.ShouldAllBe(m => m.Text == NewlineMarkOverlay.NewlineMark);

        marks.Select(m => Math.Round(m.Position.Y, 1)).Distinct().Count()
            .ShouldBe(1, "只读面（A5）用**合并口径**：两个标记都挂在上一行末尾、落在**同一水平线**上"
                + "（用户 7.45：「↵ 应该在文本末尾而不是另起一行」）");

        marks[0].Position.X.ShouldBeLessThan(marks[1].Position.X, "两个标记按先后依次排开（不是叠在一起）");
    });

    /// <summary>**没得可滚 ⇒ 竖条收起**（用户实测缺陷 ②：日志是流式的，绝大多数时候没什么可回看）。</summary>
    [Fact]
    public void 只读文本面_没有可回看内容时竖条应收起() => _ui.Run(() =>
    {
        using var host = Open(LogLines(3, markedLine: -1));
        Settle();

        var bar = host.Surface.ScrollBar;

        bar.Maximum.ShouldBe(0, "窗口行数 ≥ 缓冲行数 ⇒ 范围内没有任何可回看的内容");
        bar.IsVisible.ShouldBeFalse("没得可滚时的滚动条就是**死重量**（用户：「风格非常奇怪且无用」）");
        bar.IsEffectivelyVisible.ShouldBeFalse();
        bar.Bounds.Width.ShouldBe(0, "收起 = 不占版面（右侧那一列必须真的让出来）");
    });

    /// <summary>**有回看余地 ⇒ 竖条出现**（可见性判据的另一半；否则"永远收起"也能骗过上面那条）。</summary>
    [Fact]
    public void 只读文本面_有回看余地时竖条才出现() => _ui.Run(() =>
    {
        using var host = Open(LogLines(200, markedLine: -1));
        Settle();

        var bar = host.Surface.ScrollBar;

        bar.Maximum.ShouldBeGreaterThan(0, "缓冲 200 行 ≫ 窗口 ⇒ 真的能往回看");
        bar.IsVisible.ShouldBeTrue();
        bar.IsEffectivelyVisible.ShouldBeTrue("条必须**真的画出来**（父级不可见、被裁掉都不算）");
        bar.Bounds.Width.ShouldBeGreaterThan(0, "可见时必须有非零尺寸（被压成 0 宽 = 看不见）");
    });

    /// <summary>
    /// **竖条就是"普通细条"**：未展开态下主题**不画**整根灰轨道、**不画**上下箭头按钮（缺陷 ②）。
    /// </summary>
    /// <remarks>
    /// `Fluent` 的 `ScrollBar` 模板里确实有 `Rectangle#TrackRect`（灰轨道）与两个带箭头的
    /// `RepeatButton#PART_LineUpButton/PART_LineDownButton`，但它们靠**不透明度 0** 藏起来，
    /// 只有 `IsExpanded=true`（= 指针在上面，**或 `AllowAutoHide=False` 的强制展开**）时才显形。
    /// 所以这里断言的正是"展开态没被我们顶起来"——把 `AllowAutoHide=False` 写回去，三条断言立刻变红。
    /// </remarks>
    [Fact]
    public void 只读文本面_竖条应是普通细条_不带箭头按钮与整根灰轨道() => _ui.Run(() =>
    {
        using var host = Open(LogLines(200, markedLine: -1));
        Settle();

        var bar = host.Surface.ScrollBar;

        bar.AllowAutoHide.ShouldBeTrue("用**主题默认**（写死 False 会把 IsExpanded 顶成 true ⇒ 箭头 + 灰轨道全冒出来）");
        bar.IsExpanded.ShouldBeFalse("没有指针悬停 ⇒ 不该是展开态");

        var track = bar.GetVisualDescendants().OfType<Rectangle>()
            .Single(r => string.Equals(r.Name, "TrackRect", StringComparison.Ordinal));
        track.Opacity.ShouldBe(0, "未展开态不画整根灰轨道（用户截图里那条又宽又灰的底子）");

        LineButton(bar, "PART_LineUpButton").Opacity.ShouldBe(0, "未展开态不画上箭头按钮（用户：「两端各有一个用不上的箭头」）");
        LineButton(bar, "PART_LineDownButton").Opacity.ShouldBe(0, "未展开态不画下箭头按钮（同上）");
    });

    /// <summary>
    /// **只读文本面聚焦时不画粗边框**（缺陷 ②；用户截图里那个深色圆角粗框）。
    /// </summary>
    /// <remarks>
    /// 对照断言是必须的：只断言"只读时边框为 0"的话，把整个应用的聚焦反馈关掉也能通过。
    /// 这里同时断言**可编辑的文本面仍然有聚焦边框**（同一个控件、只差 `IsReadOnly`）。
    /// </remarks>
    [Fact]
    public void 只读文本面_聚焦时不该画粗边框_而可编辑的仍要有() => _ui.Run(() =>
    {
        using var readOnly = Open(LogLines(200, markedLine: -1));
        readOnly.Surface.Editor.Focus();
        Settle();

        readOnly.Surface.Editor.IsFocused.ShouldBeTrue("本用例前提：焦点真的在只读框里");
        FocusBorder(readOnly.Surface).BorderThickness.Top.ShouldBe(
            0,
            "只读日志面 = **一整块普通文本区**，聚焦时不许冒出深色圆角粗框");

        // 对照：可编辑的文本面（输入框用法）**必须**保留聚焦边框
        using var editable = OpenEditable();
        editable.Surface.Editor.Focus();
        Settle();

        editable.Surface.Editor.IsFocused.ShouldBeTrue("本用例前提：焦点真的在输入框里");
        FocusBorder(editable.Surface).BorderThickness.Top.ShouldBeGreaterThan(
            0,
            "可编辑的输入框**必须**保留聚焦边框（否则就是一刀把全应用的聚焦反馈砍掉了）");
    });

    /// <summary>
    /// **"复制全部（缓冲）"仍在，而且拿到的是整段缓冲**（外壳视图 `TextLogView` 删掉后的回归）。
    /// </summary>
    /// <remarks>
    /// 旧实现由外壳视图注入一个 `Func&lt;string&gt;` 取数回调；外壳删掉后取数就地收回了文本面内部
    /// （`Lines` 对分页窗口而言**就是整段缓冲**）。这条用例走**用户的真实路径**（右键菜单项）
    /// 并回读剪贴板 ⇒ 证明"没有外壳也照样复制得到全量"，而不是只看代码里还有个方法。
    /// </remarks>
    [Fact]
    public void 只读文本面_复制全部应把整段缓冲放进剪贴板() => _ui.Run(() =>
    {
        var lines = LogLines(120, markedLine: -1);
        using var host = Open(lines);
        Settle();

        var menu = host.Surface.Editor.ContextMenu.ShouldNotBeNull("日志面的右键菜单里才有「复制全部（缓冲）」");
        var items = menu.ItemsSource!.Cast<MenuItem>().ToList();

        items.Select(i => i.Header as string).ShouldContain("复制全部（缓冲）");

        var clipboard = TopLevel.GetTopLevel(host.Surface).ShouldNotBeNull().Clipboard.ShouldNotBeNull();

        items.Single(i => (i.Header as string) == "复制全部（缓冲）")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle();

        var text = clipboard.TryGetTextAsync().GetAwaiter().GetResult();
        text.ShouldNotBeNull("剪贴板里必须有内容（外壳删掉后取数收回文本面内部，取不到就是这条链断了）");

        SplitLines(text).Length.ShouldBe(
            lines.Count,
            "复制的是**整段缓冲**（120 条），不是文本框里那一屏（窗口只有几十行）");
        SplitLines(text)[0].ShouldBe(lines[0]);
        SplitLines(text)[^1].ShouldBe(lines[^1]);
    });

    // ---------------------------------------------------------------- 夹具

    /// <summary>造一份日志数据：第 <paramref name="markedLine"/> 条**自带一个真换行字符**（其余都是通道换行）。</summary>
    private static List<string> LogLines(int total, int markedLine)
    {
        var lines = new List<string>(total);

        for (var i = 0; i < total; i++)
        {
            lines.Add(i == markedLine ? "ABCDEFGH\n尾" : $"line-{i:D4}");
        }

        return lines;
    }

    /// <summary>把**日志用法**的文本面挂进真实窗口并跑完首帧（属性组合与 A5 模板逐项一致）。</summary>
    private static Host Open(IReadOnlyList<string> lines)
    {
        var surface = new TextSurface
        {
            IsReadOnly = true,
            IsMultiline = true,
            ShowScrollBar = true,
            ShowNewlineMarks = true,
            Lines = lines,
        };

        return Show(surface, lines);
    }

    /// <summary>把**输入框用法**的文本面挂进真实窗口（对照用：`ShowScrollBar=false` ⇒ 可编辑 + 内层滚动）。</summary>
    private static Host OpenEditable()
    {
        var surface = new TextSurface { IsMultiline = true, EditorMinHeight = 120 };
        return Show(surface, []);
    }

    private static Host Show(TextSurface surface, IReadOnlyList<string> lines)
    {
        var window = new Window { Width = HostWidth, Height = HostHeight, Content = surface };
        window.Show();
        Settle();

        return new Host(window, surface, lines);
    }

    /// <summary>文本框模板里的内层滚动条（正文滚动真的发生在这一层）。</summary>
    private static ScrollViewer InnerScroll(TextSurface surface) =>
        surface.Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()
        .ShouldNotBeNull("文本框模板里必须有内层滚动条（`PART_ScrollViewer`）");

    /// <summary>聚焦边框画在模板部件 `Border#PART_BorderElement` 上（不是文本框自己的 `BorderThickness`）。</summary>
    private static Border FocusBorder(TextSurface surface) =>
        surface.Editor.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => string.Equals(b.Name, "PART_BorderElement", StringComparison.Ordinal))
            .ShouldNotBeNull("`Fluent` 的文本框把边框画在 `PART_BorderElement` 上");

    /// <summary>滚动条模板里的上下箭头按钮（未展开态下它们必须是不透明度 0）。</summary>
    private static RepeatButton LineButton(ScrollBar bar, string name) =>
        bar.GetVisualDescendants().OfType<RepeatButton>()
            .Single(b => string.Equals(b.Name, name, StringComparison.Ordinal));

    /// <summary>按 LF 切行（只读文本框可能把换行规范化）。</summary>
    private static string[] SplitLines(string text) =>
        text is { Length: > 0 } ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : [];

    /// <summary>推进到"布局 + 一个 20 Hz 节拍都跑完"（节流是真实时间的 ⇒ 必须给真实时间）。</summary>
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

    /// <summary>测试宿主：窗口 + 文本面（`Dispose` 关窗并抽干队列）。</summary>
    private sealed class Host(Window window, TextSurface surface, IReadOnlyList<string> lines) : IDisposable
    {
        /// <summary>宿主窗口。</summary>
        public Window Window { get; } = window;

        /// <summary>被检文本面。</summary>
        public TextSurface Surface { get; } = surface;

        /// <summary>喂进去的日志行（复制全部的期望值）。</summary>
        public IReadOnlyList<string> Lines { get; } = lines;

        /// <inheritdoc />
        public void Dispose()
        {
            Window.Close();
            Settle();
        }
    }
}
