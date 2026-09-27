using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Reference.Fuser;

/// <summary>多源融合策略（**合并语义由模块承担** —— 框架永不仲裁，ADR-0050 M3/Q4）。</summary>
public enum FusionMode
{
    /// <summary>**按到达**：后到的覆盖先到的（框架中立的"最后值"，不做时间比较）。</summary>
    Latest = 0,

    /// <summary>**固定优先级**：只要 1 路有数据就用 1 路，否则用 2 路。</summary>
    Priority = 1,

    /// <summary>**数值平均**：两路都收到过则取两者最近值的算术平均。</summary>
    Average = 2,
}

/// <summary>多源融合模块配置。</summary>
public sealed class FuserConfig
{
    /// <summary>融合策略。</summary>
    [ConfigurationField("融合策略", Group = "融合", Description = "Latest = 按到达取最后值；Priority = 1 路优先；Average = 两路平均")]
    public FusionMode Mode { get; set; } = FusionMode.Latest;

    /// <summary>是否把融合结果从输出口转发出去（关 = 只在界面显示）。</summary>
    [ConfigurationField("转发融合结果", Group = "融合", Description = "开启后融合值会从 out 口发给下游")]
    public bool ForwardMerged { get; set; } = true;

    /// <summary>是否把结果写入订阅缓冲（关 = 只统计不刷界面）。</summary>
    [ConfigurationField("写入显示缓冲", Group = "显示")]
    public bool PublishToView { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**多源融合（双源仲裁）**——多源网络的**参考消费端**，ADR-0050 落地示例。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：ADR-0050 把"多源"定为**合法的一等组合**，并把**合并/仲裁**明确交给
///     **模块**（框架永不仲裁，M3/Q4）。本模块就是那个"承接合并"的范例：**两个输入口 = 两个发布者**，
///     模块自己决定怎么合并；</item>
///   <item><b>来源怎么区分</b>：**用口区分**——这正是 M4（来源不进数据面）后的推荐形态：
///     应用需要知道"这个值是哪来的"，就给它一个**独立的口**（本模块 <c>in1</c>/<c>in2</c>），
///     而不是让框架往数据里塞来源字段；框架侧的观测面本来就能按源归属（ADR-0050 M7）；</item>
///   <item><b>顺序纪律</b>：同 (源,宿) 对 FIFO ⇒ 每个口内的到达顺序是可靠的；跨口**无时间序**
///     （M4b）⇒ 本模块的"按到达"只承诺"最后收到的那个赢"，**不比较时刻**；</item>
///   <item><b>与第三方模块同构同路径</b>：只引 <c>HYWDebugAssistant.Sdk</c>（AR-1）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-fuser",
    "1.0.0",
    DisplayName = "多源融合器",
    Description = "把两路同类型数据按策略合并（Latest / Priority / Average），可选转发给下游",
    Tags = ["reference", "fusion", "multi-source"],
    ParticipatesInDataFlow = true)]
public sealed class FuserModule : IModule
{
    /// <summary>第 1 路输入口名。</summary>
    public const string InputPort1 = "in1";

    /// <summary>第 2 路输入口名。</summary>
    public const string InputPort2 = "in2";

    /// <summary>输出口名（转发融合结果）。</summary>
    public const string OutputPort = "out";

    /// <summary>视图数据源：融合后的当前值（f32）。</summary>
    public const string FusedSource = "fused";

    /// <summary>视图数据源：当前胜出的路（u8；0/1 = 第 1/2 路，2 = 两路平均）。</summary>
    public const string SourceSource = "winner";

    /// <summary>视图数据源：第 1 路计数（u64）。</summary>
    public const string Count1Source = "n1";

    /// <summary>视图数据源：第 2 路计数（u64）。</summary>
    public const string Count2Source = "n2";

    /// <summary>视图数据源：融合值序列（f32，环形）。</summary>
    public const string SeriesSource = "series";

    /// <summary>数据单元形状（= SDK 参考类型库的 <c>hyw/analog-sample</c>）。</summary>
    public static readonly ContractShape UnitShape = CommonDataTypes.AnalogSampleShape;

    /// <summary>数据单元布局。</summary>
    public static readonly ContractLayout UnitLayout = ContractLayout.FromShape(UnitShape);

    /// <summary>第 1 路胜出。</summary>
    public const byte WinnerFirst = 0;

    /// <summary>第 2 路胜出。</summary>
    public const byte WinnerSecond = 1;

    /// <summary>两路平均。</summary>
    public const byte WinnerAverage = 2;

    private readonly Lock _gate = new();

    private IModuleContext? _context;
    private FuserConfig _configuration = new();
    private volatile bool _configurationDirty = true;

