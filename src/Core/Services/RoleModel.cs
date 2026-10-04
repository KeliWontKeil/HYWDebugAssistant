using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// **角色**（模块声明的功能分块）的**派生**：`m/n` 与角色间的数据流。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>角色本身来自模块声明</b>（`DeclareRole`）——本文件**不发明角色**，只从**网络成员关系**推出两件派生的量：
///     ① 每个角色**输入/输出侧连接的对端角色数**（`m` / `n`）；② **角色间的数据流**（哪两个角色之间有投递）；</item>
///   <item><b>`m/n` 的口径</b>：去重键 =（对端实例, 对端角色）；**同一条网络里同一对端角色的多个口只算 1**；
///     **没有该方向的口 ⇒ <see langword="null"/>**（呈现层写 `--`）；**有口但没连 ⇒ 0**（二者含义不同）；</item>
///   <item><b>只呈现</b>：本文件产出的东西不参与建网、兼容、停流、投递与计量，也不进组合件；</item>
///   <item><b>不复制判定</b>：逐对兼容仍走 <c>ModuleRuntime</c> 的**同一入口**（`CheckCompatibility`），
///     故角色流与真实绑定不会漂移。</item>
/// </list>
/// </remarks>
internal static class RoleModel
{
    /// <summary>角色键（`实例ID/角色Id`，稳定且跨窗可比）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="roleId">角色标识。</param>
    internal static string RoleKey(string instanceId, string roleId) => $"{instanceId}/{roleId}";

    /// <summary>口径占位（未声明角色时用）：<see cref="RoleView"/> 的呈现文本。</summary>
    /// <param name="role">角色声明。</param>
    /// <param name="instance">实例（取类型镜像）。</param>
    internal static string TypeTextOf(RoleDeclaration role, ModuleInstance instance)
    {
        var declaration = instance.FindDataType(role.Type);
        return declaration is null
            ? $"（未声明类型 {role.Type}）"
            : declaration.Shape.Describe();
    }

    /// <summary>
    /// 统计某个角色**某一侧**连接的对端**角色数**。
    /// </summary>
    /// <remarks>
    /// 做法：对**本侧每个口**，找出它参与的网络里、方向相反的对端口（本侧是输入 ⇒ 找该网络的源；本侧是输出 ⇒ 找宿），
    /// 再把对端口换算成"对端实例的哪个角色" ⇒ 用（对端实例, 对端角色键）**去重**。
    /// 找不到归属角色的对端口（对端未声明角色）⇒ 记为 `{实例}/?`（仍然算一个对端，不静默丢弃）。
    /// </remarks>
    /// <param name="instance">本实例。</param>
    /// <param name="ports">本侧口名。</param>
    /// <param name="networks">全部网络。</param>
    /// <param name="roleKeyOf">对端口 → 对端角色键（由调用方注入，保证与本实例的角色定义同源）。</param>
    /// <returns>对端角色（或占位）的去重集合。</returns>
    internal static HashSet<string> PeersOf(
        ModuleInstance instance,
        IReadOnlyList<string> ports,
        IReadOnlyList<Network> networks,
        Func<string, string, string> roleKeyOf)
    {
        var isInput = ports.Count > 0 && instance.FindPort(ports[0])?.Direction == PortDirection.Receive;
        var peers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var network in networks)
        {
            foreach (var portName in ports)
            {
                var literal = $"{instance.InstanceId}.{portName}";
                var present = network.Sources.Any(s => Member(s, literal))
                    || network.Targets.Any(t => Member(t, literal));

                if (!present)
                {
                    continue;
                }

                // 输入侧 ⇒ 对端是该网络的**源**；输出侧 ⇒ 对端是该网络的**宿**
                var counterparts = isInput ? network.Sources : network.Targets;

                foreach (var member in counterparts)
                {
                    if (string.Equals(member.InstanceId, instance.InstanceId, StringComparison.Ordinal))
                    {
                        continue;   // 自环不算对端
                    }

                    _ = peers.Add(roleKeyOf(member.InstanceId, member.PortName));
                }
            }
        }

        return peers;

