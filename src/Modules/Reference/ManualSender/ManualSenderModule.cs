using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Modules.Reference.ManualSender;

/// <summary>
/// 手动发送器的自有配置（**只放"非交互"参数**；交互项一律由视图元素承担 —— ADR-0040 D5）。
/// </summary>
public sealed class ManualSenderConfig
{
    /// <summary>是否把统计写入订阅缓冲（供模块视图的数值显示；关掉只影响显示，不影响发送）。</summary>
    [ConfigurationField("写入显示缓冲", Group = "显示", Description = "关掉只影响本模块视图的数值刷新")]
    public bool PublishToView { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**手动数据发送器**（人能戳得动的源端）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>【M1-16 / ADR-0040 重写】</b>删除"改配置即发送"的权宜通道与 <c>ManualSendMode</c> 枚举：
///     人机操作改走**正规通道** —— 文本输入（B5）+ 命令按钮（B2）+ 开关（A10），
///     由框架投递到<b>本模块线程</b>的收件箱，模块在活动体内消费；</item>
///   <item>与第三方模块**同构同路径**：只引用 <c>HYWDebugAssistant.Sdk</c>（AR-1）；</item>
///   <item>输出形状与参考数据发生器**完全一致**（<c>{seq: u64, value: f32}</c>）→ 可直接接
///     记录器/断言器/信息显示模块；</item>
///   <item>序号由模块自增（**不依赖墙钟**）→ 断言器/记录器的序号单调判据可用；</item>
///   <item>**激活不自动发送**：只有"点发送"或"自动发送开关打开"才产生数据（语义可预期）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-manual-sender",
    "1.0.0",
    DisplayName = "手动数据发送器",
    Description = "由人输入数值并触发发送（单次/连续），用于手工验证链路",
    Tags = ["reference", "manual", "test"],
    ParticipatesInDataFlow = true)]
public sealed class ManualSenderModule : IModule, IActivitySource
{
    /// <summary>输出端口名。</summary>
    public const string OutputPort = "out";

    /// <summary>命令名：发送一次。</summary>
    public const string SendCommand = "send";

    /// <summary>输入源：待发送值（文本）。</summary>
    public const string ValueInput = "valueInput";

    /// <summary>输入源：自动发送开关（true/false）。</summary>
    public const string AutoInput = "autoInput";

    /// <summary>输入源：自动发送周期（毫秒，文本）。</summary>
    public const string PeriodInput = "periodInput";

    /// <summary>视图数据源：当前要发送的值（f32）。</summary>
    public const string ValueSource = "value";

    /// <summary>视图数据源：已成功投递数量（u64）。</summary>
    public const string SentSource = "sent";

    /// <summary>视图数据源：被丢弃数量（u64，队列满时体现背压）。</summary>
    public const string DroppedSource = "dropped";

    /// <summary>输出数据单元形状：与数据发生器一致，便于任意接收端互通。</summary>
    public static readonly ContractShape OutputShape = CommonDataTypes.AnalogSampleShape;

    /// <summary>输出布局（字段偏移表）。</summary>
    public static readonly ContractLayout OutputLayout = ContractLayout.FromShape(OutputShape);

    private ManualSenderConfig _configuration = new();
    private volatile bool _configurationDirty = true;

    /// <summary>要发送的值（由文本输入写入）。</summary>
    private double _value = 1.0;

    /// <summary>自动发送是否开启（由开关写入）。</summary>
    private bool _autoSend;

    /// <summary>自动发送周期（毫秒；由文本输入写入）。</summary>
    private int _periodMs = 200;

    /// <summary>待发送次数（命令写入；活动体消费）。</summary>
    private int _pending;

    private ulong _sequence;
    private long _sent;
    private long _dropped;

    /// <summary>已成功投递的数据单元数（诊断/测试用）。</summary>
    public ulong SentCount => (ulong)Interlocked.Read(ref _sent);

    /// <summary>被丢弃的数据单元数（队列满等）。</summary>
    public ulong DroppedCount => (ulong)Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public ActivityKind Kind => ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMilliseconds(20);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 读一次配置 → 框架登记该实例的配置（持久化与配置表单的事实源；加载时会被恢复值覆盖）
        _configuration = context.Configuration.Read<ManualSenderConfig>();
        _configurationDirty = false;

        // 【ADR-0046 D2/D6】声明本模块使用的数据类型（与接受端**同标识同版本** ⇒ 兼容走第一层）
        context.Declaration.DeclareDataType(CommonDataTypes.AnalogSampleV1);
        context.Declaration.DeclareDataType(CommonDataTypes.F32V1);
        context.Declaration.DeclareDataType(CommonDataTypes.U64V1);

