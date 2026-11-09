namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **一个不可变的序列窗口快照**：模块一次性发布某范围内某一通道的全部样本。
/// </summary>
/// <remarks>
/// <para>
/// 它替代“环形缓冲 + UI 侧降采样”的曲线通道：快照里的点就是范围内的全部点，
/// 没有容量上限、没有抽稀、没有“只取最后 N 点”。视图直接读取并绘制。
/// </para>
/// <para>
/// <b>不可变</b>：发布后模块不得再改 <see cref="SeriesWindowChannel.Values"/>；
/// 需要更新时构建一份新的快照并整体替换引用。视图可以跨帧持有这份引用。
/// </para>
/// <para>
/// 它只描述“数值序列 + 窗口范围 + 有效 y 范围”，不携带组/通道/波形语义；
/// 具体含义由发布它的模块决定。
/// </para>
/// </remarks>
/// <param name="Version">快照版本号（每次发布递增；视图据此判断是否需要重绘）。</param>
/// <param name="XRangeStart">窗口起点（模块定义的 x 单位）。</param>
/// <param name="XRangeEnd">窗口终点（模块定义的 x 单位）。</param>
/// <param name="LatestIndex">当前最新数据位置（模块定义的域；用于数据轴域）。</param>
/// <param name="RateHz">有效速率（Hz；&lt;=0 = 未知）。</param>
/// <param name="YMin">有效 y 下限。</param>
/// <param name="YMax">有效 y 上限。</param>
/// <param name="YAuto">y 范围是否由模块自动计算。</param>
/// <param name="Channels">窗口内的通道（只含可见通道）。</param>
public sealed record SeriesWindowSnapshot(
    long Version,
    long XRangeStart,
    long XRangeEnd,
    long LatestIndex,
    double RateHz,
    double YMin,
    double YMax,
    bool YAuto,
    IReadOnlyList<SeriesWindowChannel> Channels);

/// <summary>序列窗口中的一个通道。</summary>
/// <param name="Index">通道在组内的序号。</param>
/// <param name="Name">显示名。</param>
/// <param name="ColorArgb">曲线颜色（<c>0xAARRGGBB</c>；null = 使用默认色板）。</param>
/// <param name="Values">该通道在窗口内的全部样本；长度 = 窗口宽度。</param>
public sealed record SeriesWindowChannel(
    int Index,
    string Name,
    uint? ColorArgb,
    float[] Values);
