namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **屏幕物理像素的取整口径**（唯一一处）。
/// </summary>
/// <remarks>
/// 【用户第十二轮反馈 · `architecture/15` §3.1】模型几何（X/Y/宽/高）一律以**整数物理像素**表达：
/// 屏幕上的窗口只能落在整数像素上，若模型存小数（例如 125% 缩放下 330 DIP = 412.5px），
/// 则模型里的"贴合"在屏幕上会各自取整成 0px/1px 的缝或叠——表现为"经常有缝或错位"。
/// **所有**几何在写入模型之前都必须经过本类，禁止各自 <c>Math.Round</c>（口径不一致就等于没有口径）。
/// </remarks>
public static class ScreenPixels
{
    /// <summary>把物理像素坐标/尺寸取整到整数值（`AwayFromZero`：0.5 → 1，避免"银行家舍入"在正负方向上不一致）。</summary>
    /// <param name="value">物理像素值（可为小数）。</param>
    /// <returns>整数物理像素（仍以 <see cref="double"/> 返回，便于与既有几何运算混用）。</returns>
    public static double Round(double value) => double.IsFinite(value)
        ? Math.Round(value, MidpointRounding.AwayFromZero)
        : value;

    /// <summary>把物理像素值取整为窗口位置用的整数点坐标。</summary>
    /// <param name="value">物理像素值。</param>
    /// <returns>整数像素坐标。</returns>
    public static int ToPixel(double value) => double.IsFinite(value)
        ? (int)Math.Round(value, MidpointRounding.AwayFromZero)
        : 0;
}