        context.Declaration.DeclarePort(new PortDeclaration(OutputPort, PortDirection.Send, OutputShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        // 【ADR-0040 D5】视图自上而下：必要信息（紧凑）→ 输入 + 发送 → 自动发送（开关 + 周期）→ 配置
        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, ValueSource, "待发送值"),
                new ViewElement(ViewElementKind.Numeric, SentSource, "已发送"),
                new ViewElement(ViewElementKind.Numeric, DroppedSource, "被丢弃"),
                new ViewElement(ViewElementKind.TextInput, ValueInput, "值"),
                new ViewElement(ViewElementKind.CommandButton, SendCommand),
                new ViewElement(ViewElementKind.ToggleSlider, AutoInput, "自动发送"),
                new ViewElement(ViewElementKind.TextInput, PeriodInput, "周期 (ms)"),
                new ViewElement(ViewElementKind.ConfigurationEditor, title: "显示"),
            ],
            [
                new ViewDataSource(ValueSource, format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
                new ViewDataSource(SentSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
                new ViewDataSource(DroppedSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
            ],
            [
                new ViewCommand(SendCommand, "发送", icon: "play", description: "按当前值发送一个数据单元"),
            ])
        {
            // 【用户第十一轮反馈】模块**声明**自己合适的默认窗口尺寸（框架照用，不按元素类型猜）
            PreferredWidth = 330,
            PreferredHeight = 330,
        });
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        // **不自动发送**（语义可预期：没人操作就不产生数据）
    }

    /// <inheritdoc />
    public void OnEnabled()
    {
        // 同上：启用后等用户操作
    }

    /// <inheritdoc />
    public void OnConfigurationChanged() => _configurationDirty = true;

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schedule = Stopwatch.StartNew();
        var nextTick = TimeSpan.Zero;

        while (!context.StopSignal.IsStopRequested)
        {
            if (_configurationDirty)
            {
                _configuration = context.Configuration.Read<ManualSenderConfig>();
                _configurationDirty = false;
            }

            // ① 消费人机操作收件箱（命令 / 输入）——**模块线程**上执行
            DrainInbox(context);

            // ② 发送：命令触发的一次性发送，或自动发送的周期发送
            var periodic = _autoSend;
            var due = false;

            if (periodic)
            {
                var interval = TimeSpan.FromMilliseconds(Math.Clamp(_periodMs, 1, 60_000));
                nextTick += interval;

                if (nextTick <= schedule.Elapsed)
                {
                    due = true;
                    nextTick = schedule.Elapsed;
                }
            }
            else
            {
                nextTick = schedule.Elapsed;
            }

            if (due || Interlocked.Exchange(ref _pending, 0) == 1)
            {
                Send(context, _value);
            }

            // ③ 节拍等待（20 ms：人工操作无感，也让收件箱及时被消费）
            _ = context.StopSignal.Wait(Interval);
        }
    }

    /// <summary>
    /// 发送一个数据单元（**模块级测试可直接调用**，无需线程）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="sequence">序号。</param>
    /// <param name="value">值。</param>
    /// <returns>是否被接受（false = 队列满被丢弃）。</returns>
    public static bool SendOnce(IModuleContext context, ulong sequence, double value)
    {
        ArgumentNullException.ThrowIfNull(context);

        Span<byte> unit = stackalloc byte[OutputLayout.SizeInBytes];
        var writer = new PayloadWriter(unit, OutputLayout);
        writer.WriteU64("seq", sequence);
        writer.WriteF32("value", (float)value);

        return context.Ports.TrySend(OutputPort, unit);
    }

    /// <summary>消费收件箱：命令 = 发一次；输入 = 更新值 / 开关 / 周期（非法值上报 Issue）。</summary>
    private void DrainInbox(IModuleContext context)
    {
        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, SendCommand, StringComparison.Ordinal))
            {
                _ = Interlocked.Exchange(ref _pending, 1);
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知命令：{command}（模块未实现）"));
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            if (string.Equals(source, ValueInput, StringComparison.Ordinal))
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    _value = value;
                }
                else
                {
                    context.Issues.Report(new IssueReport(
                        IssueCategory.ModuleError,
                        IssueSeverity.Warning,
                        $"发送值无法解析为数值：\"{text}\""));
                }

                continue;
            }

            if (string.Equals(source, AutoInput, StringComparison.Ordinal))
            {
                _autoSend = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (string.Equals(source, PeriodInput, StringComparison.Ordinal))
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var period))
                {
                    _periodMs = Math.Clamp(period, 1, 60_000);
                }
                else
                {
                    context.Issues.Report(new IssueReport(
                        IssueCategory.ModuleError,
                        IssueSeverity.Warning,
                        $"周期无法解析为整数：\"{text}\""));
                }

                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知输入源：{source}"));
        }
    }

    private void Send(IModuleContext context, double value)
    {
        if (SendOnce(context, _sequence, value))
        {
            _ = Interlocked.Increment(ref _sent);
        }
        else
        {
            _ = Interlocked.Increment(ref _dropped);
        }

        _sequence++;

        if (!_configuration.PublishToView)
        {
            return;
        }

        Span<byte> valueSample = stackalloc byte[sizeof(float)];
        Span<byte> counterSample = stackalloc byte[sizeof(ulong)];

        BinaryPrimitives.WriteSingleLittleEndian(valueSample, (float)value);
        context.Ports.Publish(ValueSource, valueSample);

        BinaryPrimitives.WriteUInt64LittleEndian(counterSample, SentCount);
        context.Ports.Publish(SentSource, counterSample);

        BinaryPrimitives.WriteUInt64LittleEndian(counterSample, DroppedCount);
        context.Ports.Publish(DroppedSource, counterSample);
    }
}
