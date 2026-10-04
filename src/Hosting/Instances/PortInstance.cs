using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 口**实例**（成员统一路径的 ②实例化产物）：**只有声明与身份**，不含任何运行期指标。
/// </summary>
/// <remarks>
/// <para>连接锚定为 <c>实例ID.口名</c>（重锚依据，主文档 §9.3）。</para>
/// <para>
/// <b>【纪律 · 数据指标来源唯一】**本类型**不持有**任何「当前状态」字段</b>（`IsConnected` / `IsStalled` 这类
/// 布尔量**没有任何写入方**、恒为 <c>false</c>，一旦被读进 `PortView`，同一事实就有了第二个来源）。
/// 因此全部删除：
/// <list type="bullet">
///   <item><b>连接状态</b>：唯一来源 = **网络成员关系**（<c>Network.Members</c> ⇒ `PortView.IsConnected`）；</item>
///   <item><b>停流状态</b>：唯一来源 = **该口所在网络是否停流**（<c>Network.IsStalled</c>，其本身又是
///     `Sources.Count == 0` 的派生量 ⇒ 不存在"标志与成员不一致"）；</item>
///   <item>口实例只回答「我是谁、什么方向、什么契约」——**「我现在怎么样」一律从网络/绑定派生**。</item>
/// </list>
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

    /// <inheritdoc />
    public override string ToString() => $"{Name}({Direction})";
}