        static bool Member(NetworkMember member, string literal) =>
            string.Equals(member.InstanceId, literal[..literal.LastIndexOf('.')], StringComparison.Ordinal)
            && string.Equals(member.PortName, literal[(literal.LastIndexOf('.') + 1)..], StringComparison.Ordinal);
    }

    /// <summary>
    /// 某个角色的**承载网络键集合**（该角色的口参与的全部网络）。
    /// </summary>
    /// <remarks>
    /// 用途：把**通路维度**的框架诊断（按网络统计的速率 / 投递 / 丢弃）聚合到**角色维度**
    /// ——"这块功能自己在跑多快"。与 <see cref="PeersOf"/> 用同一套"口是否在该网络里"的成员判定，
    /// 故两个口径不会漂移。
    /// </remarks>
    /// <param name="instance">本实例。</param>
    /// <param name="ports">该角色某一侧（或两侧）的口名。</param>
    /// <param name="networks">全部网络。</param>
    /// <param name="keyOf">网络 → 框架身份键（与诊断视图的 <c>Key</c> 同源）。</param>
    /// <returns>去重后的网络键集合。</returns>
    internal static HashSet<string> NetworksOf(
        ModuleInstance instance,
        IEnumerable<string> ports,
        IReadOnlyList<Network> networks,
        Func<Network, string> keyOf)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var portNames = ports as IReadOnlyList<string> ?? [.. ports];

        foreach (var network in networks)
        {
            foreach (var portName in portNames)
            {
                var literal = $"{instance.InstanceId}.{portName}";

                if (network.Sources.Any(m => Member(m, literal)) || network.Targets.Any(m => Member(m, literal)))
                {
                    _ = keys.Add(keyOf(network));
                    break;   // 该网络已计入 ⇒ 换下一个网络（避免同一网络被多个口重复计入）
                }
            }
        }

        return keys;

        static bool Member(NetworkMember member, string literal) =>
            string.Equals(member.InstanceId, literal[..literal.LastIndexOf('.')], StringComparison.Ordinal)
            && string.Equals(member.PortName, literal[(literal.LastIndexOf('.') + 1)..], StringComparison.Ordinal);
    }

    /// <summary>
    /// 枚举**角色间的数据流**（本实例视角）：本角色的输出口 → 对端角色的输入口。
    /// </summary>
    /// <remarks>
    /// 逐 (源, 宿) 对判兼容由 `ModuleRuntime.DescribePortLinks()` 完成（与绑定构建**同一入口**）⇒ 本方法只做
    /// "**口对 → 角色对**"的换算与聚合：口 → 所属角色由本模块声明唯一确定。
    /// </remarks>
    /// <param name="instance">本实例。</param>
    /// <param name="links">逐 (源, 宿) 的口级连接（已按兼容过滤）。</param>
    /// <param name="roleKeyOf">（实例, 口）→ 该实例的所属角色键（找不到 ⇒ 返回空串，调用方丢弃该条）。</param>
    /// <returns>键 =（本端角色键, 本端是否发送侧, 对端实例, 对端角色键）⇒（口对数, 网络身份键集合）。</returns>
    internal static Dictionary<(string Local, bool LocalIsSend, string PeerInstance, string PeerRole), (int Count, HashSet<string> Networks)> FlowsOf(
        ModuleInstance instance,
        IEnumerable<(NetworkMember Source, NetworkMember Target, string NetworkKey)> links,
        Func<string, string, string> roleKeyOf)
    {
        var flows = new Dictionary<(string, bool, string, string), (int Count, HashSet<string> Networks)>();

        foreach (var (source, target, networkKey) in links)
        {
            var localIsSource = string.Equals(source.InstanceId, instance.InstanceId, StringComparison.Ordinal);
            var local = localIsSource ? source : target;
            var peer = localIsSource ? target : source;

            if (!string.Equals(local.InstanceId, instance.InstanceId, StringComparison.Ordinal))
            {
                continue;   // 本条连接与本实例无关
            }

            var localRole = roleKeyOf(local.InstanceId, local.PortName);
            var peerRole = roleKeyOf(peer.InstanceId, peer.PortName);

            if (localRole.Length == 0 || peerRole.Length == 0)
            {
                continue;   // 任一端没有归属角色（未声明角色）⇒ 本层不表达（它们仍由端口标记表达）
            }

            // **本端是源 ⇒ 数据从本角色的输出侧流出**（发送侧）；本端是宿 ⇒ 流入本角色的输入侧
            var key = (localRole, localIsSource, peer.InstanceId, peerRole);

            if (!flows.TryGetValue(key, out var entry))
            {
                entry = (0, new HashSet<string>(StringComparer.Ordinal));
            }

            entry.Networks.Add(networkKey);
            flows[key] = (entry.Count + 1, entry.Networks);   // 值元组取出即副本 ⇒ 必须写回
        }

        return flows;
    }

    /// <summary>某个（实例, 口）所属的**角色键**（找不到 ⇒ 空串）。</summary>
    /// <param name="instance">实例（可为 null）。</param>
    /// <param name="portName">口名。</param>
    internal static string RoleKeyOf(ModuleInstance? instance, string portName)
    {
        if (instance is null)
        {
            return string.Empty;
        }

        foreach (var role in instance.Roles)
        {
            if (role.Inputs.Contains(portName, StringComparer.Ordinal)
                || role.Outputs.Contains(portName, StringComparer.Ordinal))
            {
                return RoleKey(instance.InstanceId, role.Id);
            }
        }

        return string.Empty;
    }
}
