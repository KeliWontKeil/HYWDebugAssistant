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
///   <item><b>窗口位置 = 整数物理像素</b>：<see cref="ToPixel"/> 只在**窗口边界**用一次
///     （<c>ModuleWindowManager.ApplyGeometry</c>：DIP × RenderScaling → <c>PixelPoint</c>）。</item>
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
}
