using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.AttitudeDisplay;

/// <summary>
/// **三轴姿态仪**：把某一组的三个通道当作 roll / pitch / yaw，显示物体姿态。
/// </summary>
/// <remarks>
/// 与单通道显示模块同一条口径：目标角度由 <see cref="ChannelFrameFeed.Resolve"/> 一条规则给出，
/// 失联后保持还是回到单位姿态由设置决定；控件只画收到的三个角度。
/// 组号与三个通道号是**用户输入的持久化值**（组 0 = 自动跟随第一个出现的组）。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "attitude-display",
    DisplayNameKey = AttitudeDisplayText.Title,
    DescriptionKey = AttitudeDisplayText.Description,
    Tags = ["terminal", "waveform", "display", "attitude"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.AttitudeDisplayViewFactory))]
public sealed class AttitudeDisplayModule : IModule, IActivitySource, IDisposable
{
    /// <summary>输入端口。</summary>
    public const string InPort = "in";

    /// <summary>波形帧类型标识（供视图 XAML 的 <c>{x:Static}</c> 引用）。</summary>
    public const string WaveformTypeId = WaveformFrameContract.TypeId;

    /// <summary>角色 Id。</summary>
    public const string RoleId = "display";

    /// <summary>横滚角显示源（F32，度）。</summary>
    public const string RollSource = "roll";

    /// <summary>俯仰角显示源（F32，度）。</summary>
    public const string PitchSource = "pitch";

    /// <summary>偏航角显示源（F32，度）。</summary>
    public const string YawSource = "yaw";

    /// <summary>设置摘要 JSON 源。</summary>
    public const string MetaSource = "meta";

    /// <summary>状态文本源（界面作为悬停提示）。</summary>
    public const string StatusSource = "status";

    /// <summary>组号值（0 = 自动跟随第一个出现的组）。</summary>
    public const string GroupSource = "input.group";

    /// <summary>横滚通道号值（0 起）。</summary>
    public const string RollChannelSource = "input.roll";

    /// <summary>俯仰通道号值（0 起）。</summary>
    public const string PitchChannelSource = "input.pitch";

    /// <summary>偏航通道号值（0 起）。</summary>
    public const string YawChannelSource = "input.yaw";

    /// <summary>渲染物值。</summary>
    public const string StyleSource = "settings.style";

    /// <summary>输入角度单位值。</summary>
    public const string UnitSource = "settings.unit";

    /// <summary>平滑开关值。</summary>
    public const string SmoothingSource = "settings.smoothing";

    /// <summary>平滑时长值（毫秒）。</summary>
    public const string SmoothingMsSource = "settings.smoothingMs";

    /// <summary>缩放值。</summary>
    public const string ZoomSource = "settings.zoom";

    /// <summary>地面网格开关值。</summary>
    public const string GridSource = "settings.grid";

    /// <summary>轴标签开关值。</summary>
    public const string LabelsSource = "settings.labels";

    /// <summary>反转横滚值。</summary>
    public const string InvertRollSource = "settings.invertRoll";

    /// <summary>反转俯仰值。</summary>
    public const string InvertPitchSource = "settings.invertPitch";

    /// <summary>反转偏航值。</summary>
    public const string InvertYawSource = "settings.invertYaw";

    /// <summary>失联归零开关值。</summary>
    public const string LostZeroSource = "settings.lostZero";

    /// <summary>失联归零延时值（毫秒）。</summary>
    public const string LostDelaySource = "settings.lostDelay";

    /// <summary>恢复默认设置。</summary>
    public const string ResetSettingsCommand = "resetSettings";

    /// <summary>本模块图标表的属主程序集（浮层里的图标用显式属主）。</summary>
    public static System.Reflection.Assembly IconOwner => typeof(AttitudeDisplayModule).Assembly;

    /// <summary>轴数量（roll / pitch / yaw）。</summary>
    private const int AxisCount = 3;

    /// <summary>回零/状态判定的定时器周期（毫秒）。</summary>
    private const int TimerIntervalMs = 50;

    /// <summary>值被清空（还原/首次激活）的哨兵：与"用户输入空串"区分开。</summary>
    private const string ClearedMarker = "\0";

