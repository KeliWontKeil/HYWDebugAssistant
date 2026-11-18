namespace HYWDebugAssistant.Modules.GaugeDisplay.Views;

/// <summary>仪表盘视图的键（XAML/代码只经本类引用）。</summary>
public static class GaugeDisplayViewKeys
{
    public const string Value = GaugeDisplayModule.ValueSource;
    public const string Meta = GaugeDisplayModule.MetaSource;
    public const string Status = GaugeDisplayModule.StatusSource;

    public const string Group = GaugeDisplayModule.GroupSource;
    public const string Channel = GaugeDisplayModule.ChannelSource;

    public const string Minimum = GaugeDisplayModule.MinimumSource;
    public const string Maximum = GaugeDisplayModule.MaximumSource;
    public const string AutoZero = GaugeDisplayModule.AutoZeroSource;
    public const string ZeroDelay = GaugeDisplayModule.ZeroDelaySource;
    public const string Title = GaugeDisplayModule.TitleSource;
    public const string Unit = GaugeDisplayModule.UnitSource;
    public const string Smoothing = GaugeDisplayModule.SmoothingSource;
    public const string SmoothingMs = GaugeDisplayModule.SmoothingMsSource;
    public const string Decimals = GaugeDisplayModule.DecimalsSource;

    public const string ResetSettings = GaugeDisplayModule.ResetSettingsCommand;

    public const string IconSettings = "icon.settings";
    public const string IconClose = "icon.close";
}
