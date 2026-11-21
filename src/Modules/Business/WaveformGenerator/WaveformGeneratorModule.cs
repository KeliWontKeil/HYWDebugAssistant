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

namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>
/// **波形发生器**：多组多通道、波形参数、预览、连续发送、按周期一次性发送。
/// </summary>
/// <remarks>
/// 数据帧约定见 <c>dev/design/waveform-frame.md</c>；本模块自己声明同一类型与形状。
/// 低频逐帧；高频按 1 ms 时间片批量发多帧并提示，不承诺逐帧时序。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "waveform-generator",
    DisplayNameKey = WaveformGeneratorText.Title,
    DescriptionKey = WaveformGeneratorText.Description,
    Tags = ["source", "waveform", "generator"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.WaveformGeneratorViewFactory))]
public sealed class WaveformGeneratorModule : IModule, IActivitySource
{
    /// <summary>输出端口。</summary>
    public const string OutPort = "out";

    /// <summary>波形帧类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string WaveformTypeId = WaveformFrameContract.TypeId;

    /// <summary>角色 Id。</summary>
    public const string RoleId = "generate";

    /// <summary>预览序列源。</summary>
    public const string PreviewSource = "preview";

    /// <summary>组/通道摘要 JSON 源。</summary>
    public const string SnapshotSource = "snapshot";

    /// <summary>状态文本源。</summary>
    public const string StatusSource = "status";

    /// <summary>选中组名。</summary>
    public const string GroupNameSource = "groupName";

    /// <summary>选中组 ID。</summary>
    public const string GroupIdSource = "groupId";

    /// <summary>选中通道编号（1–64）。</summary>
    public const string ChannelNumberSource = "channelNumber";

    /// <summary>选中通道名。</summary>
    public const string ChannelNameSource = "channelName";

    /// <summary>选中通道波形种类键。</summary>
    public const string WaveformSource = "waveform";

    /// <summary>选中组基波频率（所有通道共享）。</summary>
    public const string FrequencySource = "frequency";

    /// <summary>选中组基波周期（由组频率派生）。</summary>
    public const string PeriodSource = "period";

    /// <summary>选中通道相位。</summary>
    public const string PhaseSource = "phase";

    /// <summary>选中通道偏移。</summary>
    public const string OffsetSource = "offset";

    /// <summary>选中通道幅值。</summary>
    public const string AmplitudeSource = "amplitude";

    /// <summary>选中通道每周期点数。</summary>
    public const string ResolutionSource = "resolution";

    /// <summary>选中通道占空比。</summary>
    public const string DutySource = "duty";

    /// <summary>选中通道载波频率。</summary>
    public const string CarrierFrequencySource = "carrierFrequency";

    /// <summary>选中通道调制比。</summary>
    public const string ModulationIndexSource = "modulationIndex";

    /// <summary>选中通道载波相位。</summary>
    public const string CarrierPhaseSource = "carrierPhase";

    /// <summary>选中通道启用开关。</summary>
    public const string ChannelEnabledSource = "channelEnabled";

    /// <summary>选中组启用开关。</summary>
    public const string GroupEnabledSource = "groupEnabled";

    /// <summary>一次性发送周期数。</summary>
    public const string OneShotPeriodsSource = "oneShotPeriods";

    /// <summary>连续发送开关。</summary>
    public const string ContinuousSource = "continuous";

    /// <summary>带参 UI 操作（JSON）。</summary>
    public const string ActionSource = "action";

    /// <summary>加组命令。</summary>
    public const string AddGroupCommand = "addGroup";

    /// <summary>删组命令。</summary>
    public const string RemoveGroupCommand = "removeGroup";

    /// <summary>加通道命令。</summary>
    public const string AddChannelCommand = "addChannel";

    /// <summary>删通道命令。</summary>
    public const string RemoveChannelCommand = "removeChannel";

    /// <summary>一次性发送命令。</summary>
    public const string SendOnceCommand = "sendOnce";

    /// <summary>连续发送命令（按钮，不再用开关）。</summary>
    public const string StartContinuousCommand = "startContinuous";

