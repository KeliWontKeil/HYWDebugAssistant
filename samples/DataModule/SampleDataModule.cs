using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.DataModule;

/// <summary>
/// **纯数据变换样例**：收到一个单元原样转发，并把收到的个数发布到界面。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>没有外部接入</b>：数据全部来自端口，模块之间靠建网连接；</item>
///   <item><b>数据契约由模块自己声明</b>：<c>sample/pulse</c> 是这个模块的约定，
///     对端要连接就必须声明同一标识、同一形状（框架只比较声明，不理解它是什么量）；</item>
///   <item><b>没有自绘界面</b>：不声明视图工厂是合法形态，窗口只显示身份与状态。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "sample",
    "data-module",
    DisplayName = "示例数据模块",
    Description = "纯数据变换的最小样例：一进一出、自己声明数据契约、发布一个计数",
    Tags = ["sample"],
    ParticipatesInDataFlow = true,
    WindowWidth = 320,
    WindowHeight = 140,
    MinWidth = 320,
    MinHeight = 140)]
public sealed class SampleDataModule : IModule
{
    /// <summary>接收口。</summary>
    public const string InPort = "in";

    /// <summary>发送口。</summary>
    public const string OutPort = "out";

    /// <summary>角色标识（一个分块、一种类型、一进一出）。</summary>
    public const string RoleId = "pass-through";

    /// <summary>显示源：已转发的单元数。</summary>
    public const string CountSource = "count";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>本模块声明的数据类型标识（模块间约定，模块自己携带）。</summary>
    public const string TypeId = "sample/pulse";

    /// <summary>本模块声明的数据类型形状：<c>{ seq: u64, value: f32 }</c>（12 字节）。</summary>
    public static readonly ContractShape Shape = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    private IModuleContext? _context;
    private ulong _forwarded;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // ① 数据类型（自己携带的约定）→ ② 两个口 → ③ 角色 → ④ 界面契约
        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));

        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
        {
            TypeId = TypeId,
            Edge = PortEdge.Left,
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
        {
            TypeId = TypeId,
            Edge = PortEdge.Right,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(RoleId, TypeId, InPort, OutPort, "原样转发"));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(CountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));

        context.Ports.SetReceiveHandler(InPort, OnReceive);
    }

    /// <inheritdoc />
    public void OnActivated() => _context?.Display.SetText(StateSource, "就绪");

    /// <summary>收到一个单元：原样从 <c>out</c> 发出，并更新计数。</summary>
    /// <param name="payload">数据单元（长度 = 契约尺寸）。</param>
    private void OnReceive(ReadOnlySpan<byte> payload)
    {
        if (_context is null)
        {
            return;
        }

        // 校验交给框架：投递按契约尺寸，模块只做自己的事。
        _ = _context.Ports.TrySend(OutPort, payload);

        _forwarded++;
        Span<byte> counter = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(counter, _forwarded);
        _context.Ports.Publish(CountSource, counter);
    }
}
