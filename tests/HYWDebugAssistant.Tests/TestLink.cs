using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Services;
using Shouldly;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **测试里的建网入口**：只有"端口标签同名"这一条产品路径（显式 Connect API 已删除）。
/// </summary>
/// <remarks>
/// 用标签建网与用户操作**同一条链路**：两次 <c>UpdatePortNetwork</c> ⇒ 一次推导 ⇒ 命名网络。
/// 测试不再能"绕过推导直接塞一条网络"——那正是"应用层看着能连、底层规则不同"的来源。
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
        SetLabel(services, source, name).ShouldBeTrue($"给源口打标签失败：{source.InstanceId}.{source.PortName}");
        SetLabel(services, target, name).ShouldBeTrue($"给宿口打标签失败：{target.InstanceId}.{target.PortName}");
        HasNetwork(services, name).ShouldBeTrue($"「{name}」应已建立（两端标签同名）");
    }

    /// <summary>给两端打同名标签，返回**是否真的建成了网络**（不兼容 = 标签保留、网络不建）。</summary>
    /// <param name="services">运行时服务。</param>
    /// <param name="source">源口。</param>
    /// <param name="target">宿口。</param>
    /// <param name="name">网络名（标签）。</param>
    /// <returns>是否建立了该名字的网络。</returns>
    public static bool TryLink(RuntimeServices services, PortRef source, PortRef target, string name)
    {
        _ = SetLabel(services, source, name);
        _ = SetLabel(services, target, name);
        return HasNetwork(services, name);
    }

    /// <summary>网络是否存在（按名字；测试用的名字互不重复）。</summary>
    /// <param name="services">运行时服务。</param>
    /// <param name="name">网络名。</param>
    /// <returns>是否存在。</returns>
    public static bool HasNetwork(RuntimeServices services, string name) =>
        services.GetNetworks().Any(network => string.Equals(network.Name, name, StringComparison.Ordinal));

    private static bool SetLabel(RuntimeServices services, PortRef port, string name) =>
        services.UpdatePortNetwork(port.InstanceId, port.PortName, name).Succeeded;
}
