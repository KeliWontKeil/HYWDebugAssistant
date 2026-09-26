using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Reference.Recorder;

/// <summary>记录器配置。</summary>
public sealed class RecorderConfig
{
    /// <summary>本地记录环容量（样本数；仅用于统计，不参与 UI 缓冲）。</summary>
    public int Capacity { get; set; } = 1024;

    /// <summary>是否断言序号单调递增（丢帧/乱序会被记下）。</summary>
    public bool ExpectMonotonicSequence { get; set; } = true;

    /// <summary>期望值下限（含）。</summary>
    public double ExpectedMin { get; set; } = double.NegativeInfinity;

    /// <summary>期望值上限（含）。</summary>
    public double ExpectedMax { get; set; } = double.PositiveInfinity;

    /// <summary>同类断言最多上报几次（**不刷屏**）。</summary>
    public int MaxIssues { get; set; } = 3;

    /// <summary>是否把样本写入订阅缓冲（供 UI 曲线显示）。</summary>
    public bool PublishToView { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**记录器**（参考组合的中间环节）——docs/architecture/08 §M1-12。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>采用**主动接收**（<c>TryReceive</c>）：在自己的活动体里取队列中的数据单元——
///     这是"允许阻塞的模块用主动模式"的推荐用法（ADR-0028 §3.3），也覆盖了这条框架路径；</item>
///   <item>职责：**记录**（计数/最值/最近值）+ **断言**（序号单调、值域）→ 违规转 Issue（受上限约束，不刷屏）；</item>
///   <item>不修改数据、不转发：参考链里它与断言器**并列**接在发生器之后（1:N 多播）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-recorder",
    "1.0.0",
    DisplayName = "参考记录器",
    Description = "记录接收到的数据单元并做基本断言（序号单调、值域）",
    Tags = ["reference", "recorder"],
    ParticipatesInDataFlow = true)]
public sealed class RecorderModule : IModule
{
    /// <summary>输入端口名。</summary>
    public const string InputPort = "in";

    /// <summary>视图数据源：已接收数量（u64）。</summary>
    public const string CountSource = "count";

    /// <summary>视图数据源：最近值（f32）。</summary>
    public const string LastSource = "last";

    /// <summary>视图数据源：样本序列（f32，环形）。</summary>
    public const string SeriesSource = "series";

    /// <summary>输入数据单元形状（与发生器一致）。</summary>
    public static readonly ContractShape InputShape = CommonDataTypes.AnalogSampleShape;

    /// <summary>输入布局。</summary>
    public static readonly ContractLayout InputLayout = ContractLayout.FromShape(InputShape);

    private readonly Lock _gate = new();

    private IModuleContext? _context;
    private RecorderConfig _configuration = new();
    private volatile bool _configurationDirty = true;
    private ulong _received;
    private long _shortUnits;
    private ulong _lastSequence;
    private bool _hasSequence;
    private double _lastValue;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;
    private int _sequenceViolations;
    private int _rangeViolations;

    /// <summary>已接收的数据单元数。</summary>
    public ulong ReceivedCount
    {
        get
        {
            lock (_gate)
            {
                return _received;
            }
        }
    }

    /// <summary>最近一次收到的值。</summary>
    public double LastValue
    {
        get
        {
            lock (_gate)
            {
                return _lastValue;
            }
        }
    }

    /// <summary>已接收值的最小值。</summary>
    public double MinValue
    {
        get
        {
            lock (_gate)
            {
                return _min;
            }
        }
    }

    /// <summary>已接收值的最大值。</summary>
    public double MaxValue
    {
        get
        {
            lock (_gate)
            {
                return _max;
            }
        }
    }

    /// <summary>序号断言违规次数。</summary>
    public int SequenceViolations
    {
        get
        {
            lock (_gate)
            {
                return _sequenceViolations;
            }
        }
    }

    /// <summary>值域断言违规次数。</summary>
    public int RangeViolations
    {
        get
        {
            lock (_gate)
            {
                return _rangeViolations;
            }
        }
    }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 上下文留存：生命周期钩子没有参数，配置热生效要在钩子里读（见 ReloadConfiguration）
        _context = context;

        // 【ADR-0046 D2/D6】声明本模块使用的数据类型（与源端同标识同版本 ⇒ 兼容走第一层）
        context.Declaration.DeclareDataType(CommonDataTypes.AnalogSampleV1);
        context.Declaration.DeclareDataType(CommonDataTypes.F32V1);
        context.Declaration.DeclareDataType(CommonDataTypes.U64V1);

        context.Declaration.DeclarePort(new PortDeclaration(InputPort, PortDirection.Receive, InputShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        // **被动接收**：框架投递循环在**数据到达时**于本实例线程上调用（入队即事件唤醒）。
        // 为何不用主动接收：`TryReceive` + `IStopSignal.Wait` 当轮询间隔会把延迟拉到 Windows 定时器粒度
        // （实测 15.70 ms vs 被动接收 893 ns）；主动接收的正当用途需要一个"等数据"原语，
        // 该原语列入 M2 的 SDK 扩展（`04a` §5.2 发现一）。
        context.Ports.SetReceiveHandler(InputPort, OnUnitReceived);

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, CountSource, "已接收"),
                new ViewElement(ViewElementKind.Numeric, LastSource, "最近值"),
                new ViewElement(ViewElementKind.Curve, SeriesSource, "记录波形"),
            ],
            [
                new ViewDataSource(
                    CountSource,
                    unit: null,
                    format: null,
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(
                    LastSource,
                    unit: null,
                    format: "F3",
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
                new ViewDataSource(
                    SeriesSource,
                    unit: null,
                    format: null,
                    kind: ViewDataSourceKind.Sequence,
                    valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
            ]));
    }

