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
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.ScaleBarVertical;

/// <summary>
/// **纵向进度条**：把波形帧里某一组的某一个通道显示成矩形刻度进度条（单通道）。
/// </summary>
/// <remarks>
/// 本模块是"显示值该是多少"的唯一事实源：样本新鲜给样本，过期按自动回零设置给 0 或保持，
/// 由 <see cref="ChannelFrameFeed.Resolve"/> 一条规则决定；控件只画收到的目标值。
/// 组号与通道号是**用户输入的持久化值**（组 0 = 自动跟随第一个出现的组）。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "scale-bar-vertical",
    DisplayNameKey = ScaleBarVerticalText.Title,
    DescriptionKey = ScaleBarVerticalText.Description,
    Tags = ["terminal", "waveform", "display", "scale-bar"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.ScaleBarVerticalViewFactory))]
public sealed class ScaleBarVerticalModule : IModule, IActivitySource, IDisposable
{
    /// <summary>输入端口。</summary>
    public const string InPort = "in";

    /// <summary>波形帧类型标识（供视图 XAML 的 <c>{x:Static}</c> 引用）。</summary>
    public const string WaveformTypeId = WaveformFrameContract.TypeId;

    /// <summary>角色 Id。</summary>
    public const string RoleId = "display";

    /// <summary>目标值显示源（F32，永远是有限的显示目标）。</summary>
    public const string ValueSource = "value";

    /// <summary>设置摘要 JSON 源。</summary>
    public const string MetaSource = "meta";

    /// <summary>状态文本源（界面作为悬停提示）。</summary>
    public const string StatusSource = "status";

    /// <summary>组号值（0 = 自动跟随第一个出现的组）。</summary>
    public const string GroupSource = "input.group";

    /// <summary>通道号值（0 起）。</summary>
    public const string ChannelSource = "input.channel";

    /// <summary>量程下限值。</summary>
    public const string MinimumSource = "settings.minimum";

    /// <summary>量程上限值。</summary>
    public const string MaximumSource = "settings.maximum";

    /// <summary>自动回零开关值。</summary>
    public const string AutoZeroSource = "settings.autoZero";

    /// <summary>回零延时值（毫秒）。</summary>
    public const string ZeroDelaySource = "settings.zeroDelay";

    /// <summary>表盘文字值。</summary>
    public const string TitleSource = "settings.title";

    /// <summary>单位值。</summary>
    public const string UnitSource = "settings.unit";

    /// <summary>平滑开关值。</summary>
    public const string SmoothingSource = "settings.smoothing";

    /// <summary>平滑时长值（毫秒）。</summary>
    public const string SmoothingMsSource = "settings.smoothingMs";

    /// <summary>读数小数位值。</summary>
    public const string DecimalsSource = "settings.decimals";

    /// <summary>填充起点开关值（true = 从零开始）。</summary>
    public const string FillFromZeroSource = "settings.fillFromZero";

    /// <summary>填充颜色角色值。</summary>
    public const string ColorSource = "settings.color";

    /// <summary>恢复默认设置。</summary>
    public const string ResetSettingsCommand = "resetSettings";

    /// <summary>本模块图标表的属主程序集（浮层里的图标用显式属主）。</summary>
    public static System.Reflection.Assembly IconOwner => typeof(ScaleBarVerticalModule).Assembly;

    /// <summary>回零/状态判定的定时器周期（毫秒）。</summary>
    private const int TimerIntervalMs = 50;

    /// <summary>值被清空（还原/首次激活）的哨兵：与"用户输入空串"区分开。</summary>
    private const string ClearedMarker = "\0";

    private readonly ChannelFrameFeed _feed = new();
    private readonly Dictionary<string, string> _lastText = new(StringComparer.Ordinal);
    private readonly InputValidity _validity = new();