    /// <summary>停止命令。</summary>
    public const string StopCommand = "stop";

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(WaveformGeneratorModule).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(WaveformGeneratorModule).Assembly, key, args);

    /// <summary>本模块挑的提示角色（相对色，不是写死的红）。</summary>
    public const string AlertRole = ViewInkRoles.Danger;

    private readonly Dictionary<uint, GeneratorRuntime> _runtimes = [];
    private readonly Dictionary<IModuleTimer, GeneratorRuntime> _timerOwners = [];
    private readonly Dictionary<string, string> _lastText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldError> _fieldErrors = new(StringComparer.Ordinal);
    private readonly InputValidity _validity = new();
    private readonly byte[] _frame = new byte[WaveformFrameContract.SizeInBytes];

    private IModuleContext? _context;
    private GeneratorConfig _config = GeneratorConfig.CreateDefault();
    private bool _configLoaded;
    private bool _textSeeded;
    private bool _sending;
    private bool _continuous;
    private bool? _lastSendEnabled;
    private bool? _lastStopEnabled;
    private string? _lastStatusText;
    private bool _limited;
    private long _revision;
    private long _structureRevision;
    private long _dropped;
    private long _reportedDropped;
    private long _lastStatusPublish;
    private long _lastDroppedStatusPublish;
    private long _previewVersion;

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

        // 端口声明在 Views/WaveformGeneratorView.axaml 的 ModuleView.Ports 里。

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            WaveformFrameContract.TypeId,
            OutPort,
            Tr(WaveformGeneratorText.RoleGenerate)));

        DeclareUi(context);
        _context = context;
    }

    private static void DeclareUi(IModuleContext context)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            PreviewSource, ViewDataSourceKind.SeriesWindow));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(SnapshotSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));

        DeclareValue(context, GroupNameSource, ValueEditor.Text);
        DeclareValue(context, GroupIdSource, ValueEditor.Number);
        DeclareValue(context, ChannelNumberSource, ValueEditor.Number);
        DeclareValue(context, ChannelNameSource, ValueEditor.Text);
        DeclareValue(context, WaveformSource, ValueEditor.Enum, [
            new ValueChoice("sine", Tr(WaveformGeneratorText.Sine)),
            new ValueChoice("square", Tr(WaveformGeneratorText.Square)),
            new ValueChoice("triangle", Tr(WaveformGeneratorText.Triangle)),
            new ValueChoice("saddle", Tr(WaveformGeneratorText.Saddle)),
            new ValueChoice("spwm", "SPWM"),
            new ValueChoice("svpwm", "SVPWM"),
            new ValueChoice("noise", Tr(WaveformGeneratorText.Noise)),
        ]);
        DeclareValue(context, FrequencySource, ValueEditor.Number);
        DeclareValue(context, PeriodSource, ValueEditor.Number);
        DeclareValue(context, PhaseSource, ValueEditor.Number);
        DeclareValue(context, OffsetSource, ValueEditor.Number);
        DeclareValue(context, AmplitudeSource, ValueEditor.Number);
        DeclareValue(context, ResolutionSource, ValueEditor.Number);
        DeclareValue(context, DutySource, ValueEditor.Number);
        DeclareValue(context, CarrierFrequencySource, ValueEditor.Number);
        DeclareValue(context, ModulationIndexSource, ValueEditor.Number);
        DeclareValue(context, CarrierPhaseSource, ValueEditor.Number);
        DeclareValue(context, OneShotPeriodsSource, ValueEditor.Number);

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            ChannelEnabledSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            GroupEnabledSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            ContinuousSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));

        DeclareValue(context, ChannelEnabledSource, ValueEditor.Boolean);
        DeclareValue(context, GroupEnabledSource, ValueEditor.Boolean);
        DeclareValue(context, ContinuousSource, ValueEditor.Boolean);
        context.Declaration.DeclareInput(ActionSource);

        context.Declaration.DeclareCommand(AddGroupCommand, Tr(WaveformGeneratorText.Group));
        context.Declaration.DeclareCommand(RemoveGroupCommand, Tr(WaveformGeneratorText.Group));
        context.Declaration.DeclareCommand(AddChannelCommand, Tr(WaveformGeneratorText.Channel));
        context.Declaration.DeclareCommand(RemoveChannelCommand, Tr(WaveformGeneratorText.Channel));
        context.Declaration.DeclareCommand(SendOnceCommand, Tr(WaveformGeneratorText.Send));
        context.Declaration.DeclareCommand(StartContinuousCommand, Tr(WaveformGeneratorText.StartContinuous));
        context.Declaration.DeclareCommand(StopCommand, Tr(WaveformGeneratorText.Stop));
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
            Persistent = false,
        });

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

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

        // 逐键文本在视图状态里：只有输入事件（或首次加载）才回读；数据/会话/定时器拍不读字段。
        if (!_textSeeded || (reason & StepReason.Input) != 0)
        {
            _textSeeded = true;
            PollTextInputs(context);
        }

        SyncTimers(context);
    }

    /// <summary>测试/单步入口：处理收件箱并同步定时器（不启动实例线程）。</summary>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!_configLoaded)
        {
            LoadConfig(context);
        }

        Pump(context);
        _textSeeded = true;
        PollTextInputs(context);
        SyncTimers(context);
    }

    private static readonly string[] PolledTextSources =
    [
        GroupNameSource,
        GroupIdSource,
        ChannelNumberSource,
        ChannelNameSource,
        FrequencySource,
        PeriodSource,
        PhaseSource,
        OffsetSource,
        AmplitudeSource,
        ResolutionSource,
        DutySource,
        CarrierFrequencySource,
        ModulationIndexSource,
        CarrierPhaseSource,
        OneShotPeriodsSource,
    ];

    /// <summary>回读逐键写入的文本；与 HEX 转换器同一“内容变化即处理”模型。</summary>
    private void PollTextInputs(IModuleContext context)
    {
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
    }

    /// <inheritdoc />
    /// <remarks>“还原”会重置配置与值；下一拍重新读配置并发布。</remarks>
    public void OnConfigurationChanged() => _configLoaded = false;

    /// <inheritdoc />
    public void OnRemoved()
    {
        foreach (var timer in _timerOwners.Keys.ToArray())
        {
            timer.Stop();
            timer.Dispose();
        }

        _timerOwners.Clear();
        _runtimes.Clear();
    }

    private void LoadConfig(IModuleContext context)
    {
        _config = context.Configuration.Read<GeneratorConfig>();
        NormalizeConfig();
        _configLoaded = true;
        context.Configuration.Write(_config);
        RebuildRuntimes(context);
        PublishAll(context);
        ValidateAllInputs(context);
    }

    private void NormalizeConfig()
    {
        var groups = _config.Groups.ToList();
        if (groups.Count == 0)
        {
            groups.Add(GeneratorConfig.CreateGroup(1, 0));
        }

        if (groups.Count > GeneratorConfig.MaxGroups)
        {
            groups.RemoveRange(GeneratorConfig.MaxGroups, groups.Count - GeneratorConfig.MaxGroups);
        }

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            // 组号是用户编辑值：0/重复不自动改号；发送前校验会拒绝，由用户修正。
            var id = group.GroupId;

            var channels = group.Channels.ToList();
            if (channels.Count == 0)
            {
                channels.Add(GeneratorConfig.CreateChannel(0));
            }

            if (channels.Count > GeneratorConfig.MaxChannels)
            {
                channels.RemoveRange(GeneratorConfig.MaxChannels, channels.Count - GeneratorConfig.MaxChannels);
            }

            var legacyResolution = 0;
            foreach (var channel in channels)
            {
                if (channel.LegacyResolution is >= 2 and <= 4096
                    && channel.LegacyResolution > legacyResolution)
                {
                    legacyResolution = channel.LegacyResolution.Value;
                }
            }

            var resolution = legacyResolution > 0
                ? legacyResolution
                : group.Resolution is >= 2 and <= 4096
                    ? group.Resolution
                    : 100;

            for (var channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                var channel = channels[channelIndex];
                // 新字段缺失（旧配置）按列表位置补 1 基编号；显式 0/重复留给校验报错，不静默改号。
                var number = channel.ChannelNumber ?? (channelIndex + 1);
                var name = channel.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "CH" + (number is >= 1 and <= GeneratorConfig.MaxChannels
                        ? number
                        : channelIndex + 1).ToString(CultureInfo.InvariantCulture);
                }

                if (name.Length > GeneratorConfig.MaxNameLength)
                {
                    name = name[..GeneratorConfig.MaxNameLength];
                }

                var carrier = double.IsFinite(channel.CarrierFrequencyHz) && channel.CarrierFrequencyHz > 0
                    ? channel.CarrierFrequencyHz
                    : 100;
                var modulation = double.IsFinite(channel.ModulationIndex) ? channel.ModulationIndex : 0.8;
                var carrierPhase = double.IsFinite(channel.CarrierPhaseDegrees) ? channel.CarrierPhaseDegrees : 0;

                channels[channelIndex] = channel with
                {
                    ChannelNumber = number,
                    Name = name,
                    CarrierFrequencyHz = carrier,
                    ModulationIndex = modulation,
                    CarrierPhaseDegrees = carrierPhase,
                    LegacyResolution = null,
                };
            }

            // 旧配置把基波频率放在通道上；迁移到组频率后清空迁移字段，保存时不再写第二份。
            var legacyFrequency = group.Channels
                .Where(item => item.Enabled && item.LegacyFrequencyHz is > 0)
                .Select(item => item.LegacyFrequencyHz)
                .FirstOrDefault()
                ?? group.Channels
                    .Where(item => item.LegacyFrequencyHz is > 0)
                    .Select(item => item.LegacyFrequencyHz)
                    .FirstOrDefault();
            var frequency = legacyFrequency ?? group.FrequencyHz;
            if (!double.IsFinite(frequency) || frequency <= 0)
            {
                frequency = 10;
            }

            channels = channels
                .Select(item => item with { LegacyFrequencyHz = null })
                .ToList();

            var groupName = group.Name;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                groupName = "G" + (groupIndex + 1).ToString(CultureInfo.InvariantCulture);
            }

            if (groupName.Length > GeneratorConfig.MaxNameLength)
            {
                groupName = groupName[..GeneratorConfig.MaxNameLength];
            }

            groups[groupIndex] = group with
            {
                GroupId = id,
                Name = groupName,
                FrequencyHz = frequency,
                Resolution = resolution,
                Channels = channels,
            };
        }

        var activeGroup = Math.Clamp(_config.ActiveGroupIndex, 0, groups.Count - 1);
        // -1 = 组选中（显示组共有属性），>=0 = 通道选中。
        var activeChannel = Math.Clamp(
            _config.ActiveChannelIndex,
            -1,
            Math.Max(0, groups[activeGroup].Channels.Count - 1));

        _config = _config with
        {
            Groups = groups,
            ActiveGroupIndex = activeGroup,
            ActiveChannelIndex = activeChannel,
            OneShotPeriods = Math.Clamp(_config.OneShotPeriods, 1, 1_000_000),
        };
    }

    private void RebuildRuntimes(IModuleContext context)
    {
        var next = new Dictionary<uint, GeneratorRuntime>();

        foreach (var group in _config.Groups)
        {
            if (!_runtimes.TryGetValue(group.GroupId, out var runtime))
            {
                runtime = new GeneratorRuntime
                {
                    Config = group,
                    Random = new Random(unchecked((int)(group.GroupId * 2654435761u))),
                };

                runtime.Timer = context.Timers.Create();
                runtime.Timer.Tick += OnGroupTimer;
                _timerOwners[runtime.Timer] = runtime;
            }

            runtime.Config = group;
            next[group.GroupId] = runtime;
        }

        foreach (var (id, runtime) in _runtimes)
        {
            if (next.ContainsKey(id))
            {
                continue;
            }

            if (runtime.Timer is { } timer)
            {
                timer.Stop();
                timer.Dispose();
                _ = _timerOwners.Remove(timer);
            }
        }

        _runtimes.Clear();
        foreach (var (id, runtime) in next)
        {
            _runtimes[id] = runtime;
        }
    }

    private void SyncTimers(IModuleContext context)
    {
        _ = context;

        foreach (var runtime in _runtimes.Values)
        {
            if (runtime.Timer is not { } timer)
            {
                continue;
            }

            var rate = WaveformMath.GroupRate(runtime.Config);
            if (!runtime.Config.Enabled
                || !_sending
                || rate <= 0
                || (!_continuous && runtime.Remaining <= 0))
            {
                if (timer.IsEnabled)
                {
                    timer.Stop();
                }

                continue;
            }

            var interval = rate <= 1000
                ? TimeSpan.FromSeconds(1 / rate)
                : TimeSpan.FromMilliseconds(1);

            if (timer.Interval != interval)
            {
                timer.Interval = interval;
            }

            if (!timer.IsEnabled)
            {
                timer.Start();
            }
        }

        if (!_sending)
        {
            return;
        }

        var enabled = _runtimes.Values.Where(runtime => runtime.Config.Enabled).ToList();
        if (enabled.Count == 0
            || (!_continuous && enabled.All(runtime => runtime.Remaining <= 0))
            || enabled.All(runtime => WaveformMath.GroupRate(runtime.Config) <= 0))
        {
            StopSending(context);
        }
    }

    private void Pump(IModuleContext context)
    {
        // **先输入后命令**：刚改完参数就点“发送”时，发送前校验必须看到最新配置。
        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            OnInput(context, source, text);
        }

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            OnCommand(context, command);
        }
    }

    private void OnCommand(IModuleContext context, string command)
    {
        var revision = _structureRevision;

        switch (command)
        {
            case AddGroupCommand:
                AddGroup(context);
                break;
            case RemoveGroupCommand:
                RemoveGroup(context);
                break;
            case AddChannelCommand:
                AddChannel(context);
                break;
            case RemoveChannelCommand:
                RemoveChannel(context);
                break;
            case SendOnceCommand:
                _ = StartSending(context, continuous: false);
                break;
            case StartContinuousCommand:
                _ = StartSending(context, continuous: true);
                break;
            case StopCommand:
                StopSending(context);
                break;
        }

        // 结构真的变了才重算；失败动作的原因（上限/最后一个）不能被“就绪”覆盖。
        if (_structureRevision != revision)
        {
            ValidateAllInputs(context);
        }
    }

    private void OnInput(IModuleContext context, string source, string text)
    {
        switch (source)
        {
            case GroupNameSource:
                if (TryNonEmptyName(text, out var groupName))
                {
                    _structureRevision++;
                    UpdateSelectedGroup(context, group => group with { Name = groupName });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case GroupIdSource:
                if (uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId)
                    && groupId != 0
                    && IsGroupIdAvailable(groupId))
                {
                    _structureRevision++;
                    UpdateSelectedGroup(context, group => group with { GroupId = groupId });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ChannelNumberSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelNumber)
                    && channelNumber is >= 1 and <= GeneratorConfig.MaxChannels
                    && IsChannelNumberAvailable(channelNumber))
                {
                    _structureRevision++;
                    UpdateSelectedChannel(context, channel => channel with { ChannelNumber = channelNumber });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ChannelNameSource:
                if (TryNonEmptyName(text, out var channelName))
                {
                    _structureRevision++;
                    UpdateSelectedChannel(context, channel => channel with { Name = channelName });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case WaveformSource:
                if (WaveformMath.TryParseKind(text.Trim(), out var kind))
                {
                    UpdateSelectedChannel(context, channel => channel with { Kind = kind });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case FrequencySource:
                if (TryPositive(text, out var frequency))
                {
                    UpdateSelectedGroup(context, group => group with { FrequencyHz = frequency });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case PeriodSource:
                if (TryPositive(text, out var period))
                {
                    UpdateSelectedGroup(context, group => group with { FrequencyHz = 1 / period });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case PhaseSource:
                if (TryFinite(text, out var phase))
                {
                    UpdateSelectedChannel(context, channel => channel with { PhaseDegrees = phase });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case OffsetSource:
                if (TryFinite(text, out var offset))
                {
                    UpdateSelectedChannel(context, channel => channel with { Offset = offset });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case AmplitudeSource:
                if (TryFinite(text, out var amplitude))
                {
                    UpdateSelectedChannel(context, channel => channel with { Amplitude = amplitude });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ResolutionSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resolution)
                    && resolution is >= 2 and <= 4096)
                {
                    UpdateSelectedGroup(context, group => group with { Resolution = resolution });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case DutySource:
                if (TryFinite(text, out var duty) && duty is >= 0.01 and <= 0.99)
                {
                    UpdateSelectedChannel(context, channel => channel with { DutyCycle = duty });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case CarrierFrequencySource:
                if (TryPositive(text, out var carrierFrequency))
                {
                    UpdateSelectedChannel(context, channel => channel with { CarrierFrequencyHz = carrierFrequency });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ModulationIndexSource:
                if (TryFinite(text, out var modulation)
                    && IsModulationIndexValid(SelectedChannel?.Kind ?? WaveformKind.Sine, modulation))
                {
                    UpdateSelectedChannel(context, channel => channel with { ModulationIndex = modulation });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case CarrierPhaseSource:
                if (TryFinite(text, out var carrierPhase))
                {
                    UpdateSelectedChannel(context, channel => channel with { CarrierPhaseDegrees = carrierPhase });
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ChannelEnabledSource:
                if (bool.TryParse(text.Trim(), out var enabled))
                {
                    UpdateSelectedChannel(context, channel => channel with { Enabled = enabled });
                }

                break;

            case GroupEnabledSource:
                if (bool.TryParse(text.Trim(), out var groupEnabled))
                {
                    var groups = _config.Groups.ToList();
                    var activeIndex = Math.Clamp(_config.ActiveGroupIndex, 0, groups.Count - 1);
                    groups[activeIndex] = groups[activeIndex] with { Enabled = groupEnabled };
                    _config = _config with { Groups = groups };
                    _structureRevision++;
                    ApplyConfig(context);
                    if (!groupEnabled && _sending && _config.Groups.All(group => !group.Enabled))
                    {
                        StopSending(context);
                    }
                }

                break;

            case OneShotPeriodsSource:
                if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var periods)
                    && periods is >= 1 and <= 1_000_000)
                {
                    _config = _config with { OneShotPeriods = periods };
                    ApplyConfig(context);
                }
                else
                {
                    PublishStatus(Tr(WaveformGeneratorText.Invalid));
                }

                break;

            case ContinuousSource:
                if (bool.TryParse(text.Trim(), out var continuous))
                {
                    if (continuous)
                    {
                        _ = StartSending(context, continuous: true);
                    }
                    else if (_continuous)
                    {
                        StopSending(context);
                    }
                }

                // 启停自己发布运行/停止状态与按钮；外层不再用“就绪”把它覆盖掉。
                return;

            case ActionSource:
                // OnAction 只在结构/选择真的变化时结算；失败动作的原因要留着。
                OnAction(context, text);
                return;
        }

        // 字段级红框统一按“当前编辑文本”结算：合法值已应用；非法值保持原配置并标红。
        ValidateAllInputs(context);
    }

    /// <summary>按当前输入文本重算所有字段的红框；先收集、后一次性应用，避免清除顺序互相覆盖。</summary>
    private void ValidateAllInputs(IModuleContext context)
    {
        var errors = new Dictionary<string, FieldError>(StringComparer.Ordinal);
        var channelSelected = SelectedChannel is not null;
        var group = SelectedGroup;
        var channel = SelectedChannel;

        string Read(string source) => context.Values.ReadText(source) ?? string.Empty;

        void Record(string source, string message, ValueValidityOrigin origin)
        {
            // 同一字段 Module 规则优先于格式；格式结论不能把已成立的模块错误降级。
            if (errors.TryGetValue(source, out var existing)
                && existing.Origin == ValueValidityOrigin.Module
                && origin == ValueValidityOrigin.Format)
            {
                return;
            }

            errors[source] = new FieldError(message, origin);
        }

        void Check(string source, bool ok, string message)
        {
            if (!ok)
            {
                Record(source, message, ValueValidityOrigin.Format);
            }
        }

        Check(GroupNameSource, TryNonEmptyName(Read(GroupNameSource), out _), Tr(WaveformGeneratorText.InvalidName));

        var groupIdText = Read(GroupIdSource);
        var groupIdOk = uint.TryParse(
                groupIdText.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var groupId)
            && groupId != 0;
        Check(GroupIdSource, groupIdOk, Tr(WaveformGeneratorText.InvalidGroupId));
        if (groupIdOk && !IsGroupIdAvailable(groupId))
        {
            Record(GroupIdSource, Tr(WaveformGeneratorText.DuplicateGroupId, groupId), ValueValidityOrigin.Module);
        }

        Check(
            FrequencySource,
            TryPositive(Read(FrequencySource), out _),
            Tr(WaveformGeneratorText.InvalidNumber));
        Check(
            PeriodSource,
            TryPositive(Read(PeriodSource), out _),
            Tr(WaveformGeneratorText.InvalidNumber));
        Check(
            ResolutionSource,
            int.TryParse(Read(ResolutionSource).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resolution)
            && resolution is >= 2 and <= 4096,
            Tr(WaveformGeneratorText.InvalidNumber));
        Check(
            OneShotPeriodsSource,
            int.TryParse(Read(OneShotPeriodsSource).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var periods)
            && periods is >= 1 and <= 1_000_000,
            Tr(WaveformGeneratorText.InvalidNumber));

        if (channelSelected && channel is not null)
        {
            var channelNumberText = Read(ChannelNumberSource);
            var numberOk = int.TryParse(
                    channelNumberText.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number)
                && number is >= 1 and <= GeneratorConfig.MaxChannels;
            Check(ChannelNumberSource, numberOk, Tr(WaveformGeneratorText.InvalidChannelNumber));
            if (numberOk && !IsChannelNumberAvailable(number))
            {
                Record(
                    ChannelNumberSource,
                    Tr(WaveformGeneratorText.DuplicateChannelNumber, group.Name, number),
                    ValueValidityOrigin.Module);
            }

            Check(ChannelNameSource, TryNonEmptyName(Read(ChannelNameSource), out _), Tr(WaveformGeneratorText.InvalidName));
            Check(PhaseSource, TryFinite(Read(PhaseSource), out _), Tr(WaveformGeneratorText.InvalidNumber));
            Check(OffsetSource, TryFinite(Read(OffsetSource), out _), Tr(WaveformGeneratorText.InvalidNumber));
            Check(AmplitudeSource, TryFinite(Read(AmplitudeSource), out _), Tr(WaveformGeneratorText.InvalidNumber));
            Check(
                DutySource,
                TryFinite(Read(DutySource), out var duty) && duty is >= 0.01 and <= 0.99,
                Tr(WaveformGeneratorText.InvalidNumber));
            Check(
                CarrierFrequencySource,
                TryPositive(Read(CarrierFrequencySource), out _),
                Tr(WaveformGeneratorText.InvalidNumber));
            Check(
                ModulationIndexSource,
                TryFinite(Read(ModulationIndexSource), out var modulation)
                && IsModulationIndexValid(channel.Kind, modulation),
                Tr(WaveformGeneratorText.InvalidNumber));
            Check(
                CarrierPhaseSource,
                TryFinite(Read(CarrierPhaseSource), out _),
                Tr(WaveformGeneratorText.InvalidNumber));

            // Nyquist 用**当前文本**的解析值算；任一字段本身不合法就不叠一条关于旧值的规则错误。
            if (group.Enabled
                && channel.Enabled
                && WaveformMath.IsModulated(channel.Kind)
                && TryPositive(Read(FrequencySource), out var frequency)
                && int.TryParse(
                    Read(ResolutionSource).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var rateResolution)
                && rateResolution is >= 2 and <= 4096
                && TryPositive(Read(CarrierFrequencySource), out var carrierFrequency))
            {
                var rate = rateResolution * frequency;

                if (2 * carrierFrequency > rate)
                {
                    var message = Tr(
                        WaveformGeneratorText.CarrierNyquist,
                        group.Name,
                        channel.Name,
                        rate,
                        carrierFrequency);
                    Record(CarrierFrequencySource, message, ValueValidityOrigin.Module);
                    Record(FrequencySource, message, ValueValidityOrigin.Module);
                    Record(ResolutionSource, message, ValueValidityOrigin.Module);
                }
            }
        }

        _fieldErrors.Clear();
        foreach (var source in PolledTextSources)
        {
            if (!channelSelected && IsChannelField(source))
            {
                _validity.Set(context, source, false);
                continue;
            }

            if (errors.TryGetValue(source, out var error))
            {
                _fieldErrors[source] = error;
                _validity.Set(context, source, true, error.Message, error.Origin);
            }
            else
            {
                _validity.Set(context, source, false);
            }
        }

        PublishSendControls(context);
    }

    private static bool IsChannelField(string source) =>
        source is ChannelNumberSource
            or ChannelNameSource
            or PhaseSource
            or OffsetSource
            or AmplitudeSource
            or DutySource
            or CarrierFrequencySource
            or ModulationIndexSource
            or CarrierPhaseSource;

    private void OnAction(IModuleContext context, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("op", out var operation))
            {
                PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
                return;
            }

            var op = operation.GetString();
            var index = root.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : 0;
            var selectionChanged = false;
            var discarded = false;
            var revisionBefore = _structureRevision;

            switch (op)
            {
                case "selectGroup":
                {
                    var groupIndex = ResolveGroupIndex(root, allowId: true);
                    if (groupIndex >= 0 && groupIndex < _config.Groups.Count)
                    {
                        selectionChanged = true;
                        discarded = _validity.HasAnyInvalid;
                        _config = _config with
                        {
                            ActiveGroupIndex = groupIndex,
                            ActiveChannelIndex = -1,
                        };
                        _structureRevision++;
                        ApplyConfig(context);
                    }

                    break;
                }

                case "selectChannel":
                {
                    var groupIndex = ResolveGroupIndex(root, allowId: false);
                    if (groupIndex >= 0
                        && groupIndex < _config.Groups.Count
                        && index >= 0
                        && index < _config.Groups[groupIndex].Channels.Count)
                    {
                        selectionChanged = true;
                        discarded = _validity.HasAnyInvalid;
                        _config = _config with
                        {
                            ActiveGroupIndex = groupIndex,
                            ActiveChannelIndex = index,
                        };
                        _structureRevision++;
                        ApplyConfig(context);
                    }

                    break;
                }

                case "addGroup":
                    AddGroup(context);
                    break;

                case "removeGroup":
                    RemoveGroup(context, ResolveGroupIndex(root, allowId: true));
                    break;

                case "addChannel":
                    AddChannel(context, ResolveGroupIndex(root, allowId: false));
                    break;

                case "removeChannel":
                    RemoveChannel(context, ResolveGroupIndex(root, allowId: false), index);
                    break;

                case "setGroupEnabled":
                    if (root.TryGetProperty("enabled", out var groupEnabledValue))
                    {
                        SetGroupEnabled(
                            context,
                            ResolveGroupIndex(root, allowId: true),
                            groupEnabledValue.GetBoolean());
                    }

                    break;

                case "setChannelEnabled":
                    if (root.TryGetProperty("enabled", out var channelEnabledValue))
                    {
                        SetChannelEnabled(
                            context,
                            ResolveGroupIndex(root, allowId: false),
                            index,
                            channelEnabledValue.GetBoolean());
                    }

                    break;
            }

            // 结构/选择真的变了才重算；失败动作（上限/最后一个）的原因不能被“就绪”覆盖。
            if (selectionChanged || _structureRevision != revisionBefore)
            {
                ValidateAllInputs(context);
            }

            if (selectionChanged)
            {
                PublishDiscardedIfNeeded(context, discarded);
            }
        }
        catch (JsonException)
        {
            PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
        }
    }

    private int ResolveGroupIndex(JsonElement root, bool allowId)
    {
        if (allowId
            && root.TryGetProperty("id", out var idElement)
            && idElement.TryGetUInt32(out var id))
        {
            for (var index = 0; index < _config.Groups.Count; index++)
            {
                if (_config.Groups[index].GroupId == id)
                {
                    return index;
                }
            }
        }

        if (root.TryGetProperty("groupIndex", out var groupElement)
            && groupElement.TryGetInt32(out var groupIndex))
        {
            return groupIndex;
        }

        if (allowId
            && root.TryGetProperty("index", out var indexElement)
            && indexElement.TryGetInt32(out var actionIndex))
        {
            return actionIndex;
        }

        return Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1);
    }

    private void SetGroupEnabled(IModuleContext context, int groupIndex, bool enabled)
    {
        if (groupIndex < 0 || groupIndex >= _config.Groups.Count)
        {
            return;
        }

        var groups = _config.Groups.ToList();
        groups[groupIndex] = groups[groupIndex] with { Enabled = enabled };
        _config = _config with { Groups = groups };
        _structureRevision++;
        ApplyConfig(context);
        if (!enabled && _sending && _config.Groups.All(group => !group.Enabled))
        {
            StopSending(context);
        }
    }

    private void SetChannelEnabled(IModuleContext context, int groupIndex, int channelIndex, bool enabled)
    {
        if (groupIndex < 0 || groupIndex >= _config.Groups.Count)
        {
            return;
        }

        var groups = _config.Groups.ToList();
        var group = groups[groupIndex];
        if (channelIndex < 0 || channelIndex >= group.Channels.Count)
        {
            return;
        }

        var channels = group.Channels.ToList();
        channels[channelIndex] = channels[channelIndex] with { Enabled = enabled };
        groups[groupIndex] = group with { Channels = channels };
        _config = _config with { Groups = groups };
        _structureRevision++;
        ApplyConfig(context);
    }

    private void AddGroup(IModuleContext context)
    {
        if (_config.Groups.Count >= GeneratorConfig.MaxGroups)
        {
            PublishStatus(Tr(WaveformGeneratorText.GroupLimit, GeneratorConfig.MaxGroups));
            return;
        }

        StopForStructureChange(context);
        var id = NextGroupId();
        var groups = _config.Groups.ToList();
        groups.Add(GeneratorConfig.CreateGroup(id, groups.Count));
        _config = _config with
        {
            Groups = groups,
            ActiveGroupIndex = groups.Count - 1,
            ActiveChannelIndex = -1,
        };

        _structureRevision++;
        ApplyConfig(context);
    }

    private void RemoveGroup(IModuleContext context, int? groupIndex = null)
    {
        if (_config.Groups.Count <= 1)
        {
            PublishStatus(Tr(WaveformGeneratorText.LastGroup));
            return;
        }

        var removeIndex = groupIndex ?? _config.ActiveGroupIndex;
        if (removeIndex < 0 || removeIndex >= _config.Groups.Count)
        {
            PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
            return;
        }

        StopForStructureChange(context);
        var groups = _config.Groups.ToList();
        groups.RemoveAt(removeIndex);
        _config = _config with
        {
            Groups = groups,
            ActiveGroupIndex = Math.Clamp(removeIndex, 0, groups.Count - 1),
            ActiveChannelIndex = -1,
        };

        _structureRevision++;
        ApplyConfig(context);
    }

    private void AddChannel(IModuleContext context, int? groupIndex = null)
    {
        var targetIndex = groupIndex ?? Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1);
        if (targetIndex < 0 || targetIndex >= _config.Groups.Count)
        {
            PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
            return;
        }

        var group = _config.Groups[targetIndex];
        if (group.Channels.Count >= GeneratorConfig.MaxChannels)
        {
            PublishStatus(Tr(WaveformGeneratorText.ChannelLimit, GeneratorConfig.MaxChannels));
            return;
        }

        StopForStructureChange(context);
        var channels = group.Channels.ToList();
        channels.Add(GeneratorConfig.CreateChannel(NextChannelNumber(group)));
        var groups = _config.Groups.ToList();
        groups[targetIndex] = group with { Channels = channels };
        _config = _config with
        {
            Groups = groups,
            ActiveGroupIndex = targetIndex,
            ActiveChannelIndex = channels.Count - 1,
        };
        _structureRevision++;
        ApplyConfig(context);
    }

    private void RemoveChannel(IModuleContext context, int? groupIndex = null, int? channelIndex = null)
    {
        var targetGroupIndex = groupIndex ?? Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1);
        if (targetGroupIndex < 0 || targetGroupIndex >= _config.Groups.Count)
        {
            PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
            return;
        }

        var group = _config.Groups[targetGroupIndex];
        if (group.Channels.Count <= 1)
        {
            PublishStatus(Tr(WaveformGeneratorText.LastChannel));
            return;
        }

        var removeIndex = channelIndex ?? _config.ActiveChannelIndex;
        if (removeIndex < 0 || removeIndex >= group.Channels.Count)
        {
            PublishStatus(Tr(WaveformGeneratorText.ActionInvalid));
            return;
        }

        StopForStructureChange(context);
        var channels = group.Channels.ToList();
        channels.RemoveAt(removeIndex);

        // 删除后保持“有通道可编辑”：删活动通道选同位置/新的末尾；删其之前的通道则活动索引前移。
        var activeChannel = targetGroupIndex == _config.ActiveGroupIndex
            ? _config.ActiveChannelIndex
            : -1;
        if (targetGroupIndex == _config.ActiveGroupIndex && activeChannel >= 0)
        {
            if (activeChannel == removeIndex)
            {
                activeChannel = Math.Min(removeIndex, channels.Count - 1);
            }
            else if (activeChannel > removeIndex)
            {
                activeChannel--;
            }
        }

        activeChannel = Math.Clamp(activeChannel, -1, channels.Count - 1);
        var groups = _config.Groups.ToList();
        groups[targetGroupIndex] = group with { Channels = channels };
        _config = _config with
        {
            Groups = groups,
            ActiveGroupIndex = targetGroupIndex,
            ActiveChannelIndex = activeChannel,
        };
        _structureRevision++;
        ApplyConfig(context);
    }

    private bool IsGroupIdAvailable(uint groupId)
    {
        var activeIndex = Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1);
        for (var index = 0; index < _config.Groups.Count; index++)
        {
            if (index != activeIndex && _config.Groups[index].GroupId == groupId)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsChannelNumberAvailable(int channelNumber)
    {
        var group = SelectedGroup;
        if (group is null)
        {
            return false;
        }

        var activeIndex = Math.Clamp(_config.ActiveChannelIndex, 0, group.Channels.Count - 1);
        for (var index = 0; index < group.Channels.Count; index++)
        {
            if (index != activeIndex && group.Channels[index].ChannelNumber == channelNumber)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidChannelNumbers(GeneratorGroupConfig group) =>
        TryValidateChannelNumbers(group, out _);

    private static bool TryValidateChannelNumbers(GeneratorGroupConfig group, out string? reason)
    {
        var used = new HashSet<int>();
        foreach (var channel in group.Channels)
        {
            if (channel.ChannelNumber is not int number
                || number is < 1 or > GeneratorConfig.MaxChannels)
            {
                reason = Tr(WaveformGeneratorText.InvalidChannelNumber);
                return false;
            }

            if (!used.Add(number))
            {
                reason = Tr(WaveformGeneratorText.DuplicateChannelNumber, group.Name, number);
                return false;
            }
        }

        reason = null;
        return true;
    }

    private uint NextGroupId()
    {
        var candidate = 1u;
        while (_config.Groups.Any(group => group.GroupId == candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static int NextChannelNumber(GeneratorGroupConfig group)
    {
        var used = new HashSet<int>();
        foreach (var channel in group.Channels)
        {
            if (channel.ChannelNumber is int number)
            {
                _ = used.Add(number);
            }
        }

        for (var candidate = 1; candidate <= GeneratorConfig.MaxChannels; candidate++)
        {
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return GeneratorConfig.MaxChannels;
    }

    /// <summary>无副作用的发送前校验；按钮闸门与实际发送共用同一判据。</summary>
    private bool TryValidateForSending(out string? reason, out double totalRate, out int batch)
    {
        reason = null;
        totalRate = 0;
        batch = 1;

        if (_config.OneShotPeriods is < 1 or > 1_000_000)
        {
            reason = Tr(WaveformGeneratorText.Invalid);
            return false;
        }

        var groupIds = new HashSet<uint>();
        foreach (var group in _config.Groups)
        {
            if (!group.Enabled)
            {
                continue;
            }

            if (group.GroupId == 0)
            {
                reason = Tr(WaveformGeneratorText.InvalidGroupId);
                return false;
            }

            if (!groupIds.Add(group.GroupId))
            {
                reason = Tr(WaveformGeneratorText.DuplicateGroupId, group.GroupId);
                return false;
            }

            if (!TryValidateChannelNumbers(group, out reason))
            {
                return false;
            }

            if (group.Resolution is < 2 or > 4096)
            {
                reason = Tr(WaveformGeneratorText.InvalidResolution, group.Name);
                return false;
            }

            if (!double.IsFinite(group.FrequencyHz) || group.FrequencyHz <= 0)
            {
                reason = Tr(WaveformGeneratorText.InvalidFrequency, group.Name);
                return false;
            }

            var rate = WaveformMath.GroupRate(group);
            if (rate <= 0)
            {
                continue;
            }

            if (rate > GeneratorConfig.MaxGroupRateHz)
            {
                reason = Tr(WaveformGeneratorText.RateUnsupported);
                return false;
            }

            foreach (var channel in group.Channels)
            {
                if (!channel.Enabled)
                {
                    continue;
                }

                if (!double.IsFinite(channel.CarrierFrequencyHz)
                    || channel.CarrierFrequencyHz <= 0
                    || !IsModulationIndexValid(channel.Kind, channel.ModulationIndex))
                {
                    reason = Tr(WaveformGeneratorText.InvalidCarrier, channel.Name);
                    return false;
                }

                if (WaveformMath.IsModulated(channel.Kind)
                    && 2 * channel.CarrierFrequencyHz > rate)
                {
                    reason = Tr(
                        WaveformGeneratorText.CarrierNyquist,
                        group.Name,
                        channel.Name,
                        rate,
                        channel.CarrierFrequencyHz);
                    return false;
                }
            }

            totalRate += rate;
            if (rate > 1000)
            {
                batch = Math.Max(batch, (int)Math.Floor(rate * 0.001));
            }
        }

        if (totalRate <= 0)
        {
            reason = Tr(WaveformGeneratorText.NoEnabledChannels);
            return false;
        }

        if (totalRate > GeneratorConfig.MaxTotalRateHz)
        {
            reason = Tr(WaveformGeneratorText.RateUnsupported);
            return false;
        }

        return true;
    }

    private bool StartSending(IModuleContext context, bool continuous)
    {
        // 发动作前先结算一次字段：用户刚改完就点发送时，状态与按钮必须基于最新文本。
        ValidateAllInputs(context);
        RebuildRuntimes(context);

        if (!TryValidateForSending(out _, out _, out var batch))
        {
            // 未就绪的原因已由 ValidateAllInputs → PublishSendControls 写进状态；不重复写。
            return false;
        }

        foreach (var runtime in _runtimes.Values)
        {
            if (!runtime.Config.Enabled)
            {
                runtime.Remaining = 0;
                runtime.PlannedFrames = 0;
                runtime.Due = 0;
                continue;
            }

            var frames = continuous
                ? -1
                : WaveformMath.OneShotFrames(runtime.Config, _config.OneShotPeriods);
            runtime.Remaining = frames;
            runtime.PlannedFrames = continuous ? 0 : frames;
            runtime.SentFrames = 0;
            runtime.Due = 0;
            runtime.Random = new Random(unchecked((int)(runtime.Config.GroupId * 2654435761u)));
        }

        StopAllTimers();
        _sending = true;
        _continuous = continuous;
        _limited = batch > 1;
        PublishSendControls(context);
        SyncTimers(context);
        PublishBool(context, ContinuousSource, continuous);
        SetInput(context, ContinuousSource, continuous ? "true" : "false");
        PublishSnapshot(context);
        PublishStatus(_limited
            ? Tr(WaveformGeneratorText.Batch, batch)
            : Tr(WaveformGeneratorText.Running));
        return true;
    }

    private void StopSending(IModuleContext context)
    {
        _sending = false;
        _continuous = false;
        PublishSendControls(context);
        StopAllTimers();
        PublishBool(context, ContinuousSource, false);
        SetInput(context, ContinuousSource, "false");
        PublishSnapshot(context);
        PublishStatus(Tr(WaveformGeneratorText.Stopped));
    }

    private void StopForStructureChange(IModuleContext context)
    {
        if (!_sending)
        {
            return;
        }

        StopSending(context);
        PublishStatus(Tr(WaveformGeneratorText.StructureStopped));
    }

    private void StopAllTimers()
    {
        foreach (var runtime in _runtimes.Values)
        {
            runtime.Timer?.Stop();
        }
    }

    private void OnGroupTimer(object? sender, EventArgs e)
    {
        if (!_sending || _context is not { } context || sender is not IModuleTimer timer)
        {
            return;
        }

        if (!_timerOwners.TryGetValue(timer, out var runtime) || !runtime.Config.Enabled)
        {
            timer.Stop();
            return;
        }

        var rate = WaveformMath.GroupRate(runtime.Config);
        if (rate <= 0)
        {
            timer.Stop();
            return;
        }

        var count = 1;
        if (rate > 1000)
        {
            runtime.Due += rate * 0.001;
            count = (int)Math.Floor(runtime.Due);
            runtime.Due -= count;
        }

        if (!_continuous)
        {
            if (runtime.Remaining <= 0)
            {
                timer.Stop();
                return;
            }

            count = (int)Math.Min(count, runtime.Remaining);
        }

        if (count <= 0)
        {
            return;
        }

        if (count > 1024)
        {
            count = 1024;
            runtime.Due = 0;
            _limited = true;
        }

        for (var frameIndex = 0; frameIndex < count; frameIndex++)
        {
            SendFrame(runtime, rate, context);
            runtime.Sequence++;
            runtime.SentFrames++;
            if (!_continuous)
            {
                runtime.Remaining--;
            }
        }

        if (!_continuous && runtime.Remaining <= 0)
        {
            timer.Stop();
        }

        if (!_continuous
            && _runtimes.Values
                .Where(item => item.Config.Enabled)
                .All(item => item.Remaining <= 0))
        {
            StopSending(context);
            return;
        }

        var now = Environment.TickCount64;
        if (_dropped > _reportedDropped && now - _lastDroppedStatusPublish >= 500)
        {
            _lastDroppedStatusPublish = now;
            _reportedDropped = _dropped;
            PublishStatus(Tr(WaveformGeneratorText.Dropped, _dropped));
        }
        else if (_limited && now - _lastStatusPublish >= 500)
        {
            _lastStatusPublish = now;
            PublishStatus(Tr(WaveformGeneratorText.Batch, Math.Max(1, count)));
        }
    }

    private void SendFrame(GeneratorRuntime runtime, double rate, IModuleContext context)
    {
        var group = runtime.Config;
        var channelCount = group.Channels
            .Select(channel => channel.ChannelNumber ?? 0)
            .DefaultIfEmpty(1)
            .Max();
        channelCount = Math.Clamp(channelCount, 1, WaveformFrameContract.MaxChannels);
        var time = (runtime.SentFrames / rate);
        var mask = 0UL;

        // 通道号是帧槽位号：编号 n 的样本必须落在 samples[n-1]。
        Array.Clear(runtime.Samples, 0, channelCount);

        foreach (var channel in group.Channels)
        {
            if (channel.ChannelNumber is not int number
                || number is < 1 or > WaveformFrameContract.MaxChannels)
            {
                continue;
            }

            var index = number - 1;
            if (index >= channelCount)
            {
                continue;
            }

            if (channel.Enabled)
            {
                runtime.Samples[index] = WaveformMath.Sample(channel, group.FrequencyHz, time, runtime.Random);
                mask |= 1UL << index;
            }
        }

        WaveformFrameWriter.Write(
            _frame,
            group.GroupId,
            runtime.Sequence,
            (float)rate,
            channelCount,
            mask,
            runtime.Samples);

        if (!context.Ports.TrySend(OutPort, _frame))
        {
            _dropped++;
        }
    }

    private void UpdateSelectedGroup(IModuleContext context, Func<GeneratorGroupConfig, GeneratorGroupConfig> update)
    {
        var groups = _config.Groups.ToList();
        var index = Math.Clamp(_config.ActiveGroupIndex, 0, groups.Count - 1);
        groups[index] = update(groups[index]);
        _config = _config with { Groups = groups };
        ApplyConfig(context);
    }

    private void UpdateSelectedChannel(IModuleContext context, Func<GeneratorChannelConfig, GeneratorChannelConfig> update)
    {
        if (_config.ActiveChannelIndex < 0)
        {
            return;
        }

        var groups = _config.Groups.ToList();
        var groupIndex = Math.Clamp(_config.ActiveGroupIndex, 0, groups.Count - 1);
        var group = groups[groupIndex];
        var channels = group.Channels.ToList();
        var channelIndex = Math.Clamp(_config.ActiveChannelIndex, 0, channels.Count - 1);
        channels[channelIndex] = update(channels[channelIndex]);
        groups[groupIndex] = group with { Channels = channels };
        _config = _config with { Groups = groups };
        ApplyConfig(context);
    }

    private void ApplyConfig(IModuleContext context)
    {
        context.Configuration.Write(_config);
        RebuildRuntimes(context);
        PublishAll(context);
        SyncTimers(context);
    }

    private GeneratorGroupConfig SelectedGroup =>
        _config.Groups[Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1)];

    private GeneratorChannelConfig? SelectedChannel
    {
        get
        {
            if (_config.ActiveChannelIndex < 0)
            {
                return null;
            }

            var group = SelectedGroup;
            return group.Channels.Count == 0
                ? null
                : group.Channels[Math.Clamp(_config.ActiveChannelIndex, 0, group.Channels.Count - 1)];
        }
    }

    private void PublishAll(IModuleContext context)
    {
        PublishFields(context);
        PublishSnapshot(context);
        PublishPreview(context);
        PublishBool(context, ContinuousSource, _continuous);
        PublishBool(context, GroupEnabledSource, SelectedGroup.Enabled);
        SetInput(context, ContinuousSource, _continuous ? "true" : "false");
        SetInput(context, GroupEnabledSource, SelectedGroup.Enabled ? "true" : "false");
    }

    /// <summary>切换选择后，若旧设置的错误值已被丢弃而新对象无错，补一句“未应用”。</summary>
    private void PublishDiscardedIfNeeded(IModuleContext context, bool discarded)
    {
        if (!discarded || _validity.HasAnyInvalid)
        {
            return;
        }

        _ = TryValidateForSending(out var reason, out _, out _);
        if (reason is null)
        {
            PublishStatus(Tr(WaveformGeneratorText.DiscardedInvalid));
        }
    }

    /// <summary>发送命令闸门：空闲且无字段错误、发送前校验通过时可用；发送中只可停止。</summary>
    private void PublishSendControls(IModuleContext context)
    {
        var gateOk = TryValidateForSending(out var gateReason, out _, out _);
        var hasErrors = _fieldErrors.Count > 0;
        var canStart = !_sending && !hasErrors && gateOk;

        // 发送中不覆盖运行状态；未发送时按“Module 字段错误 → 发送前校验 → 仅格式错误 → 就绪”取一句。
        if (!_sending)
        {
            var status = FirstModuleError() ?? gateReason;
            if (status is null)
            {
                status = hasErrors
                    ? Tr(WaveformGeneratorText.BlockedByFields)
                    : Tr(WaveformGeneratorText.Ready);
            }

            PublishStatus(status);
        }

        if (_lastSendEnabled != canStart)
        {
            _lastSendEnabled = canStart;
            context.Appearance.SetEnabled(StartContinuousCommand, canStart);
            context.Appearance.SetEnabled(SendOnceCommand, canStart);
        }

        // 停止只属于“正在发送”；空闲但配置非法时不能把停止点亮。
        var canStop = _sending;
        if (_lastStopEnabled != canStop)
        {
            _lastStopEnabled = canStop;
            context.Appearance.SetEnabled(StopCommand, canStop);
        }
    }

    /// <summary>按固定字段顺序取第一条模块规则错误；不依赖字典/集合的迭代顺序。</summary>
    private string? FirstModuleError()
    {
        foreach (var source in PolledTextSources)
        {
            if (_fieldErrors.TryGetValue(source, out var error)
                && error.Origin == ValueValidityOrigin.Module)
            {
                return error.Message;
            }
        }

        return null;
    }

    private void PublishFields(IModuleContext context)
    {
        var group = SelectedGroup;
        var channel = SelectedChannel;

        // 这些是**可编辑值**，不是只读显示面；必须走 Values，不能写 Display。
        // 写 Display 会让输入框保持旧值，随后 PollTextInputs 再把旧值套回当前通道 ——
        // 表现就是“改一个输入框，所有通道参数一起变”。
        SetInput(context, GroupNameSource, group.Name);
        SetInput(context, GroupIdSource, group.GroupId.ToString(CultureInfo.InvariantCulture));
        SetInput(context, FrequencySource, Format(group.FrequencyHz));
        SetInput(context, PeriodSource, Format(group.FrequencyHz > 0 ? 1 / group.FrequencyHz : 0));
        SetInput(context, ResolutionSource, group.Resolution.ToString(CultureInfo.InvariantCulture));
        SetInput(context, OneShotPeriodsSource, _config.OneShotPeriods.ToString(CultureInfo.InvariantCulture));

        if (channel is null)
        {
            return;
        }

        SetInput(context, ChannelNumberSource, (channel.ChannelNumber ?? 0).ToString(CultureInfo.InvariantCulture));
        SetInput(context, ChannelNameSource, channel.Name);
        SetInput(context, WaveformSource, WaveformMath.KindKey(channel.Kind));
        SetInput(context, PhaseSource, Format(channel.PhaseDegrees));
        SetInput(context, OffsetSource, Format(channel.Offset));
        SetInput(context, AmplitudeSource, Format(channel.Amplitude));
        SetInput(context, DutySource, Format(channel.DutyCycle));
        SetInput(context, CarrierFrequencySource, Format(channel.CarrierFrequencyHz));
        SetInput(context, ModulationIndexSource, Format(channel.ModulationIndex));
        SetInput(context, CarrierPhaseSource, Format(channel.CarrierPhaseDegrees));
        PublishBool(context, ChannelEnabledSource, channel.Enabled);
        PublishBool(context, GroupEnabledSource, group.Enabled);
        SetInput(context, ChannelEnabledSource, channel.Enabled ? "true" : "false");
        SetInput(context, GroupEnabledSource, group.Enabled ? "true" : "false");
    }

    private void PublishSnapshot(IModuleContext context)
    {
        var activeIndex = Math.Clamp(_config.ActiveGroupIndex, 0, _config.Groups.Count - 1);
        var activeGroup = _config.Groups[activeIndex];
        var activeRuntime = _runtimes.Values.FirstOrDefault(item => item.Config.GroupId == activeGroup.GroupId);
        var activePlanned = _sending && !_continuous && activeRuntime is not null
            ? activeRuntime.PlannedFrames
            : WaveformMath.OneShotFrames(activeGroup, _config.OneShotPeriods);
        var estimatedFrames = activeGroup.Enabled ? activePlanned : 0;

        long totalFrames = 0;
        foreach (var group in _config.Groups)
        {
            if (!group.Enabled)
            {
                continue;
            }

            var runtime = _runtimes.Values.FirstOrDefault(item => item.Config.GroupId == group.GroupId);
            totalFrames += _sending && !_continuous && runtime is not null
                ? runtime.PlannedFrames
                : WaveformMath.OneShotFrames(group, _config.OneShotPeriods);
        }

        var snapshot = new
        {
            revision = ++_revision,
            structureRevision = _structureRevision,
            groups = _config.Groups.Select((group, index) => new
            {
                index,
                id = group.GroupId,
                name = group.Name,
                enabled = group.Enabled,
                frequency = group.FrequencyHz,
                resolution = group.Resolution,
                rate = WaveformMath.GroupRate(group),
                active = index == activeIndex,
                channels = group.Channels.Select((channel, channelIndex) => new
                {
                    index = channelIndex,
                    number = channel.ChannelNumber ?? 0,
                    name = channel.Name,
                    kind = WaveformMath.KindKey(channel.Kind),
                    enabled = channel.Enabled,
                    phase = channel.PhaseDegrees,
                    offset = channel.Offset,
                    amplitude = channel.Amplitude,
                    duty = channel.DutyCycle,
                    carrierFrequency = channel.CarrierFrequencyHz,
                    modulationIndex = channel.ModulationIndex,
                    carrierPhase = channel.CarrierPhaseDegrees,
                }).ToArray(),
            }).ToArray(),
            activeGroupIndex = activeIndex,
            activeChannelIndex = _config.ActiveChannelIndex,
            oneShotPeriods = _config.OneShotPeriods,
            estimatedFrames,
            estimatedBytes = estimatedFrames * WaveformFrameContract.SizeInBytes,
            totalEstimatedFrames = totalFrames,
            totalEstimatedBytes = totalFrames * WaveformFrameContract.SizeInBytes,
            dropped = _dropped,
            sending = _sending,
            continuous = _continuous,
        };

        PublishText(context, SnapshotSource, JsonSerializer.Serialize(snapshot));
    }

    private void PublishPreview(IModuleContext context)
    {
        var group = SelectedGroup;
        var selected = SelectedChannel;
        var channels = new List<(int Index, GeneratorChannelConfig Channel)>();
        if (selected is not null)
        {
            channels.Add((Math.Clamp(_config.ActiveChannelIndex, 0, group.Channels.Count - 1), selected));
        }
        else
        {
            for (var index = 0; index < group.Channels.Count; index++)
            {
                if (group.Channels[index].Enabled)
                {
                    channels.Add((index, group.Channels[index]));
                }
            }
        }

        if (!double.IsFinite(group.FrequencyHz) || group.FrequencyHz <= 0)
        {
            channels.Clear();
        }

        if (channels.Count == 0)
        {
            context.Display.SetSeriesWindow(
                PreviewSource,
                new SeriesWindowSnapshot(++_previewVersion, 0, 0, 0, 0, -1, 1, true, []));
            return;
        }

        // 组预览用统一点数：每周期点数属于组，所有通道共用同一分辨率。
        var count = Math.Max(2, group.Resolution * 2);
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var previewChannels = new List<SeriesWindowChannel>(channels.Count);

        foreach (var (channelIndex, channel) in channels)
        {
            var seed = unchecked((int)((group.GroupId * 2654435761u) ^ (uint)channelIndex));
            var random = new Random(seed);
            var values = new float[count];
            for (var index = 0; index < count; index++)
            {
                var time = (index / (double)(count - 1)) * (2 / group.FrequencyHz);
                var value = WaveformMath.Sample(channel, group.FrequencyHz, time, random);
                values[index] = value;
                if (float.IsFinite(value))
                {
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            previewChannels.Add(new SeriesWindowChannel(channelIndex, channel.Name, null, values));
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            min = -1;
            max = 1;
        }
        else if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }
        else
        {
            var padding = (max - min) * 0.05;
            min -= padding;
            max += padding;
        }

        context.Display.SetSeriesWindow(
            PreviewSource,
            new SeriesWindowSnapshot(
                ++_previewVersion,
                0,
                2,
                2,
                0,
                min,
                max,
                true,
                previewChannels));
    }

    private void PublishStatus(string text)
    {
        if (string.Equals(_lastStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusText = text;
        if (_context is { } context)
        {
            PublishText(context, StatusSource, text);
        }
    }

    private static void PublishText(IModuleContext context, string source, string text)
    {
        context.Display.SetText(source, text);
    }

    private static void PublishBool(IModuleContext context, string source, bool value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value ? (byte)1 : (byte)0;
        context.Ports.Publish(source, buffer);
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static bool TryFinite(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static bool TryPositive(string text, out double value) =>
        TryFinite(text, out value) && value > 0;

    private static bool TryNonEmptyName(string text, out string value)
    {
        value = text.Trim();
        return value.Length is > 0 and <= GeneratorConfig.MaxNameLength;
    }

    private static bool IsModulationIndexValid(WaveformKind kind, double value) =>
        double.IsFinite(value)
        && kind switch
        {
            WaveformKind.Spwm => value is >= 0 and <= 1,
            WaveformKind.Svpwm => value is >= 0 and <= 1.15,
            _ => value is >= 0 and <= 1.15,
        };

    private void SetInput(IModuleContext context, string source, string text)
    {
        _lastText[source] = text;
        if (string.Equals(context.Values.ReadText(source), text, StringComparison.Ordinal))
        {
            return;
        }

        context.Values.AcceptText(source, text);
    }

    /// <summary>一次结算里某个字段的结论；Module 优先于 Format。</summary>
    private readonly record struct FieldError(string Message, ValueValidityOrigin Origin);
}
