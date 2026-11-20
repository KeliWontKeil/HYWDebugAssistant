using Avalonia;

namespace HYWDebugAssistant.Sdk.Ui.Geometry;

/// <summary>
/// **浮层面板落位的唯一算法**：优先贴着锚点右侧，放不下依次换左侧、下方、上方，最后夹进工作区。
/// </summary>
/// <remarks>
/// 纯几何：输入锚点矩形、面板尺寸与屏幕工作区，返回物理像素左上角；不读窗口、不依赖控件。
/// 间距只在这里给一次，调用方不再各写一套“右/左/下/上”的顺序。
/// </remarks>
internal static class PanelPlacement
{
    /// <summary>面板与锚点之间的间距（DIP；调用方按屏幕缩放换算成像素）。</summary>
    public const int Gap = 4;

    /// <summary>算出面板左上角。</summary>
    /// <param name="anchor">锚点矩形（物理像素）。</param>
    /// <param name="panel">面板尺寸（物理像素）。</param>
    /// <param name="work">屏幕工作区（物理像素）。</param>
    /// <param name="gap">间距（物理像素）。</param>
    /// <returns>面板左上角。</returns>
    public static PixelPoint Compute(PixelRect anchor, PixelSize panel, PixelRect work, int gap = Gap)
    {
        var maxX = Math.Max(work.X, work.Right - panel.Width);
        var maxY = Math.Max(work.Y, work.Bottom - panel.Height);
        var x = Math.Clamp(anchor.X, work.X, maxX);
        var y = Math.Clamp(anchor.Y, work.Y, maxY);

        // 右侧 → 左侧 → 下方 → 上方；每个候选都只在一个方向上要求放得下，另一个方向夹进工作区。
        if ((long)anchor.Right + gap + panel.Width <= work.Right)
        {
            return new PixelPoint(anchor.Right + gap, y);
        }

        if ((long)anchor.X - gap - panel.Width >= work.X)
        {
            return new PixelPoint(anchor.X - gap - panel.Width, y);
        }

        if ((long)anchor.Bottom + gap + panel.Height <= work.Bottom)
        {
            return new PixelPoint(x, anchor.Bottom + gap);
        }

        if ((long)anchor.Y - gap - panel.Height >= work.Y)
        {
            return new PixelPoint(x, anchor.Y - gap - panel.Height);
        }

        return new PixelPoint(x, y);
    }
}
