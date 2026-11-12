namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **屏幕几何的量化口径**（唯一一处）。
/// </summary>
/// <remarks>
/// <para>
/// <b>量化点只有一个：窗口边界的整数物理像素。</b>模型位置仍是 DIP（见 <c>ModuleLayoutView</c>），
/// 但它由整数物理像素**派生**：<c>X = pixel / Scaling</c>。因此模型里允许出现小数 DIP，
/// 而 <c>X * Scaling</c> 必须落在整数像素上 —— 拖动、吸附、落位都按这个口径。
/// </para>
/// <para>
/// <b>为什么不再对 DIP 独立取整</b>：缩放 ≠100% 时，整 DIP 乘回缩放仍是小数（125% 下 1 DIP = 1.25 px）；
/// 相邻窗口各自把 DIP 取整再取整像素，公共边就会差 1px。把量化放在物理像素上，
/// 组内成员共用同一个整数位移，贴合关系不会被舍入破坏。
/// </para>
/// <para>
/// <b>窗口位置与尺寸来自同一个整数矩形</b>：<see cref="PixelRectOf"/> 把 DIP 矩形的四条边各自量化成
/// <see cref="PixelEdges"/>；窗口位置取 <c>Left/Top</c>，尺寸由 <c>(Right-Left)/scaling</c> 反推，
/// 不再各自取整。
/// </para>
/// </remarks>
public static class ScreenPixels
{
    /// <summary>把**物理像素**值取整为窗口位置用的整数点坐标。</summary>
    /// <param name="pixels">物理像素值。</param>
    /// <returns>整数像素坐标。</returns>
    public static int ToPixel(double pixels) => double.IsFinite(pixels)
        ? (int)Math.Round(pixels, MidpointRounding.AwayFromZero)
        : 0;

    /// <summary>把整数**物理像素**换算成该窗口的 DIP（模型坐标）——与 <see cref="ToPixel"/> 互逆。</summary>
    /// <param name="pixel">整数物理像素（窗口位置）。</param>
    /// <param name="scaling">该窗口所在屏的缩放；≤0 / 非有限值按 1.0。</param>
    /// <returns>DIP 坐标（可能带小数；乘回缩放后应回到原像素）。</returns>
    public static double DipOfPixel(int pixel, double scaling)
    {
        var scale = double.IsFinite(scaling) && scaling > 0 ? scaling : 1.0;
        return pixel / scale;
    }

    /// <summary>
    /// 把 DIP 矩形落成一个**整数物理像素矩形**（四边各自 <see cref="ToPixel"/>）。
    /// </summary>
    /// <param name="rect">DIP 矩形（位置 + 尺寸）。</param>
    /// <param name="scaling">该窗口所在屏的缩放；≤0 / 非有限值按 1.0。</param>
    /// <remarks>
    /// 位置与尺寸必须由**同一个整数矩形**推出：窗口取 <c>Left/Top</c>，尺寸取
    /// <c>(Right-Left)/scaling</c>。若位置与尺寸各自「DIP × scaling 再取整」，
    /// <c>round(x·s) + round(w·s)</c> 可能不等于 <c>round((x+w)·s)</c> ⇒ 相邻两窗的公共边差 1px。
    /// 矩形无效（NaN 等）不抛：<see cref="ToPixel"/> 归零、按退化矩形返回。
    /// </remarks>
    public static PixelEdges PixelRectOf(CanvasRect rect, double scaling)
    {
        var scale = double.IsFinite(scaling) && scaling > 0 ? scaling : 1.0;

        return new PixelEdges(
            ToPixel(rect.Left * scale),
            ToPixel(rect.Top * scale),
            ToPixel(rect.Right * scale),
            ToPixel(rect.Bottom * scale));
    }
}

/// <summary>一个整数物理像素矩形的四条边（位置与尺寸由它一起推出）。</summary>
/// <param name="Left">左边缘（物理像素）。</param>
/// <param name="Top">上边缘（物理像素）。</param>
/// <param name="Right">右边缘（物理像素）。</param>
/// <param name="Bottom">下边缘（物理像素）。</param>
public readonly record struct PixelEdges(int Left, int Top, int Right, int Bottom);
