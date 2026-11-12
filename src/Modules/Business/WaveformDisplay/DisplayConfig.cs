namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>X 轴显示模式。</summary>
public enum DisplayXAxisMode
{
    /// <summary>模块自己的数据编号。</summary>
    Index = 0,

    /// <summary>按显式时基/声明速率换算的时间（秒）。</summary>
    Time = 1,
}

/// <summary>单通道显示配置（名称/颜色/显隐；不上线）。</summary>
public sealed record DisplayChannelConfig
{
    public string Name { get; init; } = string.Empty;
    public uint? ColorArgb { get; init; }
    public bool Visible { get; init; } = true;

    /// <summary>用户已删除该通道；保留槽位以维持帧内位置映射，不再显示或接收新数据。</summary>
    public bool Deleted { get; init; }
}

/// <summary>单组显示配置。</summary>
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

/// <summary>显示设置（随组合件持久化；与数据帧无关）。</summary>
public sealed record DisplaySettings
{
    public DisplayXAxisMode XAxisMode { get; init; } = DisplayXAxisMode.Index;

    /// <summary>跟踪窗口宽度（样本数；仅“跟踪”开启时生效）。</summary>
    public int FollowRangeSamples { get; init; } = 256;

    public double XTickStep { get; init; }
    public double YTickStep { get; init; }
    public bool Follow { get; init; } = true;
    public bool Cursors { get; init; } = true;

    /// <summary>是否用本模块声明的频率代替数据帧的 rateHz。</summary>
    public bool UseCustomRate { get; init; }

    /// <summary>自定义显示频率（Hz）；关闭开关时不参与换算。</summary>
    public double CustomRateHz { get; init; } = 1000;
}

/// <summary>波形显示配置。</summary>
public sealed record DisplayConfig
{
    public List<DisplayGroupConfig> Groups { get; init; } = [];
    public DisplaySettings Settings { get; init; } = new();
    public uint ActiveGroupId { get; init; }
    public int ActiveChannelIndex { get; init; }
    public bool KeepInactiveHistory { get; init; } = true;

    /// <summary>非活动组允许保留的样本帧合计上限（0 = 不限）。</summary>
    public int HistoryLimitSamples { get; init; }

    /// <summary>按组 ID 找显示配置（不存在 = null）。</summary>
    public DisplayGroupConfig? FindGroup(uint groupId)
    {
        foreach (var group in Groups)
        {
            if (group.GroupId == groupId)
            {
                return group;
            }
        }

        return null;
    }
}
