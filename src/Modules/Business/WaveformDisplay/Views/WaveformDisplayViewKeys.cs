namespace HYWDebugAssistant.Modules.WaveformDisplay.Views;

/// <summary>波形显示器视图的键（XAML/代码只经本类引用）。</summary>
public static class WaveformDisplayViewKeys
{
    public const string Series = WaveformDisplayModule.SeriesSource;
    public const string Meta = WaveformDisplayModule.MetaSource;
    public const string Status = WaveformDisplayModule.StatusSource;
    public const string LatestIndex = WaveformDisplayModule.LatestIndexSource;
    public const string Rate = WaveformDisplayModule.RateSource;
    public const string ImageRequest = WaveformDisplayModule.ImageRequestSource;
    public const string ImageResult = WaveformDisplayModule.ImageResultSource;
    public const string Action = WaveformDisplayModule.ActionSource;
    public const string SavePath = WaveformDisplayModule.SavePathSource;

    public const string XStart = WaveformDisplayModule.XStartSource;
    public const string XEnd = WaveformDisplayModule.XEndSource;
    public const string YMin = WaveformDisplayModule.YMinSource;
    public const string YMax = WaveformDisplayModule.YMaxSource;
    public const string YAuto = WaveformDisplayModule.YAutoSource;
    public const string Follow = WaveformDisplayModule.FollowSource;
    public const string FollowRange = WaveformDisplayModule.FollowRangeSource;
    public const string Cursors = WaveformDisplayModule.CursorsSource;
    public const string Paused = WaveformDisplayModule.PausedSource;
    public const string XAxisMode = WaveformDisplayModule.XAxisModeSource;
    public const string UseCustomRate = WaveformDisplayModule.UseCustomRateSource;
    public const string CustomRate = WaveformDisplayModule.CustomRateSource;
    public const string XTick = WaveformDisplayModule.XTickSource;
    public const string YTick = WaveformDisplayModule.YTickSource;
    public const string KeepInactive = WaveformDisplayModule.KeepInactiveSource;
    public const string HistoryLimit = WaveformDisplayModule.HistoryLimitSource;
    public const string HistoryUsed = WaveformDisplayModule.HistoryUsedSource;
    public const string ReadableTickLimits = WaveformDisplayModule.ReadableTickLimitsSource;

    public const string Clear = WaveformDisplayModule.ClearCommand;
    public const string ResetGroup = WaveformDisplayModule.ResetGroupCommand;
    public const string SaveSelection = WaveformDisplayModule.SaveSelectionCommand;
    public const string SaveAll = WaveformDisplayModule.SaveAllCommand;
    public const string SaveImage = WaveformDisplayModule.SaveImageCommand;
}
