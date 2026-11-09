using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>
/// **波形显示器**：接收波形帧，按组/通道保存历史，并把当前 X 范围内的全部点发布为不可变窗口快照。
/// </summary>
/// <remarks>
/// 范围事实：
/// <list type="bullet">
///   <item><b>X 范围</b>（活动组运行期）：图表 x 轴、数据轴范围带、x 起止输入、局部保存；</item>
///   <item><b>Y 范围</b>（活动组配置）：图表 y 轴与 Y 游标，只影响视图，不参与保存。</item>
/// </list>
/// 图表不再经过 64 个环形序列源或 256 点降采样；窗口内有多少点就发布多少点。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "waveform-display",
    DisplayNameKey = WaveformDisplayText.Title,
    DescriptionKey = WaveformDisplayText.Description,
    Tags = ["waveform", "display"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.WaveformDisplayViewFactory))]
public sealed class WaveformDisplayModule : IModule, IActivitySource, IDisposable
{
    /// <summary>输入端口。</summary>
    public const string InPort = "in";

    /// <summary>角色 Id。</summary>
    public const string RoleId = "display";

    /// <summary>全量窗口快照源（SeriesWindow）。</summary>
    public const string SeriesSource = "series";

    /// <summary>组/通道摘要 JSON 源。</summary>
    public const string MetaSource = "meta";

    /// <summary>状态文本源。</summary>
    public const string StatusSource = "status";

    /// <summary>最新数据索引（用于数据轴域；每帧更新）。</summary>
    public const string LatestIndexSource = "latestIndex";

    /// <summary>有效速率（用于时间轴换算）。</summary>
    public const string RateSource = "rate";

    /// <summary>X 范围起点输入。</summary>
    public const string XStartSource = "xStart";

    /// <summary>X 范围终点输入。</summary>
    public const string XEndSource = "xEnd";

    /// <summary>Y 下限输入。</summary>
    public const string YMinSource = "yMin";

    /// <summary>Y 上限输入。</summary>
    public const string YMaxSource = "yMax";

    /// <summary>Y 自动开关输入。</summary>
    public const string YAutoSource = "yAuto";

    /// <summary>跟随开关输入。</summary>
    public const string FollowSource = "follow";

    /// <summary>跟踪窗口宽度输入（样本数）。</summary>
    public const string FollowRangeSource = "followRange";

    /// <summary>游标显示输入。</summary>
    public const string CursorsSource = "cursors";

    /// <summary>X 轴模式输入。</summary>
    public const string XAxisModeSource = "xAxisMode";

    /// <summary>X 刻度输入（0 = 自动）。</summary>
    public const string XTickSource = "xTick";

    /// <summary>Y 刻度输入（0 = 自动）。</summary>
    public const string YTickSource = "yTick";

    /// <summary>保留非活动组历史输入。</summary>
    public const string KeepInactiveSource = "keepInactive";

    /// <summary>非活动组历史样本上限输入（个；0 = 不限）。</summary>
    public const string HistoryLimitSource = "historyLimit";

    /// <summary>非活动组当前占用的样本帧合计（显示源，Value/U64）。</summary>
    public const string HistoryUsedSource = "historyUsed";

    /// <summary>视图上报的实际可读刻度上限（JSON；视图→模块）。</summary>
    public const string ReadableTickLimitsSource = "readableTickLimits";

    /// <summary>带参 UI 操作（JSON）。</summary>
    public const string ActionSource = "action";

    /// <summary>文件选择回投的保存路径。</summary>
    public const string SavePathSource = "savePath";

    /// <summary>PNG 保存请求（模块→视图）。</summary>
    public const string ImageRequestSource = "imageRequest";

    /// <summary>PNG 写盘结果（视图→模块）。</summary>
    public const string ImageResultSource = "imageResult";

    /// <summary>清空活动组数据/编号。</summary>
    public const string ClearCommand = "clear";

    /// <summary>复位活动组显示配置。</summary>
    public const string ResetGroupCommand = "resetGroup";

    /// <summary>保存当前 X 范围。</summary>
    public const string SaveSelectionCommand = "saveSelection";

    /// <summary>保存活动组全部历史。</summary>
    public const string SaveAllCommand = "saveAll";

    /// <summary>保存当前图 PNG。</summary>
    public const string SaveImageCommand = "saveImage";

    private const int DefaultFollowRangeSamples = 256;
    private const int MinimumRangeSamples = 2;
    private const int MaximumFollowRangeSamples = 1_000_000;
    private const int LargeRangeFrameThreshold = 2048;
    private const int LargeRangeBuildIntervalMs = 50;

    /// <summary>本模块挑的提示角色（相对色，不是写死的红）。</summary>
    public const string AlertRole = ViewInkRoles.Danger;

    private readonly Dictionary<uint, DisplayGroupRuntime> _groups = [];
    private readonly List<uint> _groupOrder = [];
    private readonly Dictionary<string, string> _lastText = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidInputs = new(StringComparer.Ordinal);

    private IModuleContext? _context;
    private IModuleTimer? _uiTimer;
    private DisplayConfig _config = new();
    private bool _configLoaded;
    private uint _activeGroupId;
    private int _activeChannelIndex;
    private long _structureRevision;
    private long _metaRevision;
    private long _badFrames;
    private bool _historyLimited;
    private long _snapshotVersion;
    private long _lastLargeBuildMs;

    // 无数据时的会话级默认视口（真正有数据后，组运行期范围/配置才是唯一事实源）。
    private long _defaultRangeStart;
    private long _defaultRangeEnd = DefaultFollowRangeSamples - 1;
    private bool _defaultYAuto = true;
    private double _defaultYMin = -1;
    private double _defaultYMax = 1;
    private bool _defaultRangeEdited;
    private bool _defaultYEdited;

    // 视图上报的实际可读刻度上限（0 = 尚未上报）。
    private int _maxChartXTicks;
    private int _maxChartYTicks;
    private int _maxRangeXTicks;
    private bool _xTickParseInvalid;
    private bool _yTickParseInvalid;
    private bool _xTickAlerted;
    private bool _yTickAlerted;