    private IModuleContext? _context;
    private IModuleTimer? _timer;
    private ScaleBarSettings _settings = ScaleBarSettings.Default;
    private bool _settingsLoaded;
    private long _metaRevision;
    private bool _metaDirty = true;
    private bool _hasPublishedValue;
    private float _lastPublishedValue;
    private string _lastStatus = string.Empty;

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(ScaleBarVerticalModule).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(ScaleBarVerticalModule).Assembly, key, args);

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
            Tr(ScaleBarVerticalText.RoleDisplay)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            ValueSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(MetaSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));

        DeclareValue(context, GroupSource, ValueEditor.Number);
        DeclareValue(context, ChannelSource, ValueEditor.Number);
        DeclareSettings(context);
        context.Declaration.DeclareCommand(ResetSettingsCommand, Tr(ScaleBarVerticalText.ResetSettings));

        _context = context;
        _timer = context.Timers.Create();
        _timer.Interval = TimeSpan.FromMilliseconds(TimerIntervalMs);
        _timer.Tick += OnTimerTick;
    }

    private static void DeclareSettings(IModuleContext context)
    {
        DeclareValue(context, MinimumSource, ValueEditor.Number);
        DeclareValue(context, MaximumSource, ValueEditor.Number);
        DeclareValue(context, AutoZeroSource, ValueEditor.Boolean);
        DeclareValue(context, ZeroDelaySource, ValueEditor.Number);
        DeclareValue(context, TitleSource, ValueEditor.Text);
        DeclareValue(context, UnitSource, ValueEditor.Text);
        DeclareValue(context, SmoothingSource, ValueEditor.Boolean);
        DeclareValue(context, SmoothingMsSource, ValueEditor.Number);
        DeclareValue(context, DecimalsSource, ValueEditor.Number);
        DeclareValue(context, FillFromZeroSource, ValueEditor.Boolean);
        DeclareValue(context, ColorSource, ValueEditor.Text);
    }

    private static void DeclareValue(
        IModuleContext context,
        string name,
        ValueEditor editor) =>
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = editor,
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
        _settings = ScaleBarSettings.Default;
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
                    PublishStatus(context, Tr(ScaleBarVerticalText.InvalidFrame));
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
        var defaults = ScaleBarSettings.Default;
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
                _validity.Set(context, GroupSource, true, Tr(ScaleBarVerticalText.InvalidInteger), ValueValidityOrigin.Format);
            }
        }

        if (TryReadChanged(context, ChannelSource, out var channelText, out var channelCleared))
        {
            if (channelCleared)
            {
                candidate = candidate with { Channel = defaults.Channel };
                _validity.Set(context, ChannelSource, false);
            }
            else if (TryParseInteger(channelText, out var channel)
                     && channel is >= ScaleBarSettings.MinimumChannel and <= ScaleBarSettings.MaximumChannel)
            {
                candidate = candidate with { Channel = channel };
                _validity.Set(context, ChannelSource, false);
            }
            else
            {
                _validity.Set(context, ChannelSource, true, Tr(ScaleBarVerticalText.InvalidInteger), ValueValidityOrigin.Format);
            }
        }

        if (TryReadChanged(context, MinimumSource, out var minimumText, out var minimumCleared))
        {
            if (minimumCleared)
            {
                candidate = candidate with { Minimum = defaults.Minimum };
            }
            else if (minimumText.Length > 0 && TryParseDouble(minimumText, out var minimum))
            {
                candidate = candidate with { Minimum = minimum };
            }
        }

        if (TryReadChanged(context, MaximumSource, out var maximumText, out var maximumCleared))
        {
            if (maximumCleared)
            {
                candidate = candidate with { Maximum = defaults.Maximum };
            }
            else if (maximumText.Length > 0 && TryParseDouble(maximumText, out var maximum))
            {
                candidate = candidate with { Maximum = maximum };
            }
        }

        var minimumCurrent = context.Values.ReadText(MinimumSource);
        var maximumCurrent = context.Values.ReadText(MaximumSource);
        var minimumOk = minimumCurrent is null;
        var maximumOk = maximumCurrent is null;
        var minimumValue = defaults.Minimum;
        var maximumValue = defaults.Maximum;
        if (minimumCurrent is not null)
        {
            minimumOk = TryParseDouble(minimumCurrent, out minimumValue);
        }

        if (maximumCurrent is not null)
        {
            maximumOk = TryParseDouble(maximumCurrent, out maximumValue);
        }

        if (minimumOk && maximumOk)
        {
            if (maximumValue <= minimumValue)
            {
                var message = Tr(ScaleBarVerticalText.InvalidRange);
                _validity.Set(context, MinimumSource, true, message);
                _validity.Set(context, MaximumSource, true, message);
                candidate = candidate with { Minimum = _settings.Minimum, Maximum = _settings.Maximum };
            }
            else
            {
                candidate = candidate with { Minimum = minimumValue, Maximum = maximumValue };
                _validity.Set(context, MinimumSource, false);
                _validity.Set(context, MaximumSource, false);
            }
        }
        else
        {
            _validity.Set(context, MinimumSource, !minimumOk, Tr(ScaleBarVerticalText.InvalidNumber));
            _validity.Set(context, MaximumSource, !maximumOk, Tr(ScaleBarVerticalText.InvalidNumber));
        }

        if (TryReadChanged(context, AutoZeroSource, out var autoZeroText, out var autoZeroCleared))
        {
            if (autoZeroCleared)
            {
                candidate = candidate with { AutoZero = defaults.AutoZero };
            }
            else if (bool.TryParse(autoZeroText.Trim(), out var autoZero))
            {
                candidate = candidate with { AutoZero = autoZero };
            }
        }

        if (TryReadChanged(context, ZeroDelaySource, out var delayText, out var delayCleared))
        {
            if (delayCleared)
            {
                candidate = candidate with { ZeroDelayMs = defaults.ZeroDelayMs };
                _validity.Set(context, ZeroDelaySource, false);
            }
            else if (TryParseInRange(
                         delayText,
                         ScaleBarSettings.MinimumZeroDelayMs,
                         ScaleBarSettings.MaximumZeroDelayMs,
                         out var delay))
            {
                candidate = candidate with { ZeroDelayMs = delay };
                _validity.Set(context, ZeroDelaySource, false);
            }
            else
            {
                _validity.Set(context, ZeroDelaySource, true, Tr(ScaleBarVerticalText.InvalidNumber), ValueValidityOrigin.Format);
            }
        }

        if (TryReadChanged(context, TitleSource, out var titleText, out _))
        {
            candidate = candidate with { Title = titleText };
        }

        if (TryReadChanged(context, UnitSource, out var unitText, out _))
        {
            candidate = candidate with { Unit = unitText };
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
            else if (TryParseInRange(
                         smoothingMsText,
                         ScaleBarSettings.MinimumSmoothingMs,
                         ScaleBarSettings.MaximumSmoothingMs,
                         out var smoothingMs))
            {
                candidate = candidate with { SmoothingMs = smoothingMs };
                _validity.Set(context, SmoothingMsSource, false);
            }
            else
            {
                _validity.Set(context, SmoothingMsSource, true, Tr(ScaleBarVerticalText.InvalidNumber), ValueValidityOrigin.Format);
            }
        }

        if (TryReadChanged(context, DecimalsSource, out var decimalsText, out var decimalsCleared))
        {
            if (decimalsCleared)
            {
                candidate = candidate with { Decimals = defaults.Decimals };
                _validity.Set(context, DecimalsSource, false);
            }
            else if (TryParseInRange(
                         decimalsText,
                         ScaleBarSettings.MinimumDecimals,
                         ScaleBarSettings.MaximumDecimals,
                         out var decimals))
            {
                candidate = candidate with { Decimals = decimals };
                _validity.Set(context, DecimalsSource, false);
            }
            else
            {
                _validity.Set(context, DecimalsSource, true, Tr(ScaleBarVerticalText.InvalidNumber), ValueValidityOrigin.Format);
            }
        }

        if (TryReadChanged(context, FillFromZeroSource, out var fillFromZeroText, out var fillFromZeroCleared))
        {
            if (fillFromZeroCleared)
            {
                candidate = candidate with { FillFromZero = defaults.FillFromZero };
            }
            else if (bool.TryParse(fillFromZeroText.Trim(), out var fillFromZero))
            {
                candidate = candidate with { FillFromZero = fillFromZero };
            }
        }

        if (TryReadChanged(context, ColorSource, out var colorText, out var colorCleared))
        {
            candidate = candidate with
            {
                Color = colorCleared ? defaults.Color : NormalizeColor(colorText),
            };
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
        var defaults = ScaleBarSettings.Default;
        Accept(context, MinimumSource, Format(defaults.Minimum));
        Accept(context, MaximumSource, Format(defaults.Maximum));
        Accept(context, AutoZeroSource, defaults.AutoZero ? "true" : "false");
        Accept(context, ZeroDelaySource, defaults.ZeroDelayMs.ToString(CultureInfo.InvariantCulture));
        Accept(context, TitleSource, defaults.Title);
        Accept(context, UnitSource, defaults.Unit);
        Accept(context, SmoothingSource, defaults.Smoothing ? "true" : "false");
        Accept(context, SmoothingMsSource, defaults.SmoothingMs.ToString(CultureInfo.InvariantCulture));
        Accept(context, DecimalsSource, defaults.Decimals.ToString(CultureInfo.InvariantCulture));
        Accept(context, FillFromZeroSource, defaults.FillFromZero ? "true" : "false");
        Accept(context, ColorSource, defaults.Color);
        _lastText.Clear();
        _validity.ClearAll(context);
    }

    private static void Accept(IModuleContext context, string source, string text) =>
        context.Values.AcceptText(source, text);

    private void Publish(IModuleContext context, long nowMs)
    {
        var groupId = EffectiveGroupId();
        var target = (float)_feed.Resolve(
            groupId,
            _settings.Channel - 1,
            nowMs,
            _settings.AutoZero,
            _settings.ZeroDelayMs);

        if (!_hasPublishedValue || !target.Equals(_lastPublishedValue))
        {
            PublishFloat(context, ValueSource, target);
            _lastPublishedValue = target;
            _hasPublishedValue = true;
        }

        PublishStatus(context, ResolveStatus(groupId, nowMs));

        if (_metaDirty)
        {
            PublishMeta(context);
            _metaDirty = false;
        }
    }

    /// <summary>当前组：组号是用户配置的 1 基标识；未发现就等待，不自动跟随。</summary>
    private uint EffectiveGroupId() => (uint)_settings.GroupId;

    private string ResolveStatus(uint groupId, long nowMs)
    {
        var group = _feed.Group(groupId);
        if (group is null)
        {
            return Tr(ScaleBarVerticalText.WaitingGroup, _settings.GroupId);
        }

        if (_settings.Channel > group.ChannelCount)
        {
            return Tr(ScaleBarVerticalText.ChannelMissing, _settings.Channel, group.ChannelCount);
        }

        if (nowMs - group.LastFrameTick > _settings.ZeroDelayMs)
        {
            return _settings.AutoZero
                ? Tr(ScaleBarVerticalText.Zeroed, _settings.ZeroDelayMs)
                : Tr(ScaleBarVerticalText.Ready);
        }

        if (!_feed.ChannelValid(groupId, _settings.Channel - 1))
        {
            return Tr(ScaleBarVerticalText.ChannelInvalid, _settings.Channel);
        }

        return Tr(
            ScaleBarVerticalText.Receiving,
            groupId,
            _settings.Channel,
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
                channel = settings.Channel,
                min = settings.Minimum,
                max = settings.Maximum,
                autoZero = settings.AutoZero,
                zeroDelayMs = settings.ZeroDelayMs,
                title = settings.Title,
                unit = settings.Unit,
                smooth = settings.Smoothing,
                smoothMs = settings.SmoothingMs,
                decimals = settings.Decimals,
                fillFromZero = settings.FillFromZero,
                color = settings.Color,
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

    private static bool TryParseInRange(string text, int minimum, int maximum, out int value)
    {
        value = 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>颜色只认 <c>AARRGGBB</c>；空/非法回落到主题默认。</summary>
    private static string NormalizeColor(string text) =>
        ColorText.TryParseArgb(text, out var argb) ? ColorText.FormatArgb(argb) : string.Empty;

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static string Format(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatRate(float rateHz) =>
        rateHz > 0 && float.IsFinite(rateHz)
            ? rateHz.ToString("0.###", CultureInfo.InvariantCulture)
            : "0";
}