    private bool _hasFirst;
    private bool _hasSecond;
    private double _first;
    private double _second;
    private double _fused;
    private byte _winner = WinnerFirst;
    private ulong _count1;
    private ulong _count2;
    private ulong _emitted;

    /// <summary>第 1 路收到的单元数。</summary>
    public ulong Count1
    {
        get
        {
            lock (_gate)
            {
                return _count1;
            }
        }
    }

    /// <summary>第 2 路收到的单元数。</summary>
    public ulong Count2
    {
        get
        {
            lock (_gate)
            {
                return _count2;
            }
        }
    }

    /// <summary>融合后的当前值。</summary>
    public double FusedValue
    {
        get
        {
            lock (_gate)
            {
                return _fused;
            }
        }
    }

    /// <summary>当前胜出的路（<see cref="WinnerAverage"/> = 两路平均）。</summary>
    public byte Winner
    {
        get
        {
            lock (_gate)
            {
                return _winner;
            }
        }
    }

    /// <summary>已从输出口转发出去的单元数。</summary>
    public ulong Emitted => (ulong)Interlocked.Read(ref _emitted);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _configuration = context.Configuration.Read<FuserConfig>();
        _configurationDirty = false;

        // 【ADR-0046 D2/D6】声明本模块使用的数据类型（三个口都是同一个 `hyw/analog-sample@1`）
        context.Declaration.DeclareDataType(CommonDataTypes.AnalogSampleV1);
        context.Declaration.DeclareDataType(CommonDataTypes.F32V1);
        context.Declaration.DeclareDataType(CommonDataTypes.U64V1);
        context.Declaration.DeclareDataType(CommonDataTypes.U8V1);

