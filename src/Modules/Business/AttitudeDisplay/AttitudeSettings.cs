using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.AttitudeDisplay;

/// <summary>角度输入的原始单位。</summary>
internal enum AttitudeAngleUnit
{
    /// <summary>度。</summary>
    Degree = 0,

    /// <summary>弧度。</summary>
    Radian = 1,
}

/// <summary>三轴姿态设置（值声明负责持久化；这里只是运行期快照）。</summary>
internal sealed record AttitudeSettings
{
    /// <summary>平滑时长下限（毫秒）。</summary>
    public const int MinimumSmoothingMs = 0;

    /// <summary>平滑时长上限（毫秒）。</summary>
    public const int MaximumSmoothingMs = 2_000;

    /// <summary>失联归零延时下限（毫秒）。</summary>
    public const int MinimumLostDelayMs = 0;

    /// <summary>失联归零延时上限（毫秒）。</summary>
    public const int MaximumLostDelayMs = 600_000;

    /// <summary>缩放下限。</summary>
    public const double MinimumZoom = 0.3;

    /// <summary>缩放上限。</summary>
    public const double MaximumZoom = 3;

    /// <summary>组号（0 = 自动跟随第一个出现的组）。</summary>
    public int GroupId { get; init; }

    /// <summary>横滚通道号（0 起）。</summary>
    public int RollChannel { get; init; }

    /// <summary>俯仰通道号（0 起）。</summary>
    public int PitchChannel { get; init; } = 1;

    /// <summary>偏航通道号（0 起）。</summary>
    public int YawChannel { get; init; } = 2;

    /// <summary>渲染物。</summary>
    public AttitudeRenderStyle Style { get; init; } = AttitudeRenderStyle.Cube;

    /// <summary>输入角度单位。</summary>
    public AttitudeAngleUnit Unit { get; init; } = AttitudeAngleUnit.Degree;

    /// <summary>是否平滑。</summary>
    public bool Smoothing { get; init; } = true;

    /// <summary>平滑时长（毫秒）。</summary>
    public int SmoothingMs { get; init; } = 120;

    /// <summary>缩放。</summary>
    public double Zoom { get; init; } = 1;

    /// <summary>是否画地面网格。</summary>
    public bool ShowGrid { get; init; } = true;

    /// <summary>是否画轴标签。</summary>
    public bool ShowLabels { get; init; } = true;

    /// <summary>反转横滚。</summary>
    public bool InvertRoll { get; init; }

    /// <summary>反转俯仰。</summary>
    public bool InvertPitch { get; init; }

    /// <summary>反转偏航。</summary>
    public bool InvertYaw { get; init; }

    /// <summary>失联后是否归零到单位姿态。</summary>
    public bool LostZero { get; init; } = true;

    /// <summary>失联归零延时（毫秒）。</summary>
    public int LostDelayMs { get; init; } = 1_000;

    /// <summary>模块默认设置（同时是 XAML 预览初值的镜像）。</summary>
    public static AttitudeSettings Default { get; } = new();
}
