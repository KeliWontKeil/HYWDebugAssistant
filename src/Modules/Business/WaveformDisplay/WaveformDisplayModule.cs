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
using HYWDebugAssistant.Sdk.Values;
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
    Tags = ["terminal", "waveform", "display"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.WaveformDisplayViewFactory))]
public sealed class WaveformDisplayModule : IModule, IActivitySource, IDisposable
{
    /// <summary>输入端口。</summary>
    public const string InPort = "in";

    /// <summary>波形帧类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string WaveformTypeId = WaveformFrameContract.TypeId;

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

    /// <summary>暂停接收全部通道组的新帧。</summary>
    public const string PausedSource = "paused";

    /// <summary>X 轴模式输入。</summary>
    public const string XAxisModeSource = "xAxisMode";

    /// <summary>是否使用模块自定义显示频率。</summary>
    public const string UseCustomRateSource = "useCustomRate";

    /// <summary>模块自定义显示频率（Hz）。</summary>
    public const string CustomRateSource = "customRate";

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

    /// <summary>清空活动组数据并执行一次轻量显示复位。</summary>
    public const string ClearCommand = "clear";

    /// <summary>轻量复位活动组显示（保留历史、组/通道身份）。</summary>
    public const string ResetGroupCommand = "resetGroup";

    /// <summary>Auto：按当前数据量设置跟踪窗口、开跟踪、开 Y 自适应（一次性）。</summary>
    public const string AutoCommand = "auto";

    /// <summary>保存当前 X 范围。</summary>
    public const string SaveSelectionCommand = "saveSelection";

    /// <summary>保存活动组全部历史。</summary>
    public const string SaveAllCommand = "saveAll";

    /// <summary>保存当前图 PNG。</summary>
    public const string SaveImageCommand = "saveImage";

    private const int DefaultFollowRangeSamples = 256;
    private const int MinimumRangeSamples = 2;
    private const int MaximumFollowRangeSamples = 1_000_000;

    /// <summary>Auto 的跟踪窗口占当前数据量的比例（数据量小于最小窗口时仍用最小窗口）。</summary>
    private const double AutoWindowRatio = 0.5;
    private const int LargeRangeFrameThreshold = 2048;
    private const int LargeRangeBuildIntervalMs = 50;

    /// <summary>本模块挑的提示角色（相对色，不是写死的红）。</summary>
    public const string AlertRole = ViewInkRoles.Danger;

    private readonly Dictionary<uint, DisplayGroupRuntime> _groups = [];
    private readonly List<uint> _groupOrder = [];
    private readonly Dictionary<string, string> _lastText = new(StringComparer.Ordinal);
    private readonly InputValidity _validity = new();

    private IModuleContext? _context;
    private IModuleTimer? _uiTimer;
    private DisplaySettings _settings = new();
    private bool _settingsLoaded;
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

        // 端口声明在 Views/WaveformDisplayView.axaml 的 ModuleView.Ports 里。

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

        DeclareValue(context, XStartSource, ValueEditor.Number);
        DeclareValue(context, XEndSource, ValueEditor.Number);
        DeclareValue(context, YMinSource, ValueEditor.Number);
        DeclareValue(context, YMaxSource, ValueEditor.Number);
        DeclareValue(context, YAutoSource, ValueEditor.Boolean);
        DeclareValue(context, FollowSource, ValueEditor.Boolean, persistent: true);
        DeclareValue(context, FollowRangeSource, ValueEditor.Number, persistent: true);
        DeclareValue(context, CursorsSource, ValueEditor.Boolean, persistent: true);
        DeclareValue(context, PausedSource, ValueEditor.Boolean, persistent: true);
        DeclareValue(context, XAxisModeSource, ValueEditor.Enum, persistent: true, choices: [
            new ValueChoice("index", Tr("view.xIndex")),
            new ValueChoice("time", Tr("view.xTime")),
        ]);
        DeclareValue(context, UseCustomRateSource, ValueEditor.Boolean, persistent: true);
        DeclareValue(context, CustomRateSource, ValueEditor.Number, persistent: true);
        DeclareValue(context, XTickSource, ValueEditor.Number, persistent: true);
        DeclareValue(context, YTickSource, ValueEditor.Number, persistent: true);
        DeclareValue(context, KeepInactiveSource, ValueEditor.Boolean, persistent: true);
        DeclareValue(context, HistoryLimitSource, ValueEditor.Number, persistent: true);
        DeclareInput(context, ReadableTickLimitsSource);
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ImageRequestSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareInput(ActionSource);
        context.Declaration.DeclareInput(SavePathSource);
        context.Declaration.DeclareInput(ImageResultSource);

        context.Declaration.DeclareCommand(ClearCommand, Tr(WaveformDisplayText.Clear));
        context.Declaration.DeclareCommand(ResetGroupCommand, Tr(WaveformDisplayText.Reset));
        context.Declaration.DeclareCommand(AutoCommand, Tr(WaveformDisplayText.Auto));
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

