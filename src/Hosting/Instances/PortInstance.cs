using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 口**实例**（成员统一路径的 ②实例化产物）：声明 + 运行时状态。
/// </summary>
/// <remarks>
/// <para>连接锚定为 <c>实例ID.口名</c>（重锚依据，主文档 §9.3）。</para>
/// <para>运行时的队列/缓冲/绑定归属在 M1-5（数据面）加入；本类型只承载**声明 + 停流状态**。</para>
/// <para>
/// **【不再持有"是否已连接"】**：曾有一个 `IsConnected` 字段（既无写入方也无读取方，恒为 <c>false</c>）；
/// "连接状态"的**唯一来源**是**网络成员关系**（`IRuntimeQuery.GetNetworks()` / `PortView.IsConnected`
/// ——由 <c>RuntimeServices.IsPortConnected</c> 派生）。悬空的第二事实源已按用户裁定整体删除（ADR-0048 §8 · roadmap v1.59）。
/// </para>
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

    /// <summary>是否处于停流（上游无输入 / Disabled / 悬空）——**合法状态**，非错误。</summary>
    public bool IsStalled { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name}({Direction})";
}
