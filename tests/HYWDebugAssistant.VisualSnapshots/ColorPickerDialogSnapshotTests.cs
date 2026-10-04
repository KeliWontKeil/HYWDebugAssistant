using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **调色板窗口的渲染冒烟 + 观感快照**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它为什么存在</b>：实测缺陷是「调色板窗口只剩标题+提示+按钮，像个**复位提示**」 ——
///     根因不是布局写错，而是**控件没有模板**：`Avalonia.Controls.ColorPicker` 的 `ColorView` **自带模板但要显式挂载**
///     （`FluentTheme` 不含它的样式）。这类缺陷**任何 XAML 编译与单元断言都看不见**（XAML 编译通过、绑定也没错，
///     只是"没画出东西"），只有**真实渲染出像素**才看得见 —— 与 P39（箭头从未 `DrawGeometry`）同一类。</item>
///   <item><b>判据</b>：① 窗口高度必须明显大于"三行文字 + 一排按钮"（取色控件本身有高度）；
///     ② 渲染结果里的**不同颜色数**必须够多（色相条 + 光谱方块 ⇒ 成百上千种色；空白窗口只有个位数）；
///     ③ **必须有一个页签能输十六进制**（`TextBox` 存在）—— 这是"精确改色"的唯一入口；
///     ④ 留下 PNG（`%TEMP%\hyw-overlay-snapshot\color-picker*.png`）与**视觉树文本**供眼睛/排查用。</item>
///   <item><b>为什么单独一个测试项目</b>：真实渲染栈是**进程级**配置，见 `OverlaySnapshotTests` 的说明。</item>
///   <item><b>为什么必须与 `OverlaySnapshotTests` 同一个 collection</b>：一个进程里同时开两个无头会话会互相破坏
///     （第三十四轮实测：并行跑 ⇒ 3 项集体失败，串行跑全绿）⇒ 同 collection ⇒ **串行**。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class ColorPickerDialogSnapshotTests
{
    [Fact]
    public async Task 调色板窗口_必须真的画出取色控件_而不是只剩标题与按钮()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var output = OverlaySnapshotTests.OutputDirectory;
        _ = Directory.CreateDirectory(output);

        var spectrumFile = Path.Combine(output, "color-picker.png");
        var componentsFile = Path.Combine(output, "color-picker-components.png");

        double height = 0;
        int distinctColors = 0;
        int distinctColorsOnComponents = 0;
        double componentsHeight = 0;

        await session.Dispatch(
            () =>
            {
                // 给个初值（真实调用路径也是这么给的）；本条只关心"取色控件有没有真的画出来"
                var dialog = new ColorPickerDialog { Color = Colors.DodgerBlue };

                dialog.Show();
                Pump();

                height = dialog.Bounds.Height;
                distinctColors = Render(dialog, spectrumFile);

                // 页签里**含十六进制输入**的那一个 = 分量页（十六进制改色的唯一入口）⇒ 必须存在且画得出来
                var tab = FindTabWithHexInput(dialog);
                tab.ShouldBeGreaterThanOrEqualTo(0, "必须有一个页签能输十六进制（`IsHexInputVisible`）");

                SelectTab(dialog, tab);
                Pump();

                componentsHeight = dialog.Bounds.Height;
                distinctColorsOnComponents = Render(dialog, componentsFile);

                // 顺带留下**视觉树**（出问题时看"里面到底有什么控件、各自多大、谁被折叠"）
                var lines = new List<string>();
                DumpVisualTree(dialog, 0, lines);
                File.WriteAllLines(Path.Combine(output, "color-picker-tree.txt"), lines);
            },
            CancellationToken.None);

        File.Exists(spectrumFile).ShouldBeTrue($"应留下快照：{spectrumFile}");
        File.Exists(componentsFile).ShouldBeTrue($"应留下快照：{componentsFile}");

        // ① 取色控件（光谱 + 色相条 + 预览）自带高度 —— 只有文字和按钮时窗口矮得多
        height.ShouldBeGreaterThan(240, "窗口里必须有取色控件（`ColorView` 的模板没挂上时高度只剩标题/提示/按钮）");
        componentsHeight.ShouldBeGreaterThan(240, "分量页同样要有取色控件");

        // ② 出不出"彩色"不能靠断言看，只能数颜色：色相条一个人就能给出上百种色
        distinctColors.ShouldBeGreaterThan(64, "渲染结果必须真的包含取色控件（光谱/色相条）⇒ 颜色种类成百上千");
        distinctColorsOnComponents.ShouldBeGreaterThan(16, "分量页必须有内容（滑块 + 十六进制输入）");
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>把窗口渲染成 PNG，返回"不同颜色数"（空白窗口只有个位数）。</summary>
    private static int Render(Window window, string file)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);

#pragma warning disable CS0618   // 快照工具：旧的 Save(string) 足够
        bitmap.Save(file);
#pragma warning restore CS0618

        return CountDistinctColors(bitmap, size);
    }

    /// <summary>找出"含十六进制输入框"的页签下标（找不到 ⇒ -1）。</summary>
    private static int FindTabWithHexInput(Window window)
    {
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();

        for (var i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();

            if (window.GetVisualDescendants().Any(control => control is TextBox))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>切到指定页签并推到渲染节拍。</summary>
    private static void SelectTab(Window window, int index)
    {
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = index;
        Pump();
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

    /// <summary>数一数渲染结果里有多少种不同的颜色（空白窗口只有个位数）。</summary>
    private static int CountDistinctColors(RenderTargetBitmap bitmap, PixelSize size)
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

        var seen = new HashSet<int>();

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * stride) + (x * 4);
                _ = seen.Add((buffer[i] << 16) | (buffer[i + 1] << 8) | buffer[i + 2]);
            }
        }

        return seen.Count;
    }
}
