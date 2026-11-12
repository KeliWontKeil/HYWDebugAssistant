namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// 曲线图拖动产生的**目标视口范围**（X/Y 均为当前轴单位）。
/// </summary>
/// <remarks>
/// 控件只表达“用户拖动后希望看到的范围”，不负责夹紧、跟踪或 Y 自动策略；
/// 这些业务规则由模块根据快照和数据范围决定。
/// </remarks>
public sealed class ChartDragEventArgs : EventArgs
{
    /// <summary>创建拖动目标范围。</summary>
    /// <param name="xStart">目标 X 起点（轴单位）。</param>
    /// <param name="xEnd">目标 X 终点（轴单位）。</param>
    /// <param name="yMin">目标 Y 下限。</param>
    /// <param name="yMax">目标 Y 上限。</param>
    public ChartDragEventArgs(double xStart, double xEnd, double yMin, double yMax)
    {
        XStart = xStart;
        XEnd = xEnd;
        YMin = yMin;
        YMax = yMax;
    }

    /// <summary>目标 X 起点（Index 或 Time 轴单位）。</summary>
    public double XStart { get; }

    /// <summary>目标 X 终点（Index 或 Time 轴单位）。</summary>
    public double XEnd { get; }

    /// <summary>目标 Y 下限。</summary>
    public double YMin { get; }

    /// <summary>目标 Y 上限。</summary>
    public double YMax { get; }
}
