using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Tests.Reference.Stubs;

/// <summary>判据接收桩的配置（测试里直接写；与模块读到的同一份语义）。</summary>
public sealed class JudgementSinkStubConfig
{
    /// <summary>是否检查取值范围。</summary>
    public bool AssertRange { get; set; }

    /// <summary>取值下限（<see cref="AssertRange"/> 为真时生效）。</summary>
    public double ExpectedMin { get; set; }

    /// <summary>取值上限（<see cref="AssertRange"/> 为真时生效）。</summary>
    public double ExpectedMax { get; set; }

    /// <summary>是否检查与期望值的偏差。</summary>
    public bool AssertTolerance { get; set; }

    /// <summary>期望值（<see cref="AssertTolerance"/> 为真时生效）。</summary>
    public double Expected { get; set; }

    /// <summary>容差（<see cref="AssertTolerance"/> 为真时生效）。</summary>
    public double Tolerance { get; set; }

    /// <summary>**每类问题**的上报上限（到顶后不再刷屏）；<c>&lt;= 0</c> 表示不限。</summary>
    public int MaxIssues { get; set; } = 3;
}

/// <summary>
/// **判据接收桩模块**（判据载体 · **本项目内的测试桩**，不是产品模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：「**模块自己判定 + 按严重度上报 Issue + 同类问题限流**」这条 SDK 面
///     （`IIssueReporter` / `IssueSeverity` / 上报上限）在四个基础模块里**没有对应物**
///     （它们的报错走 G3 输入反馈）⇒ 用测试桩承载；</item>
///   <item><b>判据三类</b>：序号单调（乱序即数据流不可还原）· 取值范围（超上下限）· 与期望值的偏差（超容差）；
///     三类各自**按类别**限流，互不挤占；</item>
///   <item><b>与真实模块的关系</b>：这是"判据/断言"这类模块形态的最小可运行示例，作为**开发参考**保留在
///     判据项目内（不进产品模块目录、不打包）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "test",
    "judgement-sink",
    DisplayName = "判据接收桩（测试用）",
    Description = "记录统计并判定序号 / 范围 / 容差，按类别限流上报 Issue",
    Tags = ["test", "sink", "judge"])]
public sealed class JudgementSinkStubModule : IModule
{
    /// <summary>输入端口名。</summary>
    public const string InPort = "in";

    /// <summary>角色 Id。</summary>
    public const string RoleId = "judge";

    /// <summary>数据类型标识。</summary>
    public const string SampleTypeId = "JudgementSample";

    /// <summary>数据类型版本。</summary>
    public const int SampleVersion = 1;

    /// <summary>样本形状：`{ seq: u64, value: f32 }`（与旧判据的数值契约同形）。</summary>
    public static ContractShape SampleShape { get; } = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    /// <summary>样本布局（解码用）。</summary>
    public static ContractLayout SampleLayout { get; } = ContractLayout.FromShape(SampleShape);

    /// <summary>数据源：累计接收条数。</summary>
    public const string CountSource = "count";

    /// <summary>数据源：通过条数。</summary>
    public const string PassedSource = "passed";

    /// <summary>数据源：超差条数。</summary>
    public const string FailedSource = "failed";

    /// <summary>数据源：最近一次偏差。</summary>
    public const string DeviationSource = "deviation";

    private const string SequenceCategory = "seq";

    private const string RangeCategory = "range";

    private const string ToleranceCategory = "tolerance";

    private readonly Dictionary<string, int> _reported = new(StringComparer.Ordinal);

    private bool _hasSequence;
    private ulong _lastSequence;

    /// <summary>累计接收条数。</summary>
    public ulong ReceivedCount { get; private set; }

    /// <summary>通过条数（仅在检查容差时计数）。</summary>
    public ulong PassedCount { get; private set; }

    /// <summary>超差条数（仅在检查容差时计数）。</summary>
    public ulong FailedCount { get; private set; }

    /// <summary>序号非单调次数。</summary>
    public int SequenceViolations { get; private set; }

    /// <summary>取值超范围次数。</summary>
    public int RangeViolations { get; private set; }

    /// <summary>最近一次样本值。</summary>
    public double LastValue { get; private set; }

    /// <summary>样本最小值。</summary>
    public double MinValue { get; private set; }

