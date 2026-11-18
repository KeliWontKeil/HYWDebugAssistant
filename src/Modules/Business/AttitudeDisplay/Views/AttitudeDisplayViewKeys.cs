namespace HYWDebugAssistant.Modules.AttitudeDisplay.Views;

/// <summary>三轴姿态视图的键（XAML/代码只经本类引用）。</summary>
public static class AttitudeDisplayViewKeys
{
    public const string Roll = AttitudeDisplayModule.RollSource;
    public const string Pitch = AttitudeDisplayModule.PitchSource;
    public const string Yaw = AttitudeDisplayModule.YawSource;
    public const string Meta = AttitudeDisplayModule.MetaSource;
    public const string Status = AttitudeDisplayModule.StatusSource;

    public const string Group = AttitudeDisplayModule.GroupSource;
    public const string RollChannel = AttitudeDisplayModule.RollChannelSource;
    public const string PitchChannel = AttitudeDisplayModule.PitchChannelSource;
    public const string YawChannel = AttitudeDisplayModule.YawChannelSource;

    public const string Style = AttitudeDisplayModule.StyleSource;
    public const string Unit = AttitudeDisplayModule.UnitSource;
    public const string Smoothing = AttitudeDisplayModule.SmoothingSource;
    public const string SmoothingMs = AttitudeDisplayModule.SmoothingMsSource;
    public const string Zoom = AttitudeDisplayModule.ZoomSource;
    public const string Grid = AttitudeDisplayModule.GridSource;
    public const string Labels = AttitudeDisplayModule.LabelsSource;
    public const string InvertRoll = AttitudeDisplayModule.InvertRollSource;
    public const string InvertPitch = AttitudeDisplayModule.InvertPitchSource;
    public const string InvertYaw = AttitudeDisplayModule.InvertYawSource;
    public const string LostZero = AttitudeDisplayModule.LostZeroSource;
    public const string LostDelay = AttitudeDisplayModule.LostDelaySource;

    public const string ResetSettings = AttitudeDisplayModule.ResetSettingsCommand;

    public const string IconSettings = "icon.settings";
    public const string IconClose = "icon.close";
}
