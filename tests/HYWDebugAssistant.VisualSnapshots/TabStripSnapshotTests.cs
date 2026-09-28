using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Ui.Views;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **标签条（C2 / G1）的"真出像素"自检**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它为什么存在</b>：无头 VM 用例只能证明"元素表里有标签条"，**证明不了模板真的画出来**
///     —— 模板没挂上 / 样式选择器写错 / 被裁成 0 高时，**不报错、只是什么都不画**
///     （与调色板窗口"只剩标题与按钮"、P39"箭头从未 DrawGeometry"同一类缺陷）。</item>
///   <item><b>判据</b>：① 渲染出 PNG 且不是空白；② **切页前后两张图必须不同**（选中态与页内容都变了）；
///     ③ 页签按钮在真实 Skia 视觉树里存在且有非零尺寸。</item>
///   <item><b>为什么在同一进程里串行</b>：真实渲染栈是**进程级**配置 ⇒ 与 `OverlaySnapshotTests` /
///     `ColorPickerDialogSnapshotTests` 放**同一个 collection**（并行会让几批会话互相破坏）。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class TabStripSnapshotTests
{
    /// <summary>
    /// 本类快照的**独立输出目录**。
    /// </summary>
    /// <remarks>
    /// **为什么不与 `OverlaySnapshotTests` 共用 `%TEMP%\hyw-overlay-snapshot\`**：那个用例开头会
    /// `Directory.Delete(..., recursive: true)` 清空整个目录，而同一 collection 内**执行顺序不定**
    /// ⇒ 它跑在后面时会把本类的 PNG 一起删掉（实测：断言全过、但产物不见了，**"人工看一眼"这一步被静默破坏**）。
    /// 故本类自持一个目录，产物在整轮跑完后仍然存在。
    /// </remarks>
    private static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-tab-snapshot");

    [Fact]
    public async Task 标签条_必须真的画出页签_且切页后像素确实变化()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var output = OutputDirectory;

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);   // 只清自己这一份
        }

        _ = Directory.CreateDirectory(output);

        var page0File = Path.Combine(output, "tabs-page0.png");
        var page1File = Path.Combine(output, "tabs-page1.png");

        byte[] page0 = [];
        byte[] page1 = [];
        var tabCount = 0;
        var tabHeight = 0.0;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.AddTabs("tabs", x: 40, y: 40);

                var module = harness.Windows.Modules.ShouldHaveSingleItem();
                var window = harness.OpenWindow(module);
                Pump();

                // ① 页签在真实 Skia 视觉树里存在、有非零尺寸（模板/样式写错时这里是 0）
                var tabs = window.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(b => b.Classes.Contains("tabStripItem"))
                    .ToList();

                tabCount = tabs.Count;
                if (tabs.Count > 0)
                {
                    tabHeight = tabs[0].Bounds.Height;
                }

                page0 = Render(window, page0File);

                // ② 真点第 2 个页签（指针事件 ⇒ 同时覆盖命中层级）
                var target = tabs[1];
                var center = target.TranslatePoint(
                    new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window).ShouldNotBeNull();

                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                Pump();

                page1 = Render(window, page1File);

                // 留下视觉树，出问题时看"页签在不在、各自多大"
                var lines = new List<string>();
                DumpVisualTree(window, 0, lines);
                File.WriteAllLines(Path.Combine(output, "tabs-tree.txt"), lines);
            },
            CancellationToken.None);

        File.Exists(page0File).ShouldBeTrue($"应留下快照：{page0File}");
        File.Exists(page1File).ShouldBeTrue($"应留下快照：{page1File}");

        tabCount.ShouldBe(3, "三页 ⇒ 真实视觉树里应有 3 个页签（模板没挂上时是 0）");
        tabHeight.ShouldBeGreaterThan(0, "页签必须有非零高度（被裁/压成 0 行 = 看不见）");

        // ③ 切页必须真的改变画面：选中态移动 + 页内容从"第一页输入"变"第二页输入"
        DifferentPixelCount(page0, page1).ShouldBeGreaterThan(
            8, "切页后渲染结果必须不同 —— 相同说明'选中态/样式/内容'里至少有一处没生效（改了没生效 = 接线断）");
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
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
