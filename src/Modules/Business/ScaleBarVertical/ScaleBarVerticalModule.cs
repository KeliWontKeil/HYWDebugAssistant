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
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.ScaleBarVertical;

/// <summary>
/// **纵向刻度条**：把波形帧里某一组的某一个通道显示成矩形刻度进度条（单通道）。
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
        DeclareValue(context, ColorSource, ValueEditor.Enum, [
            new ValueChoice("green", Tr(ScaleBarVerticalText.ColorGreen)),
            new ValueChoice("blue", Tr(ScaleBarVerticalText.ColorBlue)),
            new ValueChoice("orange", Tr(ScaleBarVerticalText.ColorOrange)),
            new ValueChoice("red", Tr(ScaleBarVerticalText.ColorRed)),
            new ValueChoice("purple", Tr(ScaleBarVerticalText.ColorPurple)),
        ]);
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

        if (TryReadChanged(context, GroupSource, out var groupText))
        {
            if (TryParseInteger(groupText, out var group) && group >= 0)
            {
                candidate = candidate with { GroupId = group };
                ClearNotice(context, GroupSource);
            }
            else
            {
                ReportInvalidInteger(context, GroupSource);
            }
        }

        if (TryReadChanged(context, ChannelSource, out var channelText))
        {
            if (TryParseInteger(channelText, out var channel) && channel >= 0)
            {
                candidate = candidate with { Channel = channel };
                ClearNotice(context, ChannelSource);
            }
            else
            {
                ReportInvalidInteger(context, ChannelSource);
            }
        }

        if (TryReadChanged(context, MinimumSource, out var minimumText))
        {
            if (minimumText.Length == 0)
            {
                candidate = candidate with { Minimum = defaults.Minimum };
                ClearNotice(context, MinimumSource);
            }
            else if (TryParseDouble(minimumText, out var minimum))
            {
                candidate = candidate with { Minimum = minimum };
                ClearNotice(context, MinimumSource);
            }
            else
            {
                ReportInvalidNumber(context, MinimumSource);
            }
        }

        if (TryReadChanged(context, MaximumSource, out var maximumText))
        {
            if (maximumText.Length == 0)
            {
                candidate = candidate with { Maximum = defaults.Maximum };
                ClearNotice(context, MaximumSource);
            }
            else if (TryParseDouble(maximumText, out var maximum))
            {
                candidate = candidate with { Maximum = maximum };
                ClearNotice(context, MaximumSource);
            }
            else
            {
                ReportInvalidNumber(context, MaximumSource);
            }
        }

        if (TryReadChanged(context, AutoZeroSource, out var autoZeroText))
        {
            candidate = candidate with
            {
                AutoZero = bool.TryParse(autoZeroText.Trim(), out var autoZero) ? autoZero : defaults.AutoZero,
            };
        }

        if (TryReadChanged(context, ZeroDelaySource, out var delayText))
        {
            candidate = candidate with
            {
                ZeroDelayMs = ParseClamped(
                    delayText,
                    defaults.ZeroDelayMs,
                    ScaleBarSettings.MinimumZeroDelayMs,
                    ScaleBarSettings.MaximumZeroDelayMs),
            };
        }

        if (TryReadChanged(context, TitleSource, out var titleText))
        {
            candidate = candidate with { Title = titleText };
        }

        if (TryReadChanged(context, UnitSource, out var unitText))
        {
            candidate = candidate with { Unit = unitText };
        }

        if (TryReadChanged(context, SmoothingSource, out var smoothingText))
        {
            candidate = candidate with
            {
                Smoothing = bool.TryParse(smoothingText.Trim(), out var smoothing) ? smoothing : defaults.Smoothing,
            };
        }

        if (TryReadChanged(context, SmoothingMsSource, out var smoothingMsText))
        {
            candidate = candidate with
            {
                SmoothingMs = ParseClamped(
                    smoothingMsText,
                    defaults.SmoothingMs,
                    ScaleBarSettings.MinimumSmoothingMs,
                    ScaleBarSettings.MaximumSmoothingMs),
            };
        }

        if (TryReadChanged(context, DecimalsSource, out var decimalsText))
        {
            candidate = candidate with
            {
                Decimals = ParseClamped(
                    decimalsText,
                    defaults.Decimals,
                    ScaleBarSettings.MinimumDecimals,
                    ScaleBarSettings.MaximumDecimals),
            };
        }

        if (TryReadChanged(context, FillFromZeroSource, out var fillFromZeroText))
        {
            candidate = candidate with
            {
                FillFromZero = bool.TryParse(fillFromZeroText.Trim(), out var fillFromZero)
                    ? fillFromZero
                    : defaults.FillFromZero,
            };
        }

        if (TryReadChanged(context, ColorSource, out var colorText) && colorText.Length > 0)
        {
            candidate = candidate with { Color = NormalizeColor(colorText, defaults.Color) };
        }

        if (candidate.Maximum <= candidate.Minimum)
        {
            // 只回退"范围这一对"，其余设置照常生效；用户下一步改对上限即可恢复。
            var message = Tr(ScaleBarVerticalText.InvalidRange);
            context.Values.Report(MinimumSource, new ValueNotice(ValueNoticeSeverity.Error, message));
            context.Values.Report(MaximumSource, new ValueNotice(ValueNoticeSeverity.Error, message));
            candidate = candidate with { Minimum = _settings.Minimum, Maximum = _settings.Maximum };
        }
        else
        {
            ClearNotice(context, MinimumSource);
            ClearNotice(context, MaximumSource);
        }

        if (!candidate.Equals(_settings))
        {
            _settings = candidate;
            _metaDirty = true;
        }
    }

    /// <summary>读出某个值是否发生变化；值被清空时用哨兵表示（与"用户输入空串"区分）。</summary>
    private bool TryReadChanged(IModuleContext context, string source, out string text)
    {
        var current = context.Values.ReadText(source) ?? ClearedMarker;
        if (_lastText.TryGetValue(source, out var previous) && string.Equals(previous, current, StringComparison.Ordinal))
        {
            text = string.Empty;
            return false;
        }

        _lastText[source] = current;
        text = string.Equals(current, ClearedMarker, StringComparison.Ordinal) ? string.Empty : current;
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
    }

    private static void Accept(IModuleContext context, string source, string text) =>
        context.Values.AcceptText(source, text);

    private void Publish(IModuleContext context, long nowMs)
    {
        var groupId = EffectiveGroupId();
        var target = (float)_feed.Resolve(
            groupId,
            _settings.Channel,
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

    /// <summary>当前组：配置了具体组号就用它，0 表示跟随第一个出现的组。</summary>
    private uint EffectiveGroupId() =>
        _settings.GroupId > 0 ? (uint)_settings.GroupId : _feed.FirstGroupId;

    private string ResolveStatus(uint groupId, long nowMs)
    {
        if (groupId == 0)
        {
            return Tr(ScaleBarVerticalText.Ready);
        }

        var group = _feed.Group(groupId);
        if (group is null)
        {
            return _settings.GroupId > 0
                ? Tr(ScaleBarVerticalText.WaitingGroup, _settings.GroupId)
                : Tr(ScaleBarVerticalText.Ready);
        }

        if (_settings.Channel >= group.ChannelCount)
        {
            return Tr(ScaleBarVerticalText.ChannelMissing, _settings.Channel, group.ChannelCount);
        }

        if (nowMs - group.LastFrameTick > _settings.ZeroDelayMs)
        {
            return _settings.AutoZero
                ? Tr(ScaleBarVerticalText.Zeroed, _settings.ZeroDelayMs)
                : Tr(ScaleBarVerticalText.Ready);
        }

        if (!_feed.ChannelValid(groupId, _settings.Channel))
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

    private static void ClearNotice(IModuleContext context, string source) =>
        context.Values.Report(source, null);

    private static void ReportInvalidNumber(IModuleContext context, string source) =>
        context.Values.Report(source, new ValueNotice(
            ValueNoticeSeverity.Error,
            Tr(ScaleBarVerticalText.InvalidNumber)));

    private static void ReportInvalidInteger(IModuleContext context, string source) =>
        context.Values.Report(source, new ValueNotice(
            ValueNoticeSeverity.Error,
            Tr(ScaleBarVerticalText.InvalidInteger)));

    private static int ParseClamped(string text, int fallback, int minimum, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>颜色角色只认五个稳定键；未知值回落到默认色。</summary>
    private static string NormalizeColor(string text, string fallback)
    {
        var token = text.Trim();
        return token is "green" or "blue" or "orange" or "red" or "purple" ? token : fallback;
    }

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
