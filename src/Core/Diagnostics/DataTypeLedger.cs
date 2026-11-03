using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Diagnostics;

/// <summary>
/// **数据类型台账**：把当前全场实例声明过的类型按标识聚合，同标识出现两种以上形状即报一条。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>框架不定义类型，只对账</b>：这里只按"标识 + 形状"做机械比较，不解释它是什么量；</item>
///   <item><b>为什么需要</b>：同标识必须是同一种约定。两个模块各写一份声明时，形状一旦分叉，
///     连接期只会报"形状不一致"，用户看不出真正原因是"同一个标识有两种形状"；</item>
///   <item><b>去重</b>：同一条冲突只报一次；冲突消失后再出现会重新报（按"当前仍存在的冲突"收敛）；</item>
///   <item>只在实例增删时重算：N 为实例数（几十），成本可忽略。</item>
/// </list>
/// </remarks>
internal sealed class DataTypeLedger
{
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>按当前实例集合重算一次，并报出**新出现**的同标识冲突。</summary>
    /// <param name="instances">当前全场实例。</param>
    /// <param name="issues">框架侧上报口。</param>
    public void Reconcile(IReadOnlyCollection<ModuleInstance> instances, IIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(issues);

        var byId = new Dictionary<string, List<(ContractShape Shape, string InstanceId)>>(StringComparer.Ordinal);

        foreach (var instance in instances)
        {
            foreach (var type in instance.DataTypes)
            {
                if (!byId.TryGetValue(type.TypeId, out var declarations))
                {
                    declarations = [];
                    byId[type.TypeId] = declarations;
                }

                declarations.Add((type.Shape, instance.InstanceId));
            }
        }

        var current = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (typeId, declarations) in byId)
        {
            if (declarations.Count < 2)
            {
                continue;
            }

            // 形状的可读描述就是结构身份的规范形式：结构相等的两份形状描述相同。
            var groups = declarations
                .GroupBy(declaration => declaration.Shape.Describe(), StringComparer.Ordinal)
                .Select(group => (Shape: group.Key, Instance: group.First().InstanceId))
                .OrderBy(group => group.Shape, StringComparer.Ordinal)
                .ToList();

            if (groups.Count < 2)
            {
                continue;
            }

            var key = typeId + "|" + string.Join("|", groups.Select(group => group.Shape));
            current.Add(key);

            if (!_reported.Add(key))
            {
                continue;
            }

            issues.Report($"type:{typeId}", new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Error,
                CoreText.Get(
                    "core.runtime.dataTypeConflict",
                    typeId,
                    groups[0].Shape,
                    groups[1].Shape,
                    groups[0].Instance,
                    groups[1].Instance)));
        }

        // 冲突消失后再次出现要能重新报：只保留当前仍存在的冲突键。
        _reported.IntersectWith(current);
    }
}
