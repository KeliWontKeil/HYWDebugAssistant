using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Reference.Monitor;

/// <summary>信息显示模块配置。</summary>
public sealed class MonitorConfig
{
    /// <summary>是否把样本写入订阅缓冲（总开关；关 = 只统计不刷界面）。</summary>
    [ConfigurationField("写入显示缓冲", Group = "显示", Description = "关掉后只统计，不再刷新界面")]
    public bool PublishToView { get; set; } = true;

    /// <summary>每多少个数据单元写一条日志（1 = 全部记录；调大可降 UI 压力）。</summary>
    [ConfigurationField("日志密度", Group = "日志", Minimum = 1, Maximum = 1000, Step = 1, Description = "每 N 个数据单元记一条日志")]
    public int LogEvery { get; set; } = 1;

    /// <summary>是否写曲线数据源（关 = 曲线无数据）。</summary>
    [ConfigurationField("显示曲线", Group = "显示")]
    public bool PublishSeries { get; set; } = true;
}

/// <summary>
/// 参考桩模块：**信息显示模块**（手工验收"一眼看懂收到了什么"）——docs/architecture/11 §5。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>与第三方模块**同构同路径**：只引用 <c>HYWDebugAssistant.Sdk</c>（AR-1）；</item>
///   <item>入口形状与参考数据发生器/手动发送器一致（<c>{seq: u64, value: f32}</c>）→ 可直接接任一源端；</item>
///   <item><b>被动接收</b>（<c>SetReceiveHandler</c>）：框架在数据到达时于本实例线程上调用
///     （与记录器同一裁决 Q1-1：主动接收 + 轮询等待会被 Windows 定时器粒度拖到毫秒级）；</item>
///   <item>四类呈现：**最后值/计数/最值**（A1）+ **波形**（A3）+ **带时间戳的文本日志**（A5）；
///     日志行按**定长 48 字节**发布（框架要求同一数据源的样本长度一致：先写入者定槽，过长截断、
///     不足补空格，UI 解码后去尾空白）；日志条数上限由框架订阅环容量决定
///     （<c>SubscriptionBuffer.DefaultRingCapacity</c> = 512 条）——"日志条数"因此不做成模块配置
///     （见 `11` §10 偏离记录）；</item>
///   <item>本模块**只读不改**数据：不改写负载、不转发（与记录器/断言器的职责边界一致）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "reference-monitor",
    "1.0.0",
    DisplayName = "信息显示模块",
    Description = "显示收到的数值、波形与带时间戳的文本日志，用于手工验证链路",
    Tags = ["reference", "monitor", "test"],
    ParticipatesInDataFlow = true)]
public sealed class MonitorModule : IModule
{
    /// <summary>输入端口名。</summary>
    public const string InputPort = "in";

    /// <summary>视图数据源：最后收到的值（f32）。</summary>
    public const string LastSource = "last";

    /// <summary>视图数据源：已接收数量（u64）。</summary>
    public const string CountSource = "count";

    /// <summary>视图数据源：最小值（f32）。</summary>
    public const string MinSource = "min";

    /// <summary>视图数据源：最大值（f32）。</summary>
    public const string MaxSource = "max";

    /// <summary>视图数据源：波形序列（f32，环形）。</summary>
    public const string SeriesSource = "series";

    /// <summary>视图数据源：文本日志（UTF-8 行，环形）。</summary>
    public const string LogSource = "log";

    /// <summary>输入数据单元形状（与发生器/手动发送器一致）。</summary>
    public static readonly ContractShape InputShape =
        ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    /// <summary>输入布局。</summary>
    public static readonly ContractLayout InputLayout = ContractLayout.FromShape(InputShape);

    private readonly Lock _gate = new();

    private IModuleContext? _context;
    private MonitorConfig _configuration = new();
    private volatile bool _configurationDirty = true;
    private ulong _received;
    private double _last;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

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

    /// <summary>最后收到的值。</summary>
    public double LastValue
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        // 读一次配置 → 框架登记该实例的配置（**持久化与配置表单的事实源**；组合件加载时会被恢复值覆盖）
        _configuration = context.Configuration.Read<MonitorConfig>();
        _configurationDirty = false;

        context.Declaration.DeclarePort(new PortDeclaration(InputPort, PortDirection.Receive, InputShape));
        context.Ports.SetReceiveHandler(InputPort, OnUnitReceived);

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, LastSource, "最后值"),
                new ViewElement(ViewElementKind.Numeric, CountSource, "已接收"),
                new ViewElement(ViewElementKind.Numeric, MinSource, "最小值"),
                new ViewElement(ViewElementKind.Numeric, MaxSource, "最大值"),
                new ViewElement(ViewElementKind.Curve, SeriesSource, "波形"),
                new ViewElement(ViewElementKind.TextLog, LogSource, "接收日志"),
                new ViewElement(ViewElementKind.ConfigurationEditor, title: "显示设置"),
            ],
            [
                new ViewDataSource(LastSource, unit: null, format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32),
                new ViewDataSource(CountSource, unit: null, format: null, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64),
                new ViewDataSource(MinSource, unit: null, format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32),
                new ViewDataSource(MaxSource, unit: null, format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32),
                new ViewDataSource(SeriesSource, unit: null, format: null, kind: ViewDataSourceKind.Sequence, valueType: ContractPrimitive.F32),
                new ViewDataSource(LogSource, unit: null, format: null, kind: ViewDataSourceKind.Text),
            ]));
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
            _configuration = context.Configuration.Read<MonitorConfig>();
            _configurationDirty = false;
        }
    }

    /// <summary>
    /// 收到一个数据单元（**框架在本实例线程上调用**）——负载字节布局 = 发送方契约布局（框架不做投影）。
    /// </summary>
    /// <param name="payload">数据单元负载（生命周期仅限本次调用）。</param>
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

        var reader = new PayloadReader(payload, InputLayout);
        var sequence = reader.ReadU64("seq");
        var value = reader.ReadF32("value");

        var config = _configuration;
        ulong received;
        double min;
        double max;

        lock (_gate)
        {
            _received++;
            _last = value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);

            received = _received;
            min = _min;
            max = _max;
        }

        if (!config.PublishToView)
        {
            return;
        }

        Span<byte> sample = stackalloc byte[sizeof(float)];

        BinaryPrimitives.WriteSingleLittleEndian(sample, value);
        context.Ports.Publish(LastSource, sample);

        if (config.PublishSeries)
        {
            context.Ports.Publish(SeriesSource, sample);
        }

        BinaryPrimitives.WriteSingleLittleEndian(sample, (float)min);
        context.Ports.Publish(MinSource, sample);

        BinaryPrimitives.WriteSingleLittleEndian(sample, (float)max);
        context.Ports.Publish(MaxSource, sample);

        Span<byte> countSample = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(countSample, received);
        context.Ports.Publish(CountSource, countSample);

        var every = Math.Max(1, config.LogEvery);
        if (received % (ulong)every != 0)
        {
            return;
        }

        PublishLogLine(context, sequence, value);
    }

    private static void PublishLogLine(IModuleContext context, ulong sequence, double value)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:HH:mm:ss.fff}  #{sequence}  value={value:F3}");

        // **定长**样本：框架要求"同一数据源的样本长度一致"（先写入者定槽）；
        // 过长的行截断、不足的用空格补齐，UI 解码后去掉尾部空白。
        const int width = 48;
        line = line.Length > width ? line[..width] : line.PadRight(width);

        Span<byte> bytes = stackalloc byte[width];
        var written = Encoding.UTF8.GetBytes(line, bytes);
        context.Ports.Publish(LogSource, bytes[..written]);
    }
}
