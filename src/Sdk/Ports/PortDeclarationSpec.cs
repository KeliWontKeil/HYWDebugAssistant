using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>
/// **端口声明的“源”形态**（来自模块视图 XAML）：名称、方向、类型标识、位置与可拖动性。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么不直接是 <see cref="PortDeclaration"/></b>：后者要求一个已解析的
///     <see cref="ContractShape"/>，而 XAML 声明只写 `TypeId`；形状/语义要等
///     `OnRegistered` 把数据类型声明完之后才能解析。</item>
///   <item><b>解析唯一入口</b>：Hosting 与 `ModuleContract.Probe` 都按 `TypeId` 找到
///     <see cref="DataTypeDeclaration"/>，再用它的形状/语义构造 `PortDeclaration`。</item>
///   <item><b>旧模块兼容</b>：没有 XAML 声明的模块仍可直接调用 `DeclarePort(PortDeclaration)`。</item>
/// </list>
/// </remarks>
public sealed record PortDeclarationSpec(
    string Name,
    PortDirection Direction,
    string TypeId,
    PortEdge Edge = PortEdge.Auto,
    double AnchorOffset = 0.5,
    bool AnchorDraggable = false,
    TransferSemantics? Semantics = null);
