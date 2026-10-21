namespace HYWDebugAssistant.Modules.DataMonitor.Views;

/// <summary>**数据显示器视图的键**（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class DataMonitorViewKeys
{
    public const string Log = DataMonitorModule.LogSource;
    public const string Paused = DataMonitorModule.PausedSource;
    public const string Wrap = DataMonitorModule.LineBreakSource;
    public const string HexView = DataMonitorModule.HexViewSource;
    public const string Timestamp = DataMonitorModule.TimestampSource;
    public const string Status = DataMonitorModule.StatusSource;
    public const string Clear = DataMonitorModule.ClearCommand;
    public const string Save = DataMonitorModule.SaveCommand;
}
