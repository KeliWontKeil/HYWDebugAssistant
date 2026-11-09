using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>图表的一路序列。</summary>
/// <remarks>
/// 视图创建一次并复用；只改 <see cref="Values"/> 的内容和 <see cref="Count"/>，
/// 再抬 <see cref="ChartSurface.DataVersion"/>，不要每帧换对象。
/// </remarks>
public sealed class ChartSeries
{
    /// <summary>序列名（只用于调用方自己的图例/诊断；图表不画图例）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>固定色；<see langword="null"/> = 按索引取主题曲线色板。</summary>
    public Color? Color { get; set; }

    /// <summary>
    /// 点的容器（长度至少 <see cref="Count"/>）。
    /// </summary>
    /// <remarks>
    /// 使用只读内存是为了让“全量窗口快照”的数组可以被直接共享，不需要为每个通道再复制一份；
    /// 调用方发布后不得改写底层数组。
    /// </remarks>
    public ReadOnlyMemory<float> Values { get; set; } = ReadOnlyMemory<float>.Empty;

    /// <summary>有效点数（≤ <see cref="Values"/> 长度）。</summary>
    public int Count { get; set; }

    /// <summary>是否绘制。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>线宽（DIP）。</summary>
    public double Thickness { get; set; } = 1.4;
}