    /// <summary>上报"数据报短于本口契约"（**只报一次**，不刷屏）：版本不一致的可见信号。</summary>
    private void ReportShortUnit(IModuleContext context, int length)
    {
        if (Interlocked.Increment(ref _shortUnits) != 1)
        {
            return;
        }

        context.Issues.Report(new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Warning,
            $"收到 {length} 字节的数据报，小于本口契约 {InputLayout.SizeInBytes} 字节 —— **已丢弃**"
            + "（通常是发布方版本比本口旧；接收方必须按到达长度读，`PayloadReader.TryCreate`）。"
            + "此提示只报一次，后续同类丢弃只计数。"));
    }
    /// <inheritdoc />
    public void OnActivated() => ReloadConfiguration();

    /// <inheritdoc />
    public void OnEnabled() => ReloadConfiguration();

    /// <inheritdoc />
    public void OnConfigurationChanged() => ReloadConfiguration();

    /// <summary>立刻重新读取配置（生命周期钩子没有上下文参数，故在 <c>OnRegistered</c> 留存上下文）。</summary>
    private void ReloadConfiguration()
    {
        if (_context is { } context)
        {
            _configuration = context.Configuration.Read<RecorderConfig>();
            _configurationDirty = false;
        }
    }

    /// <summary>
    /// 收到一个数据单元（**框架在该实例线程上调用**，数据到达即触发）。
    /// </summary>
    /// <remarks>
    /// 负载的字节布局 = **发送方的契约布局**（框架不做投影/转换，§5.4.1）；本模块与上游同形状（<c>{seq:u64, value:f32}</c>），
    /// 故用自身布局读取。回调返回后缓冲即被框架归还，**不得留存引用**。
    /// </remarks>
    private void OnUnitReceived(ReadOnlySpan<byte> payload)
    {
        if (_configurationDirty)
        {
            ReloadConfiguration();
        }

        if (_context is not { } context)
        {
            return;
        }

        // 【读取纪律 · ADR-0046 D6】先判长度：兼容不保证"能按自己的契约整块读"（版本升级/混版本多源时数据报可能更短）；
        // 硬构造会抛异常，而异常会让本实例的投递循环整体退出（实测）。丢弃 + 计数，不静默、不崩。
        if (!PayloadReader.TryCreate(payload, InputLayout, out var reader))
        {
            ReportShortUnit(context, payload.Length);
            return;
        }

        var sequence = reader.ReadU64("seq");
        var value = reader.ReadF32("value");

        Record(context, sequence, value);

        if (!_configuration.PublishToView)
        {
            return;
        }

        Span<byte> valueSample = stackalloc byte[sizeof(float)];
        Span<byte> countSample = stackalloc byte[sizeof(ulong)];

        BinaryPrimitives.WriteSingleLittleEndian(valueSample, value);
        context.Ports.Publish(LastSource, valueSample);
        context.Ports.Publish(SeriesSource, valueSample);

        BinaryPrimitives.WriteUInt64LittleEndian(countSample, ReceivedCount);
        context.Ports.Publish(CountSource, countSample);
    }

    /// <summary>记录并断言一个数据单元（模块级测试也可直接调用）。</summary>
    /// <param name="context">模块上下文（Issue 上报用）。</param>
    /// <param name="sequence">序号。</param>
    /// <param name="value">值。</param>
    public void Record(IModuleContext context, ulong sequence, float value)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = _configuration;
        int sequenceViolations;
        int rangeViolations;
        ulong previousSequence;
        bool sequenceViolated = false;
        bool rangeViolated = false;

        lock (_gate)
        {
            previousSequence = _lastSequence;

            _received++;
            _lastValue = value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);

            if (config.ExpectMonotonicSequence && _hasSequence && sequence <= _lastSequence)
            {
                _sequenceViolations++;
                sequenceViolated = true;
            }

            _lastSequence = sequence;
            _hasSequence = true;

            if (value < config.ExpectedMin || value > config.ExpectedMax)
            {
                _rangeViolations++;
                rangeViolated = true;
            }

            sequenceViolations = _sequenceViolations;
            rangeViolations = _rangeViolations;
        }

        ReportThrottled(
            context,
            sequenceViolated,
            sequenceViolations,
            "记录器：序号非单调",
            $"收到序号 {sequence}（上一个 {previousSequence}）：丢帧或乱序 — 数据仍被记录，请检查上游产生与投递节拍",
            config.MaxIssues);

        ReportThrottled(
            context,
            rangeViolated,
            rangeViolations,
            "记录器：值超出期望范围",
            $"收到值 {value:F3}，期望区间 [{config.ExpectedMin:F3}, {config.ExpectedMax:F3}]",
            config.MaxIssues);
    }

    private static void ReportThrottled(
        IModuleContext context,
        bool violatedThisTime,
        int occurrence,
        string title,
        string detail,
        int maxIssues)
    {
        if (!violatedThisTime)
        {
            return;   // 本次没有违规 → 不报（**不得**因为历史计数落在上限内就重复上报）
        }

        if (occurrence > Math.Max(1, maxIssues))
        {
            return;   // 超过上限只在计数里体现（不刷屏）
        }

        context.Issues.Report(new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Warning,
            $"{title}（第 {occurrence} 次）：{detail}"));
    }
}
