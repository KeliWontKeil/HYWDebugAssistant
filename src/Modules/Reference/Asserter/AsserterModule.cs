using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Reference.Asserter;

/// <summary>断言器配置。</summary>
public sealed class AsserterConfig
{
    /// <summary>期望值（配合常量波形使用）。</summary>
    public double Expected { get; set; }

    /// <summary>容差（|值 − 期望| ≤ 容差 视为通过）。</summary>
    public double Tolerance { get; set; } = 0.001;

    /// <summary>期望收到的数量（0 = 不检查）。</summary>
    public int ExpectedCount { get; set; }

    /// <summary>同类断言最多上报几次（不刷屏）。</summary>
    public int MaxIssues { get; set; } = 3;

    /// <summary>是否把偏差写入订阅缓冲（供 UI 曲线显示）。</summary>
    public bool PublishToView { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**断言器**（参考组合的校验端）——docs/architecture/08 §M1-12。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>职责：对收到的数据单元做**值断言**与**数量断言**，违规即上 Issue（受上限约束）；</item>
///   <item>与记录器一样用**主动接收**（模块可阻塞 → 自行取数据）；</item>
///   <item>它是参考组合的**判据端**：T1 的 AC-2/AC-5 用它断言"数据确实按预期流过"。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-asserter",
    "1.0.0",
    DisplayName = "参考断言器",
    Description = "对接收到的数据单元做值与数量断言，违规上报 Issue",
    Tags = ["reference", "asserter"],
    ParticipatesInDataFlow = true)]
public sealed class AsserterModule : IModule
{
    /// <summary>输入端口名。</summary>
    public const string InputPort = "in";

    /// <summary>视图数据源：通过数（u64）。</summary>
    public const string PassedSource = "passed";

    /// <summary>视图数据源：失败数（u64）。</summary>
    public const string FailedSource = "failed";

    /// <summary>视图数据源：最近偏差（f32）。</summary>
    public const string DeviationSource = "deviation";

    /// <summary>输入数据单元形状（与发生器一致）。</summary>
    public static readonly ContractShape InputShape = CommonDataTypes.AnalogSampleShape;

    /// <summary>输入布局。</summary>
    public static readonly ContractLayout InputLayout = ContractLayout.FromShape(InputShape);

    private readonly Lock _gate = new();

    private IModuleContext? _context;
    private AsserterConfig _configuration = new();
    private volatile bool _configurationDirty = true;
    private long _shortUnits;
    private ulong _passed;
    private ulong _failed;
    private double _lastDeviation;

    /// <summary>通过数。</summary>
    public ulong PassedCount
    {
        get
        {
            lock (_gate)
            {
                return _passed;
            }
        }
    }

    /// <summary>失败数。</summary>
    public ulong FailedCount
    {
        get
        {
            lock (_gate)
            {
                return _failed;
            }
        }
    }

    /// <summary>最近一次偏差（值 − 期望）。</summary>
    public double LastDeviation
    {
        get
        {
            lock (_gate)
            {
                return _lastDeviation;
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

        // **被动接收**：框架投递循环在数据到达时于本实例线程上调用（入队即事件唤醒）。
        // 理由同记录器：主动接收 + `IStopSignal.Wait` 当轮询会把延迟拉到定时器粒度（实测 15.70 ms vs 893 ns）。
        context.Ports.SetReceiveHandler(InputPort, OnUnitReceived);

        // 【ADR-0052】角色 = 模块的功能分块：本模块是"断言"这一块（一种类型 + 一个输入口）
        context.Declaration.DeclareRole(RoleDeclaration.Input(
            "assert", CommonDataTypes.AnalogSampleRef, InputPort, "断言"));

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, PassedSource, "通过"),
                new ViewElement(ViewElementKind.Numeric, FailedSource, "失败"),
                new ViewElement(ViewElementKind.Curve, DeviationSource, "偏差"),
            ],
            [
                new ViewDataSource(
                    PassedSource,
                    unit: null,
                    format: null,
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(
                    FailedSource,
                    unit: null,
                    format: null,
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(
                    DeviationSource,
                    unit: null,
                    format: "F4",
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
            _configuration = context.Configuration.Read<AsserterConfig>();
            _configurationDirty = false;
        }
    }

    /// <summary>
    /// 收到一个数据单元（**框架在该实例线程上调用**，数据到达即触发）。
    /// </summary>
    /// <remarks>负载布局 = 发送方契约布局（框架不转换）；本模块与上游同形状，故用自身布局读取。</remarks>
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

        var value = reader.ReadF32("value");
        var deviation = Assert(context, value);

        if (!_configuration.PublishToView)
        {
            return;
        }

        Span<byte> deviationSample = stackalloc byte[sizeof(float)];
        Span<byte> sample = stackalloc byte[sizeof(ulong)];

        BinaryPrimitives.WriteSingleLittleEndian(deviationSample, (float)deviation);
        context.Ports.Publish(DeviationSource, deviationSample);
        PublishCounters(context, sample);
    }

    /// <summary>断言一个值（模块级测试也可直接调用）。</summary>
    /// <param name="context">模块上下文（Issue 上报用）。</param>
    /// <param name="value">收到的值。</param>
    /// <returns>偏差（值 − 期望）。</returns>
    public double Assert(IModuleContext context, float value)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = _configuration;
        var deviation = value - config.Expected;
        ulong failed;

        lock (_gate)
        {
            if (Math.Abs(deviation) <= config.Tolerance)
            {
                _passed++;
            }
            else
            {
                _failed++;
            }

            _lastDeviation = deviation;
            failed = _failed;
        }

        if (Math.Abs(deviation) > config.Tolerance)
        {
            var limit = Math.Max(1, config.MaxIssues);
            if (failed <= (ulong)limit)
            {
                context.Issues.Report(new IssueReport(
                    IssueCategory.ModuleError,
                    IssueSeverity.Warning,
                    $"断言器：值超出容差（第 {failed} 次）：收到 {value:F4}，期望 {config.Expected:F4} ± {config.Tolerance:F4}（偏差 {deviation:F4}）"));
            }
        }

        return deviation;
    }

    /// <summary>数量断言（参考测试在停流后调用；也可作为模块自检的一环）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>是否满足期望数量（未设置期望时恒为 true）。</returns>
    public bool AssertCount(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = _configuration;
        if (config.ExpectedCount <= 0)
        {
            return true;
        }

        var received = PassedCount + FailedCount;
        if (received >= (ulong)config.ExpectedCount)
        {
            return true;
        }

        context.Issues.Report(new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Error,
            $"断言器：收到数量不足：实际 {received}，期望 ≥ {config.ExpectedCount}"));

        return false;
    }

    private void PublishCounters(IModuleContext context, Span<byte> sample)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(sample, PassedCount);
        context.Ports.Publish(PassedSource, sample);

        BinaryPrimitives.WriteUInt64LittleEndian(sample, FailedCount);
        context.Ports.Publish(FailedSource, sample);
    }
}
