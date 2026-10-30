using Avalonia;

namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **模块内容区的几何** —— 窗口描边与四周预留带的**唯一来源**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>内容区 = 窗口客户区 − 描边 − 预留带</b>。模块视图的坐标以内容区左上角为原点，
///     所以"内容区多大"只能有一处算法：真机、快照用例与预览都读这里，
///     否则两边差 1～2px，"快照看着行、真机却挤"就会反复出现。</item>
///   <item><b>左右预留带 = 端口标记带的厚度</b>：标记沿边向内占那么宽，内容让开的是**同一块地方**，
///     不是相加。</item>
///   <item><b>上下比左右厚</b>：标记之外，左下角还要留运行指示的位置。</item>
///   <item><see cref="ContentMargin"/> 供**没有窗口外壳的宿主**（测试/预览）使用：
///     它把描边一起算进外边距，于是那一层拿到的大小与真机内容区一致。</item>
/// </list>
/// </remarks>
public static class ModuleSurfaceMetrics
{
    /// <summary>模块窗口描边厚度（`ModuleWindow` 的 Frame）。</summary>
    public const double FrameBorder = 1;

    /// <summary>左侧预留带厚度。</summary>
    public const double InsetLeft = 20;

    /// <summary>顶部预留带厚度。</summary>
    public const double InsetTop = 22;

    /// <summary>右侧预留带厚度。</summary>
    public const double InsetRight = 20;

    /// <summary>底部预留带厚度。</summary>
    public const double InsetBottom = 24;

    /// <summary>模块视图四周的预留带（描边以内）。</summary>
    public static Thickness Inset => new(InsetLeft, InsetTop, InsetRight, InsetBottom);

    /// <summary>无外壳宿主用的外边距（预留带 + 描边）。</summary>
    public static Thickness ContentMargin => new(
        InsetLeft + FrameBorder,
        InsetTop + FrameBorder,
        InsetRight + FrameBorder,
        InsetBottom + FrameBorder);

    /// <summary>按窗口客户区尺寸算出内容区尺寸。</summary>
    /// <param name="windowWidth">窗口客户区宽。</param>
    /// <param name="windowHeight">窗口客户区高。</param>
    public static Size ContentSize(double windowWidth, double windowHeight) => new(
        Math.Max(0, windowWidth - (2 * FrameBorder) - InsetLeft - InsetRight),
        Math.Max(0, windowHeight - (2 * FrameBorder) - InsetTop - InsetBottom));
}