    private readonly ChannelFrameFeed _feed = new();
    private readonly Dictionary<string, string> _lastText = new(StringComparer.Ordinal);
    private readonly InputValidity _validity = new();
    private readonly float[] _lastPublished = new float[AxisCount];
    private readonly bool[] _hasPublished = new bool[AxisCount];

    private IModuleContext? _context;
    private IModuleTimer? _timer;
    private AttitudeSettings _settings = AttitudeSettings.Default;
    private bool _settingsLoaded;
    private long _metaRevision;
    private bool _metaDirty = true;
    private string _lastStatus = string.Empty;

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(AttitudeDisplayModule).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(AttitudeDisplayModule).Assembly, key, args);

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

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleId,
            WaveformFrameContract.TypeId,
            InPort,
            Tr(AttitudeDisplayText.RoleDisplay)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            RollSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            PitchSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            YawSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(MetaSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));

        DeclareValue(context, GroupSource, ValueEditor.Number);
        DeclareValue(context, RollChannelSource, ValueEditor.Number);
        DeclareValue(context, PitchChannelSource, ValueEditor.Number);
        DeclareValue(context, YawChannelSource, ValueEditor.Number);
        DeclareSettings(context);
        context.Declaration.DeclareCommand(ResetSettingsCommand, Tr(AttitudeDisplayText.ResetSettings));

        _context = context;
        _timer = context.Timers.Create();
        _timer.Interval = TimeSpan.FromMilliseconds(TimerIntervalMs);
        _timer.Tick += OnTimerTick;
    }

    private static void DeclareSettings(IModuleContext context)
    {
        DeclareValue(context, StyleSource, ValueEditor.Enum, [
            new ValueChoice("axes", Tr(AttitudeDisplayText.StyleAxes)),
            new ValueChoice("cube", Tr(AttitudeDisplayText.StyleCube)),
            new ValueChoice("drone", Tr(AttitudeDisplayText.StyleDrone)),
        ]);
        DeclareValue(context, UnitSource, ValueEditor.Enum, [
            new ValueChoice("deg", Tr(AttitudeDisplayText.UnitDegree)),
            new ValueChoice("rad", Tr(AttitudeDisplayText.UnitRadian)),
        ]);
        DeclareValue(context, SmoothingSource, ValueEditor.Boolean);
        DeclareValue(context, SmoothingMsSource, ValueEditor.Number);
        DeclareValue(context, ZoomSource, ValueEditor.Number);
        DeclareValue(context, GridSource, ValueEditor.Boolean);
        DeclareValue(context, LabelsSource, ValueEditor.Boolean);
        DeclareValue(context, InvertRollSource, ValueEditor.Boolean);
        DeclareValue(context, InvertPitchSource, ValueEditor.Boolean);
        DeclareValue(context, InvertYawSource, ValueEditor.Boolean);
        DeclareValue(context, LostZeroSource, ValueEditor.Boolean);
        DeclareValue(context, LostDelaySource, ValueEditor.Number);
    }

    private static void DeclareValue(
        IModuleContext context,
        string name,
        ValueEditor editor,
        IReadOnlyList<ValueChoice>? choices = null) =>
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = editor,
            Choices = choices,
            Persistent = true,
            ResetWithModule = true,
        });

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Pull;

    /// <inheritdoc />
    public void OnActivated() => _timer?.Start();

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_settingsLoaded)
        {
            LoadSettings();
        }

        Pump(context);

        if (reason.HasFlag(StepReason.Data))
        {
            DrainFrames(context);
        }

        ApplySettings(context);
        Publish(context, Environment.TickCount64);
    }

    /// <inheritdoc />
    public void OnRemoved() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
            _timer = null;
        }
    }

    /// <summary>测试/单步入口：处理收件箱、端口数据与设置，然后发布一次。</summary>
    /// <param name="context">模块上下文。</param>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_settingsLoaded)
        {
            LoadSettings();
        }

        Pump(context);
        DrainFrames(context);
        ApplySettings(context);
        Publish(context, Environment.TickCount64);
    }

    private void LoadSettings()
    {
        _settings = AttitudeSettings.Default;
        _lastText.Clear();
        _settingsLoaded = true;
    }

    private void Pump(IModuleContext context)
    {
        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, ResetSettingsCommand, StringComparison.Ordinal))
            {
                ResetSettings(context);
            }
        }

        // 离散值（布尔/枚举）提交时会投递输入事件：本模块只读值，排空即可。
        while (context.Inbox.TryDequeueInput(out _, out _))
        {
        }
    }

    private void DrainFrames(IModuleContext context)
    {
        while (context.Ports.TryReceive(out var buffer, out _))
        {
            try
            {
                var result = _feed.ApplyFrame(buffer.Span, Environment.TickCount64);
                if ((result & FrameApplyResult.InvalidFrame) != 0)
                {
                    PublishStatus(context, Tr(AttitudeDisplayText.InvalidFrame));
                    continue;
                }

                if ((result & FrameApplyResult.StructureChanged) != 0)
                {
                    _metaDirty = true;
                }
            }
            finally
            {
                context.Ports.ReleaseBuffer(buffer);
            }
        }
    }

    private void ApplySettings(IModuleContext context)
    {
        var defaults = AttitudeSettings.Default;
        var candidate = _settings;

        if (TryReadChanged(context, GroupSource, out var groupText, out var groupCleared))
        {
            if (groupCleared)
            {
                candidate = candidate with { GroupId = defaults.GroupId };
                _validity.Set(context, GroupSource, false);
            }
            else if (TryParseInteger(groupText, out var group) && group >= 1)
            {
                candidate = candidate with { GroupId = group };
                _validity.Set(context, GroupSource, false);
            }
            else
            {
                _validity.Set(context, GroupSource, true, Tr(AttitudeDisplayText.InvalidInteger));
            }
        }

        if (TryReadChanged(context, RollChannelSource, out var rollText, out var rollCleared))
        {
            if (rollCleared)
            {
                candidate = candidate with { RollChannel = defaults.RollChannel };
                _validity.Set(context, RollChannelSource, false);
            }
            else if (TryParseInteger(rollText, out var roll)
                     && roll is >= 1 and <= AttitudeSettings.MaximumChannel)
            {
                candidate = candidate with { RollChannel = roll };
                _validity.Set(context, RollChannelSource, false);
            }
            else
            {
                _validity.Set(context, RollChannelSource, true, Tr(AttitudeDisplayText.InvalidInteger));
            }
        }

        if (TryReadChanged(context, PitchChannelSource, out var pitchText, out var pitchCleared))
        {
            if (pitchCleared)
            {
                candidate = candidate with { PitchChannel = defaults.PitchChannel };
                _validity.Set(context, PitchChannelSource, false);
            }
            else if (TryParseInteger(pitchText, out var pitch)
                     && pitch is >= 1 and <= AttitudeSettings.MaximumChannel)
            {
                candidate = candidate with { PitchChannel = pitch };
                _validity.Set(context, PitchChannelSource, false);
            }
            else
            {
                _validity.Set(context, PitchChannelSource, true, Tr(AttitudeDisplayText.InvalidInteger));
            }
        }

        if (TryReadChanged(context, YawChannelSource, out var yawText, out var yawCleared))
        {
            if (yawCleared)
            {
                candidate = candidate with { YawChannel = defaults.YawChannel };
                _validity.Set(context, YawChannelSource, false);
            }
            else if (TryParseInteger(yawText, out var yaw)
                     && yaw is >= 1 and <= AttitudeSettings.MaximumChannel)
            {
                candidate = candidate with { YawChannel = yaw };
                _validity.Set(context, YawChannelSource, false);
            }
            else
            {
                _validity.Set(context, YawChannelSource, true, Tr(AttitudeDisplayText.InvalidInteger));
            }
        }

        if (TryReadChanged(context, StyleSource, out var styleText, out var styleCleared))
        {
            if (styleCleared)
            {
                candidate = candidate with { Style = defaults.Style };
            }
            else if (styleText.Length > 0)
            {
                candidate = candidate with { Style = ParseStyle(styleText, _settings.Style) };
            }
        }

        if (TryReadChanged(context, UnitSource, out var unitText, out var unitCleared))
        {
            if (unitCleared)
            {
                candidate = candidate with { Unit = defaults.Unit };
            }
            else if (unitText.Length > 0)
            {
                candidate = candidate with { Unit = ParseUnit(unitText, _settings.Unit) };
            }
        }

        if (TryReadChanged(context, SmoothingSource, out var smoothingText, out var smoothingCleared))
        {
            if (smoothingCleared)
            {
                candidate = candidate with { Smoothing = defaults.Smoothing };
            }
            else if (bool.TryParse(smoothingText.Trim(), out var smoothing))
            {
                candidate = candidate with { Smoothing = smoothing };
            }
        }

        if (TryReadChanged(context, SmoothingMsSource, out var smoothingMsText, out var smoothingMsCleared))
        {
            if (smoothingMsCleared)
            {
                candidate = candidate with { SmoothingMs = defaults.SmoothingMs };
                _validity.Set(context, SmoothingMsSource, false);
            }
            else if (TryParseClamped(
                         smoothingMsText,
                         AttitudeSettings.MinimumSmoothingMs,
                         AttitudeSettings.MaximumSmoothingMs,
                         out var smoothingMs))
            {
                candidate = candidate with { SmoothingMs = smoothingMs };
                _validity.Set(context, SmoothingMsSource, false);
            }
            else
            {
                _validity.Set(context, SmoothingMsSource, true, Tr(AttitudeDisplayText.InvalidNumber));
            }
        }

        if (TryReadChanged(context, ZoomSource, out var zoomText, out var zoomCleared))
        {
            if (zoomCleared)
            {
                candidate = candidate with { Zoom = defaults.Zoom };
                _validity.Set(context, ZoomSource, false);
            }
            else if (TryParseZoom(zoomText, out var zoom))
            {
                candidate = candidate with { Zoom = zoom };
                _validity.Set(context, ZoomSource, false);
            }
            else
            {
                _validity.Set(context, ZoomSource, true, Tr(AttitudeDisplayText.InvalidNumber));
            }
        }

        if (TryReadChanged(context, GridSource, out var gridText, out var gridCleared))
        {
            if (gridCleared)
            {
                candidate = candidate with { ShowGrid = defaults.ShowGrid };
            }
            else if (bool.TryParse(gridText.Trim(), out var grid))
            {
                candidate = candidate with { ShowGrid = grid };
            }
        }

        if (TryReadChanged(context, LabelsSource, out var labelsText, out var labelsCleared))
        {
            if (labelsCleared)
            {
                candidate = candidate with { ShowLabels = defaults.ShowLabels };
            }
            else if (bool.TryParse(labelsText.Trim(), out var labels))
            {
                candidate = candidate with { ShowLabels = labels };
            }
        }

        if (TryReadChanged(context, InvertRollSource, out var invertRollText, out var invertRollCleared))
        {
            if (invertRollCleared)
            {
                candidate = candidate with { InvertRoll = defaults.InvertRoll };
            }
            else if (bool.TryParse(invertRollText.Trim(), out var invertRoll))
            {
                candidate = candidate with { InvertRoll = invertRoll };
            }
        }

        if (TryReadChanged(context, InvertPitchSource, out var invertPitchText, out var invertPitchCleared))
        {
            if (invertPitchCleared)
            {
                candidate = candidate with { InvertPitch = defaults.InvertPitch };
            }
            else if (bool.TryParse(invertPitchText.Trim(), out var invertPitch))
            {
                candidate = candidate with { InvertPitch = invertPitch };
            }
        }

        if (TryReadChanged(context, InvertYawSource, out var invertYawText, out var invertYawCleared))
        {
            if (invertYawCleared)
            {
                candidate = candidate with { InvertYaw = defaults.InvertYaw };
            }
            else if (bool.TryParse(invertYawText.Trim(), out var invertYaw))
            {
                candidate = candidate with { InvertYaw = invertYaw };
            }
        }

        if (TryReadChanged(context, LostZeroSource, out var lostZeroText, out var lostZeroCleared))
        {
            if (lostZeroCleared)
            {
                candidate = candidate with { LostZero = defaults.LostZero };
            }
            else if (bool.TryParse(lostZeroText.Trim(), out var lostZero))
            {
                candidate = candidate with { LostZero = lostZero };
            }
        }

        if (TryReadChanged(context, LostDelaySource, out var lostDelayText, out var lostDelayCleared))
        {
            if (lostDelayCleared)
            {
                candidate = candidate with { LostDelayMs = defaults.LostDelayMs };
                _validity.Set(context, LostDelaySource, false);
            }
            else if (TryParseClamped(
                         lostDelayText,
                         AttitudeSettings.MinimumLostDelayMs,
                         AttitudeSettings.MaximumLostDelayMs,
                         out var lostDelay))
            {
                candidate = candidate with { LostDelayMs = lostDelay };
                _validity.Set(context, LostDelaySource, false);
            }
            else
            {
                _validity.Set(context, LostDelaySource, true, Tr(AttitudeDisplayText.InvalidNumber));
            }
        }

        if (!candidate.Equals(_settings))
        {
            _settings = candidate;
            _metaDirty = true;
        }
    }

    /// <summary>读出某个值是否发生变化；`cleared` 区分“框架清空”（回默认）与“用户输入空串”（非法）。</summary>
    private bool TryReadChanged(
        IModuleContext context,
        string source,
        out string text,
        out bool cleared)
    {
        var current = context.Values.ReadText(source);
        cleared = current is null;
        var marker = current ?? ClearedMarker;
        if (_lastText.TryGetValue(source, out var previous) && string.Equals(previous, marker, StringComparison.Ordinal))
        {
            text = string.Empty;
            return false;
        }

        _lastText[source] = marker;
        text = current ?? string.Empty;
        return true;
    }

    private void ResetSettings(IModuleContext context)
    {
        var defaults = AttitudeSettings.Default;
        Accept(context, StyleSource, StyleToken(defaults.Style));
        Accept(context, UnitSource, UnitToken(defaults.Unit));
        Accept(context, SmoothingSource, defaults.Smoothing ? "true" : "false");
        Accept(context, SmoothingMsSource, defaults.SmoothingMs.ToString(CultureInfo.InvariantCulture));
        Accept(context, ZoomSource, defaults.Zoom.ToString("0.###", CultureInfo.InvariantCulture));
        Accept(context, GridSource, defaults.ShowGrid ? "true" : "false");
        Accept(context, LabelsSource, defaults.ShowLabels ? "true" : "false");
        Accept(context, InvertRollSource, defaults.InvertRoll ? "true" : "false");
        Accept(context, InvertPitchSource, defaults.InvertPitch ? "true" : "false");
        Accept(context, InvertYawSource, defaults.InvertYaw ? "true" : "false");
        Accept(context, LostZeroSource, defaults.LostZero ? "true" : "false");
        Accept(context, LostDelaySource, defaults.LostDelayMs.ToString(CultureInfo.InvariantCulture));
        _lastText.Clear();
        _validity.ClearAll(context);
    }

    private static void Accept(IModuleContext context, string source, string text) =>
        context.Values.AcceptText(source, text);

    private void Publish(IModuleContext context, long nowMs)
    {
        var groupId = EffectiveGroupId();
        var lostDelay = _settings.LostDelayMs;
        var lostZero = _settings.LostZero;

        PublishAngle(context, 0, RollSource, _settings.RollChannel, _settings.InvertRoll, groupId, nowMs, lostDelay, lostZero);
        PublishAngle(context, 1, PitchSource, _settings.PitchChannel, _settings.InvertPitch, groupId, nowMs, lostDelay, lostZero);
        PublishAngle(context, 2, YawSource, _settings.YawChannel, _settings.InvertYaw, groupId, nowMs, lostDelay, lostZero);

        PublishStatus(context, ResolveStatus(groupId, nowMs));

        if (_metaDirty)
        {
            PublishMeta(context);
            _metaDirty = false;
        }
    }

    /// <summary>当前组：组号是用户配置的 1 基标识；未发现就等待，不自动跟随。</summary>
    private uint EffectiveGroupId() => (uint)_settings.GroupId;

    private void PublishAngle(
        IModuleContext context,
        int slot,
        string source,
        int channel,
        bool invert,
        uint groupId,
        long nowMs,
        int lostDelayMs,
        bool lostZero)
    {
        var raw = _feed.Resolve(groupId, channel - 1, nowMs, lostZero, lostDelayMs);
        var degrees = _settings.Unit == AttitudeAngleUnit.Radian ? raw * (180.0 / Math.PI) : raw;
        var value = (float)(invert ? -degrees : degrees);

        if (_hasPublished[slot] && value.Equals(_lastPublished[slot]))
        {
            return;
        }

        PublishFloat(context, source, value);
        _lastPublished[slot] = value;
        _hasPublished[slot] = true;
    }

    private string ResolveStatus(uint groupId, long nowMs)
    {
        var group = _feed.Group(groupId);
        if (group is null)
        {
            return Tr(AttitudeDisplayText.WaitingGroup, _settings.GroupId);
        }

        if (_settings.RollChannel > group.ChannelCount)
        {
            return Tr(AttitudeDisplayText.ChannelMissing, _settings.RollChannel, group.ChannelCount);
        }

        if (_settings.PitchChannel > group.ChannelCount)
        {
            return Tr(AttitudeDisplayText.ChannelMissing, _settings.PitchChannel, group.ChannelCount);
        }

        if (_settings.YawChannel > group.ChannelCount)
        {
            return Tr(AttitudeDisplayText.ChannelMissing, _settings.YawChannel, group.ChannelCount);
        }

        if (nowMs - group.LastFrameTick > _settings.LostDelayMs)
        {
            return _settings.LostZero
                ? Tr(AttitudeDisplayText.Lost, _settings.LostDelayMs)
                : Tr(AttitudeDisplayText.Ready);
        }

        if (!_feed.ChannelValid(groupId, _settings.RollChannel - 1))
        {
            return Tr(AttitudeDisplayText.ChannelInvalid, _settings.RollChannel);
        }

        if (!_feed.ChannelValid(groupId, _settings.PitchChannel - 1))
        {
            return Tr(AttitudeDisplayText.ChannelInvalid, _settings.PitchChannel);
        }

        if (!_feed.ChannelValid(groupId, _settings.YawChannel - 1))
        {
            return Tr(AttitudeDisplayText.ChannelInvalid, _settings.YawChannel);
        }

        return Tr(
            AttitudeDisplayText.Receiving,
            groupId,
            FormatRate(group.RateHz));
    }

    private void PublishStatus(IModuleContext context, string text)
    {
        if (string.Equals(text, _lastStatus, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatus = text;
        context.Display.SetText(StatusSource, text);
    }

    private void PublishMeta(IModuleContext context)
    {
        var settings = _settings;
        var snapshot = new
        {
            revision = ++_metaRevision,
            settings = new
            {
                group = settings.GroupId,
                roll = settings.RollChannel,
                pitch = settings.PitchChannel,
                yaw = settings.YawChannel,
                style = StyleToken(settings.Style),
                unit = UnitToken(settings.Unit),
                smooth = settings.Smoothing,
                smoothMs = settings.SmoothingMs,
                zoom = settings.Zoom,
                grid = settings.ShowGrid,
                labels = settings.ShowLabels,
            },
        };

        context.Display.SetText(MetaSource, JsonSerializer.Serialize(snapshot));
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        ApplySettings(context);
        Publish(context, Environment.TickCount64);
    }

    private static void PublishFloat(IModuleContext context, string source, float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        context.Ports.Publish(source, buffer);
    }

    private static AttitudeRenderStyle ParseStyle(string text, AttitudeRenderStyle fallback)
    {
        if (string.Equals(text.Trim(), "axes", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Axes;
        }

        if (string.Equals(text.Trim(), "cube", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Cube;
        }

        if (string.Equals(text.Trim(), "drone", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Drone;
        }

        return fallback;
    }

    private static AttitudeAngleUnit ParseUnit(string text, AttitudeAngleUnit fallback)
    {
        if (string.Equals(text.Trim(), "rad", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeAngleUnit.Radian;
        }

        if (string.Equals(text.Trim(), "deg", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeAngleUnit.Degree;
        }

        return fallback;
    }

    private static string StyleToken(AttitudeRenderStyle style) => style switch
    {
        AttitudeRenderStyle.Axes => "axes",
        AttitudeRenderStyle.Drone => "drone",
        _ => "cube",
    };

    private static string UnitToken(AttitudeAngleUnit unit) =>
        unit == AttitudeAngleUnit.Radian ? "rad" : "deg";

    private static bool TryParseClamped(string text, int minimum, int maximum, out int value)
    {
        value = 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = Math.Clamp(parsed, minimum, maximum);
        return true;
    }

    private static bool TryParseZoom(string text, out double value)
    {
        value = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = Math.Clamp(parsed, AttitudeSettings.MinimumZoom, AttitudeSettings.MaximumZoom);
        return true;
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string FormatRate(float rateHz) =>
        rateHz > 0 && float.IsFinite(rateHz)
            ? rateHz.ToString("0.###", CultureInfo.InvariantCulture)
            : "0";
}
