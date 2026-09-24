using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 口**实例**（成员统一路径的 ②实例化产物）：声明 + 运行时状态。
/// </summary>
/// <remarks>
/// <para>连接锚定为 <c>实例ID.口名</c>（重锚依据，主文档 §9.3）。</para>
/// <para>运行时的队列/缓冲/绑定归属在 M1-5（数据面）加入；本类型先承载声明与连接状态。</para>
/// </remarks>
public sealed class PortInstance
{
    internal PortInstance(PortDeclaration declaration)
    {
        Declaration = declaration;
    }

    /// <summary>口声明（不可变）。</summary>
    public PortDeclaration Declaration { get; }

    /// <summary>口名（模块内唯一）。</summary>
    public string Name => Declaration.Name;

    /// <summary>方向（两态）。</summary>
    public PortDirection Direction => Declaration.Direction;

    /// <summary>是否已建立网络绑定（M1-6 由绑定流程置位）。</summary>
    public bool IsConnected { get; internal set; }

    /// <summary>是否处于停流（上游无输入 / Disabled / 悬空）——**合法状态**，非错误。</summary>
    public bool IsStalled { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name}({Direction})";
}
