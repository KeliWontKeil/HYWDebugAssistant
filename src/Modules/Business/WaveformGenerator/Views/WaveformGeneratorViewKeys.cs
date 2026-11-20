namespace HYWDebugAssistant.Modules.WaveformGenerator.Views;

/// <summary>波形发生器视图的键（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class WaveformGeneratorViewKeys
{
    public const string Preview = WaveformGeneratorModule.PreviewSource;
    public const string Status = WaveformGeneratorModule.StatusSource;
    public const string Snapshot = WaveformGeneratorModule.SnapshotSource;

    public const string GroupName = WaveformGeneratorModule.GroupNameSource;
    public const string GroupId = WaveformGeneratorModule.GroupIdSource;
    public const string ChannelNumber = WaveformGeneratorModule.ChannelNumberSource;
    public const string ChannelName = WaveformGeneratorModule.ChannelNameSource;
    public const string Waveform = WaveformGeneratorModule.WaveformSource;
    public const string Frequency = WaveformGeneratorModule.FrequencySource;
    public const string Period = WaveformGeneratorModule.PeriodSource;
    public const string Phase = WaveformGeneratorModule.PhaseSource;
    public const string Offset = WaveformGeneratorModule.OffsetSource;
    public const string Amplitude = WaveformGeneratorModule.AmplitudeSource;
    public const string Resolution = WaveformGeneratorModule.ResolutionSource;
    public const string Duty = WaveformGeneratorModule.DutySource;
    public const string CarrierFrequency = WaveformGeneratorModule.CarrierFrequencySource;
    public const string ModulationIndex = WaveformGeneratorModule.ModulationIndexSource;
    public const string CarrierPhase = WaveformGeneratorModule.CarrierPhaseSource;
    public const string ChannelEnabled = WaveformGeneratorModule.ChannelEnabledSource;
    public const string GroupEnabled = WaveformGeneratorModule.GroupEnabledSource;
    public const string OneShotPeriods = WaveformGeneratorModule.OneShotPeriodsSource;
    public const string Continuous = WaveformGeneratorModule.ContinuousSource;
    public const string Action = WaveformGeneratorModule.ActionSource;

    public const string IconAdd = "icon.edit.add";
    public const string IconRemove = "icon.edit.remove";
    public const string IconTrash = "icon.edit.trash";

    public const string AddGroup = WaveformGeneratorModule.AddGroupCommand;
    public const string RemoveGroup = WaveformGeneratorModule.RemoveGroupCommand;
    public const string AddChannel = WaveformGeneratorModule.AddChannelCommand;
    public const string RemoveChannel = WaveformGeneratorModule.RemoveChannelCommand;
    public const string SendOnce = WaveformGeneratorModule.SendOnceCommand;
    public const string StartContinuous = WaveformGeneratorModule.StartContinuousCommand;
    public const string Stop = WaveformGeneratorModule.StopCommand;
}