    private static void DeclareValue(
        IModuleContext context,
        string name,
        ValueEditor editor,
        bool persistent = false,
        IReadOnlyList<ValueChoice>? choices = null) =>
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = editor,
            Choices = choices,
            Persistent = persistent,
        });

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Pull;

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_settingsLoaded)
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

        if (!_settingsLoaded)
        {
            LoadConfig(context);
        }

        Pump(context);
        PollTextInputs(context);
        DrainFrames(context);
    }

    /// <inheritdoc />
    /// <remarks>“还原”会重置配置与值；下一拍重新读配置并发布，避免界面/模块各留一份旧值。</remarks>
    public void OnConfigurationChanged() => _settingsLoaded = false;

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
        // 显示设置来自值（XAML 初值/持久化值）；组与通道只在数据帧到达时存在。
        _settings = ReadSettings(context);
        NormalizeSettings();

        _settingsLoaded = true;
        _activeGroupId = 0;
        _activeChannelIndex = 0;
        _defaultRangeStart = 0;
        _defaultRangeEnd = _settings.FollowRangeSamples - 1;
        _defaultRangeEdited = false;
        _defaultYEdited = false;
        _uiTimer?.Start();
        PublishMeta(context);
        PublishEmptySnapshot(context);
        PublishStatus(Tr(_settings.Paused ? WaveformDisplayText.Paused : WaveformDisplayText.Ready));
    }

    private static DisplaySettings ReadSettings(IModuleContext context)
    {
        var defaults = new DisplaySettings();

        var axisMode = string.Equals(
            context.Values.ReadText(XAxisModeSource),
            "time",
            StringComparison.OrdinalIgnoreCase)
                ? DisplayXAxisMode.Time
                : DisplayXAxisMode.Index;

        var followRange = ParseInt(context.Values.ReadText(FollowRangeSource), defaults.FollowRangeSamples);
        var customRate = ParseDouble(context.Values.ReadText(CustomRateSource), defaults.CustomRateHz);
        var xTick = ParseDouble(context.Values.ReadText(XTickSource), defaults.XTickStep);
        var yTick = ParseDouble(context.Values.ReadText(YTickSource), defaults.YTickStep);
        var historyLimit = ParseInt(context.Values.ReadText(HistoryLimitSource), defaults.HistoryLimitSamples);

        return new DisplaySettings
        {
            XAxisMode = axisMode,
            Follow = ParseBool(context.Values.ReadText(FollowSource), defaults.Follow),
            FollowRangeSamples = followRange,
            Cursors = ParseBool(context.Values.ReadText(CursorsSource), defaults.Cursors),
            Paused = ParseBool(context.Values.ReadText(PausedSource), defaults.Paused),
            UseCustomRate = ParseBool(context.Values.ReadText(UseCustomRateSource), defaults.UseCustomRate),
            CustomRateHz = customRate,
            XTickStep = xTick,
            YTickStep = yTick,
            KeepInactiveHistory = ParseBool(
                context.Values.ReadText(KeepInactiveSource),
                defaults.KeepInactiveHistory),
            HistoryLimitSamples = Math.Max(0, historyLimit),
        };
    }

    private void NormalizeSettings()
    {
        _settings = _settings with
        {
            FollowRangeSamples = Math.Clamp(
                _settings.FollowRangeSamples <= 0 ? DefaultFollowRangeSamples : _settings.FollowRangeSamples,
                MinimumRangeSamples,
                MaximumFollowRangeSamples),
            XTickStep = double.IsFinite(_settings.XTickStep) && _settings.XTickStep > 0
                ? _settings.XTickStep
                : 0,
            YTickStep = double.IsFinite(_settings.YTickStep) && _settings.YTickStep > 0
                ? _settings.YTickStep
                : 0,
            CustomRateHz = double.IsFinite(_settings.CustomRateHz) && _settings.CustomRateHz > 0
                ? _settings.CustomRateHz
                : 1000,
            HistoryLimitSamples = Math.Max(0, _settings.HistoryLimitSamples),
        };
    }

    private static bool ParseBool(string? text, bool fallback) =>
        bool.TryParse(text, out var value) ? value : fallback;

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : fallback;

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
        CustomRateSource,
    ];

    /// <summary>回读用户逐键写入的文本；内容变化立即进入同一条解析/校验路径。</summary>
    private void PollTextInputs(IModuleContext context)
    {
        // 先取一份本拍的用户编辑快照：处理第一个字段时 `PublishInputs` 会回写其他字段，
        // 若边读边处理，就会把用户同一拍刚输入的另一格覆盖掉。
        var pending = new List<(string Source, string Text)>();
        foreach (var source in PolledTextSources)
        {
            var text = context.Values.ReadText(source);
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
        return DisplayAxisUnits.UsesTime(_settings.XAxisMode, rate)
            ? span / rate
            : span;
    }

    /// <summary>用户刻度比实际可读上限更密时标红；范围缩小或窗口变宽后自动清除。</summary>
    private void UpdateTickAlerts(IModuleContext context)
    {
        var xTick = _settings.XTickStep;
        var yTick = _settings.YTickStep;
        var xSpan = CurrentXSpan();
        var (yMin, yMax) = CurrentYRange();
        var ySpan = Math.Max(0, yMax - yMin);

        var xDense = IsDense(xSpan, xTick, _maxChartXTicks) || IsDense(xSpan, xTick, _maxRangeXTicks);
        var yDense = IsDense(ySpan, yTick, _maxChartYTicks);
        _validity.Set(
            context,
            XTickSource,
            _xTickParseInvalid || xDense,
            _xTickParseInvalid ? Tr(WaveformDisplayText.InvalidNumber) : Tr(WaveformDisplayText.TickTooDense));
        _validity.Set(
            context,
            YTickSource,
            _yTickParseInvalid || yDense,
            _yTickParseInvalid ? Tr(WaveformDisplayText.InvalidNumber) : Tr(WaveformDisplayText.TickTooDense));
    }

    private static bool IsDense(double span, double step, int readableLimit) =>
        readableLimit > 0 && step > 0 && span > 0 && span / step > readableLimit;

    /// <summary>单个输入框的校验红框（同值不重复写；原因走统一有效性记账）。</summary>
    private void SetInputValidity(IModuleContext context, string source, bool invalid) =>
        _validity.Set(context, source, invalid, invalid ? Tr(WaveformDisplayText.InvalidNumber) : null);

    private void ClearInputAlerts(IModuleContext context)
    {
        _validity.ClearAll(context);
        _xTickParseInvalid = false;
        _yTickParseInvalid = false;
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
            case AutoCommand:
                ApplyAuto(context);
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
                    _settings = _settings with { Cursors = cursors };
                    PublishMeta(context);
                    PublishInputs(context);
                }

                break;
            case PausedSource:
                if (bool.TryParse(text.Trim(), out var paused))
                {
                    ApplyPaused(context, paused);
                }

                break;
            case XAxisModeSource:
                ApplyXAxisMode(context, text);
                break;
            case UseCustomRateSource:
                if (bool.TryParse(text.Trim(), out var useCustomRate))
                {
                    _settings = _settings with { UseCustomRate = useCustomRate };
                    EnsureTimeModeRate(context);
                    BuildSnapshot(context);
                    PublishMeta(context);
                    PublishInputs(context);
                }

                break;
            case CustomRateSource:
                if (TryDouble(text, out var customRate) && customRate > 0)
                {
                    _settings = _settings with { CustomRateHz = customRate };
                    EnsureTimeModeRate(context);
                    BuildSnapshot(context);
                    PublishMeta(context);
                    PublishInputs(context);
                    SetInputValidity(context, CustomRateSource, invalid: false);
                }
                else
                {
                    SetInputValidity(context, CustomRateSource, invalid: true);
                }

                break;
            case FollowRangeSource:
                if (TryParseFollowRange(text, out var followRange))
                {
                    _settings = _settings with { FollowRangeSamples = followRange };
                    _defaultRangeStart = 0;
                    _defaultRangeEnd = followRange - 1;
                    if (ActiveGroup is { } followGroup && followGroup.DisplayIndex > 0 && _settings.Follow)
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
                    _settings = _settings with { XTickStep = xTick };
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
                    _settings = _settings with { YTickStep = yTick };
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
                    _settings = _settings with { KeepInactiveHistory = keepInactive };
                    if (!keepInactive)
                    {
                        foreach (var pair in _groups)
                        {
                            if (pair.Key != _activeGroupId)
                            {
                                pair.Value.ClearHistory();
                            }
                        }

                        _historyLimited = false;
                    }

                    PublishMeta(context);
                    PublishInputs(context);
                }

                break;
            case HistoryLimitSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
                    && limit >= 0)
                {
                    _settings = _settings with { HistoryLimitSamples = limit };
                    _historyLimited = false;
                    TrimInactiveHistory(context, incoming: 0);
                    PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
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
        long rangeStart;
        long rangeEnd;
        if (group is null || !group.HasRange || group.DisplayIndex <= 0)
        {
            rangeStart = startInput ? sample : _defaultRangeStart;
            rangeEnd = startInput ? _defaultRangeEnd : sample;
        }
        else
        {
            rangeStart = startInput ? sample : group.RangeStart;
            rangeEnd = startInput ? group.RangeEnd : sample;
        }

        if (rangeEnd <= rangeStart)
        {
            SetInputValidity(context, startInput ? XStartSource : XEndSource, invalid: true);
            return;
        }

        SetInputValidity(context, XStartSource, invalid: false);
        SetInputValidity(context, XEndSource, invalid: false);
        ApplyManualRange(
            context,
            rangeStart,
            rangeEnd,
            startInput ? RangeEditIntent.Width : RangeEditIntent.Move);
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
                    YFitChannel = -1,
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
            YFitChannel = -1,
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
                    YFitChannel = -1,
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
            YFitChannel = -1,
        };

        UpdateGroupConfig(context, group, config);
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private void SetFollow(IModuleContext context, bool follow)
    {
        _settings = _settings with { Follow = follow };

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

    /// <summary>
    /// 暂停只切断接收门：端口继续排空，但帧不解析、不建组、不写历史；恢复时从下一帧继续。
    /// </summary>
    private void ApplyPaused(IModuleContext context, bool paused)
    {
        if (_settings.Paused == paused)
        {
            return;
        }

        _settings = _settings with { Paused = paused };
        if (!paused)
        {
            foreach (var group in _groups.Values)
            {
                group.ResumeFromPause();
            }
        }

        PublishMeta(context);
        PublishInputs(context);
        PublishStatus(Tr(paused
            ? WaveformDisplayText.Paused
            : ActiveGroup is { DisplayIndex: > 0 }
                ? WaveformDisplayText.Receiving
                : WaveformDisplayText.Ready));
    }

    private void ApplyXAxisMode(IModuleContext context, string text)
    {
        var previous = _settings.XAxisMode;
        var mode = string.Equals(text.Trim(), "time", StringComparison.OrdinalIgnoreCase)
            ? DisplayXAxisMode.Time
            : DisplayXAxisMode.Index;

        if (mode == DisplayXAxisMode.Time && EffectiveRate(ActiveGroup) <= 0)
        {
            mode = DisplayXAxisMode.Index;
            PublishStatus(Tr("status.rateUnknown"));
        }

        var rate = EffectiveRate(ActiveGroup);
        var tick = ConvertTickForMode(_settings.XTickStep, previous, mode, rate);
        _settings = _settings with
        {
            XAxisMode = mode,
            XTickStep = tick,
        };
        UpdateTickAlerts(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    /// <summary>X 刻度以当前轴单位保存；换模式时换算一次，0（自动）不换算。</summary>
    private static double ConvertTickForMode(
        double tick,
        DisplayXAxisMode previous,
        DisplayXAxisMode next,
        double rate)
    {
        if (previous == next || !double.IsFinite(tick) || tick <= 0)
        {
            return tick > 0 && double.IsFinite(tick) ? tick : 0;
        }

        if (previous != DisplayXAxisMode.Time && next == DisplayXAxisMode.Time)
        {
            return rate > 0 ? tick / rate : 0;
        }

        if (previous == DisplayXAxisMode.Time && next != DisplayXAxisMode.Time)
        {
            return rate > 0 ? tick * rate : 0;
        }

        return tick;
    }

    /// <summary>速率丢失时 Time 模式没有意义：回到 Index，并把刻度恢复为自动。</summary>
    private void EnsureTimeModeRate(IModuleContext context)
    {
        if (_settings.XAxisMode != DisplayXAxisMode.Time
            || EffectiveRate(ActiveGroup) > 0)
        {
            return;
        }

        _settings = _settings with
        {
            XAxisMode = DisplayXAxisMode.Index,
            XTickStep = 0,
        };
        UpdateTickAlerts(context);
        PublishStatus(Tr("status.rateUnknown"));
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
                        && startElement.TryGetDouble(out var rangeStartAxis)
                        && endElement.TryGetDouble(out var rangeEndAxis)
                        && double.IsFinite(rangeStartAxis)
                        && double.IsFinite(rangeEndAxis))
                    {
                        var startOnly = root.TryGetProperty("startOnly", out var startOnlyElement)
                            && startOnlyElement.ValueKind == JsonValueKind.True;
                        var rate = EffectiveRate(ActiveGroup);
                        ApplyManualRange(
                            context,
                            DisplayAxisUnits.AxisToSamples(rangeStartAxis, _settings.XAxisMode, rate),
                            DisplayAxisUnits.AxisToSamples(rangeEndAxis, _settings.XAxisMode, rate),
                            startOnly ? RangeEditIntent.Width : RangeEditIntent.Move);
                    }

                    break;

                case "dragView":
                    if (root.TryGetProperty("mode", out var modeElement)
                        && modeElement.GetString() is { } mode
                        && (string.Equals(mode, "pan", StringComparison.Ordinal)
                            || string.Equals(mode, "zoom", StringComparison.Ordinal)
                            || string.Equals(mode, "wheel", StringComparison.Ordinal))
                        && root.TryGetProperty("xStart", out var dragXStart)
                        && root.TryGetProperty("xEnd", out var dragXEnd)
                        && root.TryGetProperty("yMin", out var dragYMin)
                        && root.TryGetProperty("yMax", out var dragYMax)
                        && dragXStart.TryGetDouble(out var dragStart)
                        && dragXEnd.TryGetDouble(out var dragEnd)
                        && dragYMin.TryGetDouble(out var yMin)
                        && dragYMax.TryGetDouble(out var yMax))
                    {
                        ApplyDragView(
                            context,
                            zoom: string.Equals(mode, "zoom", StringComparison.Ordinal),
                            keepFollow: string.Equals(mode, "zoom", StringComparison.Ordinal),
                            wheel: string.Equals(mode, "wheel", StringComparison.Ordinal),
                            dragStart,
                            dragEnd,
                            yMin,
                            yMax);
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
                    if (root.TryGetProperty("index", out var indexElement) && ActiveGroup is { } selectedGroup)
                    {
                        var index = indexElement.GetInt32();
                        if (index >= 0
                            && index < selectedGroup.Config.Channels.Count
                            && selectedGroup.IsKnown(index)
                            && !selectedGroup.IsDeleted(index))
                        {
                            _activeChannelIndex = index;
                            PublishMeta(context);
                        }
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

                case "deleteChannel":
                    if (root.TryGetProperty("index", out var deleteIndex)
                        && deleteIndex.ValueKind == JsonValueKind.Number)
                    {
                        DeleteChannel(context, deleteIndex.GetInt32());
                    }

                    break;

                case "fitY":
                    if (root.TryGetProperty("index", out var fitIndex) && ActiveGroup is { } fitGroup)
                    {
                        var index = fitIndex.GetInt32();
                        var already = fitGroup.Config.YAuto && fitGroup.Config.YFitChannel == index;
                        var next = fitGroup.IsDisplayable(index) && !already
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

    private static void UpdateGroupConfig(
        IModuleContext context,
        DisplayGroupRuntime group,
        DisplayGroupConfig config)
    {
        _ = context;
        group.Config = config;
    }

    private void UpdateChannelAt(
        IModuleContext context,
        int index,
        Func<DisplayChannelConfig, DisplayChannelConfig> update)
    {
        var group = ActiveGroup;
        if (group is null || index < 0 || index >= group.Config.Channels.Count || !group.IsKnown(index) || group.IsDeleted(index))
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

    private void DrainFrames(IModuleContext context)
    {
        while (context.Ports.TryReceive(out var buffer, out _))
        {
            try
            {
                // 暂停 = 收下即弃：仍排空端口，但既不校验也不建组/写历史。
                if (!_settings.Paused)
                {
                    HandleFrame(context, buffer.Span);
                }
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
        var groupExisted = _groups.ContainsKey(frame.GroupId);
        var group = GetOrCreateGroup(context, frame.GroupId);
        var newGroup = !groupExisted;
        var firstFrame = group.DisplayIndex == 0;

        // 删除通道是“本次显示移除”，不是持久身份：下一帧的有效位仍报告该槽位时恢复。
        // 未曾在 validMask 中出现过的空洞不建行；先扩槽位，再物化有效通道。
        var channelsChanged = group.PresentChannelCount != frame.ChannelCount;
        if (channelsChanged)
        {
            EnsureGroupChannels(context, group, frame.ChannelCount);
        }

        var knownChanged = MarkKnownChannels(context, group, frame.ChannelCount, frame.ValidMask);
        var revived = ReviveDeletedChannels(context, group, frame.ChannelCount, frame.ValidMask);
        var maskChanged = group.LastValidMask != frame.ValidMask;
        group.SetValidMask(frame.ValidMask);
        channelsChanged |= knownChanged || revived || maskChanged;

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
        // 非活动组达到上限时丢弃最旧样本、继续写入新样本，而不是停止接收。
        var saving = active || _settings.KeepInactiveHistory;
        if (saving)
        {
            if (!active)
            {
                TrimInactiveHistory(context, incoming: 1);
            }

            group.Append(frame.Samples, frame.ValidMask, now);
        }

        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());

        if (firstFrame && saving)
        {
            ApplyNoDataDefaults(context, group);
        }

        if (!active)
        {
            if (channelsChanged || newGroup || firstFrame)
            {
                PublishMeta(context);
            }

            return;
        }

        PublishValue(context, LatestIndexSource, group.DisplayIndex > 0 ? (ulong)(group.DisplayIndex - 1) : 0UL);
        PublishValue(context, RateSource, (float)EffectiveRate(group));

        if (!group.HasRange)
        {
            SetDefaultRange(group);
            BuildSnapshot(context);
        }
        else if (_settings.Follow)
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

        if (firstFrame || channelsChanged || newGroup)
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

        if (_defaultRangeEdited && !_settings.Follow && latest > 0)
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
        if (!_settings.KeepInactiveHistory)
        {
            return 0;
        }

        long total = 0;
        foreach (var (id, group) in _groups)
        {
            if (id != _activeGroupId)
            {
                total += group.RetainedFrames;
            }
        }

        return total;
    }

    /// <summary>
    /// **按上限裁剪非活动组历史**：先丢最旧，再写入新样本；活动组永不裁剪。
    /// </summary>
    /// <param name="context">模块上下文（用于发布已用量与状态）。</param>
    /// <param name="incoming">即将追加的帧数（调用方已确认要保存）。</param>
    private void TrimInactiveHistory(IModuleContext context, int incoming)
    {
        var limit = _settings.HistoryLimitSamples;
        if (!_settings.KeepInactiveHistory || limit <= 0)
        {
            _historyLimited = false;
            return;
        }

        var total = InactiveSampleCount();
        var overflow = total + incoming - limit;
        var dropped = 0L;

        while (overflow > 0)
        {
            DisplayGroupRuntime? victim = null;
            var oldest = long.MaxValue;

            foreach (var id in _groupOrder)
            {
                if (id == _activeGroupId
                    || !_groups.TryGetValue(id, out var candidate)
                    || candidate.RetainedFrames == 0)
                {
                    continue;
                }

                var candidateOldest = candidate.DisplayIndex - candidate.RetainedFrames;
                if (candidateOldest < oldest)
                {
                    oldest = candidateOldest;
                    victim = candidate;
                }
            }

            if (victim is null)
            {
                break;
            }

            var take = Math.Min(victim.RetainedFrames, overflow);
            victim.DropOldestFrames(take);
            dropped += take;
            total -= take;
            overflow -= take;
        }

        if (dropped > 0)
        {
            PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());

            if (!_historyLimited)
            {
                _historyLimited = true;
                PublishStatus(Tr("status.historyLimit"));
            }
        }
        else if (total < limit)
        {
            _historyLimited = false;
        }
    }

    private DisplayGroupRuntime GetOrCreateGroup(IModuleContext context, uint groupId)
    {
        if (_groups.TryGetValue(groupId, out var existing))
        {
            return existing;
        }

        // 组只由数据帧发现；这里不查/不写任何持久化配置。
        var config = new DisplayGroupConfig { GroupId = groupId };
        _structureRevision++;

        var runtime = new DisplayGroupRuntime
        {
            GroupId = groupId,
            Config = config,
        };

        _groups[groupId] = runtime;
        _groupOrder.Add(groupId);
        return runtime;
    }

    /// <summary>
    /// 数据帧的 validMask 置位时恢复已删除通道。删除只清本帧显示，不改变槽位映射。
    /// </summary>
    private bool ReviveDeletedChannels(
        IModuleContext context,
        DisplayGroupRuntime group,
        int presentCount,
        ulong validMask)
    {
        var limit = Math.Min(presentCount, group.Config.Channels.Count);
        if (limit <= 0)
        {
            return false;
        }

        var channels = group.Config.Channels.ToList();
        var revived = false;
        for (var index = 0; index < limit; index++)
        {
            if (!channels[index].Known
                || !channels[index].Deleted
                || (validMask & (1UL << index)) == 0)
            {
                continue;
            }

            channels[index] = channels[index] with { Deleted = false };
            revived = true;
        }

        if (revived)
        {
            UpdateGroupConfig(context, group, group.Config with { Channels = channels });
            _structureRevision++;
        }

        return revived;
    }

    /// <summary>把当前帧 validMask 置位的槽位标记为“曾出现”；空洞不建行。</summary>
    private bool MarkKnownChannels(
        IModuleContext context,
        DisplayGroupRuntime group,
        int presentCount,
        ulong validMask)
    {
        var limit = Math.Min(presentCount, group.Config.Channels.Count);
        if (limit <= 0)
        {
            return false;
        }

        var channels = group.Config.Channels.ToList();
        var changed = false;
        for (var index = 0; index < limit; index++)
        {
            if ((validMask & (1UL << index)) == 0 || channels[index].Known)
            {
                continue;
            }

            channels[index] = channels[index] with { Known = true };
            changed = true;
        }

        if (changed)
        {
            UpdateGroupConfig(context, group, group.Config with { Channels = channels });
            _structureRevision++;
        }

        return changed;
    }

    private void EnsureGroupChannels(IModuleContext context, DisplayGroupRuntime group, int count)
    {
        var channels = group.Config.Channels.ToList();
        var added = false;
        while (channels.Count < count)
        {
            channels.Add(new DisplayChannelConfig());
            added = true;
        }

        if (added)
        {
            UpdateGroupConfig(context, group, group.Config with { Channels = channels });
            _structureRevision++;
        }

        // 历史按槽位保留；缺失/恢复只改变 PresentChannelCount，不重建数据。
        group.EnsureChannels(channels.Count);
        group.SetPresentChannels(count);
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

        if (!_settings.KeepInactiveHistory && _groups.TryGetValue(_activeGroupId, out var previous))
        {
            previous.ClearHistory();
        }

        _activeGroupId = groupId;

        // 旧活动组刚变为非活动组：立即按上限保留最新一段，避免切组后历史继续膨胀。
        TrimInactiveHistory(context, incoming: 0);

        var targetConfig = _groups.TryGetValue(groupId, out var targetGroup)
            ? targetGroup.Config
            : null;
        _activeChannelIndex = targetConfig is null ? 0 : FirstChannelIndex(targetConfig);
        _structureRevision++;

        if (_groups.TryGetValue(groupId, out var group))
        {
            if (_settings.Follow && group.DisplayIndex > 0)
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

        _historyLimited = false;
        ResetActiveView(context, active, clearHistory: true);
        PublishValue(context, LatestIndexSource, 0UL);
        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());
        PublishStatus(Tr(WaveformDisplayText.Cleared));
    }

    /// <summary>
    /// **Auto**：按当前数据量设置跟踪窗口（数据少用最小窗口，多则按比例），开跟踪、开 Y 自适应。
    /// </summary>
    /// <remarks>一次性动作：只设置一次当前值，之后仍按用户的范围操作规则走（不会被反复拉回）。</remarks>
    private void ApplyAuto(IModuleContext context)
    {
        var group = ActiveGroup;
        var hasData = group is { DisplayIndex: > 0 };
        var count = hasData ? group!.DisplayIndex : 0;

        var window = hasData
            ? Math.Clamp(
                Math.Max(DefaultFollowRangeSamples, (int)Math.Round(count * AutoWindowRatio)),
                MinimumRangeSamples,
                MaximumFollowRangeSamples)
            : DefaultFollowRangeSamples;

        _settings = _settings with
        {
            Follow = true,
            FollowRangeSamples = window,
        };

        if (group is { DisplayIndex: > 0 })
        {
            // 显式 Auto 允许重新缩小到当前数据：先清自适应锁存，再按全通道自适应。
            group.ResetAutoY();
            UpdateGroupConfig(context, group, group.Config with
            {
                YAuto = true,
                YFitChannel = -1,
            });
            UpdateFollowRange(group);
            BuildSnapshot(context);
        }
        else
        {
            _defaultYAuto = true;
            _defaultYMin = -1;
            _defaultYMax = 1;
            _defaultRangeStart = 0;
            _defaultRangeEnd = window - 1;
            _defaultRangeEdited = true;
            PublishEmptySnapshot(context);
        }

        PublishMeta(context);
        PublishInputs(context);
    }

    private void ResetActiveGroup(IModuleContext context)
    {
        var active = ActiveGroup;
        if (active is null)
        {
            return;
        }

        ResetActiveView(context, active, clearHistory: false);
    }

    /// <summary>
    /// 轻量显示复位：恢复视口/显示设置与 Y 自动，但保留组名、通道名、颜色与显隐。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="active">活动组。</param>
    /// <param name="clearHistory">是否同时清历史（“清空”与“显示复位”的唯一差别）。</param>
    private void ResetActiveView(
        IModuleContext context,
        DisplayGroupRuntime active,
        bool clearHistory)
    {
        if (clearHistory)
        {
            active.ClearHistory();
        }
        else
        {
            active.ResetRange();
            active.ResetAutoY();
        }

        // 显示设置回到默认；组/通道身份字段（名称、颜色、显隐）不在这里重置。
        // 暂停是接收门，不是显示参数：清空/显示复位不擅自恢复数据流。
        _settings = new DisplaySettings { Paused = _settings.Paused };
        _defaultRangeStart = 0;
        _defaultRangeEnd = _settings.FollowRangeSamples - 1;
        _defaultYAuto = true;
        _defaultYMin = -1;
        _defaultYMax = 1;
        _defaultRangeEdited = false;
        _defaultYEdited = false;
        ClearInputAlerts(context);

        UpdateGroupConfig(context, active, active.Config with
        {
            YAuto = true,
            YMin = -1,
            YMax = 1,
            YFitChannel = -1,
        });

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

    /// <summary>手动删除通道：清历史、打删除标记；删空后整组移除。</summary>
    private void DeleteChannel(IModuleContext context, int index)
    {
        var group = ActiveGroup;
        if (group is null
            || index < 0
            || index >= group.Config.Channels.Count
            || !group.IsKnown(index)
            || group.IsDeleted(index))
        {
            return;
        }

        group.DeleteChannel(index);
        var channels = group.Config.Channels.ToList();
        channels[index] = channels[index] with { Deleted = true };
        if (channels.All(channel => !channel.Known || channel.Deleted))
        {
            RemoveGroup(context, group);
            return;
        }

        var config = group.Config with { Channels = channels };
        if (config.YFitChannel == index)
        {
            config = config with { YFitChannel = -1 };
        }

        UpdateGroupConfig(context, group, config);
        if (_activeChannelIndex == index
            || _activeChannelIndex < 0
            || _activeChannelIndex >= channels.Count
            || channels[_activeChannelIndex].Deleted)
        {
            _activeChannelIndex = NextChannelIndex(config, index);
        }

        _structureRevision++;
        BuildSnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    /// <summary>删除整组运行期与配置；活动组删除后选下一个剩余组。</summary>
    private void RemoveGroup(IModuleContext context, DisplayGroupRuntime group)
    {
        _groups.Remove(group.GroupId);
        _groupOrder.Remove(group.GroupId);

        if (_pendingGroup?.GroupId == group.GroupId)
        {
            _pendingGroup = null;
            _pendingSave = PendingSave.None;
        }

        var wasActive = _activeGroupId == group.GroupId;
        if (wasActive)
        {
            _activeGroupId = 0;
            _activeChannelIndex = 0;
        }
        _structureRevision++;
        PublishValue(context, HistoryUsedSource, (ulong)InactiveSampleCount());

        if (!wasActive)
        {
            PublishMeta(context);
            return;
        }

        var next = _groupOrder.FirstOrDefault();
        if (next != 0 && _groups.ContainsKey(next))
        {
            ActivateGroup(context, next);
            return;
        }

        PublishEmptySnapshot(context);
        PublishMeta(context);
        PublishInputs(context);
    }

    private static int FirstChannelIndex(DisplayGroupConfig group)
    {
        for (var index = 0; index < group.Channels.Count; index++)
        {
            if (group.Channels[index].Known && !group.Channels[index].Deleted)
            {
                return index;
            }
        }

        return -1;
    }

    private static int NextChannelIndex(DisplayGroupConfig group, int deletedIndex)
    {
        for (var index = deletedIndex + 1; index < group.Channels.Count; index++)
        {
            if (group.Channels[index].Known && !group.Channels[index].Deleted)
            {
                return index;
            }
        }

        for (var index = deletedIndex - 1; index >= 0; index--)
        {
            if (group.Channels[index].Known && !group.Channels[index].Deleted)
            {
                return index;
            }
        }

        return -1;
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
            _settings.FollowRangeSamples,
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
            _settings.FollowRangeSamples,
            MinimumRangeSamples,
            MaximumFollowRangeSamples);
        var start = Math.Max(0, latest - width + 1);
        group.SetRange(start, latest);
    }

    /// <summary>一次用户范围操作：只改宽度，或移动范围。</summary>
    private enum RangeEditIntent
    {
        /// <summary>改宽度：保留当前跟踪状态。</summary>
        Width,

        /// <summary>移动数据范围：关闭跟踪。</summary>
        Move,
    }

    private void ApplyManualRange(
        IModuleContext context,
        long start,
        long end,
        RangeEditIntent intent)
    {
        var settings = _settings;
        var group = ActiveGroup;
        long currentStart;
        long currentEnd;
        long rangeStart;
        long rangeEnd;

        if (group is { DisplayIndex: > 0 })
        {
            currentStart = group.RangeStart;
            currentEnd = group.RangeEnd;
            var latest = group.DisplayIndex - 1;
            var clamped = ClampRange(start, end, latest);
            rangeStart = clamped.Start;
            rangeEnd = clamped.End;
            group.SetRange(rangeStart, rangeEnd);
        }
        else
        {
            currentStart = _defaultRangeStart;
            currentEnd = _defaultRangeEnd;
            rangeStart = Math.Max(0, Math.Min(start, end));
            rangeEnd = Math.Max(rangeStart + 1, Math.Max(start, end));
            _defaultRangeStart = rangeStart;
            _defaultRangeEnd = rangeEnd;
            _defaultRangeEdited = true;
        }

        var rangeChanged = rangeStart != currentStart || rangeEnd != currentEnd;
        var width = (int)Math.Clamp(
            rangeEnd - rangeStart + 1,
            (long)MinimumRangeSamples,
            (long)MaximumFollowRangeSamples);
        _settings = _settings with
        {
            // 只有实际移动范围才关闭跟踪；没有变化的点击/抖动不算用户操作。
            Follow = intent == RangeEditIntent.Width || !rangeChanged
                ? settings.Follow
                : false,
            FollowRangeSamples = width,
        };

        if (group is { DisplayIndex: > 0 })
        {
            BuildSnapshot(context);
        }
        else
        {
            PublishEmptySnapshot(context);
        }

        PublishMeta(context);
    }

    /// <summary>
    /// 应用曲线区拖动产生的目标视口：X 为轴单位，Y 为数据值。
    /// </summary>
    /// <remarks>
    /// 视图只提交轴单位；这里是曲线拖动 X 的**唯一**换算点，不得在视图或下游再乘一次速率。
    /// 平移关闭实际改变的轴（X ⇒ Follow，Y ⇒ YAuto）；右键缩放只改实际变化的轴。
    /// 滚轮是手动视口操作：X 改变时也关闭 Follow，保证指针位置不漂移。
    /// 跟踪开启时横向缩放保持跟踪并更新跟踪宽度，其余情况按目标范围精确应用。
    /// </remarks>
    private void ApplyDragView(
        IModuleContext context,
        bool zoom,
        bool keepFollow,
        bool wheel,
        double xStartAxis,
        double xEndAxis,
        double yMin,
        double yMax)
    {
        var group = ActiveGroup;
        var hasData = group is { DisplayIndex: > 0 };

        if (!double.IsFinite(xStartAxis)
            || !double.IsFinite(xEndAxis)
            || !double.IsFinite(yMin)
            || !double.IsFinite(yMax)
            || yMax <= yMin)
        {
            return;
        }

        var settings = _settings;
        var rate = EffectiveRate(group);
        var targetStart = DisplayAxisUnits.AxisToSamples(xStartAxis, settings.XAxisMode, rate);
        var targetEnd = DisplayAxisUnits.AxisToSamples(xEndAxis, settings.XAxisMode, rate);

        bool xChanged;
        bool yChanged;
        long targetXStart;
        long targetXEnd;

        if (hasData)
        {
            var latest = group!.DisplayIndex - 1;

            // 滚轮是"整次缩放"：任意一轴触界就不应只缩放另一轴。
            if (wheel
                && (targetStart < 0
                    || targetEnd > latest
                    || targetEnd - targetStart + 1 < MinimumRangeSamples))
            {
                return;
            }

            var target = ClampDragRangePreservingWidth(targetStart, targetEnd, latest);
            targetXStart = target.Start;
            targetXEnd = target.End;
            xChanged = targetXStart != group.RangeStart || targetXEnd != group.RangeEnd;
            var (currentYMin, currentYMax) = EffectiveY(group);
            yChanged = Math.Abs(yMin - currentYMin) > 1e-9
                || Math.Abs(yMax - currentYMax) > 1e-9;
        }
        else
        {
            // 无数据时没有样本可夹紧；拖动/缩放改写会话默认视口，下一次有数据时由
            // ApplyNoDataDefaults 应用。没有这段，空图表上的曲线区手势会被直接丢掉。
            targetXStart = Math.Max(0, Math.Min(targetStart, targetEnd));
            targetXEnd = Math.Max(targetXStart + 1, Math.Max(targetStart, targetEnd));
            xChanged = targetXStart != _defaultRangeStart || targetXEnd != _defaultRangeEnd;
            yChanged = Math.Abs(yMin - _defaultYMin) > 1e-9
                || Math.Abs(yMax - _defaultYMax) > 1e-9;
        }

        if (!xChanged && !yChanged)
        {
            return;
        }

        if (xChanged)
        {
            var width = (int)Math.Clamp(
                targetXEnd - targetXStart + 1,
                (long)MinimumRangeSamples,
                (long)MaximumFollowRangeSamples);

            if (hasData)
            {
                group!.SetRange(targetXStart, targetXEnd);
            }
            else
            {
                _defaultRangeStart = targetXStart;
                _defaultRangeEnd = targetXEnd;
                _defaultRangeEdited = true;
            }

            settings = settings with
            {
                Follow = keepFollow ? settings.Follow : false,
                FollowRangeSamples = width,
            };
        }

        if (yChanged)
        {
            if (hasData)
            {
                group!.Config = group.Config with
                {
                    YAuto = false,
                    YMin = yMin,
                    YMax = yMax,
                    YFitChannel = -1,
                };
            }
            else
            {
                _defaultYAuto = false;
                _defaultYMin = yMin;
                _defaultYMax = yMax;
                _defaultYEdited = true;
            }
        }

        _settings = settings;

        // 横向缩放保持跟踪时，显示窗口仍贴住最新样本；宽度用本次缩放后的跨度。
        if (xChanged && zoom && settings.Follow && hasData)
        {
            UpdateFollowRange(group!);
        }

        if (hasData)
        {
            BuildSnapshot(context);
        }
        else
        {
            PublishEmptySnapshot(context);
        }

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

    /// <summary>
    /// **拖动视口用的夹紧**：先保住目标宽度，再整体平移进数据范围。
    /// </summary>
    /// <remarks>
    /// 平移到边界时只应该"停住"，不能因为 start/end 分别夹紧而把宽度压小（那会看起来像缩放）。
    /// </remarks>
    private static (long Start, long End) ClampDragRangePreservingWidth(long start, long end, long latest)
    {
        if (latest <= 0)
        {
            return (0, 0);
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        var width = Math.Clamp(end - start + 1, MinimumRangeSamples, latest + 1);
        start = Math.Clamp(start, 0, latest + 1 - width);
        return (start, start + width - 1);
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
        var useFit = fitIndex >= 0
            && fitIndex < group.Config.Channels.Count
            && group.IsDisplayable(fitIndex);

        for (var index = 0; index < group.Config.Channels.Count; index++)
        {
            if (!group.IsDisplayable(index))
            {
                continue;
            }

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

        for (var index = 0; index < group.Config.Channels.Count; index++)
        {
            var config = index < group.Config.Channels.Count ? group.Config.Channels[index] : null;
            if (config is null
                || !group.IsDisplayable(index)
                || config.Visible == false
                || index >= group.Histories.Length)
            {
                continue;
            }

            var values = new float[width];
            var history = group.Histories[index];
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            for (var offset = 0; offset < width; offset++)
            {
                var value = history[start + offset];
                values[offset] = value;
                if (float.IsFinite(value))
                {
                    min = MathF.Min(min, value);
                    max = MathF.Max(max, value);
                }
            }

            var hasRange = min <= max;
            channels.Add(new SeriesWindowChannel(
                index,
                string.IsNullOrWhiteSpace(config?.Name) ? Tr(WaveformDisplayText.ChannelFallback, index + 1) : config!.Name,
                config?.ColorArgb,
                values)
            {
                WindowMin = hasRange ? min : float.NaN,
                WindowMax = hasRange ? max : float.NaN,
            });
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

    /// <summary>有效时间基：模块自定义频率优先，否则用当前活动组帧头声明速率。</summary>
    private double EffectiveRate(DisplayGroupRuntime? group) =>
        _settings.UseCustomRate && _settings.CustomRateHz > 0
            ? _settings.CustomRateHz
            : group?.DeclaredRateHz ?? 0;

    private void PublishMeta(IModuleContext context)
    {
        var active = ActiveGroup;
        var groups = new List<object>(_groupOrder.Count);

        foreach (var id in _groupOrder)
        {
            if (!_groups.TryGetValue(id, out var group))
            {
                continue;
            }

            groups.Add(new
            {
                id,
                name = GroupDisplayName(group),
                channels = group.Config.Channels.Count(channel => channel.Known && !channel.Deleted),
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
            : active.Config.Channels
                .Select((channel, index) => (channel, index))
                .Where(item => item.channel.Known && !item.channel.Deleted)
                .Select(item => new
                {
                    index = item.index,
                    name = ChannelDisplayName(item.channel, item.index),
                    color = item.channel.ColorArgb,
                    visible = item.channel.Visible,
                    missing = !active.IsPresent(item.index) || !active.IsValid(item.index),
                })
                .ToArray();

        var settings = _settings;
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
                useCustomRate = settings.UseCustomRate,
                customRate = settings.CustomRateHz,
                xTick = settings.XTickStep,
                yTick = settings.YTickStep,
                follow = settings.Follow,
                followRange = settings.FollowRangeSamples,
                cursors = settings.Cursors,
                paused = settings.Paused,
                keepInactive = _settings.KeepInactiveHistory,
                historyLimit = _settings.HistoryLimitSamples,
            },
            badFrames = _badFrames,
            historyLimited = _historyLimited,
        };

        context.Display.SetText(MetaSource, JsonSerializer.Serialize(snapshot));
    }

    private void PublishInputs(IModuleContext context)
    {
        var group = ActiveGroup;
        var settings = _settings;
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
        SetInput(context, PausedSource, settings.Paused ? "true" : "false");
        SetInput(context, XAxisModeSource, settings.XAxisMode == DisplayXAxisMode.Time ? "time" : "index");
        SetInput(context, UseCustomRateSource, settings.UseCustomRate ? "true" : "false");
        SetInput(context, CustomRateSource, Format(settings.CustomRateHz));
        SetInput(context, XTickSource, Format(settings.XTickStep));
        SetInput(context, YTickSource, Format(settings.YTickStep));
        SetInput(context, KeepInactiveSource, _settings.KeepInactiveHistory ? "true" : "false");
        SetInput(context, HistoryLimitSource, _settings.HistoryLimitSamples.ToString(CultureInfo.InvariantCulture));
        SetInput(context, FollowRangeSource, DisplayAxisUnits.FormatFollowRange(
            settings.FollowRangeSamples,
            settings.XAxisMode,
            rate));
    }

    /// <summary>回写模块认可的输入值；值相同不写，避免再次触发 Input step 形成回声。</summary>
    private void SetInput(IModuleContext context, string source, string text)
    {
        _lastText[source] = text;
        if (string.Equals(context.Values.ReadText(source), text, StringComparison.Ordinal))
        {
            return;
        }

        context.Values.AcceptText(source, text);
    }

    private static string FormatX(long sample, DisplayXAxisMode mode, double rate) =>
        DisplayAxisUnits.FormatSamples(sample, mode, rate);

    private bool TryParseFollowRange(string text, out int samples)
    {
        samples = 0;
        var rate = EffectiveRate(ActiveGroup);
        if (DisplayAxisUnits.UsesTime(_settings.XAxisMode, rate))
        {
            if (!TryDouble(text, out var seconds) || seconds <= 0)
            {
                return false;
            }

            samples = (int)Math.Min(
                int.MaxValue,
                DisplayAxisUnits.AxisToSamples(seconds, _settings.XAxisMode, rate));
        }
        else if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out samples))
        {
            return false;
        }

        return samples is >= MinimumRangeSamples and <= MaximumFollowRangeSamples;
    }

    private bool TryParseX(IModuleContext context, DisplayGroupRuntime? group, string text, out long sample)
    {
        _ = context;
        sample = 0;
        var rate = EffectiveRate(group);
        if (DisplayAxisUnits.UsesTime(_settings.XAxisMode, rate))
        {
            return TryDouble(text, out var seconds)
                && (sample = DisplayAxisUnits.AxisToSamples(seconds, _settings.XAxisMode, rate)) >= 0;
        }

        return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sample)
            || TryDouble(text, out var value)
            && (sample = DisplayAxisUnits.AxisToSamples(value, _settings.XAxisMode, rate)) >= 0;
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
                var channelIndices = new List<int>();
                var names = new List<string>();
                for (var index = 0; index < group.Config.Channels.Count; index++)
                {
                    var channel = group.Config.Channels[index];
                    if (!channel.Known || channel.Deleted)
                    {
                        continue;
                    }

                    channelIndices.Add(index);
                    names.Add(ChannelDisplayName(channel, index));
                }

                if (channelIndices.Count == 0)
                {
                    PublishStatus(Tr("status.saveNoData"));
                    return;
                }

                _exportPath = path;
                _export = new CsvExport(
                    path,
                    group,
                    channelIndices.ToArray(),
                    names.ToArray(),
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
