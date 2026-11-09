using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Views;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Modules.WaveformDisplay.Views;

/// <summary>波形显示器视图：全量窗口快照 + 组标题 + 数据轴 + X/Y 精确输入。</summary>
public sealed partial class WaveformDisplayView : ModuleView, IModuleViewElement
{
    private readonly List<ChartSeries> _series = [];
    private readonly ChartSurface? _chart;
    private readonly RangeSelector? _range;
    private readonly ModuleSelect? _axisModeSelect;
    private readonly StackPanel? _groupCardsPanel;
    private readonly ModuleTextSurface? _xStartInput;
    private readonly ModuleTextSurface? _xEndInput;
    private readonly ModuleTextSurface? _yMinInput;
    private readonly ModuleTextSurface? _yMaxInput;
    private readonly ModuleTextSurface? _followRangeInput;
    private readonly ModuleTextSurface? _xTickInput;
    private readonly ModuleTextSurface? _yTickInput;
    private readonly ModuleTextSurface? _historyLimitInput;
    private readonly TextBlock? _rangeStatus;
    private readonly TextBlock? _historyUsageLabel;

    private readonly List<GroupInfo> _groups = [];
    private readonly List<ChannelInfo> _channels = [];
    private readonly List<GroupHeaderVisual> _headers = [];
    private readonly List<ChannelRowVisual> _channelRows = [];
    private readonly HashSet<(uint GroupId, int Index)> _autoColorAssigned = [];
    private readonly Dictionary<(uint GroupId, int Index), uint> _autoColorByKey = [];

    // 绝对色：设计时/主题缺失时的文字与边框兜底（静态色，不是主题色）。
    private static readonly IBrush FallbackText = new SolidColorBrush(Colors.Gray);
    private static readonly IBrush FallbackBorder = new SolidColorBrush(Colors.Gray);
    private static readonly IBrush FallbackAccent = new SolidColorBrush(Colors.DodgerBlue);

    private IModuleViewContext? _context;
    private long _metaRevision = -1;
    private long _structureRevision = -1;
    private uint _activeGroupId;
    private int _activeChannelIndex;
    private long _snapshotVersion = -1;
    private long _latestIndex;
    private double _rate;
    private DisplayXAxisMode _xAxisMode;
    private bool _cursors = true;
    private double _xTick;
    private double _yTick;
    private double _xTickEffective;
    private double _yTickEffective;
    private int _followRange = 256;
    private int _historyLimit;
    private long _historyUsed;
    private int _chartMaxX;
    private int _chartMaxY;
    private int _rangeMaxX;
    private string _reportedTickLimits = string.Empty;
    private bool _suppressRange;
    private long _lastImageRequestId = -1;

    /// <summary>构造视图。</summary>
    public WaveformDisplayView()
    {
        AvaloniaXamlLoader.Load(this);
        _chart = this.FindControl<ChartSurface>("Chart");
        _range = this.FindControl<RangeSelector>("Range");
        _axisModeSelect = this.FindControl<ModuleSelect>("AxisModeSelect");
        _groupCardsPanel = this.FindControl<StackPanel>("GroupCardsPanel");
        _xStartInput = this.FindControl<ModuleTextSurface>("XStartInput");
        _xEndInput = this.FindControl<ModuleTextSurface>("XEndInput");
        _yMinInput = this.FindControl<ModuleTextSurface>("YMinInput");
        _yMaxInput = this.FindControl<ModuleTextSurface>("YMaxInput");
        _followRangeInput = this.FindControl<ModuleTextSurface>("FollowRangeInput");
        _xTickInput = this.FindControl<ModuleTextSurface>("XTickInput");
        _yTickInput = this.FindControl<ModuleTextSurface>("YTickInput");
        _historyLimitInput = this.FindControl<ModuleTextSurface>("HistoryLimitInput");
        _rangeStatus = this.FindControl<TextBlock>("RangeStatus");
        _historyUsageLabel = this.FindControl<TextBlock>("HistoryUsageLabel");

        if (_chart is not null)
        {
            _chart.Series = _series;
        }

        if (_axisModeSelect is not null)
        {
            _axisModeSelect.Options =
            [
                new ModuleSelectOption("index", Tr("view.xIndex")),
                new ModuleSelectOption("time", Tr("view.xTime")),
            ];
            _axisModeSelect.OptionSelected += OnAxisModeSelected;
        }

        if (_range is not null)
        {
            _range.RangeChanged += OnRangeChanged;
        }

        ApplyPreviewDefaults();
    }

