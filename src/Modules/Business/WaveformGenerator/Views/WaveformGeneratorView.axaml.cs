using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.WaveformGenerator.Views;

/// <summary>
/// 波形发生器视图：组段 → 通道段 → 波形参数，全部固定坐标常显。
/// </summary>
/// <remarks>
/// 预览器不会调用 <see cref="Refresh"/>；因此参数控件不能靠运行时 <c>IsVisible</c> 才出现。
/// 数据只决定输入值与 <c>IsEnabled</c>，不决定控件是否存在。
/// </remarks>
public sealed partial class WaveformGeneratorView : ModuleView, IModuleViewElement
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<ChartSeries> _seriesList = [];
    private readonly ChartSurface? _chart;
    private readonly ModuleSegmentedControl? _groupChips;
    private readonly ModuleSegmentedControl? _channelChips;
    private readonly ModuleSegmentedControl? _waveformType;
    private readonly ModuleTextSurface? _groupNameInput;
    private readonly ModuleTextSurface? _groupIdInput;
    private readonly ModuleLabel? _groupRateLabel;
    private readonly ModuleTextSurface? _channelNameInput;
    private readonly ModuleToggle? _channelEnabledToggle;
    private readonly ModuleTextSurface? _frequencyInput;
    private readonly ModuleTextSurface? _periodInput;
    private readonly ModuleTextSurface? _phaseInput;
    private readonly ModuleTextSurface? _offsetInput;
    private readonly ModuleTextSurface? _amplitudeInput;
    private readonly ModuleTextSurface? _resolutionInput;
    private readonly ModuleTextSurface? _dutyInput;
    private readonly ModuleTextSurface? _carrierFrequencyInput;
    private readonly ModuleTextSurface? _modulationIndexInput;
    private readonly ModuleTextSurface? _carrierPhaseInput;
    private readonly ModuleTextSurface? _periodsInput;
    private readonly ModuleLabel? _estimateLabel;
    private readonly ModuleLabel? _totalEstimateLabel;
    private readonly StackPanel? _parameterRows;
    private readonly Grid? _baseParameterRow;
    private readonly Control? _phaseCell;
    private readonly Grid? _dutyParameterRow;
    private readonly Grid? _carrierParameterRow;
    private readonly Control?[] _channelControls;

    private IModuleViewContext? _context;
    private long _snapshotRevision = -1;
    private long _previewVersion = -1;
    private int _activeGroupIndex;
    private int _activeChannelIndex = -1;
    private bool _syncingGroups;
    private bool _syncingChannels;

    /// <summary>构造视图。</summary>
    public WaveformGeneratorView()
    {
        AvaloniaXamlLoader.Load(this);
        _chart = this.FindControl<ChartSurface>("Chart");
        _groupChips = this.FindControl<ModuleSegmentedControl>("GroupChips");
        _channelChips = this.FindControl<ModuleSegmentedControl>("ChannelChips");
        _waveformType = this.FindControl<ModuleSegmentedControl>("WaveformType");
        _groupNameInput = this.FindControl<ModuleTextSurface>("GroupNameInput");
        _groupIdInput = this.FindControl<ModuleTextSurface>("GroupIdInput");
        _groupRateLabel = this.FindControl<ModuleLabel>("GroupRateLabel");
        _channelNameInput = this.FindControl<ModuleTextSurface>("ChannelNameInput");
        _channelEnabledToggle = this.FindControl<ModuleToggle>("ChannelEnabledToggle");
        _frequencyInput = this.FindControl<ModuleTextSurface>("FrequencyInput");
        _periodInput = this.FindControl<ModuleTextSurface>("PeriodInput");
        _phaseInput = this.FindControl<ModuleTextSurface>("PhaseInput");
        _offsetInput = this.FindControl<ModuleTextSurface>("OffsetInput");
        _amplitudeInput = this.FindControl<ModuleTextSurface>("AmplitudeInput");
        _resolutionInput = this.FindControl<ModuleTextSurface>("ResolutionInput");
        _dutyInput = this.FindControl<ModuleTextSurface>("DutyInput");
        _carrierFrequencyInput = this.FindControl<ModuleTextSurface>("CarrierFrequencyInput");
        _modulationIndexInput = this.FindControl<ModuleTextSurface>("ModulationIndexInput");
        _carrierPhaseInput = this.FindControl<ModuleTextSurface>("CarrierPhaseInput");
        _periodsInput = this.FindControl<ModuleTextSurface>("PeriodsInput");
        _estimateLabel = this.FindControl<ModuleLabel>("EstimateLabel");
        _totalEstimateLabel = this.FindControl<ModuleLabel>("TotalEstimateLabel");
        _parameterRows = this.FindControl<StackPanel>("ParameterRows");
        _baseParameterRow = this.FindControl<Grid>("BaseParameterRow");
        _phaseCell = this.FindControl<Grid>("PhaseCell");
        _dutyParameterRow = this.FindControl<Grid>("DutyParameterRow");
        _carrierParameterRow = this.FindControl<Grid>("CarrierParameterRow");

        _channelControls =
        [
            _channelNameInput,
            _channelEnabledToggle,
            _waveformType,
            _phaseInput,
            _offsetInput,
            _amplitudeInput,
            _dutyInput,
            _carrierFrequencyInput,
            _modulationIndexInput,
            _carrierPhaseInput,
        ];

        if (_chart is not null)
        {
            _chart.Series = _seriesList;
        }

        if (_groupChips is not null)
        {
            _groupChips.OptionSelected += OnGroupSelected;
        }

        if (_channelChips is not null)
        {
            _channelChips.OptionSelected += OnChannelSelected;
        }

        if (_waveformType is not null)
        {
            _waveformType.Options =
            [
                new ModuleSegmentOption("sine", Tr(WaveformGeneratorText.Sine), "icon.wave.sine", ToolTip: Tr(WaveformGeneratorText.Sine)),
                new ModuleSegmentOption("square", Tr(WaveformGeneratorText.Square), "icon.wave.square", ToolTip: Tr(WaveformGeneratorText.Square)),
                new ModuleSegmentOption("triangle", Tr(WaveformGeneratorText.Triangle), "icon.wave.triangle", ToolTip: Tr(WaveformGeneratorText.Triangle)),
                new ModuleSegmentOption("saddle", Tr(WaveformGeneratorText.Saddle), "icon.wave.saddle", ToolTip: Tr(WaveformGeneratorText.Saddle)),
                new ModuleSegmentOption("spwm", "SPWM", "icon.wave.spwm", ToolTip: "SPWM"),
                new ModuleSegmentOption("svpwm", "SVPWM", "icon.wave.svpwm", ToolTip: "SVPWM"),
                new ModuleSegmentOption("noise", Tr(WaveformGeneratorText.Noise), "icon.wave.noise", ToolTip: Tr(WaveformGeneratorText.Noise)),
            ];
        }

    }

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(WaveformGeneratorView).Assembly, key);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;
        ReadSnapshot(context);
        RefreshPreview(context);
    }

    private void ReadSnapshot(IModuleViewContext context)
    {
        var snapshot = context.ReadText(WaveformGeneratorViewKeys.Snapshot);
        if (snapshot.Revision == _snapshotRevision || snapshot.Lines.Count == 0)
        {
            return;
        }

        _snapshotRevision = snapshot.Revision;

        try
        {
            var model = JsonSerializer.Deserialize<GeneratorSnapshot>(
                string.Concat(snapshot.Lines),
                SnapshotJsonOptions);
            if (model is null || model.Groups.Count == 0)
            {
                return;
            }

            ApplySnapshot(model);
        }
        catch (JsonException)
        {
            // 坏快照不打断刷新；控件保持常显，不因数据错误整片消失。
        }
    }

    private void ApplySnapshot(GeneratorSnapshot snapshot)
    {
        _activeGroupIndex = Math.Clamp(snapshot.ActiveGroupIndex, 0, snapshot.Groups.Count - 1);
        var group = snapshot.Groups[_activeGroupIndex];
        _activeChannelIndex = snapshot.ActiveChannelIndex >= 0
            && snapshot.ActiveChannelIndex < group.Channels.Count
            ? snapshot.ActiveChannelIndex
            : -1;

        UpdateGroupChips(snapshot);
        UpdateChannelChips(group);
        UpdateSettings(group);
        UpdateEstimate(snapshot);
    }

    private void UpdateGroupChips(GeneratorSnapshot snapshot)
    {
        if (_groupChips is null)
        {
            return;
        }

        var options = new List<ModuleSegmentOption>(snapshot.Groups.Count);
        foreach (var group in snapshot.Groups)
        {
            options.Add(new ModuleSegmentOption(
                group.Index.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(group.Name) ? "G" + (group.Index + 1).ToString(CultureInfo.InvariantCulture) : group.Name,
                ToolTip: "G" + group.Id.ToString(CultureInfo.InvariantCulture)));
        }

        _syncingGroups = true;
        _groupChips.Options = options;
        _groupChips.SelectedKey = _activeGroupIndex.ToString(CultureInfo.InvariantCulture);
        _syncingGroups = false;
    }

    private void UpdateChannelChips(GeneratorGroupSnapshot group)
    {
        if (_channelChips is null)
        {
            return;
        }

        var options = new List<ModuleSegmentOption>(group.Channels.Count);
        foreach (var channel in group.Channels)
        {
            options.Add(new ModuleSegmentOption(
                channel.Index.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(channel.Name) ? "CH" + (channel.Index + 1).ToString(CultureInfo.InvariantCulture) : channel.Name,
                ToolTip: channel.Enabled ? null : Tr("view.disabled")));
        }

        _syncingChannels = true;
        _channelChips.Options = options;
        _channelChips.SelectedKey = _activeChannelIndex >= 0
            ? _activeChannelIndex.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        _syncingChannels = false;
    }

    private void UpdateSettings(GeneratorGroupSnapshot group)
    {
        SetInput(_groupNameInput, group.Name);
        SetInput(_groupIdInput, group.Id.ToString(CultureInfo.InvariantCulture));
        SetInput(_frequencyInput, Format(group.Frequency));
        SetInput(_periodInput, Format(group.Frequency > 0 ? 1 / group.Frequency : 0));
        SetInput(_resolutionInput, group.Resolution.ToString(CultureInfo.InvariantCulture));
        if (_groupRateLabel is not null)
        {
            _groupRateLabel.Text = group.Rate > 0
                ? group.Rate.ToString("0.#", CultureInfo.InvariantCulture) + " Hz"
                : "-";
        }

        var channel = _activeChannelIndex >= 0 && _activeChannelIndex < group.Channels.Count
            ? group.Channels[_activeChannelIndex]
            : null;

        if (channel is null)
        {
            SetParameterVisibility(false, false, false, false);
            ClearChannelInputs();
            SetChannelEnabled(false);
            return;
        }

        var periodic = channel.Kind is "sine" or "square" or "triangle" or "saddle" or "spwm" or "svpwm";
        var modulated = channel.Kind is "spwm" or "svpwm";
        SetParameterVisibility(true, periodic, channel.Kind == "square", modulated);

        SetChannelEnabled(true);
        SetInput(_channelNameInput, channel.Name);
        if (_waveformType is not null)
        {
            _waveformType.SelectedKey = channel.Kind;
        }

        SetInput(_phaseInput, Format(channel.Phase));
        SetInput(_offsetInput, Format(channel.Offset));
        SetInput(_amplitudeInput, Format(channel.Amplitude));
        SetInput(_dutyInput, Format(channel.Duty));
        SetInput(_carrierFrequencyInput, Format(channel.CarrierFrequency));
        SetInput(_modulationIndexInput, Format(channel.ModulationIndex));
        SetInput(_carrierPhaseInput, Format(channel.CarrierPhase));
    }

    /// <summary>
    /// 参数块按波形类型隐藏/显示；XAML 默认全显，运行期第一次 Refresh 才收紧。
    /// </summary>
    private void SetParameterVisibility(bool hasChannel, bool showPhase, bool showDuty, bool showCarrier)
    {
        if (_parameterRows is not null)
        {
            _parameterRows.IsVisible = hasChannel;
        }

        if (_baseParameterRow is not null)
        {
            _baseParameterRow.IsVisible = hasChannel;
        }

        if (_phaseCell is not null)
        {
            _phaseCell.IsVisible = hasChannel && showPhase;
        }

        if (_dutyParameterRow is not null)
        {
            _dutyParameterRow.IsVisible = hasChannel && showDuty;
        }

        if (_carrierParameterRow is not null)
        {
            _carrierParameterRow.IsVisible = hasChannel && showCarrier;
        }
    }

    private void ClearChannelInputs()
    {
        SetInput(_channelNameInput, string.Empty);
        SetInput(_phaseInput, string.Empty);
        SetInput(_offsetInput, string.Empty);
        SetInput(_amplitudeInput, string.Empty);
        SetInput(_dutyInput, string.Empty);
        SetInput(_carrierFrequencyInput, string.Empty);
        SetInput(_modulationIndexInput, string.Empty);
        SetInput(_carrierPhaseInput, string.Empty);
    }

    private void SetChannelEnabled(bool enabled)
    {
        foreach (var control in _channelControls)
        {
            if (control is not null)
            {
                control.IsEnabled = enabled;
            }
        }
    }

    private void UpdateEstimate(GeneratorSnapshot snapshot)
    {
        if (_estimateLabel is not null)
        {
            _estimateLabel.Text = Tr("view.estimate")
                .Replace("{0}", snapshot.EstimatedFrames.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{1}", snapshot.EstimatedBytes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        if (_totalEstimateLabel is not null)
        {
            _totalEstimateLabel.Text = Tr("view.totalEstimate")
                .Replace("{0}", snapshot.TotalEstimatedFrames.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{1}", snapshot.TotalEstimatedBytes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        SetInput(_periodsInput, snapshot.OneShotPeriods.ToString(CultureInfo.InvariantCulture));
    }

    private void RefreshPreview(IModuleViewContext context)
    {
        if (_chart is null)
        {
            return;
        }

        var snapshot = context.ReadSeriesWindow(WaveformGeneratorViewKeys.Preview);
        if (snapshot is null)
        {
            ClearSeries();
            ApplyPreviewAxis(1);
            _chart.DataVersion++;
            return;
        }

        var changed = snapshot.Version != _previewVersion;
        _previewVersion = snapshot.Version;

        if (snapshot.Channels.Count == 0)
        {
            ClearSeries();
            ApplyPreviewAxis(1);
            if (changed)
            {
                _chart.DataVersion++;
            }

            return;
        }

        EnsureSeries(snapshot.Channels.Count);
        var maxCount = 1;
        for (var index = 0; index < _seriesList.Count; index++)
        {
            var series = _seriesList[index];
            if (index >= snapshot.Channels.Count)
            {
                series.Count = 0;
                series.Visible = false;
                continue;
            }

            var channel = snapshot.Channels[index];
            series.Name = channel.Name;
            series.Color = channel.ColorArgb is { } argb
                ? Color.FromUInt32(argb)
                : PreviewColor(channel.Index);
            series.Values = channel.Values;
            series.Count = channel.Values.Length;
            series.Visible = true;
            maxCount = Math.Max(maxCount, channel.Values.Length);
        }

        ApplyPreviewAxis(maxCount);
        _chart.YMin = snapshot.YMin;
        _chart.YMax = snapshot.YMax;

        if (changed)
        {
            _chart.DataVersion++;
        }
    }

    /// <summary>
    /// 预览横轴与显示器索引模式同义：x = 数据点序号；两个基波周期的参考线放在中点。
    /// </summary>
    private void ApplyPreviewAxis(int pointCount)
    {
        if (_chart is null)
        {
            return;
        }

        var count = Math.Max(1, pointCount);
        _chart.XStart = 0;
        _chart.XEnd = Math.Max(1, count - 1);
        _chart.XStep = 1;
        _chart.XAxisKind = ChartAxisKind.Index;
        _chart.XTickStep = 0;
        _chart.ReferenceLines = count > 2 ? [new ChartReferenceLine(count / 2.0)] : [];
    }

    private void ClearSeries()
    {
        foreach (var series in _seriesList)
        {
            series.Count = 0;
            series.Visible = false;
        }
    }

    private void EnsureSeries(int count)
    {
        while (_seriesList.Count < count)
        {
            _seriesList.Add(new ChartSeries());
        }
    }

    private Color? PreviewColor(int index)
    {
        var picker = _context?.ColorPicker;
        if (picker is null || picker.SuggestedColors.Count == 0)
        {
            return null;
        }

        return Color.FromUInt32(picker.SuggestedColors[index % picker.SuggestedColors.Count]);
    }

    private void OnGroupSelected(object? sender, EventArgs e)
    {
        if (_syncingGroups || _context is null || _groupChips is null)
        {
            return;
        }

        if (int.TryParse(_groupChips.SelectedKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            _ = _context.SubmitInput(
                WaveformGeneratorViewKeys.Action,
                JsonSerializer.Serialize(new { op = "selectGroup", groupIndex = index }));
        }
    }

    private void OnChannelSelected(object? sender, EventArgs e)
    {
        if (_syncingChannels || _context is null || _channelChips is null)
        {
            return;
        }

        if (int.TryParse(_channelChips.SelectedKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            _ = _context.SubmitInput(
                WaveformGeneratorViewKeys.Action,
                JsonSerializer.Serialize(new
                {
                    op = "selectChannel",
                    groupIndex = _activeGroupIndex,
                    index,
                }));
        }
    }

    private static string Format(double value) =>
        double.IsFinite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : string.Empty;

    private static void SetInput(ModuleTextSurface? input, string text)
    {
        if (input is null || input.IsKeyboardFocusWithin)
        {
            return;
        }

        input.Text = text;
    }

    private sealed record GeneratorSnapshot(
        int ActiveGroupIndex,
        int ActiveChannelIndex,
        int OneShotPeriods,
        long EstimatedFrames,
        long EstimatedBytes,
        long TotalEstimatedFrames,
        long TotalEstimatedBytes,
        List<GeneratorGroupSnapshot> Groups);

    private sealed record GeneratorGroupSnapshot(
        int Index,
        uint Id,
        string Name,
        bool Enabled,
        double Frequency,
        int Resolution,
        double Rate,
        List<GeneratorChannelSnapshot> Channels);

    private sealed record GeneratorChannelSnapshot(
        int Index,
        string Name,
        string Kind,
        bool Enabled,
        double Phase,
        double Offset,
        double Amplitude,
        double Duty,
        double CarrierFrequency,
        double ModulationIndex,
        double CarrierPhase);
}

/// <summary>波形发生器视图工厂。</summary>
public sealed class WaveformGeneratorViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new WaveformGeneratorView();
}
