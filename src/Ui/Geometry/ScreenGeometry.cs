using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **屏幕几何的唯一换算入口**：DIP 落点 → 所在屏缩放；窗口 → 可见物理外框。
/// </summary>
/// <remarks>
/// 这里只做**查询/换算**，不保存第二份布局事实：
/// <list type="bullet">
///   <item>模型位置/尺寸仍是 DIP；物理投影 = DIP × 该窗口所在屏的缩放；</item>
///   <item>屏幕归属按每块屏的 <c>Bounds ÷ 该屏 Scale</c> 得到的 DIP 矩形判断；</item>
///   <item>窗口物理外框优先 DWM 可见外框，取不到时回退 <c>Position + ClientSize × 缩放</c>。</item>
/// </list>
/// </remarks>
public static class ScreenGeometry
{
    /// <summary>按 DIP 落点解析所在屏的缩放；无匹配时回退主屏，再回退 1。</summary>
    /// <param name="screens">屏幕集合。</param>
    /// <param name="dipX">DIP X。</param>
    /// <param name="dipY">DIP Y。</param>
    /// <param name="fallbackScaling">所有屏都不包含该点时的回退缩放（通常是主屏）。</param>
    public static double ResolveScalingForDip(
        IEnumerable<Screen> screens,
        double dipX,
        double dipY,
        double fallbackScaling = 1.0)
    {
        ArgumentNullException.ThrowIfNull(screens);

        foreach (var screen in screens)
        {
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var bounds = screen.Bounds;

            if (dipX >= bounds.X / scaling
                && dipX < (bounds.X + bounds.Width) / scaling
                && dipY >= bounds.Y / scaling
                && dipY < (bounds.Y + bounds.Height) / scaling)
            {
                return scaling;
            }
        }

        return fallbackScaling > 0 ? fallbackScaling : 1.0;
    }

    /// <summary>
    /// 窗口的**可见物理外框**：优先 DWM 扩展外框；取不到时用
    /// <c>Position + ClientSize × 缩放</c>（隐藏窗口也可用，不依赖 PointToScreen）。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="frame">物理外框。</param>
    public static bool TryGetPhysicalFrame(Window window, out CanvasRect frame)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (WindowFrameProbe.TryGetVisibleFrame(window, out var visible))
        {
            frame = new CanvasRect(visible.X, visible.Y, visible.Width, visible.Height);
            return true;
        }

        var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1.0;
        var position = window.Position;
        frame = new CanvasRect(
            position.X,
            position.Y,
            window.ClientSize.Width * scaling,
            window.ClientSize.Height * scaling);
        return frame.IsValid;
    }

    /// <summary>把物理矩形换算成 DIP 矩形。</summary>
    /// <param name="physical">物理矩形。</param>
    /// <param name="scaling">缩放；≤0 按 1。</param>
    public static CanvasRect ToDip(CanvasRect physical, double scaling)
    {
        var scale = scaling > 0 ? scaling : 1.0;
        return new CanvasRect(
            physical.X / scale,
            physical.Y / scale,
            physical.Width / scale,
            physical.Height / scale);
    }

    /// <summary>把 DIP 矩形换算成物理矩形。</summary>
    /// <param name="dip">DIP 矩形。</param>
    /// <param name="scaling">缩放；≤0 按 1。</param>
    public static CanvasRect ToPhysical(CanvasRect dip, double scaling)
    {
        var scale = scaling > 0 ? scaling : 1.0;
        return new CanvasRect(
            dip.X * scale,
            dip.Y * scale,
            dip.Width * scale,
            dip.Height * scale);
    }
}