    /// <summary>
    /// 设计预览/首次布局的初始值：与运行期“无数据”快照同形。
    /// 运行期第一次 Refresh 会用真实快照覆盖，不形成第二套业务事实。
    /// </summary>
    private void ApplyPreviewDefaults()
    {
        if (_chart is not null)
        {
            _chart.XStart = 0;
            _chart.XEnd = 255;
            _chart.XStep = 1;
            _chart.XAxisKind = ChartAxisKind.Index;
            _chart.XTickStep = 0;
            _chart.YTickStep = 0;
            _chart.YMin = -1;
            _chart.YMax = 1;
        }

        if (_range is not null)
        {
            _range.Minimum = 0;
            _range.Maximum = 255;
            _range.RangeStart = 0;
            _range.RangeEnd = 255;
            _range.TickStep = 0;
            _range.XAxisKind = ChartAxisKind.Index;
            _range.ShowTicks = true;
            _range.ShowTickLabels = true;
        }

        SetInput(_xStartInput, "0");
        SetInput(_xEndInput, "255");
        SetInput(_yMinInput, "-1");
        SetInput(_yMaxInput, "1");
        SetInput(_followRangeInput, "256");
        SetInput(_xTickInput, "0");
        SetInput(_yTickInput, "0");
        SetInput(_historyLimitInput, "0");
    }

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(WaveformDisplayView).Assembly, key);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;
        ReadMeta(context);
        ReadSnapshot(context);
        HandleImageRequest(context);
    }

    private void ReadMeta(IModuleViewContext context)
    {
        var snapshot = context.ReadText(WaveformDisplayViewKeys.Meta);
        if (snapshot.Revision == _metaRevision || snapshot.Lines.Count == 0)
        {
            return;
        }

        _metaRevision = snapshot.Revision;

        try
        {
            using var document = JsonDocument.Parse(string.Concat(snapshot.Lines));
            var root = document.RootElement;
            var structure = root.TryGetProperty("structureRevision", out var structureElement)
                ? structureElement.GetInt64()
                : 0;
            var previousActive = _activeGroupId;
            _activeGroupId = root.TryGetProperty("activeGroupId", out var groupElement)
                ? groupElement.GetUInt32()
                : 0;
            _activeChannelIndex = root.TryGetProperty("activeChannelIndex", out var channelElement)
                ? Math.Max(0, channelElement.GetInt32())
                : 0;

            ParseGroups(root);
            ParseChannels(root);
            ParseSettings(root);

            if (structure != _structureRevision || previousActive != _activeGroupId)
            {
                _structureRevision = structure;
                RebuildGroupCards();
            }
            else
            {
                UpdateGroupVisuals();
            }

            UpdateSettingInputs();
        }
        catch (JsonException)
        {
            // 坏快照/设计时样例不打断刷新；仍把可用的默认设置落到输入框，
            // 让预览与运行期无数据状态使用同一套初始值。
            UpdateSettingInputs();
        }
    }

    private void ParseGroups(JsonElement root)
    {
        _groups.Clear();
        if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("id", out var idElement) || !idElement.TryGetUInt32(out var id))
            {
                continue;
            }

            _groups.Add(new GroupInfo(
                id,
                group.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                group.TryGetProperty("channels", out var channels) ? channels.GetInt32() : 0,
                group.TryGetProperty("dataCount", out var dataCount) ? dataCount.GetInt64() : 0,
                ReadDouble(group, "actualRate"),
                ReadDouble(group, "declaredRate"),
                group.TryGetProperty("lost", out var lost) ? lost.GetInt64() : 0,
                !group.TryGetProperty("yAuto", out var yAuto) || yAuto.GetBoolean(),
                ReadDouble(group, "yMin", -1),
                ReadDouble(group, "yMax", 1),
                group.TryGetProperty("yFitChannel", out var fit) ? fit.GetInt32() : -1));
        }
    }

    private void ParseChannels(JsonElement root)
    {
        _channels.Clear();
        if (!root.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var channel in channels.EnumerateArray())
        {
            _channels.Add(new ChannelInfo(
                index,
                channel.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                channel.TryGetProperty("color", out var color)
                    && color.ValueKind == JsonValueKind.Number
                    && color.TryGetUInt32(out var argb)
                    ? argb
                    : null,
                !channel.TryGetProperty("visible", out var visible) || visible.GetBoolean()));
            index++;
        }
    }

    private void ParseSettings(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settings))
        {
            return;
        }

        _xAxisMode = settings.TryGetProperty("xAxisMode", out var mode)
            && string.Equals(mode.GetString(), "time", StringComparison.Ordinal)
            ? DisplayXAxisMode.Time
            : DisplayXAxisMode.Index;
        _cursors = !settings.TryGetProperty("cursors", out var cursors) || cursors.GetBoolean();
        _xTick = ReadDouble(settings, "xTick");
        _yTick = ReadDouble(settings, "yTick");
        _followRange = settings.TryGetProperty("followRange", out var followRange) ? followRange.GetInt32() : 256;
        _historyLimit = settings.TryGetProperty("historyLimit", out var limit) ? limit.GetInt32() : 0;

        if (_axisModeSelect is not null)
        {
            _axisModeSelect.SelectedKey = _xAxisMode == DisplayXAxisMode.Time ? "time" : "index";
        }
    }

    private void UpdateSettingInputs()
    {
        SetInput(_xTickInput, _xTick.ToString("0.######", CultureInfo.InvariantCulture));
        SetInput(_yTickInput, _yTick.ToString("0.######", CultureInfo.InvariantCulture));
        SetInput(_followRangeInput, _followRange.ToString(CultureInfo.InvariantCulture));
        SetInput(_historyLimitInput, _historyLimit.ToString(CultureInfo.InvariantCulture));
        UpdateHistoryUsage();
    }

    private void RebuildGroupCards()
    {
        if (_groupCardsPanel is null)
        {
            return;
        }

        _groupCardsPanel.Children.Clear();
        _headers.Clear();
        _channelRows.Clear();

        foreach (var group in _groups)
        {
            var active = group.Id == _activeGroupId;
            var card = new StackPanel { Spacing = 0 };

            var headerGrid = new Grid
            {
                Height = 24,
                ColumnDefinitions = new ColumnDefinitions("20,*,44,64,14"),
                Background = Brushes.Transparent,
            };
            var id = new TextBlock
            {
                Text = group.Id.ToString(CultureInfo.InvariantCulture),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(3, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerGrid.Children.Add(id);

            var name = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(group.Name)
                    ? Tr("view.groupFallback").Replace("{0}", group.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    : group.Name,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(1, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Tag = group.Id,
            };
            name.KeyDown += OnGroupNameKeyDown;
            name.LostFocus += OnGroupNameLostFocus;
            Grid.SetColumn(name, 1);
            headerGrid.Children.Add(name);

            var count = new TextBlock
            {
                Text = group.DataCount.ToString("N0", CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = FindBrush("TextSecondary", FallbackText),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(2, 0, 3, 0),
            };
            Grid.SetColumn(count, 2);
            headerGrid.Children.Add(count);

            var rate = new TextBlock
            {
                Text = FormatGroupRate(group) + (group.Lost > 0 ? " !" + group.Lost.ToString(CultureInfo.InvariantCulture) : string.Empty),
                FontSize = 10,
                Foreground = FindBrush("TextSecondary", FallbackText),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(2, 0, 3, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(rate, 3);
            headerGrid.Children.Add(rate);

            var chevron = new TextBlock
            {
                Text = active ? "▾" : "▸",
                FontSize = 10,
                Foreground = FindBrush("TextSecondary", FallbackText),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(chevron, 4);
            headerGrid.Children.Add(chevron);

            var header = new Border
            {
                Height = 24,
                Background = active ? FindBrush("BgModule") : Brushes.Transparent,
                BorderBrush = active ? FindBrush("AccentPrimary", FallbackAccent) : FindBrush("BorderDefault", FallbackBorder),
                BorderThickness = active ? new Thickness(1, 1, 1, 0) : new Thickness(0, 0, 0, 1),
                CornerRadius = active ? new CornerRadius(3, 3, 0, 0) : new CornerRadius(0),
                Child = headerGrid,
            };
            header.PointerPressed += (_, args) =>
            {
                if (args.Source is TextBox)
                {
                    return;
                }

                if (args.GetCurrentPoint(header).Properties.IsLeftButtonPressed && group.Id != _activeGroupId)
                {
                    SelectGroup(group.Id);
                    args.Handled = true;
                }
            };
            card.Children.Add(header);

            var body = new Border
            {
                BorderBrush = active ? FindBrush("AccentPrimary", FallbackAccent) : Brushes.Transparent,
                BorderThickness = active ? new Thickness(1, 0, 1, 1) : new Thickness(0),
                Background = FindBrush("BgChrome"),
            };
            var rows = new StackPanel { Spacing = 0 };
            if (active)
            {
                foreach (var channel in _channels)
                {
                    rows.Children.Add(CreateChannelRow(channel));
                }
            }

            body.Child = rows;
            body.IsVisible = active;
            card.Children.Add(body);
            _groupCardsPanel.Children.Add(card);
            _headers.Add(new GroupHeaderVisual(group.Id, header, name, id, count, rate, chevron, body));
        }

        if (_groups.Count == 0)
        {
            _groupCardsPanel.Children.Add(new TextBlock
            {
                Text = Tr("view.empty"),
                FontSize = 11,
                Foreground = FindBrush("TextMuted", FallbackText),
                Margin = new Thickness(6),
            });
        }

        UpdateGroupVisuals();
    }

    private Grid CreateChannelRow(ChannelInfo channel)
    {
        var row = new Grid
        {
            Height = 20,
            ColumnDefinitions = new ColumnDefinitions("16,*,66,18,18"),
            ClipToBounds = true,
            Background = channel.Index == _activeChannelIndex
                ? FindBrush("SurfaceTabHoverBrush")
                : Brushes.Transparent,
        };

        var color = ResolveChannelBrush(channel);
        var colorButton = new Button
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(2, 0, 1, 0),
            Background = color,
            Tag = channel.Index,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        colorButton.Classes.Add("module-color-swatch");
        colorButton.Click += (_, _) => OpenColorPicker(colorButton, channel);
        ToolTip.SetTip(colorButton, Tr("view.color"));
        row.Children.Add(colorButton);

        var name = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(channel.Name)
                ? Tr("view.channelFallback").Replace("{0}", (channel.Index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                : channel.Name,
            FontSize = 11,
            Padding = new Thickness(1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Tag = channel.Index,
        };
        name.KeyDown += OnChannelNameKeyDown;
        name.LostFocus += OnChannelNameLostFocus;
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var readout = new TextBlock
        {
            FontSize = 9,
            Foreground = FindBrush("TextSecondary", FallbackText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 0, 3, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(readout, 2);
        row.Children.Add(readout);

        var hide = MakeToggleButton("icon.group.hide", Tr("view.hideCurve"), channel.Visible);
        hide.Click += (_, _) =>
        {
            // 行只建一次：必须读当前通道状态，不能使用创建行时捕获的旧值。
            var current = _channels.Find(item => item.Index == channel.Index);
            SubmitAction(new
            {
                op = "setVisible",
                index = channel.Index,
                visible = !(current?.Visible ?? channel.Visible),
            });
        };
        Grid.SetColumn(hide, 3);
        row.Children.Add(hide);

        var fit = MakeToggleButton("icon.group.fit-y", Tr("view.fitY"), false);
        fit.Click += (_, _) => SubmitAction(new { op = "fitY", index = channel.Index });
        Grid.SetColumn(fit, 4);
        row.Children.Add(fit);

        row.PointerPressed += (_, args) =>
        {
            if (args.Source is TextBox or Button)
            {
                return;
            }

            if (args.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                SubmitAction(new { op = "selectChannel", index = channel.Index });
            }
        };

        _channelRows.Add(new ChannelRowVisual(channel.Index, row, colorButton, name, readout, hide, fit));
        return row;
    }

    private void UpdateGroupVisuals()
    {
        foreach (var header in _headers)
        {
            var group = _groups.Find(item => item.Id == header.GroupId);
            if (group is null)
            {
                continue;
            }

            var active = group.Id == _activeGroupId;
            header.Name.IsReadOnly = !active;
            header.Header.BorderBrush = active
                ? FindBrush("AccentPrimary", FallbackAccent)
                : FindBrush("BorderDefault", FallbackBorder);
            header.Header.Background = active ? FindBrush("BgModule") : Brushes.Transparent;
            header.Header.BorderThickness = active
                ? new Thickness(1, 1, 1, 0)
                : new Thickness(0, 0, 0, 1);
            header.Header.CornerRadius = active ? new CornerRadius(3, 3, 0, 0) : new CornerRadius(0);
            header.Body.IsVisible = active;
            header.Body.BorderBrush = active
                ? FindBrush("AccentPrimary", FallbackAccent)
                : Brushes.Transparent;
            header.Body.BorderThickness = active ? new Thickness(1, 0, 1, 1) : new Thickness(0);
            header.Chevron.Text = active ? "▾" : "▸";
            header.Count.Text = group.DataCount.ToString("N0", CultureInfo.InvariantCulture);
            header.Rate.Text = FormatGroupRate(group)
                + (group.Lost > 0 ? " !" + group.Lost.ToString(CultureInfo.InvariantCulture) : string.Empty);

            if (!header.Name.IsKeyboardFocusWithin)
            {
                header.Name.Text = string.IsNullOrWhiteSpace(group.Name)
                    ? Tr("view.groupFallback").Replace("{0}", group.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    : group.Name;
            }

            header.Id.FontWeight = active ? FontWeight.Bold : FontWeight.SemiBold;
        }

        var activeGroup = _groups.Find(item => item.Id == _activeGroupId);
        foreach (var row in _channelRows)
        {
            var channel = _channels.Find(item => item.Index == row.Index);
            if (channel is null)
            {
                continue;
            }

            row.Row.Background = channel.Index == _activeChannelIndex
                ? FindBrush("SurfaceTabHoverBrush")
                : Brushes.Transparent;
            row.ColorButton.Background = ResolveChannelBrush(channel);
            row.Hide.IsChecked = channel.Visible;
            row.Fit.IsChecked = activeGroup is { YAuto: true, YFitChannel: var fit } && fit == channel.Index;
            if (!row.Name.IsKeyboardFocusWithin)
            {
                row.Name.Text = string.IsNullOrWhiteSpace(channel.Name)
                    ? Tr("view.channelFallback").Replace("{0}", (channel.Index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    : channel.Name;
            }
        }
    }

    private void ReadSnapshot(IModuleViewContext context)
    {
        var snapshot = context.ReadSeriesWindow(WaveformDisplayViewKeys.Series);
        if (snapshot is null)
        {
            return;
        }

        var latestValue = context.ReadLatest(WaveformDisplayViewKeys.LatestIndex);
        _latestIndex = Math.Max(0, (long)(latestValue ?? snapshot.LatestIndex));
        var rateValue = context.ReadLatest(WaveformDisplayViewKeys.Rate);
        _rate = rateValue ?? snapshot.RateHz;
        var historyUsed = context.ReadLatest(WaveformDisplayViewKeys.HistoryUsed);
        _historyUsed = historyUsed is { } used ? Math.Max(0, (long)used) : 0;

        var changed = snapshot.Version != _snapshotVersion;
        _snapshotVersion = snapshot.Version;

        UpdateChart(snapshot, changed);
        RefreshTickMetrics(context, snapshot);
        if (_chart is not null
            && (Math.Abs(_chart.XTickStep - _xTickEffective) > double.Epsilon
                || Math.Abs(_chart.YTickStep - _yTickEffective) > double.Epsilon))
        {
            _chart.XTickStep = _xTickEffective;
            _chart.YTickStep = _yTickEffective;
        }

        UpdateGroupVisuals();
        UpdateRange(snapshot);
        UpdateRangeInputs(snapshot);
        UpdateYInputs(snapshot);
        UpdateReadouts(snapshot);
        UpdateRangeStatus(snapshot);
        UpdateHistoryUsage();
    }

    /// <summary>
    /// 从控件**实际尺寸与字体测量**得出可读刻度上限，并上报给模块；同时决定本地生效刻度。
    /// </summary>
    private void RefreshTickMetrics(IModuleViewContext context, SeriesWindowSnapshot snapshot)
    {
        _chartMaxX = _chart?.GetMaxReadableXTicks() ?? 0;
        _chartMaxY = _chart?.GetMaxReadableYTicks() ?? 0;
        _rangeMaxX = _range?.GetMaxReadableXTicks() ?? 0;

        var limits = JsonSerializer.Serialize(new
        {
            chartX = _chartMaxX,
            chartY = _chartMaxY,
            rangeX = _rangeMaxX,
        });
        if (!string.Equals(limits, _reportedTickLimits, StringComparison.Ordinal))
        {
            _reportedTickLimits = limits;
            context.WriteInputText(WaveformDisplayViewKeys.ReadableTickLimits, limits);
        }

        var xSpan = XSpan(snapshot);
        var ySpan = Math.Max(0, snapshot.YMax - snapshot.YMin);
        var xDense = IsDense(xSpan, _xTick, _chartMaxX) || IsDense(xSpan, _xTick, _rangeMaxX);
        var yDense = IsDense(ySpan, _yTick, _chartMaxY);
        _xTickEffective = xDense ? 0 : _xTick;
        _yTickEffective = yDense ? 0 : _yTick;
    }

    private double XSpan(SeriesWindowSnapshot snapshot)
    {
        var span = Math.Max(0, snapshot.XRangeEnd - snapshot.XRangeStart);
        return _xAxisMode == DisplayXAxisMode.Time && _rate > 0 ? span / _rate : span;
    }

    private static bool IsDense(double span, double step, int readableLimit) =>
        readableLimit > 0 && step > 0 && span > 0 && span / step > readableLimit;

    private void UpdateHistoryUsage()
    {
        if (_historyUsageLabel is null)
        {
            return;
        }

        _historyUsageLabel.Text = _historyLimit > 0
            ? Tr("view.historyUsage").Replace("{0}", _historyUsed.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                .Replace("{1}", _historyLimit.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
            : Tr("view.historyUsageUnlimited").Replace("{0}", _historyUsed.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal);
    }

    private void UpdateChart(SeriesWindowSnapshot snapshot, bool changed)
    {
        if (_chart is null)
        {
            return;
        }

        var count = snapshot.Channels.Count > 0 ? snapshot.Channels[0].Values.Length : 0;
        var time = _xAxisMode == DisplayXAxisMode.Time && _rate > 0;
        var xStart = time ? snapshot.XRangeStart / _rate : snapshot.XRangeStart;
        var xEnd = time ? snapshot.XRangeEnd / _rate : snapshot.XRangeEnd;
        var step = count > 1 ? (xEnd - xStart) / (count - 1) : 1;

        _chart.XStart = xStart;
        _chart.XEnd = xEnd;
        _chart.XStep = step;
        _chart.XAxisKind = time ? ChartAxisKind.Time : ChartAxisKind.Index;
        _chart.XTickStep = _xTickEffective;
        _chart.YTickStep = _yTickEffective;
        _chart.YMin = snapshot.YMin;
        _chart.YMax = snapshot.YMax;
        _chart.ShowCursors = _cursors;
        _chart.ShowYCursors = _cursors;

        EnsureAutoColors(snapshot.Channels);
        EnsureSeries(snapshot.Channels.Count);
        for (var index = 0; index < _series.Count; index++)
        {
            var series = _series[index];
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
                : AutoColorFor(channel.Index) is { } auto
                    ? Color.FromUInt32(auto)
                    : null;
            series.Values = channel.Values;
            series.Count = channel.Values.Length;
            series.Visible = true;
        }

        if (changed)
        {
            _chart.DataVersion++;
        }
    }

    private void EnsureSeries(int count)
    {
        while (_series.Count < count)
        {
            _series.Add(new ChartSeries());
        }
    }

    private void UpdateRange(SeriesWindowSnapshot snapshot)
    {
        if (_range is null)
        {
            return;
        }

        _suppressRange = true;
        try
        {
            var time = _xAxisMode == DisplayXAxisMode.Time && _rate > 0;
            var maximum = time ? Math.Max(1e-6, _latestIndex / _rate) : Math.Max(1, _latestIndex);
            if (time)
            {
                maximum = Math.Max(maximum, snapshot.XRangeEnd / _rate);
            }
            else
            {
                maximum = Math.Max(maximum, snapshot.XRangeEnd);
            }

            _range.Minimum = 0;
            _range.Maximum = maximum;
            _range.MinimumRangeSpan = time ? 2 / _rate : 2;
            _range.TickStep = _xTickEffective;
            _range.XAxisKind = time ? ChartAxisKind.Time : ChartAxisKind.Index;
            _range.ShowTicks = true;
            _range.ShowTickLabels = true;
            _range.RangeStart = time ? snapshot.XRangeStart / _rate : snapshot.XRangeStart;
            _range.RangeEnd = time ? snapshot.XRangeEnd / _rate : snapshot.XRangeEnd;
        }
        finally
        {
            _suppressRange = false;
        }
    }

    private void UpdateRangeInputs(SeriesWindowSnapshot snapshot)
    {
        SetInput(_xStartInput, FormatX(snapshot.XRangeStart));
        SetInput(_xEndInput, FormatX(snapshot.XRangeEnd));
    }

    private void UpdateYInputs(SeriesWindowSnapshot snapshot)
    {
        SetInput(_yMinInput, Format(snapshot.YMin));
        SetInput(_yMaxInput, Format(snapshot.YMax));
    }

    private void UpdateReadouts(SeriesWindowSnapshot snapshot)
    {
        var count = snapshot.Channels.Count > 0 ? snapshot.Channels[0].Values.Length : 0;
        foreach (var row in _channelRows)
        {
            var channel = FindChannel(snapshot, row.Index);
            if (channel is null || count <= 0)
            {
                row.Readout.Text = "A - B -";
                continue;
            }

            var a = Math.Clamp((int)Math.Round(_chart?.CursorAFraction ?? 0, MidpointRounding.AwayFromZero) * (count - 1), 0, count - 1);
            var b = Math.Clamp((int)Math.Round(_chart?.CursorBFraction ?? 0, MidpointRounding.AwayFromZero) * (count - 1), 0, count - 1);
            row.Readout.Text = "A " + FormatValue(channel.Values[a]) + " B " + FormatValue(channel.Values[b]);
        }
    }

    private static SeriesWindowChannel? FindChannel(SeriesWindowSnapshot snapshot, int index)
    {
        for (var i = 0; i < snapshot.Channels.Count; i++)
        {
            if (snapshot.Channels[i].Index == index)
            {
                return snapshot.Channels[i];
            }
        }

        return null;
    }

    private void UpdateRangeStatus(SeriesWindowSnapshot snapshot)
    {
        if (_rangeStatus is null)
        {
            return;
        }

        if (snapshot.Channels.Count == 0)
        {
            _rangeStatus.Text = Tr("view.rangeStatus")
                .Replace("{0}", "0", StringComparison.Ordinal)
                .Replace("{1}", FormatX(snapshot.XRangeStart), StringComparison.Ordinal)
                .Replace("{2}", FormatX(snapshot.XRangeEnd), StringComparison.Ordinal);
            return;
        }

        var count = snapshot.Channels[0].Values.Length;
        var start = FormatX(snapshot.XRangeStart);
        var end = FormatX(snapshot.XRangeEnd);
        _rangeStatus.Text = Tr("view.rangeStatus")
            .Replace("{0}", count.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{1}", start, StringComparison.Ordinal)
            .Replace("{2}", end, StringComparison.Ordinal);
    }

    private void OnRangeChanged(object? sender, EventArgs e)
    {
        if (_suppressRange || _context is null || _range is null
            || double.IsNaN(_range.RangeStart) || double.IsNaN(_range.RangeEnd))
        {
            return;
        }

        var time = _xAxisMode == DisplayXAxisMode.Time && _rate > 0;
        var start = time ? (long)Math.Round(_range.RangeStart * _rate) : (long)Math.Round(_range.RangeStart);
        var end = time ? (long)Math.Round(_range.RangeEnd * _rate) : (long)Math.Round(_range.RangeEnd);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        SubmitAction(new { op = "setRange", start, end });
    }

    private void OnAxisModeSelected(object? sender, EventArgs e)
    {
        if (_axisModeSelect is null || _context is null)
        {
            return;
        }

        _ = _context.SubmitInput(WaveformDisplayViewKeys.XAxisMode, _axisModeSelect.SelectedKey);
    }

    private void OnGroupNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || box.Tag is not uint id)
        {
            return;
        }

        SubmitAction(new { op = "renameGroup", id, name = box.Text ?? string.Empty });
        e.Handled = true;
    }

    private void OnGroupNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not uint id)
        {
            return;
        }

        SubmitAction(new { op = "renameGroup", id, name = box.Text ?? string.Empty });
    }

    private void OnChannelNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || box.Tag is not int index)
        {
            return;
        }

        SubmitAction(new { op = "renameChannel", index, name = box.Text ?? string.Empty });
        e.Handled = true;
    }

    private void OnChannelNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not int index)
        {
            return;
        }

        SubmitAction(new { op = "renameChannel", index, name = box.Text ?? string.Empty });
    }

    private async void OpenColorPicker(Button anchor, ChannelInfo channel)
    {
        if (_context?.ColorPicker is not { } picker || picker.SuggestedColors.Count == 0)
        {
            return;
        }

        var key = (_activeGroupId, channel.Index);
        var initial = channel.Color
            ?? AutoColorFor(channel.Index)
            ?? picker.SuggestedColors[channel.Index % picker.SuggestedColors.Count];
        var owner = TopLevel.GetTopLevel(this) as Window;

        ModuleColorPickResult result;
        try
        {
            result = await picker.PickAsync(
                owner,
                initial,
                Tr("view.colorTitle"),
                Tr("view.colorReset"));
        }
        catch (Exception)
        {
            // 取色窗口失败不应打断显示刷新。
            return;
        }

        if (!result.Confirmed)
        {
            return;
        }

        if (result.Reset)
        {
            _autoColorAssigned.Remove(key);
            _autoColorByKey.Remove(key);
            SubmitAction(new { op = "resetColor", index = channel.Index });
            return;
        }

        _autoColorAssigned.Add(key);
        _autoColorByKey[key] = result.ColorArgb;
        SubmitAction(new
        {
            op = "setColor",
            index = channel.Index,
            color = result.ColorArgb.ToString("X8", CultureInfo.InvariantCulture),
        });
    }

    private void EnsureAutoColors(IReadOnlyList<SeriesWindowChannel> channels)
    {
        if (_context?.ColorPicker is not { } picker || picker.SuggestedColors.Count == 0)
        {
            return;
        }

        foreach (var channel in channels)
        {
            if (channel.ColorArgb is not null)
            {
                continue;
            }

            var key = (_activeGroupId, channel.Index);
            if (!_autoColorAssigned.Add(key))
            {
                continue;
            }

            var color = picker.SuggestedColors[channel.Index % picker.SuggestedColors.Count];
            _autoColorByKey[key] = color;
            if (!SubmitAction(new
                {
                    op = "setColor",
                    index = channel.Index,
                    color = color.ToString("X8", CultureInfo.InvariantCulture),
                }))
            {
                _autoColorAssigned.Remove(key);
                _autoColorByKey.Remove(key);
            }
        }
    }

    private uint? AutoColorFor(int index)
    {
        var key = (_activeGroupId, index);
        if (_autoColorByKey.TryGetValue(key, out var color))
        {
            return color;
        }

        return _context?.ColorPicker is { SuggestedColors.Count: > 0 } picker
            ? picker.SuggestedColors[index % picker.SuggestedColors.Count]
            : null;
    }

    private IBrush ResolveChannelBrush(ChannelInfo channel)
    {
        if (channel.Color is { } argb)
        {
            return new SolidColorBrush(Color.FromUInt32(argb));
        }

        return AutoColorFor(channel.Index) is { } auto
            ? new SolidColorBrush(Color.FromUInt32(auto))
            : FindBrush("AccentSecondary", FallbackAccent);
    }

    private void SelectGroup(uint id) => SubmitAction(new { op = "selectGroup", id });

    private bool SubmitAction(object value)
    {
        if (_context is null)
        {
            return false;
        }

        return _context.SubmitInput(WaveformDisplayViewKeys.Action, JsonSerializer.Serialize(value));
    }

    private static void SetInput(ModuleTextSurface? input, string text)
    {
        if (input is null || input.IsKeyboardFocusWithin)
        {
            return;
        }

        input.Text = text;
    }

    private string FormatX(long sample)
    {
        if (_xAxisMode == DisplayXAxisMode.Time && _rate > 0)
        {
            return (sample / _rate).ToString("0.######", CultureInfo.InvariantCulture);
        }

        return sample.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatValue(double value) =>
        double.IsFinite(value) ? value.ToString("0.##", CultureInfo.InvariantCulture) : "-";

    private IBrush FindBrush(string key, IBrush? fallback = null) =>
        this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : fallback ?? Brushes.Transparent;

    private static ToggleButton MakeToggleButton(string iconKey, string tooltip, bool isChecked)
    {
        var button = new ToggleButton
        {
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsChecked = isChecked,
            Content = new IconControl
            {
                IconKey = iconKey,
                Owner = typeof(WaveformDisplayView).Assembly,
                Width = 12,
                Height = 12,
            },
        };
        button.Classes.Add("module-row-action");
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private void HandleImageRequest(IModuleViewContext context)
    {
        if (_chart is null)
        {
            return;
        }

        var snapshot = context.ReadText(WaveformDisplayViewKeys.ImageRequest);
        if (snapshot.Lines.Count == 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(string.Concat(snapshot.Lines));
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement)
                || !idElement.TryGetInt64(out var id)
                || id == _lastImageRequestId
                || !root.TryGetProperty("path", out var pathElement)
                || pathElement.GetString() is not { Length: > 0 } path)
            {
                return;
            }

            _lastImageRequestId = id;
            try
            {
                var bytes = _chart.CapturePng() ?? throw new InvalidOperationException("empty chart");
                File.WriteAllBytes(path, bytes);
                _ = context.SubmitInput(
                    WaveformDisplayViewKeys.ImageResult,
                    JsonSerializer.Serialize(new { id, ok = true, error = string.Empty }));
            }
            catch (Exception exception)
            {
                _ = context.SubmitInput(
                    WaveformDisplayViewKeys.ImageResult,
                    JsonSerializer.Serialize(new { id, ok = false, error = exception.Message }));
            }
        }
        catch (JsonException)
        {
            // 坏请求只忽略。
        }
    }

    private static double ReadDouble(JsonElement element, string property, double fallback = 0) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;

    private static string FormatGroupRate(GroupInfo group)
    {
        if (group.ActualRate > 0 && group.DeclaredRate > 0)
        {
            return group.ActualRate.ToString("0.#", CultureInfo.InvariantCulture)
                + "/"
                + group.DeclaredRate.ToString("0.#", CultureInfo.InvariantCulture)
                + "Hz";
        }

        var rate = group.ActualRate > 0 ? group.ActualRate : group.DeclaredRate;
        return rate > 0
            ? rate.ToString("0.#", CultureInfo.InvariantCulture) + "Hz"
            : "-";
    }

    private sealed record GroupInfo(
        uint Id,
        string Name,
        int Channels,
        long DataCount,
        double ActualRate,
        double DeclaredRate,
        long Lost,
        bool YAuto,
        double YMin,
        double YMax,
        int YFitChannel);

    private sealed record ChannelInfo(int Index, string Name, uint? Color, bool Visible);

    private sealed record GroupHeaderVisual(
        uint GroupId,
        Border Header,
        TextBox Name,
        TextBlock IdText,
        TextBlock Count,
        TextBlock Rate,
        TextBlock Chevron,
        Border Body)
    {
        public TextBlock Id => IdText;
    }

    private sealed record ChannelRowVisual(
        int Index,
        Grid Row,
        Button ColorButton,
        TextBox Name,
        TextBlock Readout,
        ToggleButton Hide,
        ToggleButton Fit);
}

/// <summary>波形显示器视图工厂。</summary>
public sealed class WaveformDisplayViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new WaveformDisplayView();
}
