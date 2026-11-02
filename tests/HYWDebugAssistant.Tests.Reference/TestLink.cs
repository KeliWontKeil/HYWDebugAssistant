using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Services;
using Shouldly;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **验收场景里的建网入口**：只有"端口标签同名"这一条产品路径（显式 Connect API 已删除）。
/// </summary>
/// <remarks>
/// 与用户操作同一条链路：两次 <c>UpdatePortNetwork</c> ⇒ 一次推导 ⇒ 命名网络。
/// 验收场景不再能"绕过推导直接塞一条网络"。
/// </remarks>
internal static class TestLink
{
    /// <summary>给两端打同名标签并断言网络已建立。</summary>
    /// <param name="services">运行时服务。</param>
    /// <param name="source">源口。</param>
    /// <param name="target">宿口。</param>
    /// <param name="name">网络名（标签）。</param>
    public static void Link(RuntimeServices services, PortRef source, PortRef target, string name)
    {
        services.UpdatePortNetwork(source.InstanceId, source.PortName, name).Succeeded
            .ShouldBeTrue($"给源口打标签失败：{source.InstanceId}.{source.PortName}");
        services.UpdatePortNetwork(target.InstanceId, target.PortName, name).Succeeded
            .ShouldBeTrue($"给宿口打标签失败：{target.InstanceId}.{target.PortName}");

        services.GetNetworks()
            .Any(network => string.Equals(network.Name, name, StringComparison.Ordinal))
            .ShouldBeTrue($"「{name}」应已建立（两端标签同名）");
    }
}
