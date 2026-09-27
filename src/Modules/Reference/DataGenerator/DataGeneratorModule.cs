using System.Buffers.Binary;
using System.Diagnostics;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Modules.Reference.DataGenerator;

/// <summary>信号波形（可编程数据发生）。</summary>
public enum GeneratorWaveform
{
    /// <summary>常量（值 = <see cref="DataGeneratorConfig.Offset"/>）。</summary>
    Constant = 0,

    /// <summary>正弦。</summary>
    Sine = 1,

    /// <summary>方波。</summary>
    Square = 2,

    /// <summary>三角。</summary>
    Triangle = 3,

    /// <summary>确定性伪随机（由 <see cref="DataGeneratorConfig.Seed"/> 决定，便于复现）。</summary>
    Noise = 4,
}

/// <summary>数据发生器配置（模块自有类型：随组合件保存，Schema 由本类型定义）。</summary>
public sealed class DataGeneratorConfig
{
    /// <summary>产生周期（毫秒；活动体的节拍）。</summary>
    public int PeriodMs { get; set; } = 10;

    /// <summary>波形。</summary>
    public GeneratorWaveform Waveform { get; set; } = GeneratorWaveform.Sine;

    /// <summary>频率（Hz；常量波形忽略）。</summary>
    public double Frequency { get; set; } = 1.0;

    /// <summary>幅值。</summary>
    public double Amplitude { get; set; } = 1.0;

    /// <summary>直流偏置（常量波形的值 = 偏置）。</summary>
    public double Offset { get; set; }

    /// <summary>噪声种子（确定性复现用）。</summary>
    public int Seed { get; set; } = 12345;

    /// <summary>是否把样本写入订阅缓冲（供 UI 数值/曲线显示）。</summary>
    public bool PublishToView { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**可编程数据发生器**（参考组合的源端）——docs/architecture/08 §M1-12。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>与第三方模块**同构同路径**：只引用 <c>HYWDebugAssistant.Sdk</c>（AR-1）；</item>
///   <item>活动形式：**周期活动**（在专属线程上自主循环，按配置节拍产生数据单元）；</item>
///   <item>两条输出通道：**口**（契约数据单元 <c>{seq: u64, value: f32}</c>，供其他模块）
///     + **订阅缓冲**（供 UI 数值/曲线）；</item>
///   <item>相位由 <c>seq</c> 推出（**不依赖墙钟**）→ 相同配置与种子下输出完全可复现（利于断言与测试）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-data-generator",
    "1.0.0",
    DisplayName = "参考数据发生器",
    Description = "按配置周期产生数据单元，用于参考组合的源端",
    Tags = ["reference", "generator"],
    ParticipatesInDataFlow = true)]
public sealed class DataGeneratorModule : IModule, IActivitySource
{
    /// <summary>默认输出端口名。</summary>
    public const string OutputPort = "out";

    /// <summary>视图数据源：当前值（f32）。</summary>
    public const string ValueSource = "value";

    /// <summary>视图数据源：累计序号（u64）。</summary>
    public const string SequenceSource = "seq";

    /// <summary>输出数据单元形状：<c>{ seq: u64, value: f32 }</c>（定长 12 字节）。</summary>
    public static readonly ContractShape OutputShape = CommonDataTypes.AnalogSampleShape;

    /// <summary>输出布局（字段偏移表）。</summary>
    public static readonly ContractLayout OutputLayout = ContractLayout.FromShape(OutputShape);

    private volatile bool _configurationDirty = true;
    private DataGeneratorConfig _configuration = new();

    /// <inheritdoc />
    public ActivityKind Kind => ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMilliseconds(Math.Clamp(_configuration.PeriodMs, 1, 60_000));

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 【ADR-0046 D2/D6】声明本模块使用的**数据类型**（标识 + 版本 + 定长形状 + 计量 + 解码器）：
        // 参考模块统一采用 SDK 的**参考类型库**（`hyw/*`）⇒ 五个参考模块声明同一个 `hyw/analog-sample@1`，
        // 兼容判定因此走"**同标识同版本**"层（真实链路即回归）。
        context.Declaration.DeclareDataType(CommonDataTypes.AnalogSampleV1);
        context.Declaration.DeclareDataType(CommonDataTypes.F32V1);
        context.Declaration.DeclareDataType(CommonDataTypes.U64V1);

        context.Declaration.DeclarePort(new PortDeclaration(OutputPort, PortDirection.Send, OutputShape)
        {
            Type = CommonDataTypes.AnalogSampleRef,
        });

