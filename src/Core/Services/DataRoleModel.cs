using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// **数据角色的派生**（纯函数）【ADR-0048】：角色 = （**方向** × **类型身份**），在模块内聚合。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只呈现</b>：本文件产出的键与集合**不参与**建网、兼容判定、停流、投递与计量（R9）；
///     ⇒ 因此它**不写入任何运行时状态**（R10 的"只读"判据靠这一点成立）；</item>
///   <item><b>类型身份</b>（R2）：已声明类型 ⇒ `标识@v版本`；未声明（旧模块）⇒ **结构描述**（`ContractShape.Describe()`）。
///     **不得用呈现文本一律代替**——那会把"不同标识但形状相同"合并（ADR-0051 明确二者不是同一种量）；</item>
///   <item><b>聚合</b>（R1）：同方向 + 同类型身份的口属于**同一个角色**（一个角色可有多个口）；</item>
///   <item><b>零口</b>（R4）：没有数据口 ⇒ 角色集合为**空**（调用方不画方块）。</item>
/// </list>
/// </remarks>
internal static class DataRoleModel
{
    /// <summary>类型身份的**结构回落**（未声明类型时使用）。</summary>
    /// <param name="port">口实例。</param>
    internal static string StructuralTypeKey(PortInstance port) => port.Declaration.Shape.Describe();

    /// <summary>把类型身份与方向压成**角色键**（`send:{TypeKey}` / `recv:{TypeKey}`）。</summary>
    /// <param name="isSend">是否发送角色。</param>
    /// <param name="typeKey">类型身份。</param>
    internal static string RoleKey(bool isSend, string typeKey) => $"{(isSend ? "send" : "recv")}:{typeKey}";

    /// <summary>枚举一个实例的**角色**（按"发送在前、类型键字典序"稳定排序）。</summary>
    /// <param name="instance">实例。</param>
    /// <param name="typeKeyOf">口 → 类型身份（由调用方注入，保证与兼容判定同源）。</param>
    internal static List<RoleAccumulator> Collect(
        ModuleInstance instance,
        Func<PortInstance, string> typeKeyOf)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(typeKeyOf);

        var byKey = new Dictionary<string, RoleAccumulator>(StringComparer.Ordinal);

        foreach (var port in instance.Ports)
        {
            var isSend = port.Direction == PortDirection.Send;
            var typeKey = typeKeyOf(port);
            var key = RoleKey(isSend, typeKey);

            if (!byKey.TryGetValue(key, out var role))
            {
                role = new RoleAccumulator(key, isSend, typeKey, port.Declaration.Shape.Describe());
                byKey[key] = role;
            }

            role.Ports.Add(port.Name);
        }

        // 稳定排序：**发送角色在前**，同方向按类型键字典序（UI 渲染顺序稳定 ⇒ 断言可写）
        return [.. byKey.Values
            .OrderByDescending(role => role.IsSend)
            .ThenBy(role => role.TypeKey, StringComparer.Ordinal)];
    }

    /// <summary>角色累加器（收集同角色的多个口）。</summary>
    internal sealed class RoleAccumulator(string key, bool isSend, string typeKey, string typeText)
    {
        internal string Key { get; } = key;

        internal bool IsSend { get; } = isSend;

        internal string TypeKey { get; } = typeKey;

        internal string TypeText { get; } = typeText;

        internal List<string> Ports { get; } = [];

        /// <summary>该角色下已建立的口级连接数（由调用方填充）。</summary>
        internal int ConnectionCount { get; set; }
    }

    /// <summary>
    /// **口级连接**（源口 → 宿口）：与绑定构建**同一判定**（逐对兼容 + 方向合法）。
    /// </summary>
    /// <remarks>
    /// 用**网络声明 + 逐对兼容**而不是"已建立的绑定"：这样**停流网络**的成员关系也能呈现
    /// （停流是合法状态，用户仍需要看到"本来连给谁"）。判定规则与 `RebuildBindingsCore` 完全一致
    /// （同一 `CheckCompatibility` + 同一方向检查），故两者不会漂移。
    /// </remarks>
    internal readonly record struct PortLink(NetworkMember Source, NetworkMember Target, string NetworkKey);
}
