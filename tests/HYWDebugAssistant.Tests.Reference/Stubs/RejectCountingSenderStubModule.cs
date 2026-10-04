using System.Buffers.Binary;
using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Tests.Reference.Stubs;

/// <summary>
/// **"发送是否被拒"计数桩**（判据载体 · **本项目内的测试桩**，不是产品模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要它</b>：模块侧只有"`TrySend` 返回 true/false"一个事实，而**产品模块的发送计数
///     只在成功时才自增**（`_sent`）⇒ "发出去但被框架静默拒掉"这件事在**产品口径里根本观测不到**
///     （发出的计数与收到的行数**一起少**，对账恒等）。要钉住"已连通的网络上发送不得被拒"，必须有一个
///     **把拒绝次数记下来**的载体 ⇒ 用测试桩（用户："不删任何的框架能力与接口"）。</item>
///   <item><b>载荷口径</b>：与四个正式模块**逐字相同**（`RawBytes` = `{ len:u16, data:bytes[1472] }`）
///     ⇒ 可直接连 `hyw/data-monitor`。</item>
///   <item><b>节拍</b>：1 ms 一条（与三个轮询型产品模块同档）—— 单元越密，"撞上框架内部时间窗"的概率越高。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "test",
    "reject-counting-sender",
    "1.0.0",
    DisplayName = "发送拒绝计数桩（测试用）",
    Description = "1 ms 一条持续发送；分别累计「框架接受了多少次」与「框架拒绝了多少次」",
    Tags = ["test", "source"])]
public sealed class RejectCountingSenderStubModule : IModule, IActivitySource
{
    /// <summary>输出端口名。</summary>
    public const string OutPort = "out";

    /// <summary>角色 Id。</summary>
    public const string RoleId = "emit";

    /// <summary>数据源：框架**接受**（`TrySend` = true）的次数。</summary>
    public const string SentSource = "sent";

    /// <summary>数据源：框架**拒绝**（`TrySend` = false）的次数。</summary>
    public const string RejectedSource = "rejected";

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

    /// <summary>发送间隔（毫秒）—— 1 ms（与三个轮询型产品模块同档）。</summary>
    public const int IntervalMs = 1;

    private ulong _sent;
    private ulong _rejected;

    /// <inheritdoc />
    public ActivityKind Kind => ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMs);

    /// <summary>构造一个 `{ len, data }` 定长单元（与正式发送器同一口径：`data` 尾部补零）。</summary>
    /// <param name="text">要承载的文本。</param>
    /// <returns>单元字节（`2 + 1472`）。</returns>
    public static byte[] EncodeUnit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var payload = Encoding.UTF8.GetBytes(text);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, TextCapacity);

        var unit = new byte[RawBytesShape.SizeInBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(unit, (ushort)payload.Length);
        payload.CopyTo(unit.AsSpan(sizeof(ushort)));
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

        context.Declaration.DeclareRole(RoleDeclaration.Output(RoleId, new DataTypeRef(RawBytesTypeId, RawBytesVersion), OutPort, "发数"));
    }

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 先落一次样本（`Latest` 读 0 也能读：样本尺寸在首写之前是 0，先写入者定槽）
        context.Ports.Publish(SentSource, BitConverter.GetBytes(0UL));
        context.Ports.Publish(RejectedSource, BitConverter.GetBytes(0UL));

        while (!context.StopSignal.IsStopRequested)
        {
            if (context.Ports.TrySend(OutPort, EncodeUnit("tick")))
            {
                _sent++;
                context.Ports.Publish(SentSource, BitConverter.GetBytes(_sent));
            }
            else
            {
                // **框架拒绝了这一条**：端口已连通时这不该发生（发生了就是"静默丢单元"）
                _rejected++;
                context.Ports.Publish(RejectedSource, BitConverter.GetBytes(_rejected));
            }

            _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(IntervalMs));
        }
    }
}
