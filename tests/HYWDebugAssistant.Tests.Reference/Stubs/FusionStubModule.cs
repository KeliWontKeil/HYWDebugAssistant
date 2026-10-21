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

/// <summary>融合策略（与判据测试一一对应）。</summary>
public enum FusionStrategy
{
    /// <summary>后到者覆盖（默认）。</summary>
    Latest = 0,

    /// <summary>两路最近值的算术平均。</summary>
    Average = 1,

    /// <summary>第 1 路优先（第 2 路只在第 1 路没数据时生效）。</summary>
    Priority = 2,
}

/// <summary>融合桩模块的配置（测试里用"与 UI 同一条命令"写回）。</summary>
public sealed class FusionStubConfig
{
    /// <summary>策略。</summary>
    public FusionStrategy Mode { get; set; } = FusionStrategy.Latest;

    /// <summary>是否把融合结果继续发给下游。</summary>
    public bool ForwardMerged { get; set; } = true;
}

/// <summary>
/// **多源融合桩模块**（判据载体 · **本项目内的测试桩**，不是产品模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：「**一个模块两个输入口 + 一个输出口**」这条形态（多源 / 角色分块 / 角色速率）
///     在四个基础模块里**没有对应物** —— 但它是**框架能力**，必须继续被判据网覆盖 ⇒ 用测试桩承载；</item>
///   <item><b>与真实模块的关系</b>：链路两端仍是**四个正式模块**（`text-sender` / `hex-sender` 作为发布者、
///     `data-monitor` 作为下游显示）；本桩只承担"多源合并"这一件事；</item>
///   <item><b>载荷口径</b>：口的形状 = 与发送器**逐字相同**的 `RawBytes`（`{ len:u16, data:bytes[1472] }`）
///     ⇒ 无需任何转换即可连通；桩把 `data` 里的 UTF-8 文本解析成数值（测试里发的是 `"5.0"` 这种文本）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "test",
    "fusion-stub",
    "1.0.0",
    DisplayName = "多源融合桩（测试用）",
    Description = "两路输入 + 一路输出：合并策略由模块自己决定（框架永不仲裁）",
    Tags = ["test", "fuse"])]
public sealed class FusionStubModule : IModule, IActivitySource
{
    /// <summary>第 1 路输入口。</summary>
    public const string InputPort1 = "in1";

    /// <summary>第 2 路输入口。</summary>
    public const string InputPort2 = "in2";

    /// <summary>输出口。</summary>
    public const string OutputPort = "out";

    /// <summary>数据源：融合结果（数值）。</summary>
    public const string FusedSource = "fused";

    /// <summary>数据源：第 1 路累计数。</summary>
    public const string Count1Source = "n1";

    /// <summary>数据源：第 2 路累计数。</summary>
    public const string Count2Source = "n2";

    /// <summary>角色 Id（**两入一出 = 一块功能**）。</summary>
    public const string RoleId = "fuse";

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

    private static readonly ContractLayout Layout = ContractLayout.FromShape(RawBytesShape);

    private readonly Lock _gate = new();

    private float? _first;
    private float? _second;
    private long _count1;
    private long _count2;
    private FusionStubConfig _config = new();

    /// <inheritdoc />
    public Sdk.Lifecycle.ActivityKind Kind => Sdk.Lifecycle.ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMilliseconds(20);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(RawBytesTypeId, RawBytesVersion, RawBytesShape));

        context.Declaration.DeclarePort(new PortDeclaration(InputPort1, PortDirection.Receive, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
        });
        context.Declaration.DeclarePort(new PortDeclaration(InputPort2, PortDirection.Receive, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
        });
        context.Declaration.DeclarePort(new PortDeclaration(OutputPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
        });

        // **两入一出 = 一块功能分块**（角色由模块自己声明，框架不派生）
        // 两入一出 ⇒ **直接构造**（`Both` 只收单口；多口角色用构造器）
        context.Declaration.DeclareRole(new RoleDeclaration(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            [InputPort1, InputPort2],
            [OutputPort],
            "融合"));

        // 融合结果与两路计数（测试按名字读 ⇒ 代码里声明）。
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(FusedSource, ViewDataSourceKind.Value, ContractPrimitive.F64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(Count1Source, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(Count2Source, ViewDataSourceKind.Value, ContractPrimitive.U64));
    }

    /// <inheritdoc />
    public void OnConfigurationChanged() => _configDirty = true;

    private bool _configDirty = true;

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!context.StopSignal.IsStopRequested)
        {
            EnsureConfig(context);

            while (context.Ports.TryReceive(out var buffer, out var sourcePort))
            {
                try
                {
                    Handle(context, sourcePort, buffer.Span);
                }
                finally
                {
                    context.Ports.ReleaseBuffer(buffer);
                }
            }

            _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>
    /// **配置热生效**：`OnConfigurationChanged` 只置脏标记；活动体下一轮、以及直接调 <see cref="Handle"/>
    /// 的测试路径都会重新 `Read` ⇒ 两条路径读到的是**同一份**配置（不会出现"测试里改了策略却不生效"）。
    /// </summary>
    private void EnsureConfig(IModuleContext context)
    {
        if (_configDirty)
        {
            _config = context.Configuration.Read<FusionStubConfig>();
            _configDirty = false;
        }
    }

    /// <summary>处理一个到达的单元（**测试可直接调用**）。</summary>
    /// <param name="context">上下文。</param>
    /// <param name="sourcePort">到达的口名（**来源用口区分**）。</param>
    /// <param name="payload">载荷。</param>
    public void Handle(IModuleContext context, string? sourcePort, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureConfig(context);

        if (!PayloadReader.TryCreate(payload, Layout, out var reader))
        {
            return;
        }

        var length = reader.ReadU16("len");
        var text = Encoding.UTF8.GetString(reader.ReadBytes("data")[..length]).TrimEnd('\0', ' ');

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        float fused;

        lock (_gate)
        {
            if (string.Equals(sourcePort, InputPort1, StringComparison.Ordinal))
            {
                _first = value;
                _count1++;
            }
            else if (string.Equals(sourcePort, InputPort2, StringComparison.Ordinal))
            {
                _second = value;
                _count2++;
            }
            else
            {
                return;
            }

            fused = _config.Mode switch
            {
                FusionStrategy.Average when _first is { } a && _second is { } b => (a + b) / 2f,
                FusionStrategy.Priority => _first ?? _second ?? 0f,
                _ => value,   // Latest：后到者覆盖
            };
        }

        // 声明面把 `fused` 记为 8 字节浮点（声明是数据源宽度的唯一来源，读方按它切片）
        context.Ports.Publish(FusedSource, BitConverter.GetBytes((double)fused));
        context.Ports.Publish(Count1Source, BitConverter.GetBytes((ulong)Interlocked.Read(ref _count1)));
        context.Ports.Publish(Count2Source, BitConverter.GetBytes((ulong)Interlocked.Read(ref _count2)));

        if (_config.ForwardMerged)
        {
            // 融合结果**作为新的发布者**发给下游（文本形式，便于显示器直接显示与断言）
            var forwarded = Encoding.UTF8.GetBytes(fused.ToString("0.###", CultureInfo.InvariantCulture));
            var unit = new byte[sizeof(ushort) + TextCapacity];
            BinaryPrimitives.WriteUInt16LittleEndian(unit, (ushort)forwarded.Length);
            forwarded.CopyTo(unit.AsSpan(sizeof(ushort)));
            _ = context.Ports.TrySend(OutputPort, unit);
        }
    }
}
