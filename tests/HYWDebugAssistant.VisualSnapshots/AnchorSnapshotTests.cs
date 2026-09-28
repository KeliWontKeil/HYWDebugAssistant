using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **端口锚点「固定 / 解锁」图标按钮（G5 · C5-7）的"真出像素 + 真指针"自检**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它为什么存在</b>：按钮用**负外边距**摆在标记**之外**的空白带里 —— 这类"子元素排在父元素
///     bounds 之外"的做法，**排版能过、编译能过，只有真指针才检验得了能不能命中**；再加上图标是
///     `IsVisible` 绑定的两态字形（模板/绑定写错时**不报错、只是什么都不画**）。</item>
///   <item><b>判据</b>：① 平时不可见（`Opacity = 0`）；② 悬停该标记后淡入；③ **真指针点击**能把该口写进运行时
///     几何、图标切到"锁"、光标变默认箭头；④ 按钮矩形**不与标记、指示灯重叠**且在窗口内；⑤ 两帧像素不同。</item>
///   <item><b>为什么在同一进程里串行</b>：真实渲染栈是**进程级**配置 ⇒ 与其余快照用例放同一个 collection。</item>
///   <item>本类自持输出目录，不与他人共用（共用目录会被别人的 <c>Directory.Delete</c> 连带清掉）。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class AnchorSnapshotTests
{
    /// <summary>本类快照的独立输出目录。</summary>
    private static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-anchor-snapshot");

    [Fact]
    public async Task 端口锚点固定按钮_悬停淡入_真点击写进运行时_且不压标记与指示灯()    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var output = OutputDirectory;

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);   // 只清自己这一份
        }

        _ = Directory.CreateDirectory(output);

        var hiddenFile = Path.Combine(output, "anchor-button-hidden.png");
        var shownFile = Path.Combine(output, "anchor-button-shown.png");
        var fixedFile = Path.Combine(output, "anchor-button-fixed.png");

        byte[] hidden = [];
        byte[] shown = [];

        var hiddenBefore = -1.0;
        var shownOnHover = -1.0;
        var cursorBefore = default(Cursor?);
        var cursorAfter = default(Cursor?);

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.AddAnchor("a1", x: 40, y: 40);

                var module = harness.Windows.Modules.ShouldHaveSingleItem();
                var window = harness.OpenWindow(module);
                Pump();

                var marker = FindMarker(window).ShouldNotBeNull();
                var vm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
                vm.IsAnchorFixed.ShouldBeFalse("模块声明了 `AnchorDraggable = true` ⇒ 初始可拖");

                var toggle = FindToggle(window).ShouldNotBeNull(
                    "标记层里必须有「固定 / 解锁」图标按钮（没有它 = 用户没有任何操作入口）");
                var content = FindContent(window).ShouldNotBeNull("窗口里必须有内容区（用来断言按钮不压内容）");

                // 两态图标必须**真的互斥**：两块字形同在一个 `Panel` 里，都可见时后画的会盖住前一块
                // ⇒ 画面上永远是同一个图标（"改了没生效"里最隐蔽的一种）。
                var glyphs = toggle.GetVisualDescendants().OfType<TextBlock>().ToList();
                glyphs.Count.ShouldBe(2, "图标按钮里应有**两块**字形（锁 / 开锁）");
                var lockGlyph = glyphs.Single(t => t.Text == "\uE72E");
                var unlockGlyph = glyphs.Single(t => t.Text == "\uE785");
                unlockGlyph.IsVisible.ShouldBeTrue("未固定 ⇒ 显示**开锁**字形");
                lockGlyph.IsVisible.ShouldBeFalse("未固定时不得显示锁字形（否则两态看起来一模一样）");

                // ① 平时完全不可见（与模块按钮簇同一口味），先留一帧"没有按钮"的画
                hiddenBefore = toggle.Opacity;
                hidden = Render(window, hiddenFile);

                // ② **几何就是这一条的要点**（用户 2026-09 实测："实际落在了内侧遮挡 UI，而不是边框留白处"）：
                //    按钮必须落在**预留带里、标记长度之外** ⇒ 既不压标记内容/指示灯，也**不压模块内容区**，且在窗口内。
                var lines0 = new List<string>();
                DumpVisualTree(window, 0, lines0);
                File.WriteAllLines(Path.Combine(output, "anchor-tree-before.txt"), lines0);

                var toggleRect = RectIn(toggle, window);
                var markerRect = RectIn(marker, window);
                var lampRect = RectIn(FindLamp(marker).ShouldNotBeNull(), window);
                var contentRect = RectIn(content, window);

                toggleRect.Intersects(markerRect).ShouldBeFalse(
                    $"按钮必须落在标记**之外**（按钮 {toggleRect}，标记 {markerRect}）");
                toggleRect.Intersects(lampRect).ShouldBeFalse("按钮不得压住连接指示灯");
                toggleRect.Intersects(contentRect).ShouldBeFalse(
                    "按钮**不得压住模块内容区** —— 预留带与内容区的 20px 内边距是**同一块地方**，"
                    + "只有带内、且落在标记长度之外，才是真正的空白处");
                new Rect(window.Bounds.Size).Contains(toggleRect).ShouldBeTrue(
                    "按钮必须落在窗口内（落到窗口外就等于点不到）");

                // ③ 真指针悬停**标记** ⇒ 按钮淡入（过渡 120ms；无头节拍约 0.6ms/帧 ⇒ 推足帧数走完过渡）
                var markerCenter = marker.TranslatePoint(
                    new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window).ShouldNotBeNull();

                window.MouseMove(markerCenter);
                Pump(times: 400);
                shownOnHover = toggle.Opacity;
                cursorBefore = marker.Cursor;

                shown = Render(window, shownFile);

                // ④ **真指针点击按钮**：保证"看得见就点得着"
                var toggleCenter = toggle.TranslatePoint(
                    new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window).ShouldNotBeNull();

                window.MouseDown(toggleCenter, MouseButton.Left);
                window.MouseUp(toggleCenter, MouseButton.Left);
                Pump();

                vm.IsAnchorFixed.ShouldBeTrue("点击图标按钮 = 固定该口（写进运行时，不只是改本地标志）");
                harness.Runtime.Content.Layout.GetPort("a1", vm.PortName).IsAnchorFixed.ShouldBe(
                    true,
                    "固定态必须落进组合件几何（否则刷新一次就回到声明值）");

                cursorAfter = marker.Cursor;

                // 两态图标**换过来了**吗（固定态断言在点击之后）
                unlockGlyph.IsVisible.ShouldBeFalse("固定后不得再显示开锁字形");
                lockGlyph.IsVisible.ShouldBeTrue("固定后必须显示锁字形");

                _ = Render(window, fixedFile);
                var lines = new List<string>();
                DumpVisualTree(window, 0, lines);
                File.WriteAllLines(Path.Combine(output, "anchor-tree.txt"), lines);
            },
            CancellationToken.None);

        File.Exists(hiddenFile).ShouldBeTrue($"应留下快照：{hiddenFile}");
        File.Exists(shownFile).ShouldBeTrue($"应留下快照：{shownFile}");
        File.Exists(fixedFile).ShouldBeTrue($"应留下快照：{fixedFile}");

        hiddenBefore.ShouldBe(0.0, "平时按钮必须完全不可见（常驻图标会把标记区搞得凌乱）");
        // 判据用 **> 0.5**（"确实淡入了"）而不是"等于 0.95"：过渡是按渲染时钟推进的，
        // 断言终值会把结论绑死在"无头节拍每帧走多少毫秒"上（实测 0.6ms/帧），那种用例迟早假失败。
        shownOnHover.ShouldBeGreaterThan(0.5, "悬停该标记时按钮必须淡入（否则用户找不到入口）");

        // 光标必须说实话：可拖 = 移动光标；固定 = 默认箭头（用户裁定：悬停反馈**只**改指针形态）
        cursorBefore.ShouldNotBeNull();
        cursorAfter.ShouldNotBeNull();
        cursorBefore.ToString().ShouldBe("SizeAll", "可拖动 ⇒ 移动光标");
        cursorAfter.ToString().ShouldBe("Arrow", "固定后必须换成默认箭头");

        // ⑤ **按钮真的画出了像素**：同一位置、同一指针下，"平时（隐藏）"与"悬停后（淡入）"两帧必须明显不同。
        //    这是本条最该守住的一条 —— 按钮排在标记**之外**，被裁掉/挪到窗口外/`Opacity` 没生效时
        //    **不报错、只是看不见**（与"箭头从未 DrawGeometry"同一类缺陷）。
        //    判据不用"锁/开锁两态帧"：13px 字号下两块字形只差一个锁梁，像素差会小到与判据阈值打架
        //    （实测两态帧只差几个像素）；而"按钮出现"是几百像素级的差别，判据稳。
        DifferentPixelCount(hidden, shown).ShouldBeGreaterThan(
            40, "悬停后按钮必须真的出现在画面上（20×20 的按钮 ≈ 400 像素，出现时差异应远超 40）");
    }

    /// <summary>跑若干遍 UI 队列与渲染节拍（无头环境要手动推；过渡动画靠它推进）。</summary>
    private static void Pump(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>窗内的端口标记块（按样式类定位）。</summary>
    private static Border? FindMarker(Visual root) =>
        root.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("portMarker"));

    /// <summary>「固定 / 解锁」图标按钮（按样式类定位）。</summary>
    private static Button? FindToggle(Visual root) =>
        root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("anchorToggle"));

    /// <summary>标记上的连接指示灯。</summary>
    private static StateIndicator? FindLamp(Visual marker) =>
        marker.GetVisualDescendants().OfType<StateIndicator>().FirstOrDefault(c => c.Classes.Contains("markerLamp"));

    /// <summary>
    /// 模块**真实内容区**（内缩 20px 之后的元素宿主）—— 用来断言"按钮不压内容"。
    /// </summary>
    /// <remarks>
    /// 不能拿 `ModuleArea` 本身比：它铺满整个窗口（那 20px 内边距在它内部的 `ScrollViewer` 上），
    /// 拿它比会连预留带一起算进去 ⇒ 断言恒假。取它内部的元素宿主才是"真的画着东西的地方"。
    /// </remarks>
    private static Control? FindContent(Visual root)
    {
        var area = root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "ModuleArea");
        return area?.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
    }

    /// <summary>某控件在指定根坐标系里的矩形（几何类断言用 —— "有没有重叠"只能这么问）。</summary>
    private static Rect RectIn(Visual visual, Visual root)
    {
        var origin = visual.TranslatePoint(default, root);
        return origin is null ? default : new Rect(origin.Value, visual.Bounds.Size);
    }

    /// <summary>把窗口渲染成 PNG 并返回像素字节（供两帧比对）。</summary>
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

    /// <summary>两帧之间"有多少个像素不同"（通道差超过阈值的像素数）。</summary>
    private static int DifferentPixelCount(byte[] a, byte[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        var different = 0;

        for (var i = 0; i + 3 < length; i += 4)
        {
            var delta = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
            if (delta > 24)
            {
                different++;
            }
        }

        return different;
    }

    /// <summary>把视觉树写成"类型 + 名字 + 位置尺寸"的文本（排查"控件在不在、被谁盖住/折叠"用）。</summary>
    private static void DumpVisualTree(Visual visual, int depth, List<string> lines)
    {
        var bounds = visual.Bounds;
        lines.Add($"{new string(' ', depth * 2)}{visual.GetType().Name} " +
                  $"name={(visual as Control)?.Name ?? "-"} @({bounds.X:0},{bounds.Y:0}) {bounds.Width:0}x{bounds.Height:0}");

        foreach (var child in visual.GetVisualChildren())
        {
            DumpVisualTree(child, depth + 1, lines);
        }
    }
}
