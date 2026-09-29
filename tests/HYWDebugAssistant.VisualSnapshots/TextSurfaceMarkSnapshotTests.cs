using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HYWDebugAssistant.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **文本面换行标记（`↵`）的两种口径 —— 在真实布局下的"真出像素"自检**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要有它</b>：主测试项目的无头会话是**空绘制**的 —— 实测它的 `TextLine.Width` **恒为 0**
///     ⇒ 所有标记的 x 都是"内边距 + 间距"，"标记在**行末**还是在**行首**"这件事**在那里量不出来**
///     （无头用例只能钉"个数与行号"）。真实布局（Skia）才有真的行宽，本用例补的正是这一格。</item>
///   <item><b>钉的口径</b>（两条都是用户裁定，见 `NewlineMarkOverlay.MergeEmptyLineMarks`）：
///     ① **可写面（输入框）= ¶ 口径**：空行自己显示 `↵` ⇒ `甲⏎⏎乙` 的两个标记落在**两行**上，
///        且空行那个在**行首**（x 小）、`甲` 那个在**行末**（x 大）；
///     ② **只读面（A5 日志）= 合并口径**：连续换行的标记都挂到上一行末尾 ⇒ **同一个 Y**、x 依次排开。</item>
///   <item><b>产物</b>：两张 PNG（<c>%TEMP%\hyw-textsurface-mark-snapshot\</c>）——调这块时直接打开看。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class TextSurfaceMarkSnapshotTests
{
    /// <summary>快照输出目录（本类自持一份；别的类会清自己的目录，别共用）。</summary>
    private static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-textsurface-mark-snapshot");

    /// <summary>可写面（输入框）用 ¶ 口径：空行自己显示 `↵`（两个标记落在**两行**上）。</summary>
    [Fact]
    public async Task 可写面_空行的换行标记应在真实布局下落在那一个空行上()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var editableFile = Prepare() is { } directory ? Path.Combine(directory, "editable-para.png") : string.Empty;

        await session.Dispatch(
            () =>
            {
                var surface = new TextSurface { IsMultiline = true, ShowNewlineMarks = true, Text = "甲\r\n\r\n乙" };
                var window = Show(surface);

                surface.Marks.MergeEmptyLineMarks.ShouldBeFalse("可写面（输入框）用 ¶ 口径");

                var marks = surface.Marks.Marks;
                marks.Count.ShouldBe(2, "两个真换行 ⇒ 两个标记");

                var rows = marks.Select(m => Math.Round(m.Position.Y, 1)).Distinct().ToArray();
                rows.Length.ShouldBe(
                    2,
                    "¶ 口径：一个标记在 `甲` 那行的行末，另一个在**空行**上（同一 Y 就是被并到上一行去了）");

                marks[0].Position.X.ShouldBeGreaterThan(
                    marks[1].Position.X,
                    "**真实行宽下才量得出来**：`甲` 那行的标记在行末（x 大），空行那个没有可依附的文字 ⇒ 在行首（x 小）");

                Save(window, editableFile);
            },
            CancellationToken.None);

        File.Exists(editableFile).ShouldBeTrue($"应留下快照：{editableFile}");
    }

    /// <summary>只读面（A5 日志）用合并口径：连续换行的标记都挂在上一行末尾（**同一个 Y**、x 依次排开）。</summary>
    [Fact]
    public async Task 只读面_连续换行的标记应在真实布局下并到上一行末尾()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

        var logFile = Prepare() is { } directory ? Path.Combine(directory, "log-merged.png") : string.Empty;

        await session.Dispatch(
            () =>
            {
                var surface = new TextSurface
                {
                    IsReadOnly = true,
                    IsMultiline = true,
                    ShowScrollBar = true,
                    ShowNewlineMarks = true,
                    Lines = ["line-0000", "甲\n\n乙", "line-0002"],
                };

                var window = Show(surface);

                surface.Marks.MergeEmptyLineMarks.ShouldBeTrue("只读面（A5 日志）用合并口径");
                TextLines(surface.Editor.Text ?? string.Empty).ShouldBe(
                    ["line-0000", "甲", "", "乙", "line-0002"],
                    "窗口里确实有一个**真实的连续换行**（通道换行不参与标记）");

                var marks = surface.Marks.Marks;
                marks.Count.ShouldBe(2, "数据里那两个真换行 ⇒ 两个标记（一个都不能少）");

                marks.Select(m => Math.Round(m.Position.Y, 1)).Distinct().Count()
                    .ShouldBe(1, "合并口径：两个标记都挂在上一行末尾（**同一水平线**）");

                marks[0].Position.X.ShouldBeLessThan(marks[1].Position.X, "两个标记按先后依次排开，不叠在一起");

                Save(window, logFile);
            },
            CancellationToken.None);

        File.Exists(logFile).ShouldBeTrue($"应留下快照：{logFile}");
    }

    /// <summary>本类是否已经清过输出目录（**只清一次**：两条用例各有自己的产物，别互相删）。</summary>
    private static bool _cleaned;

    /// <summary>清一次本类的输出目录并返回它（`null` = 建不出目录，后面会以"文件不存在"失败）。</summary>
    private static string? Prepare()
    {
        try
        {
            if (!_cleaned)
            {
                _cleaned = true;

                if (Directory.Exists(OutputDirectory))
                {
                    Directory.Delete(OutputDirectory, recursive: true);
                }
            }

            _ = Directory.CreateDirectory(OutputDirectory);
            return OutputDirectory;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>把文本面挂进真实窗口、跑几帧让它布局完（真实布局 ⇒ 行宽是真的）。</summary>
    private static Window Show(TextSurface surface)
    {
        var window = new Window { Width = 300, Height = 130, Content = surface };
        window.Show();

        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    /// <summary>渲染成 PNG（人工看一眼用；断言不依赖像素内容）。</summary>
    private static void Save(Window window, string file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);

#pragma warning disable CS0618   // 快照工具：旧的 Save(string) 足够
        bitmap.Save(file);
#pragma warning restore CS0618
    }

    /// <summary>按 LF 切行（只读文本框可能把换行规范化）。</summary>
    private static string[] TextLines(string text) =>
        text is { Length: > 0 } ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : [];
}
