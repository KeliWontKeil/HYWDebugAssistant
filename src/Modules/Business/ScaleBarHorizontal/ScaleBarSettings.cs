namespace HYWDebugAssistant.Modules.ScaleBarHorizontal;

/// <summary>横向进度条设置（值声明负责持久化；这里只是运行期快照）。</summary>
internal sealed record ScaleBarSettings
{
    /// <summary>回零延时下限（毫秒）。</summary>
    public const int MinimumZeroDelayMs = 0;

    /// <summary>回零延时上限（毫秒）。</summary>
    public const int MaximumZeroDelayMs = 600_000;

    /// <summary>平滑时长下限（毫秒）。</summary>
    public const int MinimumSmoothingMs = 0;

    /// <summary>平滑时长上限（毫秒）。</summary>
    public const int MaximumSmoothingMs = 2_000;

    /// <summary>小数位下限。</summary>
    public const int MinimumDecimals = 0;

    /// <summary>小数位上限。</summary>
    public const int MaximumDecimals = 4;

    /// <summary>通道号下限（用户编号 1 基）。</summary>
    public const int MinimumChannel = 1;

    /// <summary>通道号上限（用户编号 1 基；内部槽位 = Channel - 1）。</summary>
    public const int MaximumChannel = WaveformFrameContract.MaxChannels;

    /// <summary>组号（1 基正整数；无效时等待该组，不自动跟随）。</summary>
    public int GroupId { get; init; } = 1;

    /// <summary>通道号（1 基正整数；内部帧槽位 = Channel - 1）。</summary>
    public int Channel { get; init; } = 1;

    /// <summary>量程下限。</summary>
    public double Minimum { get; init; }

    /// <summary>量程上限。</summary>
    public double Maximum { get; init; } = 100;

    /// <summary>是否自动回零。</summary>
    public bool AutoZero { get; init; } = true;

    /// <summary>回零延时（毫秒；0 = 下一拍即回零）。</summary>
    public int ZeroDelayMs { get; init; } = 800;

    /// <summary>标题。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>单位。</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>填充起点（true = 从零开始）。</summary>
    public bool FillFromZero { get; init; } = true;

    /// <summary>填充颜色（<c>AARRGGBB</c>；空 = 主题默认）。</summary>
    public string Color { get; init; } = string.Empty;

    /// <summary>是否平滑。</summary>
    public bool Smoothing { get; init; } = true;

    /// <summary>平滑时长（毫秒）。</summary>
    public int SmoothingMs { get; init; } = 120;

    /// <summary>读数小数位。</summary>
    public int Decimals { get; init; } = 2;

    /// <summary>模块默认设置（同时是 XAML 预览初值的镜像）。</summary>
    public static ScaleBarSettings Default { get; } = new();
}
