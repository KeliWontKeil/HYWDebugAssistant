using Avalonia.Controls;
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
            Content = ModulePreviewChrome.Create(content, view?.Ports),
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
}
