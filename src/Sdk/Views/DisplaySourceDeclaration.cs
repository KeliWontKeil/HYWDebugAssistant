using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **一条显示源**的代码级声明（模块在 <c>OnRegistered</c> 里交给框架）。
/// </summary>
/// <remarks>
/// <para>
/// 显示源 = 模块愿意让界面去读的一条数据通道（最新值 / 序列 / 文本日志 / 行集…）。
/// 框架据此建**订阅缓冲**：单值走"最新值槽"，其余走"环形缓冲"。
/// </para>
/// <para>
/// <b>它是契约，不是界面</b>：声明"我有哪些源"与"界面把它们画在哪"是两件事 ——
/// 前者决定框架给模块准备什么缓冲、能否接收写入；后者由模块自己的视图负责。
/// 声明缺失时写入会被丢弃（框架不猜一个没登记的名字）。
/// </para>
/// </remarks>
/// <param name="Name">源名（模块内唯一；界面按这个名字取数）。</param>
/// <param name="Kind">源的类型（决定缓冲形态）。</param>
/// <param name="ValueType">数值型源的值类型（<see cref="ViewDataSourceKind.Value"/> / <see cref="ViewDataSourceKind.Sequence"/> 时给）。</param>
/// <param name="Unit">单位文本（空 = 无）。</param>
public sealed record DisplaySourceDeclaration(
    string Name,
    ViewDataSourceKind Kind,
    ContractPrimitive? ValueType = null,
    string? Unit = null);
