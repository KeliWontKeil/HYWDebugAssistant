namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>X 轴显示模式。</summary>
public enum DisplayXAxisMode
{
    /// <summary>模块自己的数据编号。</summary>
    Index = 0,

    /// <summary>按显式时基/声明速率换算的时间（秒）。</summary>
    Time = 1,
}

/// <summary>单通道运行期显示状态（名称/颜色/显隐；不是持久化对象）。</summary>
public sealed record DisplayChannelConfig
{
    public string Name { get; init; } = string.Empty;
    public uint? ColorArgb { get; init; }
    public bool Visible { get; init; } = true;

    /// <summary>本次显示中已移除该通道；槽位保留以维持帧内位置映射，下一帧仍报告时自动恢复。</summary>
    public bool Deleted { get; init; }

    /// <summary>该槽位是否有有效样本历史；空洞不建行。</summary>
    public bool Known { get; init; }
}

/// <summary>单组运行期显示状态（数据帧发现组时才存在；不是持久化对象）。</summary>
public sealed record DisplayGroupConfig
{
    public uint GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<DisplayChannelConfig> Channels { get; init; } = [];

    /// <summary>Y 轴是否自动。</summary>
    public bool YAuto { get; init; } = true;

    /// <summary>手动 Y 下限（自动时作为关闭自动后的初值）。</summary>
    public double YMin { get; init; } = -1;

    /// <summary>手动 Y 上限。</summary>
    public double YMax { get; init; } = 1;

    /// <summary>Y 自适应使用的通道索引；-1 = 全部可见通道。</summary>
    public int YFitChannel { get; init; } = -1;
}

/// <summary>显示设置（值声明负责持久化；这里只是运行期快照）。</summary>
public sealed record DisplaySettings
{
    public DisplayXAxisMode XAxisMode { get; init; } = DisplayXAxisMode.Index;

    /// <summary>跟踪窗口宽度（样本数；仅“跟踪”开启时生效）。</summary>
    public int FollowRangeSamples { get; init; } = 256;

    public double XTickStep { get; init; }
    public double YTickStep { get; init; }
    public bool Follow { get; init; } = true;
    public bool Cursors { get; init; } = true;

    /// <summary>暂停接收全部通道组的新帧；已显示的历史与视图保持不动。</summary>
    public bool Paused { get; init; }

    /// <summary>是否用本模块声明的频率代替数据帧的 rateHz。</summary>
    public bool UseCustomRate { get; init; }

    /// <summary>自定义显示频率（Hz）；关闭开关时不参与换算。</summary>
    public double CustomRateHz { get; init; } = 1000;

    /// <summary>是否保留非活动组历史。</summary>
    public bool KeepInactiveHistory { get; init; } = true;

    /// <summary>非活动组允许保留的样本帧合计上限（0 = 不限）。</summary>
    public int HistoryLimitSamples { get; init; }
}
