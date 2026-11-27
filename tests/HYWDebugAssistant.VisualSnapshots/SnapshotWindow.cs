using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **快照窗口的构建**：把“模块视图宿主 + 窗口外壳”收成一处，避免每个快照各写一套外边距。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>普通快照</b>：`ModuleView` 不在设计环境，不画外壳 ⇒ 这里用
///     <see cref="ModulePreviewChrome"/> 补外框与端口占位；</item>
///   <item><b>预览快照</b>：`ModuleView` 已自己画外壳（含端口占位与范围框）⇒ 直接放进窗口，
///     不能再包一层，否则会出现双重边框。</item>
/// </list>
/// </remarks>
internal static class SnapshotWindow
{
    /// <summary>普通快照：外层补模块窗口外壳。</summary>
    public static Window CreateFramed(Control content, ModuleView? view, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = ModulePreviewChrome.Create(content, view?.Ports, width, height),
        };

        return window;
    }

    /// <summary>预览快照：视图自己已经画外壳，窗口只负责给尺寸。</summary>
    public static Window CreatePreview(Control content, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Content = content,
    };

    /// <summary>
    /// **封面**：把模块按它自己的尺寸定死，再等比缩放进固定比例的画布（清单预览图块的比例）。
    /// </summary>
    /// <param name="content">模块视图宿主（按 <paramref name="width"/> × <paramref name="height"/> 布局）。</param>
    /// <param name="width">模块自然宽。</param>
    /// <param name="height">模块自然高。</param>
    /// <param name="coverWidth">画布宽。</param>
    /// <param name="coverHeight">画布高。</param>
    /// <remarks>
    /// **缩放不是拉伸**：窄高模块（竖标尺、姿态球）若被拉成 9:5 会严重失真，
    /// 而 `Viewbox` 的等比缩放只会在四周留底 —— 人眼看到的仍是"这个模块长什么样"。
    /// 封面因此不重排模块布局，模块也不需要为封面改任何尺寸声明。
    /// </remarks>
    public static Window CreateCover(
        Control content,
        double width,
        double height,
        double coverWidth,
        double coverHeight)
    {
        content.Width = width;
        content.Height = height;

        return new Window
        {
            Width = coverWidth,
            Height = coverHeight,
            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = content,
            },
        };
    }
}
