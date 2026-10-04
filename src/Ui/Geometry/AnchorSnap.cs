namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **网络标签（端口锚点）自身的吸附**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>与"连接对齐"分工</b>：连接对齐（<c>IRuntimeQuery.SuggestAnchorDock</c>）回答的是
///     "**吸到哪里就能连上**"（判据在 Core，与建网同源）；本类回答的是"**与模块中线对齐**"——
///     一个纯排版的诉求，与能否建网无关。前者优先（能连上就是最该去的位置），本类是**退一步的对齐辅助**；</item>
///   <item><b>纯函数</b>：只由"归一化偏移 + 沿边长 + 容差"决定，无状态、可单测；</item>
///   <item><b>容差复用设置里的「吸附距离」</b>（`ApplicationSettings.SnapDistance`）：用户对"多近才吸"
///     的偏好是全局面一致的，不另起一个只对标签生效的数字。</item>
/// </list>
/// </remarks>
public static class AnchorSnap
{
    /// <summary>**中线**在沿边归一化坐标里的值（= 边缘中点）。</summary>
    public const double Center = 0.5;

    /// <summary>
    /// 把沿边归一化偏移吸到**模块中线**（0.5）：离得够近 ⇒ 吸过去；否则**原样返回**。
    /// </summary>
    /// <param name="offset">指针算出来的归一化偏移（未吸附）。</param>
    /// <param name="extentDip">该边方向的**长度**（DIP；≤ 0 时不做吸附，避免除零/无意义吸附）。</param>
    /// <param name="toleranceDip">容差（DIP）。</param>
    /// <returns>吸附后的偏移。</returns>
    /// <remarks>
    /// 「够近」用的是**像素距离**而不是归一化距离：标记越短，同样的归一化差代表的像素越小
    /// （用归一化容差会出现"窗口越小越吸不住"这种说不清的行为）。
    /// </remarks>
    public static double ToEdgeCenter(double offset, double extentDip, double toleranceDip)
    {
        if (extentDip <= 0 || toleranceDip <= 0)
        {
            return offset;
        }

        var clamped = Math.Clamp(offset, 0, 1);
        return Math.Abs(clamped - Center) * extentDip <= toleranceDip ? Center : offset;
    }

    /// <summary>该偏移是否**已经在**中线上（用于提示文案：只有真的吸上去才提示一次）。</summary>
    /// <param name="offset">归一化偏移。</param>
    public static bool IsAtCenter(double offset) => Math.Abs(offset - Center) < 1e-9;
}