        // 视图：预置元素组合（模块不提供 View 类型 —— C1-3 / Q3-7）
        // 【ADR-0052】角色 = 模块的功能分块：本模块是"产生数据"这一块（一种类型 + 一个输出口）
        context.Declaration.DeclareRole(RoleDeclaration.Output(
            "generate", CommonDataTypes.AnalogSampleRef, OutputPort, "数据发生"));

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, ValueSource, "当前值"),
                new ViewElement(ViewElementKind.Curve, ValueSource, "波形"),
                new ViewElement(ViewElementKind.Numeric, SequenceSource, "序号"),
            ],
            [
                // 数据源**引用类型** ⇒ 呈现期解码走模块注册的解码器（框架只回调，D1 ③）
                new ViewDataSource(
                    ValueSource,
                    unit: "V",
                    format: "F3",
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.F32)
                {
                    Type = CommonDataTypes.F32Ref,
                },
                new ViewDataSource(
                    SequenceSource,
                    unit: null,
                    format: null,
                    kind: ViewDataSourceKind.Value,
                    valueType: ContractPrimitive.U64)
                {
                    Type = CommonDataTypes.U64Ref,
                },
            ]));
    }

    /// <inheritdoc />
    public void OnActivated() => _configurationDirty = true;

    /// <inheritdoc />
    public void OnEnabled() => _configurationDirty = true;

    /// <inheritdoc />
    public void OnConfigurationChanged() => _configurationDirty = true;

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _configuration = context.Configuration.Read<DataGeneratorConfig>();
        _configurationDirty = false;

        Span<byte> unit = stackalloc byte[OutputLayout.SizeInBytes];
        Span<byte> valueSample = stackalloc byte[sizeof(float)];
        Span<byte> sequenceSample = stackalloc byte[sizeof(ulong)];

        var random = new Random(_configuration.Seed);
        var schedule = Stopwatch.StartNew();
        var nextTick = TimeSpan.Zero;
        ulong sequence = 0;

        while (!context.StopSignal.IsStopRequested)
        {
            if (_configurationDirty)
            {
                // 配置热生效：下一循环起用新配置（**不重建实例**，§4.5）
                _configuration = context.Configuration.Read<DataGeneratorConfig>();
                random = new Random(_configuration.Seed);
                _configurationDirty = false;
            }

            var config = _configuration;
            var value = (float)NextValue(config, sequence, random);

            var writer = new PayloadWriter(unit, OutputLayout);
            writer.WriteU64("seq", sequence);
            writer.WriteF32("value", value);

            _ = context.Ports.TrySend(OutputPort, unit);

            if (config.PublishToView)
            {
                BinaryPrimitives.WriteSingleLittleEndian(valueSample, value);
                BinaryPrimitives.WriteUInt64LittleEndian(sequenceSample, sequence);
                context.Ports.Publish(ValueSource, valueSample);
                context.Ports.Publish(SequenceSource, sequenceSample);
            }

            sequence++;

            // 按配置节拍推进（落后立即追赶，不累积误差；等待期间响应停止信号）
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(config.PeriodMs, 1, 60_000));
            nextTick += interval;
            var remaining = nextTick - schedule.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                _ = context.StopSignal.Wait(remaining);
            }
            else
            {
                nextTick = schedule.Elapsed;
            }
        }
    }

    /// <summary>
    /// 计算第 <paramref name="sequence"/> 个样本（**由序号推相位、不依赖墙钟** → 可复现）。
    /// </summary>
    /// <param name="config">配置。</param>
    /// <param name="sequence">序号（从 0 起）。</param>
    /// <param name="random">噪声源（仅噪声波形使用）。</param>
    public static double NextValue(DataGeneratorConfig config, ulong sequence, Random random)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(random);

        if (config.Waveform == GeneratorWaveform.Constant)
        {
            return config.Offset;
        }

        var seconds = sequence * (double)Math.Clamp(config.PeriodMs, 1, 60_000) / 1000.0;
        var phase = 2 * Math.PI * config.Frequency * seconds;

        var shape = config.Waveform switch
        {
            GeneratorWaveform.Sine => Math.Sin(phase),
            GeneratorWaveform.Square => Math.Sin(phase) >= 0 ? 1d : -1d,
            GeneratorWaveform.Triangle => (2 / Math.PI) * Math.Asin(Math.Sin(phase)),
            GeneratorWaveform.Noise => (random.NextDouble() * 2) - 1,
            _ => 0d,
        };

        return config.Offset + (config.Amplitude * shape);
    }
}
