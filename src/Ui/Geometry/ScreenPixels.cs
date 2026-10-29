namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **屏幕几何的取整口径**（唯一一处）。
/// </summary>
/// <remarks>
/// <para>
/// 两件事，尺度不同，都在这里：
/// <list type="number">
///   <item><b>模型几何 = DIP</b>（见 <c>ModuleLayoutView</c>）：<see cref="RoundDip"/> 把模块坐标/尺寸
///     取整到**整 DIP** —— 拖动与吸附在整数格上做，避免"贴齐"在缩放 ≠100% 时差几个物理像素
///     （小数 DIP 一旦乘上缩放再取整，舍入误差在两端各自累积 ⇒ 看得见的缝）。</item>
///   <item><b>窗口边界 = 一个整数物理矩形</b>：<see cref="PixelRectOf"/> 把 DIP 矩形的**四条边各自取整**
///     成 <see cref="PixelEdges"/>；窗口位置取 <c>Left/Top</c>，尺寸由 <c>(Right-Left)/scaling</c> 反推
///     —— 位置与尺寸来自**同一个派生值**，不再各自取整（那样在 125%/150% 下相邻窗口的公共边会差 1px）。</item>
/// </list>
/// </para>
/// <para>
/// 【用户第十二轮反馈 · `architecture/15` §3.1】口径必须只有一处：**所有**几何在写入模型之前都要经过本类，
/// 禁止各自 <c>Math.Round</c>（口径不一致就等于没有口径）。
/// </para>
/// </remarks>
public static class ScreenPixels
{
    /// <summary>把 **DIP** 坐标/尺寸取整到整数值（`AwayFromZero`：0.5 → 1，避免"银行家舍入"在正负方向上不一致）。</summary>
    /// <param name="dip">DIP 值（可为小数）。</param>
    /// <returns>整 DIP（仍以 <see cref="double"/> 返回，便于与既有几何运算混用）。</returns>
    public static double RoundDip(double dip) => double.IsFinite(dip)
        ? Math.Round(dip, MidpointRounding.AwayFromZero)
        : dip;

    /// <summary>把**物理像素**值取整为窗口位置用的整数点坐标。</summary>
    /// <param name="pixels">物理像素值。</param>
    /// <returns>整数像素坐标。</returns>
    public static int ToPixel(double pixels) => double.IsFinite(pixels)
        ? (int)Math.Round(pixels, MidpointRounding.AwayFromZero)
        : 0;

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
