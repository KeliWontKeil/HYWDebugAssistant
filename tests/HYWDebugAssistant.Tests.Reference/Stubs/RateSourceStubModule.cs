using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Tests.Reference.Stubs;

/// <summary>定值数据源的配置（测试里用"与 UI 同一条命令"写回）。</summary>
public sealed class RateSourceStubConfig
{
    /// <summary>两次发送之间的间隔（毫秒）。</summary>
    public int IntervalMs { get; set; } = 5;

    /// <summary>发送的数值（以文本形式进定长单元，便于显示器直接显示与断言）。</summary>
    public double Value { get; set; } = 0.5;
}

/// <summary>
/// **定值数据源桩模块**（判据载体 · **本项目内的测试桩**，不是产品模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：「**不需要人来戳就能持续出数**」这条能力（`IActivitySource` 周期活动体
///     + 订阅写入 + 定长单元发送）在四个基础模块里**没有对应物** —— 发送器要么等人点、要么等输入文本
///     （而文本属视图状态、**不持久化**）⇒ 保存/加载类判据（AC-3/AC-4/AC-5）无法用发送器承载
///     ⇒ 用测试桩承载；</item>
///   <item><b>与真实模块的关系</b>：载荷口径与四个正式模块**逐字相同**（`RawBytes` = `{ len:u16, data:bytes[1472] }`）
///     ⇒ 可与 `hyw/data-monitor` 直接连通，也能喂给融合桩；数值另外写一份 `value`（F32）订阅样本；</item>
///   <item><b>配置热生效</b>：`OnConfigurationChanged` 只置脏标记，活动体下一轮 `Read` 新配置
///     ⇒ "改配置不重建实例、新值立刻生效"这条判据有可观测的载体。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "test",
    "rate-source",
    "1.0.0",
    DisplayName = "定值数据源桩（测试用）",
    Description = "按间隔发出定长单元并写数值订阅；配置（间隔 / 定值）热生效",
    Tags = ["test", "source"])]
public sealed class RateSourceStubModule : IModule, IActivitySource
{
    /// <summary>输出端口名。</summary>
    public const string OutPort = "out";

    /// <summary>角色 Id。</summary>
    public const string RoleId = "emit";

    /// <summary>数据源：发出的数值。</summary>
    public const string ValueSource = "value";

    /// <summary>数据源：累计发出条数。</summary>
    public const string TickSource = "tick";

    /// <summary>数据类型标识（与四个正式模块**逐字相同** ⇒ 开箱即连）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单条字节上限。</summary>
    public const int TextCapacity = 1472;

    /// <summary>**`RawBytes`@v1 形状**。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    /// <summary>单元布局（测试里解码用）。</summary>
    public static ContractLayout UnitLayout { get; } = ContractLayout.FromShape(RawBytesShape);

    private RateSourceStubConfig _config = new();
    private bool _configDirty = true;
    private ulong _tick;

    /// <inheritdoc />
    public ActivityKind Kind => ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMilliseconds(20);

    /// <summary>把数值格式化成单元里的文本（不变量文化：与显示器、断言口径一致）。</summary>
    /// <param name="value">数值。</param>
    /// <returns>文本。</returns>
    public static string FormatValue(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 构造一个 `{ len, data }` **定长单元**（与正式发送器同一口径：`data` 尾部补零）。
    /// </summary>
    /// <param name="value">要发送的数值。</param>
    /// <returns>单元字节（`2 + 1472`）。</returns>
    /// <remarks>公开的目的：测试可以断言"同一配置 ⇒ 逐字节相同的单元"（可复现），并复用它构造融合桩的输入。</remarks>
    public static byte[] EncodeUnit(double value)
    {
        var text = Encoding.UTF8.GetBytes(FormatValue(value));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, TextCapacity);

        var unit = new byte[RawBytesShape.SizeInBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(unit, (ushort)text.Length);
        text.CopyTo(unit.AsSpan(sizeof(ushort)));
        return unit;
    }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(RawBytesTypeId, RawBytesVersion, RawBytesShape));

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
        });

        // 单口单块（与两个发送器同一形态）——框架不派生角色，模块自己声明
        context.Declaration.DeclareRole(RoleDeclaration.Output(RoleId, new DataTypeRef(RawBytesTypeId, RawBytesVersion), OutPort, "发数"));

        // **界面契约也在代码里**：测试要读 `value` / `tick`，就必须先声明它们。
        // 声明缺失时框架不建缓冲（写入被丢弃），现场表现是"订阅源列表里没有这一条"。
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ValueSource, ViewDataSourceKind.Value, ContractPrimitive.F32));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TickSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
    }

    /// <inheritdoc />
    public void OnConfigurationChanged() => _configDirty = true;

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!context.StopSignal.IsStopRequested)
        {
            if (_configDirty)
            {
                // **配置热生效**：与 UI 同一条命令写回 ⇒ 活动体下一轮就按新配置发数
                _config = context.Configuration.Read<RateSourceStubConfig>();
                _configDirty = false;
            }

            var value = _config.Value;

            _ = context.Ports.TrySend(OutPort, EncodeUnit(value));
            context.Ports.Publish(ValueSource, BitConverter.GetBytes((float)value));
            context.Ports.Publish(TickSource, BitConverter.GetBytes(_tick));
            _tick++;

            _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(Math.Max(1, _config.IntervalMs)));
        }
    }
}