    private PendingSave _pendingSave;
    private DisplayGroupRuntime? _pendingGroup;
    private long _pendingStart;
    private long _pendingEnd;
    private CsvExport? _export;
    private string _exportPath = string.Empty;
    private long _imageRequestId;
    private string _imagePath = string.Empty;

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(WaveformDisplayModule).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(WaveformDisplayModule).Assembly, key, args);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            WaveformFrameContract.TypeId,
            WaveformFrameContract.Shape,
            TransferSemantics.Stream)
        {
            Metering = WaveformFrameContract.Metering,
        });

        context.Declaration.DeclarePort(new PortDeclaration(
            InPort,
            PortDirection.Receive,
            WaveformFrameContract.Shape,
            TransferSemantics.Stream)
        {
            TypeId = WaveformFrameContract.TypeId,
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleId,
            WaveformFrameContract.TypeId,
            InPort,
            Tr(WaveformDisplayText.RoleDisplay)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            SeriesSource, ViewDataSourceKind.SeriesWindow));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(MetaSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            LatestIndexSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            RateSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            HistoryUsedSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        DeclareInput(context, XStartSource);
        DeclareInput(context, XEndSource);
        DeclareInput(context, YMinSource);
        DeclareInput(context, YMaxSource);
        DeclareInput(context, YAutoSource);
        DeclareInput(context, FollowSource);
        DeclareInput(context, FollowRangeSource);
        DeclareInput(context, CursorsSource);
        DeclareInput(context, XAxisModeSource);
        DeclareInput(context, XTickSource);
        DeclareInput(context, YTickSource);
        DeclareInput(context, KeepInactiveSource);
        DeclareInput(context, HistoryLimitSource);
        DeclareInput(context, ReadableTickLimitsSource);
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ImageRequestSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareInput(ActionSource);
        context.Declaration.DeclareInput(SavePathSource);
        context.Declaration.DeclareInput(ImageResultSource);

        context.Declaration.DeclareCommand(ClearCommand, Tr(WaveformDisplayText.Clear));
        context.Declaration.DeclareCommand(ResetGroupCommand, Tr(WaveformDisplayText.Reset));
        context.Declaration.DeclareCommand(SaveSelectionCommand, Tr(WaveformDisplayText.SaveSelection));
        context.Declaration.DeclareCommand(SaveAllCommand, Tr(WaveformDisplayText.SaveAll));
        context.Declaration.DeclareCommand(SaveImageCommand, Tr(WaveformDisplayText.SaveImage));

        _uiTimer = context.Timers.Create();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(250);
        _uiTimer.Tick += OnUiTimer;
        _context = context;
    }

    private static void DeclareInput(IModuleContext context, string name) =>
        context.Declaration.DeclareInput(name);

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Pull;

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_configLoaded)
        {
            LoadConfig(context);
        }

        Pump(context);

        // 与 HEX 转换器同一模型：`ModuleTextSurface` 逐键把文本写进 ViewState，
        // 框架随即以 Input 原因唤醒本拍；这里回读尚未提交的编辑并即时处理。
        PollTextInputs(context);

        if (reason.HasFlag(StepReason.Data))
        {
            DrainFrames(context);
        }
    }

    /// <summary>测试/单步入口：处理收件箱与端口数据（不启动实例线程）。</summary>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_configLoaded)
        {
            LoadConfig(context);
        }

        Pump(context);
        PollTextInputs(context);
        DrainFrames(context);
    }

    /// <inheritdoc />
    public void OnRemoved() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _export?.Dispose();
        _export = null;

        if (_uiTimer is not null)
        {
            _uiTimer.Stop();
            _uiTimer.Dispose();
            _uiTimer = null;
        }
    }

    private void LoadConfig(IModuleContext context)
    {
        _config = context.Configuration.Read<DisplayConfig>();
        NormalizeConfig();
        _configLoaded = true;
        context.Configuration.Write(_config);
        _activeGroupId = _config.ActiveGroupId;
        _activeChannelIndex = _config.ActiveChannelIndex;
        _defaultRangeStart = 0;
        _defaultRangeEnd = _config.Settings.FollowRangeSamples - 1;
        _uiTimer?.Start();
        PublishMeta(context);
        PublishEmptySnapshot(context);
        PublishStatus(Tr(WaveformDisplayText.Ready));
    }

    private void NormalizeConfig()
    {
        var settings = _config.Settings;
        var followRange = Math.Clamp(
            settings.FollowRangeSamples <= 0 ? DefaultFollowRangeSamples : settings.FollowRangeSamples,
            MinimumRangeSamples,
            MaximumFollowRangeSamples);
        var xTick = double.IsFinite(settings.XTickStep) && settings.XTickStep > 0 ? settings.XTickStep : 0;
        var yTick = double.IsFinite(settings.YTickStep) && settings.YTickStep > 0 ? settings.YTickStep : 0;

        var groups = _config.Groups.ToList();
        var used = new HashSet<uint>();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var id = group.GroupId;
            if (id == 0 || !used.Add(id))
            {
                var candidate = 1u;
                while (used.Contains(candidate))
                {
                    candidate++;
                }

                id = candidate;
                _ = used.Add(id);
            }

            var yMin = double.IsFinite(group.YMin) ? group.YMin : -1;
            var yMax = double.IsFinite(group.YMax) ? group.YMax : 1;
            if (yMax <= yMin)
            {
                yMax = yMin + 1;
            }

            var channels = group.Channels.ToList();
            if (channels.Count == 0)
            {
                channels.Add(new DisplayChannelConfig());
            }

            groups[index] = group with
            {
                GroupId = id,
                Name = group.Name ?? string.Empty,
                YMin = yMin,
                YMax = yMax,
                YFitChannel = group.YFitChannel < -1 ? -1 : group.YFitChannel,
                Channels = channels,
            };
        }

        _config = _config with
        {
            Groups = groups,
            Settings = settings with
            {
                FollowRangeSamples = followRange,
                XTickStep = xTick,
                YTickStep = yTick,
            },
            ActiveChannelIndex = Math.Max(0, _config.ActiveChannelIndex),
            HistoryLimitSamples = Math.Max(0, _config.HistoryLimitSamples),
        };

        if (_config.FindGroup(_config.ActiveGroupId) is null)
        {
            _config = _config with { ActiveGroupId = 0 };
        }
    }

    private void Pump(IModuleContext context)
    {
        while (context.Inbox.TryDequeueCommand(out var command))
        {
            OnCommand(context, command);
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            OnInput(context, source, text);
        }
    }

    private static readonly string[] PolledTextSources =
    [
        XStartSource,
        XEndSource,
        YMinSource,
        YMaxSource,
        XTickSource,
        YTickSource,
        FollowRangeSource,
        HistoryLimitSource,
    ];

    /// <summary>回读用户逐键写入的文本；内容变化立即进入同一条解析/校验路径。</summary>
    private void PollTextInputs(IModuleContext context)
    {
        // 先取一份本拍的用户编辑快照：处理第一个字段时 `PublishInputs` 会回写其他字段，
        // 若边读边处理，就会把用户同一拍刚输入的另一格覆盖掉。
        var pending = new List<(string Source, string Text)>();
        foreach (var source in PolledTextSources)
        {
            var text = context.ViewState.GetInputText(source);
            if (text is null
                || (_lastText.TryGetValue(source, out var last) && string.Equals(last, text, StringComparison.Ordinal)))
            {
                continue;
            }

            pending.Add((source, text));
        }

        foreach (var (source, text) in pending)
        {
            _lastText[source] = text;
            OnInput(context, source, text);
        }

        var limits = context.ViewState.GetInputText(ReadableTickLimitsSource);
        if (limits is not null
            && (!_lastText.TryGetValue(ReadableTickLimitsSource, out var lastLimits)
                || !string.Equals(lastLimits, limits, StringComparison.Ordinal)))
        {
            _lastText[ReadableTickLimitsSource] = limits;
            ApplyReadableTickLimits(context, limits);
        }
    }

    private void ApplyReadableTickLimits(IModuleContext context, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            _maxChartXTicks = ReadLimit(root, "chartX");
            _maxChartYTicks = ReadLimit(root, "chartY");
            _maxRangeXTicks = ReadLimit(root, "rangeX");
            UpdateTickAlerts(context);
        }
        catch (JsonException)
        {
            // 坏的上报只忽略；不能打断显示刷新。
        }
    }

    private static int ReadLimit(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var limit)
            ? Math.Max(0, limit)
            : 0;

    /// <summary>当前视口（无数据时用会话默认值）。</summary>
    private (long Start, long End) CurrentXRange()
    {
        var group = ActiveGroup;
        if (group is { DisplayIndex: > 0 } && group.HasRange)
        {
            return (group.RangeStart, group.RangeEnd);
        }

        return (_defaultRangeStart, Math.Max(_defaultRangeStart + 1, _defaultRangeEnd));
    }

    private (double Min, double Max) CurrentYRange()
    {
        var group = ActiveGroup;
        if (group is { DisplayIndex: > 0 })
        {
            return EffectiveY(group);
        }

        return (_defaultYMin, _defaultYMax);
    }

    /// <summary>X 轴显示跨度（时间轴换算成秒）。</summary>
    private double CurrentXSpan()
    {
        var (start, end) = CurrentXRange();
        var span = Math.Max(0, end - start);
        var rate = EffectiveRate(ActiveGroup);
        return _config.Settings.XAxisMode == DisplayXAxisMode.Time && rate > 0
            ? span / rate
            : span;
    }

    /// <summary>用户刻度比实际可读上限更密时标红；范围缩小或窗口变宽后自动清除。</summary>
    private void UpdateTickAlerts(IModuleContext context)
    {
        var xTick = _config.Settings.XTickStep;
        var yTick = _config.Settings.YTickStep;
        var xSpan = CurrentXSpan();
        var (yMin, yMax) = CurrentYRange();
        var ySpan = Math.Max(0, yMax - yMin);

        var xDense = IsDense(xSpan, xTick, _maxChartXTicks) || IsDense(xSpan, xTick, _maxRangeXTicks);
        var yDense = IsDense(ySpan, yTick, _maxChartYTicks);
        SetTickAlert(context, XTickSource, _xTickParseInvalid || xDense, ref _xTickAlerted);
        SetTickAlert(context, YTickSource, _yTickParseInvalid || yDense, ref _yTickAlerted);
    }

    private static bool IsDense(double span, double step, int readableLimit) =>
        readableLimit > 0 && step > 0 && span > 0 && span / step > readableLimit;

    private static void SetTickAlert(IModuleContext context, string source, bool invalid, ref bool alerted)
    {
        if (invalid == alerted)
        {
            return;
        }

        alerted = invalid;
        context.Appearance.SetOutlineRole(source, invalid ? AlertRole : null);
    }

    /// <summary>单个输入框的非刻度校验红框（同值不重复设置外观）。</summary>
    private void SetInputValidity(IModuleContext context, string source, bool invalid)
    {
        if (invalid)
        {
            if (_invalidInputs.Add(source))
            {
                context.Appearance.SetOutlineRole(source, AlertRole);
            }
        }
        else if (_invalidInputs.Remove(source))
        {
            context.Appearance.SetOutlineRole(source, null);
        }
    }

    private void ClearInputAlerts(IModuleContext context)
    {
        foreach (var source in _invalidInputs)
        {
            context.Appearance.SetOutlineRole(source, null);
        }

        _invalidInputs.Clear();
        _xTickParseInvalid = false;
        _yTickParseInvalid = false;
        SetTickAlert(context, XTickSource, false, ref _xTickAlerted);
        SetTickAlert(context, YTickSource, false, ref _yTickAlerted);
    }

    private void OnCommand(IModuleContext context, string command)
    {
        switch (command)
        {
            case ClearCommand:
                ClearActive(context);
                break;
            case ResetGroupCommand:
                ResetActiveGroup(context);
                break;
            case SaveSelectionCommand:
                RequestCsv(context, selection: true);
                break;
            case SaveAllCommand:
                RequestCsv(context, selection: false);
                break;
            case SaveImageCommand:
                RequestImage(context);
                break;
        }
    }

    private void OnInput(IModuleContext context, string source, string text)
    {
        switch (source)
        {
            case XStartSource:
                ApplyRangeInput(context, startInput: true, text);
                break;
            case XEndSource:
                ApplyRangeInput(context, startInput: false, text);
                break;
            case YMinSource:
                ApplyYInput(context, minInput: true, text);
                break;
            case YMaxSource:
                ApplyYInput(context, minInput: false, text);
                break;
            case YAutoSource:
                if (bool.TryParse(text.Trim(), out var yAuto))
                {
                    SetYAuto(context, yAuto);
                }

                break;
            case FollowSource:
                if (bool.TryParse(text.Trim(), out var follow))
                {
                    SetFollow(context, follow);
                }

                break;
            case CursorsSource:
                if (bool.TryParse(text.Trim(), out var cursors))
                {
                    _config = _config with { Settings = _config.Settings with { Cursors = cursors } };
                    context.Configuration.Write(_config);
                    PublishMeta(context);
                    PublishInputs(context);
                }

                break;
            case XAxisModeSource:
                ApplyXAxisMode(context, text);
                break;
            case FollowRangeSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var followRange)
                    && followRange is >= MinimumRangeSamples and <= MaximumFollowRangeSamples)
                {
                    _config = _config with { Settings = _config.Settings with { FollowRangeSamples = followRange } };
                    context.Configuration.Write(_config);
                    _defaultRangeStart = 0;
                    _defaultRangeEnd = followRange - 1;
                    if (ActiveGroup is { } followGroup && followGroup.DisplayIndex > 0 && _config.Settings.Follow)
                    {
                        UpdateFollowRange(followGroup);
                    }

                    BuildSnapshot(context);
                    PublishMeta(context);
                    PublishInputs(context);
                    SetInputValidity(context, FollowRangeSource, invalid: false);
                }
                else
                {
                    SetInputValidity(context, FollowRangeSource, invalid: true);
                }

                break;
            case XTickSource:
                if (TryDouble(text, out var xTick) && xTick >= 0)
                {
                    _xTickParseInvalid = false;
                    _config = _config with { Settings = _config.Settings with { XTickStep = xTick } };
                    context.Configuration.Write(_config);
                    UpdateTickAlerts(context);
                    PublishMeta(context);
                    PublishInputs(context);
                }
                else
                {
                    _xTickParseInvalid = true;
                    UpdateTickAlerts(context);
                }

                break;
            case YTickSource:
                if (TryDouble(text, out var yTick) && yTick >= 0)
                {
                    _yTickParseInvalid = false;
                    _config = _config with { Settings = _config.Settings with { YTickStep = yTick } };
                    context.Configuration.Write(_config);
                    UpdateTickAlerts(context);
                    PublishMeta(context);
                    PublishInputs(context);
                }
                else
                {
                    _yTickParseInvalid = true;
                    UpdateTickAlerts(context);
                }

                break;
            case KeepInactiveSource:
                if (bool.TryParse(text.Trim(), out var keepInactive))
                {
                    _config = _config with { KeepInactiveHistory = keepInactive };
                    if (!keepInactive)
                    {
                        foreach (var pair in _groups)
                        {
                            if (pair.Key != _activeGroupId)
                            {
                                pair.Value.ClearHistory();
                            }
                        }
                    }

                    context.Configuration.Write(_config);
                    PublishMeta(context);
                    PublishInputs(context);
                }

                break;
            case HistoryLimitSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
                    && limit >= 0)
                {
                    _config = _config with { HistoryLimitSamples = limit };
                    _historyLimited = false;
                    context.Configuration.Write(_config);
                    PublishMeta(context);
                    PublishInputs(context);
                    SetInputValidity(context, HistoryLimitSource, invalid: false);
                }
                else
                {
                    SetInputValidity(context, HistoryLimitSource, invalid: true);
                }

                break;
            case ActionSource:
                OnAction(context, text);
                break;
            case SavePathSource:
                OnSavePath(context, text);
                break;
            case ImageResultSource:
                OnImageResult(text);
                break;
        }
    }

    private void ApplyRangeInput(IModuleContext context, bool startInput, string text)
    {
        var valid = TryParseX(context, ActiveGroup, text, out var sample);
        if (!valid)
        {
            SetInputValidity(context, startInput ? XStartSource : XEndSource, invalid: true);
            return;
        }

        var group = ActiveGroup;
        if (group is null || !group.HasRange || group.DisplayIndex <= 0)
        {
            var start = startInput ? sample : _defaultRangeStart;
            var end = startInput ? _defaultRangeEnd : sample;
            if (end <= start)
            {
                SetInputValidity(context, startInput ? XStartSource : XEndSource, invalid: true);
                return;
            }

            _defaultRangeStart = Math.Max(0, start);
            _defaultRangeEnd = Math.Max(_defaultRangeStart + 1, end);
            _defaultRangeEdited = true;
            SetInputValidity(context, XStartSource, invalid: false);
            SetInputValidity(context, XEndSource, invalid: false);
            PublishEmptySnapshot(context);
            PublishMeta(context);
            return;
        }

        var rangeStart = startInput ? sample : group.RangeStart;
        var rangeEnd = startInput ? group.RangeEnd : sample;
        if (rangeEnd <= rangeStart)
        {
            SetInputValidity(context, startInput ? XStartSource : XEndSource, invalid: true);
            return;
        }

        SetInputValidity(context, XStartSource, invalid: false);
        SetInputValidity(context, XEndSource, invalid: false);
        SetRange(context, group, rangeStart, rangeEnd, follow: false);
    }

    private void ApplyYInput(IModuleContext context, bool minInput, string text)
    {
        if (!TryDouble(text, out var value) || !double.IsFinite(value))
        {
            SetInputValidity(context, minInput ? YMinSource : YMaxSource, invalid: true);
            return;
        }

        var group = ActiveGroup;
        if (group is null || group.DisplayIndex <= 0)
        {
            var yMin = minInput ? value : _defaultYMin;
            var yMax = minInput ? _defaultYMax : value;
            if (yMax <= yMin)
            {
                SetInputValidity(context, minInput ? YMinSource : YMaxSource, invalid: true);
                return;
            }

            _defaultYAuto = false;
            _defaultYMin = yMin;
            _defaultYMax = yMax;
            _defaultYEdited = true;
            if (group is not null)
            {
                UpdateGroupConfig(context, group, group.Config with
                {
                    YAuto = false,
                    YMin = yMin,
                    YMax = yMax,
                });
            }

            SetInputValidity(context, YMinSource, invalid: false);
            SetInputValidity(context, YMaxSource, invalid: false);
            PublishEmptySnapshot(context);
            PublishMeta(context);
            return;
        }

        var (currentMin, currentMax) = EffectiveY(group);
        var nextMin = minInput ? value : currentMin;
        var nextMax = minInput ? currentMax : value;
        if (nextMax <= nextMin)
        {
            SetInputValidity(context, minInput ? YMinSource : YMaxSource, invalid: true);
            return;
        }

        SetInputValidity(context, YMinSource, invalid: false);
        SetInputValidity(context, YMaxSource, invalid: false);
        UpdateGroupConfig(context, group, group.Config with
        {
            YAuto = false,
            YMin = nextMin,
            YMax = nextMax,
        });
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void SetYAuto(IModuleContext context, bool auto)
    {
        var group = ActiveGroup;
        if (group is null || group.DisplayIndex <= 0)
        {
            _defaultYAuto = auto;
            if (auto)
            {
                _defaultYMin = -1;
                _defaultYMax = 1;
            }

            _defaultYEdited = true;
            if (group is not null)
            {
                UpdateGroupConfig(context, group, group.Config with
                {
                    YAuto = auto,
                    YMin = auto ? group.Config.YMin : _defaultYMin,
                    YMax = auto ? group.Config.YMax : _defaultYMax,
                });
            }

            PublishEmptySnapshot(context);
            PublishMeta(context);
            return;
        }

        if (auto)
        {
            // 用户显式开启自适应 = 重新以当前数据为起点；之后只增不减。
            group.ResetAutoY();
        }

        var (effectiveMin, effectiveMax) = EffectiveY(group);
        var config = group.Config with
        {
            YAuto = auto,
            YMin = auto ? group.Config.YMin : effectiveMin,
            YMax = auto ? group.Config.YMax : effectiveMax,
        };

        UpdateGroupConfig(context, group, config);
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void SetFollow(IModuleContext context, bool follow)
    {
        _config = _config with { Settings = _config.Settings with { Follow = follow } };
        context.Configuration.Write(_config);

        if (follow && ActiveGroup is { } group && group.DisplayIndex > 0)
        {
            UpdateFollowRange(group);
            BuildSnapshot(context);
        }

        if (ActiveGroup is not { DisplayIndex: > 0 })
        {
            PublishEmptySnapshot(context);
        }

        PublishMeta(context);
        PublishInputs(context);
    }

    private void ApplyXAxisMode(IModuleContext context, string text)
    {
        var mode = string.Equals(text.Trim(), "time", StringComparison.OrdinalIgnoreCase)
            ? DisplayXAxisMode.Time
            : DisplayXAxisMode.Index;

        if (mode == DisplayXAxisMode.Time && EffectiveRate(ActiveGroup) <= 0)
        {
            mode = DisplayXAxisMode.Index;
            PublishStatus(Tr("status.rateUnknown"));
        }

        _config = _config with { Settings = _config.Settings with { XAxisMode = mode } };
        context.Configuration.Write(_config);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void OnAction(IModuleContext context, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("op", out var opElement) || opElement.GetString() is not { } op)
            {
                return;
            }

            switch (op)
            {
                case "selectGroup":
                    if (root.TryGetProperty("id", out var idElement)
                        && idElement.TryGetUInt32(out var id)
                        && _groups.ContainsKey(id))
                    {
                        ActivateGroup(context, id);
                    }

                    break;

                case "setRange":
                    if (root.TryGetProperty("start", out var startElement)
                        && root.TryGetProperty("end", out var endElement)
                        && startElement.TryGetInt64(out var rangeStart)
                        && endElement.TryGetInt64(out var rangeEnd))
                    {
                        if (ActiveGroup is { DisplayIndex: > 0 } rangeGroup)
                        {
                            SetRange(context, rangeGroup, rangeStart, rangeEnd, follow: false);
                        }
                        else
                        {
                            _defaultRangeStart = Math.Max(0, Math.Min(rangeStart, rangeEnd));
                            _defaultRangeEnd = Math.Max(_defaultRangeStart + 1, Math.Max(rangeStart, rangeEnd));
                            _defaultRangeEdited = true;
                            PublishEmptySnapshot(context);
                            PublishMeta(context);
                        }
                    }

                    break;

                case "renameGroup":
                    if (root.TryGetProperty("name", out var groupName))
                    {
                        var name = groupName.GetString() ?? string.Empty;
                        var target = ActiveGroup;
                        if (root.TryGetProperty("id", out var groupIdElement)
                            && groupIdElement.TryGetUInt32(out var groupId)
                            && _groups.TryGetValue(groupId, out var namedGroup))
                        {
                            target = namedGroup;
                        }

                        if (target is not null)
                        {
                            UpdateGroupConfig(context, target, target.Config with { Name = name.Trim() });
                            PublishMeta(context);
                        }
                    }

                    break;

                case "selectChannel":
                    if (root.TryGetProperty("index", out var indexElement))
                    {
                        _activeChannelIndex = Math.Max(0, indexElement.GetInt32());
                        _config = _config with { ActiveChannelIndex = _activeChannelIndex };
                        context.Configuration.Write(_config);
                        PublishMeta(context);
                    }

                    break;

                case "setColor":
                    if (root.TryGetProperty("index", out var colorIndex)
                        && root.TryGetProperty("color", out var colorElement))
                    {
                        var index = colorIndex.GetInt32();
                        var color = ParseColor(colorElement.GetString() ?? string.Empty);
                        UpdateChannelAt(context, index, channel => channel with { ColorArgb = color });
                    }

                    break;

                case "resetColor":
                    if (root.TryGetProperty("index", out var resetColorIndex))
                    {
                        var index = resetColorIndex.GetInt32();
                        UpdateChannelAt(context, index, channel => channel with { ColorArgb = null });
                    }

                    break;

                case "renameChannel":
                    if (root.TryGetProperty("index", out var renameIndex)
                        && root.TryGetProperty("name", out var renameName))
                    {
                        var index = renameIndex.GetInt32();
                        var name = renameName.GetString() ?? string.Empty;
                        UpdateChannelAt(context, index, channel => channel with { Name = name.Trim() });
                    }

                    break;

                case "setVisible":
                    if (root.TryGetProperty("index", out var showIndex)
                        && root.TryGetProperty("visible", out var showValue))
                    {
                        var index = showIndex.GetInt32();
                        var visible = showValue.GetBoolean();
                        UpdateChannelAt(context, index, channel => channel with { Visible = visible });
                    }

                    break;

                case "fitY":
                    if (root.TryGetProperty("index", out var fitIndex) && ActiveGroup is { } fitGroup)
                    {
                        var index = fitIndex.GetInt32();
                        var next = index >= 0 && index < fitGroup.ChannelCount
                            && fitGroup.Config.YFitChannel != index
                            ? index
                            : -1;
                        // 显式"适应"允许重新缩小到当前通道/全部数据。
                        fitGroup.ResetAutoY();
                        UpdateGroupConfig(context, fitGroup, fitGroup.Config with
                        {
                            YAuto = true,
                            YFitChannel = next,
                        });
                        BuildSnapshot(context);
                        PublishMeta(context);
                        PublishInputs(context);
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // 非法操作只忽略，不打断接收。
        }
    }

    private void UpdateGroupConfig(IModuleContext context, DisplayGroupRuntime group, DisplayGroupConfig config)
    {
        group.Config = config;
        ReplaceGroupConfig(config);
        context.Configuration.Write(_config);
    }

    private void UpdateChannelAt(
        IModuleContext context,
        int index,
        Func<DisplayChannelConfig, DisplayChannelConfig> update)
    {
        var group = ActiveGroup;
        if (group is null || index < 0 || index >= group.Config.Channels.Count)
        {
            return;
        }

        var channels = group.Config.Channels.ToList();
        channels[index] = update(channels[index]);
        UpdateGroupConfig(context, group, group.Config with { Channels = channels });
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void ReplaceGroupConfig(DisplayGroupConfig config)
    {
        var groups = _config.Groups.ToList();
        for (var index = 0; index < groups.Count; index++)
        {
            if (groups[index].GroupId == config.GroupId)
            {
                groups[index] = config;
                _config = _config with { Groups = groups };
                return;
            }
        }

        groups.Add(config);
        _config = _config with { Groups = groups };
    }

    private void DrainFrames(IModuleContext context)
    {
        while (context.Ports.TryReceive(out var buffer, out _))
        {
            try
            {
                HandleFrame(context, buffer.Span);
            }
            finally
            {
                context.Ports.ReleaseBuffer(buffer);
            }
        }
    }

    private void HandleFrame(IModuleContext context, ReadOnlySpan<byte> payload)
    {
        if (!WaveformFrameReader.TryRead(payload, out var frame, out _))
        {
            _badFrames++;
            PublishStatus(Tr(WaveformDisplayText.Invalid));
            return;
        }

        var now = Environment.TickCount64;
        var group = GetOrCreateGroup(context, frame.GroupId);
        var firstFrame = group.DisplayIndex == 0;

        if (group.ChannelCount != frame.ChannelCount)
        {
            EnsureGroupChannels(context, group, frame.ChannelCount);
        }

        if (frame.RateHz > 0 && float.IsFinite(frame.RateHz))
        {
            group.DeclaredRateHz = frame.RateHz;
        }

        group.ObserveSequence(frame.Sequence);
        group.MarkReceived(now);

        if (_activeGroupId == 0)
        {
            ActivateGroup(context, group.GroupId);
        }

        var active = group.GroupId == _activeGroupId;

        // 活动组永远保留（否则显示会自己冻住）；历史上限只约束非活动组。
        var saving = active || (_config.KeepInactiveHistory && !HistoryLimitReached(group));
        if (saving)
        {
            group.Append(frame.Samples, frame.ValidMask, now);
        }

        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());

        if (firstFrame && saving)
        {
            ApplyNoDataDefaults(context, group);
        }

        if (!active)
        {
            return;
        }

        PublishValue(context, LatestIndexSource, group.DisplayIndex > 0 ? (ulong)(group.DisplayIndex - 1) : 0UL);
        PublishValue(context, RateSource, (float)EffectiveRate(group));

        if (!group.HasRange)
        {
            SetDefaultRange(group);
            BuildSnapshot(context);
        }
        else if (_config.Settings.Follow)
        {
            var width = (int)Math.Max(MinimumRangeSamples, group.RangeEnd - group.RangeStart + 1);
            var large = width > LargeRangeFrameThreshold;
            if (!large || now - _lastLargeBuildMs >= LargeRangeBuildIntervalMs)
            {
                UpdateFollowRange(group);
                BuildSnapshot(context);
                if (large)
                {
                    _lastLargeBuildMs = now;
                }
            }
        }
        else
        {
            BuildSnapshot(context);
        }

        if (firstFrame)
        {
            PublishMeta(context);
        }

        PublishStatus(Tr(WaveformDisplayText.Receiving));
    }

    /// <summary>首个数据帧到达时，把无数据阶段编辑过的默认视口夹紧后应用到这个组。</summary>
    private void ApplyNoDataDefaults(IModuleContext context, DisplayGroupRuntime group)
    {
        if (!_defaultRangeEdited && !_defaultYEdited)
        {
            return;
        }

        var latest = Math.Max(0, group.DisplayIndex - 1);
        if (_defaultYEdited)
        {
            var config = group.Config with
            {
                YAuto = _defaultYAuto,
                YMin = _defaultYAuto ? group.Config.YMin : _defaultYMin,
                YMax = _defaultYAuto ? group.Config.YMax : _defaultYMax,
            };
            UpdateGroupConfig(context, group, config);
        }

        if (_defaultRangeEdited && !_config.Settings.Follow && latest > 0)
        {
            var start = Math.Clamp(_defaultRangeStart, 0, latest);
            var end = Math.Clamp(_defaultRangeEnd, 0, latest);
            if (end > start)
            {
                group.SetRange(start, end);
            }
        }
    }

    /// <summary>非活动组当前保留的样本帧合计。</summary>
    private long InactiveSampleCount()
    {
        if (!_config.KeepInactiveHistory)
        {
            return 0;
        }

        long total = 0;
        foreach (var (id, group) in _groups)
        {
            if (id != _activeGroupId)
            {
                total += group.DisplayIndex;
            }
        }

        return total;
    }

    private bool HistoryLimitReached(DisplayGroupRuntime group)
    {
        _ = group;
        if (_config.HistoryLimitSamples <= 0)
        {
            return false;
        }

        if (InactiveSampleCount() < _config.HistoryLimitSamples)
        {
            return false;
        }

        if (!_historyLimited)
        {
            _historyLimited = true;
            PublishStatus(Tr("status.historyLimit"));
        }

        return true;
    }

    private DisplayGroupRuntime GetOrCreateGroup(IModuleContext context, uint groupId)
    {
        if (_groups.TryGetValue(groupId, out var existing))
        {
            return existing;
        }

        var config = _config.FindGroup(groupId);
        if (config is null)
        {
            config = new DisplayGroupConfig { GroupId = groupId };
            var groups = _config.Groups.ToList();
            groups.Add(config);
            _config = _config with { Groups = groups };
            context.Configuration.Write(_config);
            _structureRevision++;
        }

        var runtime = new DisplayGroupRuntime
        {
            GroupId = groupId,
            Config = config,
        };

        _groups[groupId] = runtime;
        _groupOrder.Add(groupId);
        return runtime;
    }

    private void EnsureGroupChannels(IModuleContext context, DisplayGroupRuntime group, int count)
    {
        group.EnsureChannels(count);
        var channels = group.Config.Channels.ToList();

        if (channels.Count > count)
        {
            channels.RemoveRange(count, channels.Count - count);
        }

        while (channels.Count < count)
        {
            channels.Add(new DisplayChannelConfig());
        }

        UpdateGroupConfig(context, group, group.Config with { Channels = channels });
        _structureRevision++;

        if (group.GroupId == _activeGroupId)
        {
            group.ResetRange();
            if (group.DisplayIndex > 0)
            {
                SetDefaultRange(group);
                BuildSnapshot(context);
            }
        }
    }

    private void ActivateGroup(IModuleContext context, uint groupId)
    {
        if (_activeGroupId == groupId && _groups.TryGetValue(groupId, out var current))
        {
            if (!current.HasRange && current.DisplayIndex > 0)
            {
                SetDefaultRange(current);
                BuildSnapshot(context);
            }

            return;
        }

        if (!_config.KeepInactiveHistory && _groups.TryGetValue(_activeGroupId, out var previous))
        {
            previous.ClearHistory();
        }

        _activeGroupId = groupId;
        _activeChannelIndex = 0;
        _config = _config with { ActiveGroupId = groupId, ActiveChannelIndex = 0 };
        context.Configuration.Write(_config);
        _structureRevision++;

        if (_groups.TryGetValue(groupId, out var group))
        {
            if (_config.Settings.Follow && group.DisplayIndex > 0)
            {
                UpdateFollowRange(group);
            }
            else if (!group.HasRange && group.DisplayIndex > 0)
            {
                SetDefaultRange(group);
            }

            BuildSnapshot(context);
        }
        else
        {
            PublishEmptySnapshot(context);
        }

        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
        PublishMeta(context);
        PublishInputs(context);
    }

    private void ClearActive(IModuleContext context)
    {
        var active = ActiveGroup;
        if (active is null)
        {
            return;
        }

        active.ClearHistory();
        _historyLimited = false;
        _defaultRangeStart = 0;
        _defaultRangeEnd = _config.Settings.FollowRangeSamples - 1;
        _defaultYAuto = true;
        _defaultYMin = -1;
        _defaultYMax = 1;
        _defaultRangeEdited = false;
        _defaultYEdited = false;
        BuildSnapshot(context);
        PublishValue(context, LatestIndexSource, 0UL);
        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
        PublishMeta(context);
        PublishInputs(context);
        PublishStatus(Tr(WaveformDisplayText.Cleared));
    }

    private void ResetActiveGroup(IModuleContext context)
    {
        var active = ActiveGroup;
        if (active is null)
        {
            return;
        }

        var channels = active.Config.Channels
            .Select(_ => new DisplayChannelConfig())
            .ToList();

        active.ResetAutoY();
        UpdateGroupConfig(context, active, active.Config with
        {
            Name = string.Empty,
            Channels = channels,
            YAuto = true,
            YMin = -1,
            YMax = 1,
            YFitChannel = -1,
        });

        _config = _config with
        {
            Settings = new DisplaySettings(),
        };
        context.Configuration.Write(_config);
        _structureRevision++;
        _defaultRangeStart = 0;
        _defaultRangeEnd = _config.Settings.FollowRangeSamples - 1;
        _defaultYAuto = true;
        _defaultYMin = -1;
        _defaultYMax = 1;
        _defaultRangeEdited = false;
        _defaultYEdited = false;
        ClearInputAlerts(context);

        if (active.DisplayIndex > 0)
        {
            SetDefaultRange(active);
        }
        else
        {
            active.ResetRange();
        }

        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void SetDefaultRange(DisplayGroupRuntime group)
    {
        if (group.DisplayIndex <= 0)
        {
            group.ResetRange();
            return;
        }

        var latest = group.DisplayIndex - 1;
        var width = Math.Clamp(
            _config.Settings.FollowRangeSamples,
            MinimumRangeSamples,
            MaximumFollowRangeSamples);
        var start = Math.Max(0, latest - width + 1);
        group.SetRange(start, latest);
    }

    private void UpdateFollowRange(DisplayGroupRuntime group)
    {
        if (group.DisplayIndex <= 0)
        {
            group.ResetRange();
            return;
        }

        var latest = group.DisplayIndex - 1;
        var width = Math.Clamp(
            _config.Settings.FollowRangeSamples,
            MinimumRangeSamples,
            MaximumFollowRangeSamples);
        var start = Math.Max(0, latest - width + 1);
        group.SetRange(start, latest);
    }

    private void SetRange(IModuleContext context, DisplayGroupRuntime group, long start, long end, bool follow)
    {
        if (group.DisplayIndex <= 0)
        {
            group.ResetRange();
            _config = _config with { Settings = _config.Settings with { Follow = follow } };
            context.Configuration.Write(_config);
            BuildSnapshot(context);
            PublishInputs(context);
            return;
        }

        var latest = group.DisplayIndex - 1;
        var clamped = ClampRange(start, end, latest);
        group.SetRange(clamped.Start, clamped.End);
        _config = _config with { Settings = _config.Settings with { Follow = follow } };
        context.Configuration.Write(_config);
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private static (long Start, long End) ClampRange(long start, long end, long latest)
    {
        if (latest <= 0)
        {
            return (0, 0);
        }

        start = Math.Clamp(start, 0, latest);
        end = Math.Clamp(end, 0, latest);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end - start + 1 < MinimumRangeSamples)
        {
            end = Math.Min(latest, start + MinimumRangeSamples - 1);
            start = Math.Max(0, end - MinimumRangeSamples + 1);
        }

        return (start, end);
    }

    private static (double Min, double Max) EffectiveY(DisplayGroupRuntime group)
    {
        if (!group.Config.YAuto)
        {
            var manualMin = group.Config.YMin;
            var manualMax = group.Config.YMax;
            if (manualMax <= manualMin)
            {
                manualMax = manualMin + 1;
            }

            return (manualMin, manualMax);
        }

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var fitIndex = group.Config.YFitChannel;
        var useFit = fitIndex >= 0 && fitIndex < group.ChannelCount;

        for (var index = 0; index < group.ChannelCount; index++)
        {
            if (useFit && index != fitIndex)
            {
                continue;
            }

            var channel = index < group.Config.Channels.Count ? group.Config.Channels[index] : null;
            if (channel is { Visible: false })
            {
                continue;
            }

            if (!group.HasRange || index >= group.Histories.Length)
            {
                continue;
            }

            var history = group.Histories[index];
            for (var sample = group.RangeStart; sample <= group.RangeEnd; sample++)
            {
                var value = history[sample];
                if (float.IsFinite(value))
                {
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return group.HasAutoY ? (group.AutoYMin, group.AutoYMax) : (-1, 1);
        }

        double paddedMin;
        double paddedMax;
        if (max - min < 1e-9)
        {
            paddedMin = min - 1;
            paddedMax = max + 1;
        }
        else
        {
            var padding = (max - min) * 0.05;
            paddedMin = min - padding;
            paddedMax = max + padding;
        }

        // 只会被挤高，不会自动缩回；缩小只走显式"适应"/复位/清空。
        group.ExpandAutoY(paddedMin, paddedMax);
        return (group.AutoYMin, group.AutoYMax);
    }

    private void BuildSnapshot(IModuleContext context)
    {
        var group = ActiveGroup;
        if (group is null || !group.HasRange || group.DisplayIndex <= 0)
        {
            PublishEmptySnapshot(context);
            return;
        }

        var start = group.RangeStart;
        var end = group.RangeEnd;
        var width = (int)Math.Min(int.MaxValue, end - start + 1);
        var channels = new List<SeriesWindowChannel>();
        var rate = EffectiveRate(group);

        for (var index = 0; index < group.ChannelCount; index++)
        {
            var config = index < group.Config.Channels.Count ? group.Config.Channels[index] : null;
            if (config is { Visible: false } || index >= group.Histories.Length)
            {
                continue;
            }

            var values = new float[width];
            var history = group.Histories[index];
            for (var offset = 0; offset < width; offset++)
            {
                values[offset] = history[start + offset];
            }

            channels.Add(new SeriesWindowChannel(
                index,
                string.IsNullOrWhiteSpace(config?.Name) ? Tr(WaveformDisplayText.ChannelFallback, index + 1) : config!.Name,
                config?.ColorArgb,
                values));
        }

        var (yMin, yMax) = EffectiveY(group);
        var latest = group.DisplayIndex - 1;
        var snapshot = new SeriesWindowSnapshot(
            ++_snapshotVersion,
            start,
            end,
            latest,
            rate,
            yMin,
            yMax,
            group.Config.YAuto,
            channels);

        context.Display.SetSeriesWindow(SeriesSource, snapshot);
        PublishValue(context, LatestIndexSource, (ulong)latest);
        PublishValue(context, RateSource, (float)rate);
        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
        UpdateTickAlerts(context);
        PublishInputs(context);
    }

    private void PublishEmptySnapshot(IModuleContext context)
    {
        var group = ActiveGroup;
        var latest = group is { DisplayIndex: > 0 } ? group.DisplayIndex - 1 : 0;
        var (start, end) = CurrentXRange();
        var (yMin, yMax, yAuto) = NoDataY();
        var snapshot = new SeriesWindowSnapshot(
            ++_snapshotVersion,
            start,
            Math.Max(start + 1, end),
            latest,
            EffectiveRate(group),
            yMin,
            yMax,
            yAuto,
            []);
        context.Display.SetSeriesWindow(SeriesSource, snapshot);
        PublishValue(context, LatestIndexSource, (ulong)Math.Max(0, latest));
        PublishValue(context, RateSource, (float)EffectiveRate(group));
        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
        UpdateTickAlerts(context);
        PublishInputs(context);
    }

    private (double Min, double Max, bool Auto) NoDataY()
    {
        var group = ActiveGroup;
        if (group is null)
        {
            return (_defaultYMin, _defaultYMax, _defaultYAuto);
        }

        return group.Config.YAuto
            ? (-1, 1, true)
            : (group.Config.YMin, group.Config.YMax, false);
    }

    private static double EffectiveRate(DisplayGroupRuntime? group) => group?.DeclaredRateHz ?? 0;

    private void PublishMeta(IModuleContext context)
    {
        var active = ActiveGroup;
        var groups = new List<object>(_groupOrder.Count);
        var ordered = new List<uint>();

        foreach (var config in _config.Groups)
        {
            if (_groups.ContainsKey(config.GroupId))
            {
                ordered.Add(config.GroupId);
            }
        }

        foreach (var id in _groupOrder)
        {
            if (!ordered.Contains(id))
            {
                ordered.Add(id);
            }
        }

        foreach (var id in ordered)
        {
            if (!_groups.TryGetValue(id, out var group))
            {
                continue;
            }

            groups.Add(new
            {
                id,
                name = GroupDisplayName(group),
                channels = group.ChannelCount,
                dataCount = group.DisplayIndex,
                declaredRate = group.DeclaredRateHz,
                actualRate = group.ActualRateHz,
                lost = group.LostFrames,
                received = group.ReceivedFrames,
                bytes = group.EstimatedBytes,
                active = id == _activeGroupId,
                yAuto = group.Config.YAuto,
                yMin = group.Config.YMin,
                yMax = group.Config.YMax,
                yFitChannel = group.Config.YFitChannel,
            });
        }

        var channels = active is null
            ? null
            : active.Config.Channels.Select((channel, index) => new
            {
                index,
                name = ChannelDisplayName(channel, index),
                color = channel.ColorArgb,
                visible = channel.Visible,
            }).ToArray();

        var settings = _config.Settings;
        UpdateTickAlerts(context);
        var snapshot = new
        {
            revision = ++_metaRevision,
            structureRevision = _structureRevision,
            activeGroupId = _activeGroupId,
            activeChannelIndex = _activeChannelIndex,
            groups,
            channels,
            settings = new
            {
                xAxisMode = settings.XAxisMode == DisplayXAxisMode.Time ? "time" : "index",
                xTick = settings.XTickStep,
                yTick = settings.YTickStep,
                follow = settings.Follow,
                followRange = settings.FollowRangeSamples,
                cursors = settings.Cursors,
                keepInactive = _config.KeepInactiveHistory,
                historyLimit = _config.HistoryLimitSamples,
            },
            badFrames = _badFrames,
            historyLimited = _historyLimited,
        };

        context.Display.SetText(MetaSource, JsonSerializer.Serialize(snapshot));
    }

    private void PublishInputs(IModuleContext context)
    {
        var group = ActiveGroup;
        var settings = _config.Settings;
        var rate = EffectiveRate(group);
        var hasData = group is { DisplayIndex: > 0 };

        if (hasData && group!.HasRange)
        {
            SetInput(context, XStartSource, FormatX(group.RangeStart, settings.XAxisMode, rate));
            SetInput(context, XEndSource, FormatX(group.RangeEnd, settings.XAxisMode, rate));
        }
        else
        {
            SetInput(context, XStartSource, FormatX(_defaultRangeStart, settings.XAxisMode, rate));
            SetInput(context, XEndSource, FormatX(_defaultRangeEnd, settings.XAxisMode, rate));
        }

        var yMin = -1d;
        var yMax = 1d;
        var yAuto = true;
        if (hasData)
        {
            (yMin, yMax) = EffectiveY(group!);
            yAuto = group!.Config.YAuto;
        }
        else
        {
            (yMin, yMax, yAuto) = NoDataY();
        }

        SetInput(context, YMinSource, Format(yMin));
        SetInput(context, YMaxSource, Format(yMax));
        SetInput(context, YAutoSource, yAuto ? "true" : "false");
        SetInput(context, FollowSource, settings.Follow ? "true" : "false");
        SetInput(context, CursorsSource, settings.Cursors ? "true" : "false");
        SetInput(context, XAxisModeSource, settings.XAxisMode == DisplayXAxisMode.Time ? "time" : "index");
        SetInput(context, XTickSource, Format(settings.XTickStep));
        SetInput(context, YTickSource, Format(settings.YTickStep));
        SetInput(context, KeepInactiveSource, _config.KeepInactiveHistory ? "true" : "false");
        SetInput(context, HistoryLimitSource, _config.HistoryLimitSamples.ToString(CultureInfo.InvariantCulture));
        SetInput(context, FollowRangeSource, settings.FollowRangeSamples.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>回写模块认可的输入值；值相同不写，避免再次触发 Input step 形成回声。</summary>
    private void SetInput(IModuleContext context, string source, string text)
    {
        _lastText[source] = text;
        if (string.Equals(context.ViewState.GetInputText(source), text, StringComparison.Ordinal))
        {
            return;
        }

        context.ViewState.SetInputValue(source, text);
    }

    private static string FormatX(long sample, DisplayXAxisMode mode, double rate) =>
        sample < 0
            ? string.Empty
            : mode == DisplayXAxisMode.Time && rate > 0
                ? (sample / rate).ToString("0.######", CultureInfo.InvariantCulture)
                : sample.ToString(CultureInfo.InvariantCulture);

    private bool TryParseX(IModuleContext context, DisplayGroupRuntime? group, string text, out long sample)
    {
        _ = context;
        sample = 0;
        var rate = EffectiveRate(group);
        if (_config.Settings.XAxisMode == DisplayXAxisMode.Time && rate > 0)
        {
            return TryDouble(text, out var seconds)
                && (sample = (long)Math.Round(seconds * rate)) >= 0;
        }

        return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sample)
            || TryDouble(text, out var value) && (sample = (long)Math.Round(value)) >= 0;
    }

    private void RequestCsv(IModuleContext context, bool selection)
    {
        if (_export is not null)
        {
            PublishStatus(Tr("status.saveBusy"));
            return;
        }

        var group = ActiveGroup;
        if (group is null || group.DisplayIndex <= 0)
        {
            PublishStatus(Tr("status.saveNoData"));
            return;
        }

        var end = group.DisplayIndex - 1;
        var start = 0L;

        if (selection)
        {
            if (!group.HasRange)
            {
                PublishStatus(Tr("status.saveNoSelection"));
                return;
            }

            start = group.RangeStart;
            end = group.RangeEnd;
        }

        if (end < start)
        {
            PublishStatus(Tr("status.saveNoData"));
            return;
        }

        _pendingSave = selection ? PendingSave.Selection : PendingSave.All;
        _pendingGroup = group;
        _pendingStart = start;
        _pendingEnd = end;
        context.Files.Request(new FilePickRequest(
            SavePathSource,
            FilePickMode.Save,
            Tr("save.filter.csv"),
            "waveform.csv"));
    }

    private void RequestImage(IModuleContext context)
    {
        if (_export is not null)
        {
            PublishStatus(Tr("status.saveBusy"));
            return;
        }

        if (ActiveGroup is null)
        {
            PublishStatus(Tr("status.saveNoData"));
            return;
        }

        _pendingSave = PendingSave.Image;
        context.Files.Request(new FilePickRequest(
            SavePathSource,
            FilePickMode.Save,
            Tr("save.filter.png"),
            "waveform.png"));
    }

    private void OnSavePath(IModuleContext context, string text)
    {
        var path = text.Trim();
        if (path.Length == 0)
        {
            _pendingSave = PendingSave.None;
            PublishStatus(Tr("status.saveNoPath"));
            return;
        }

        if (_pendingSave == PendingSave.Image)
        {
            _pendingSave = PendingSave.None;
            _imagePath = path;
            _imageRequestId++;
            PublishText(context, ImageRequestSource, JsonSerializer.Serialize(new
            {
                id = _imageRequestId,
                path,
            }));
            PublishStatus(Tr("status.imagePending"));
            return;
        }

        if (_pendingSave is PendingSave.Selection or PendingSave.All)
        {
            var group = _pendingGroup ?? ActiveGroup;
            _pendingSave = PendingSave.None;
            _pendingGroup = null;
            if (group is null)
            {
                PublishStatus(Tr("status.saveNoData"));
                return;
            }

            try
            {
                var names = new string[group.Config.Channels.Count];
                for (var index = 0; index < names.Length; index++)
                {
                    names[index] = ChannelDisplayName(group.Config.Channels[index], index);
                }

                _exportPath = path;
                _export = new CsvExport(
                    path,
                    group,
                    group.ChannelCount,
                    names,
                    EffectiveRate(group),
                    _pendingStart,
                    _pendingEnd);
                PublishStatus(Tr("status.saving", 0));
                AdvanceExport();
            }
            catch (Exception exception)
            {
                _export?.Dispose();
                _export = null;
                PublishStatus(Tr("status.saveError", exception.Message));
            }
        }
    }

    private void OnImageResult(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement)
                || !idElement.TryGetInt64(out var id)
                || id != _imageRequestId)
            {
                return;
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            if (ok)
            {
                PublishStatus(Tr("status.imageSaved", _imagePath));
                return;
            }

            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString() ?? string.Empty
                : string.Empty;
            PublishStatus(Tr("status.imageError", error));
        }
        catch (JsonException)
        {
            // 坏消息只忽略。
        }
    }

    private void AdvanceExport()
    {
        if (_export is null)
        {
            return;
        }

        try
        {
            var done = _export.WriteChunk(2000);
            if (!done)
            {
                var percent = _export.TotalRows <= 0
                    ? 100
                    : (int)Math.Clamp(_export.WrittenRows * 100 / _export.TotalRows, 0, 99);
                PublishStatus(Tr("status.saving", percent));
                return;
            }

            var rows = _export.WrittenRows;
            _export.Dispose();
            _export = null;
            PublishStatus(Tr("status.saved", _exportPath, rows));
        }
        catch (Exception exception)
        {
            _export?.Dispose();
            _export = null;
            PublishStatus(Tr("status.saveError", exception.Message));
        }
    }

    private DisplayGroupRuntime? ActiveGroup =>
        _groups.TryGetValue(_activeGroupId, out var group) ? group : null;

    private static string GroupDisplayName(DisplayGroupRuntime group) =>
        string.IsNullOrWhiteSpace(group.Config.Name)
            ? Tr(WaveformDisplayText.GroupFallback, group.GroupId)
            : group.Config.Name;

    private static string ChannelDisplayName(DisplayChannelConfig channel, int index) =>
        string.IsNullOrWhiteSpace(channel.Name)
            ? Tr(WaveformDisplayText.ChannelFallback, index + 1)
            : channel.Name;

    private void PublishStatus(string text)
    {
        if (_context is { } context)
        {
            PublishText(context, StatusSource, text);
        }
    }

    private static void PublishText(IModuleContext context, string source, string text) =>
        context.Display.SetText(source, text);

    private static void PublishValue(IModuleContext context, string source, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        context.Ports.Publish(source, buffer);
    }

    private static void PublishValue(IModuleContext context, string source, float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        context.Ports.Publish(source, buffer);
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static uint? ParseColor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 6)
        {
            trimmed = "FF" + trimmed;
        }

        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)
            ? argb
            : null;
    }

    private void OnUiTimer(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        var now = Environment.TickCount64;
        foreach (var group in _groups.Values)
        {
            group.DecayRate(now);
        }

        AdvanceExport();
        PublishMeta(context);
    }

    private enum PendingSave
    {
        None = 0,
        Selection = 1,
        All = 2,
        Image = 3,
    }
}