        // **两个输入口**：多源（两个发布者）由**口**区分；输出口复用同一类型 ⇒ 可继续接下游
        context.Declaration.DeclarePort(new PortDeclaration(InputPort1, PortDirection.Receive, UnitShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        context.Declaration.DeclarePort(new PortDeclaration(InputPort2, PortDirection.Receive, UnitShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutputPort, PortDirection.Send, UnitShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        context.Ports.SetReceiveHandler(InputPort1, payload => OnUnitReceived(portIndex: 0, payload));
        context.Ports.SetReceiveHandler(InputPort2, payload => OnUnitReceived(portIndex: 1, payload));

        // 【ADR-0052】角色 = 模块的功能分块：**一块"融合"**（一种类型 + 2 个输入口 + 1 个输出口）。
        // 这正是"功能分块"要表达的东西：两路进来、一路出去是**一件事**，不该被画成"一收一发两块"。
        // **只呈现**：不参与建网/兼容/停流/投递/计量（声明顺序 = 呈现顺序）。
        context.Declaration.DeclareRole(new RoleDeclaration(
            "fuse",
            CommonDataTypes.AnalogSampleRef,
            [InputPort1, InputPort2],
            [OutputPort],
            "融合"));

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, FusedSource, "融合值"),
                new ViewElement(ViewElementKind.Numeric, SourceSource, "胜出源"),
                new ViewElement(ViewElementKind.Numeric, Count1Source, "第 1 路"),
                new ViewElement(ViewElementKind.Numeric, Count2Source, "第 2 路"),
                new ViewElement(ViewElementKind.Curve, SeriesSource, "融合波形"),
                new ViewElement(ViewElementKind.ConfigurationEditor, title: "融合设置"),
            ],
            [
                new ViewDataSource(FusedSource, unit: null, format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
                new ViewDataSource(SourceSource, unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U8)
                {
                    Type = CommonDataTypes.U8Ref,
                },
                new ViewDataSource(Count1Source, unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(Count2Source, unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(SeriesSource, unit: null, format: null, kind: ViewDataSourceKind.Sequence, valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
            ])
        {
            PreferredWidth = 340,
            PreferredHeight = 420,
        });
    }

    /// <inheritdoc />
    public void OnActivated() => ReloadConfiguration();

    /// <inheritdoc />
    public void OnEnabled() => ReloadConfiguration();

    /// <inheritdoc />
    public void OnConfigurationChanged() => ReloadConfiguration();

    private void ReloadConfiguration()
    {
        if (_context is { } context)
        {
            _configuration = context.Configuration.Read<FuserConfig>();
            _configurationDirty = false;
        }
    }

    /// <summary>
    /// **融合一个单元**（公开以便模块级测试直接驱动；框架路径经两个接收回调进入）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="portIndex">来源**口序号**（0 = <see cref="InputPort1"/>，1 = <see cref="InputPort2"/>）。</param>
    /// <param name="value">该单元的值（调用方已按自己的布局解出）。</param>
    /// <returns>融合后的值。</returns>
    public double Fuse(IModuleContext context, int portIndex, double value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegative(portIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(portIndex, 1);

        if (_configurationDirty)
        {
            ReloadConfiguration();
        }

        var config = _configuration;
        double fused;
        byte winner;
        ulong count1;
        ulong count2;

        lock (_gate)
        {
            if (portIndex == 0)
            {
                _first = value;
                _hasFirst = true;
                _count1++;
            }
            else
            {
                _second = value;
                _hasSecond = true;
                _count2++;
            }

            (fused, winner) = config.Mode switch
            {
                // **按到达**：本次到达的那一路赢（不比较时刻 —— 跨口无时间序，M4b）
                FusionMode.Latest => (value, portIndex == 0 ? WinnerFirst : WinnerSecond),

                // **固定优先级**：1 路有过数据就用 1 路
                FusionMode.Priority => _hasFirst
                    ? (_first, WinnerFirst)
                    : (_second, WinnerSecond),

                // **平均**：两路都有数据才平均，否则用现有那一路
                FusionMode.Average => _hasFirst && _hasSecond
                    ? ((_first + _second) / 2, WinnerAverage)
                    : _hasFirst ? (_first, WinnerFirst) : (_second, WinnerSecond),

                _ => (value, WinnerFirst),
            };

            _fused = fused;
            _winner = winner;
            count1 = _count1;
            count2 = _count2;
        }

        PublishView(context, config, fused, winner, count1, count2);

        if (config.ForwardMerged)
        {
            ForwardMerged(context, fused);
        }

        return fused;
    }

    /// <summary>把融合结果写入订阅缓冲（供界面显示）。</summary>
    private static void PublishView(
        IModuleContext context,
        FuserConfig config,
        double fused,
        byte winner,
        ulong count1,
        ulong count2)
    {
        if (!config.PublishToView)
        {
            return;
        }

        Span<byte> sample = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(sample, (float)fused);

        context.Ports.Publish(FusedSource, sample);
        context.Ports.Publish(SeriesSource, sample);

        Span<byte> flag = stackalloc byte[sizeof(byte)];
        flag[0] = winner;
        context.Ports.Publish(SourceSource, flag);

        Span<byte> counter = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(counter, count1);
        context.Ports.Publish(Count1Source, counter);

        BinaryPrimitives.WriteUInt64LittleEndian(counter, count2);
        context.Ports.Publish(Count2Source, counter);
    }

    /// <summary>把融合结果从输出口发出去（下游看到的是**模块自己产出的**单元，序号由本模块维护）。</summary>
    private void ForwardMerged(IModuleContext context, double fused)
    {
        Span<byte> unit = stackalloc byte[UnitLayout.SizeInBytes];
        var writer = new PayloadWriter(unit, UnitLayout);
        writer.WriteU64("seq", Interlocked.Increment(ref _emittedSequence));
        writer.WriteF32("value", (float)fused);

        _ = context.Ports.TrySend(OutputPort, unit);
        _ = Interlocked.Increment(ref _emitted);
    }

    private void OnUnitReceived(int portIndex, ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context || payload.Length < UnitLayout.SizeInBytes)
        {
            return;
        }

        // 【读取纪律 · ADR-0046 D6】先判长度：兼容不保证"能按自己的契约整块读"（版本升级/混版本多源时数据报可能更短）；
        // 硬构造会抛异常，而异常会让本实例的投递循环整体退出（实测）。丢弃 + 计数，不静默、不崩。
        if (!PayloadReader.TryCreate(payload, UnitLayout, out var reader))
        {
            ReportShortUnit(context, portIndex, payload.Length);
            return;
        }

        _ = Fuse(context, portIndex, reader.ReadF32("value"));
    }

    /// <summary>上报"数据报短于本口契约"（**只报一次**，不刷屏）：版本不一致的可见信号。</summary>
    private void ReportShortUnit(IModuleContext context, int portIndex, int length)
    {
        if (Interlocked.Increment(ref _shortUnits) != 1)
        {
            return;
        }

        var port = portIndex == 0 ? InputPort1 : InputPort2;
        context.Issues.Report(new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Warning,
            $"口 '{port}' 收到 {length} 字节的数据报，小于本口契约 {UnitLayout.SizeInBytes} 字节 —— **已丢弃**"
            + "（通常是发布方版本比本口旧；接收方必须按到达长度读，`PayloadReader.TryCreate`）。"
            + "此提示只报一次，后续同类丢弃只计数。"));
    }

    private long _shortUnits;

    private ulong _emittedSequence;
}