    /// <summary>样本最大值。</summary>
    public double MaxValue { get; private set; }

    /// <summary>最近一次偏差（样本值 − 期望值）。</summary>
    public double LastDeviation { get; private set; }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(SampleTypeId, SampleShape));
        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, SampleShape)
        {
            TypeId = SampleTypeId,
        });
        context.Declaration.DeclareRole(RoleDeclaration.Input(RoleId, SampleTypeId, InPort, "判据"));

        // 被动接收模式：单元解出 `{ seq, value }` 后逐条判定（测试也可直接调 `Judge`）
        context.Ports.SetReceiveHandler(InPort, payload => Handle(context, payload));

        // 四个统计量的界面契约（代码声明是缓冲的唯一来源）。
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(CountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(PassedSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(FailedSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DeviationSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
    }

    /// <summary>
    /// **被动接收入口**：把一个 `{ seq, value }` 单元解出来交给 <see cref="Judge"/>。
    /// </summary>
    /// <param name="context">上下文。</param>
    /// <param name="payload">单元字节。</param>
    public void Handle(IModuleContext context, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!PayloadReader.TryCreate(payload, SampleLayout, out var reader))
        {
            return;
        }

        _ = Judge(context, reader.ReadU64("seq"), reader.ReadF32("value"));
    }

    /// <summary>
    /// 判定一条样本（**测试可直接调用**）——返回与期望值的偏差。
    /// </summary>
    /// <param name="context">上下文。</param>
    /// <param name="sequence">样本序号（模块自己维护的单调性判据，框架不代管）。</param>
    /// <param name="value">样本值。</param>
    /// <returns>偏差（样本值 − 期望值）。</returns>
    public double Judge(IModuleContext context, ulong sequence, float value)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = context.Configuration.Read<JudgementSinkStubConfig>();
        var deviation = value - config.Expected;

        ReceivedCount++;
        LastValue = value;
        LastDeviation = deviation;
        MinValue = ReceivedCount == 1 ? value : Math.Min(MinValue, value);
        MaxValue = ReceivedCount == 1 ? value : Math.Max(MaxValue, value);

        if (_hasSequence && sequence <= _lastSequence)
        {
            SequenceViolations++;
            Report(
                context,
                config,
                SequenceCategory,
                $"序号非单调：{_lastSequence} → {sequence}（数据流无法按到达顺序还原）",
                IssueSeverity.Warning);
        }

        _hasSequence = true;
        _lastSequence = sequence;

        if (config.AssertRange && (value < config.ExpectedMin || value > config.ExpectedMax))
        {
            RangeViolations++;
            Report(
                context,
                config,
                RangeCategory,
                $"值超出期望范围：{value:0.###} 不在 [{config.ExpectedMin:0.###}, {config.ExpectedMax:0.###}] 内",
                IssueSeverity.Warning);
        }

        if (config.AssertTolerance)
        {
            if (Math.Abs(deviation) <= config.Tolerance)
            {
                PassedCount++;
            }
            else
            {
                FailedCount++;
                Report(
                    context,
                    config,
                    ToleranceCategory,
                    $"值超出容差：期望 {config.Expected:0.###} ± {config.Tolerance:0.###}，实际 {value:0.###}（偏差 {deviation:0.###}）",
                    IssueSeverity.Warning);
            }
        }

        context.Ports.Publish(CountSource, BitConverter.GetBytes(ReceivedCount));
        context.Ports.Publish(PassedSource, BitConverter.GetBytes(PassedCount));
        context.Ports.Publish(FailedSource, BitConverter.GetBytes(FailedCount));
        context.Ports.Publish(DeviationSource, BitConverter.GetBytes((float)deviation));

        return deviation;
    }

    /// <summary>
    /// 按**类别**限流上报（原文口径：同类问题超过上限后不刷屏；<c>MaxIssues &lt;= 0</c> = 不限）。
    /// </summary>
    private void Report(IModuleContext context, JudgementSinkStubConfig config, string category, string message, IssueSeverity severity)
    {
        var reported = _reported.GetValueOrDefault(category);

        if (config.MaxIssues > 0 && reported >= config.MaxIssues)
        {
            return;
        }

        _reported[category] = reported + 1;
        context.Issues.Report(new IssueReport(IssueCategory.ModuleError, severity, message, category));
    }
}
