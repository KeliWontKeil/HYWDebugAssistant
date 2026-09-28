using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 模块**实例工厂**：按 Definition 创建实例与成员实例表——成员统一路径的 ②实例化（基线 §4.10.2）。
/// </summary>
/// <remarks>
/// 流程：<c>new 模块对象</c> → <c>OnRegistered(context)</c>（模块在此声明口与视图）→ **成员实例化**（口实例 + 视图宿主）。
/// 钩子异常不逸出：捕获后转 Issue，实例仍返回（其活动此后不再启动）。
/// <para>本步不创建线程与队列（属数据面 M1-5）；实例状态为 <see cref="ModuleInstanceState.Registered"/>。</para>
/// </remarks>
public static class ModuleInstanceFactory
{
    /// <summary>创建实例。</summary>
    /// <param name="instanceId">实例 ID（工作现场内唯一、稳定）。</param>
    /// <param name="definition">模块定义。</param>
    /// <param name="issues">框架侧上报口。</param>
    public static ModuleInstance Create(string instanceId, Registry.ModuleDefinition definition, Diagnostics.IIssueSink issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(issues);

        var module = (Sdk.Lifecycle.IModule)Activator.CreateInstance(definition.EntryType)!;
        var context = new Context.ModuleContext(instanceId, definition.Id, issues);
        var objectPath = $"module:{instanceId}";

        // ① 声明（异常边界：钩子异常不得逸出）
        try
        {
            module.OnRegistered(context);
        }
        catch (Exception ex)
        {
            issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                Sdk.Reporting.IssueCategory.ModuleError,
                Sdk.Reporting.IssueSeverity.Error,
                $"模块在 OnRegistered 中抛出异常：{ex.GetType().Name} — {ex.Message}"));
        }

        // ② 成员实例化：口实例（含口名唯一性校验）+ **数据类型镜像**（ADR-0046 D2）
        var ports = BuildPortInstances(context, objectPath, issues);
        var dataTypes = BuildDataTypeMirror(context, objectPath, issues);

        // ②b **角色**（功能分块，ADR-0052）：在口与类型都就绪之后校验（口存在且方向相符、类型可解析、口归属唯一）
        var roles = BuildRoles(context, ports, dataTypes, objectPath, issues);

        // ②c **内部处理顺序**（角色 → 角色，ADR-0052）：两端角色必须已声明
        var flows = BuildFlows(context, roles, objectPath, issues);

        // ③ 视图宿主（M1-9 由 UI 层按声明渲染；此处仅保留声明）
        var instance = new ModuleInstance(
            instanceId, definition, module, ports, dataTypes, roles, flows, context.DeclaredView, context)
        {
            State = ModuleInstanceState.Registered,
        };

        return instance;
    }

    /// <summary>
    /// 校验并实例化**角色**（功能分块）【ADR-0052】：一种类型 + 输入口/输出口；口必须存在且方向相符；
    /// **一个口最多属于一个角色**；Id 唯一；类型必须能在本模块解析。
    /// </summary>
    /// <remarks>
    /// 不合法的声明**报 Issue 并忽略该条**（其余照常）；一个口被两个角色抢 ⇒ 只保留**先声明**的那条。
    /// <b>口数不设限、运行期也不提示</b>：&gt;2 口既可能是"多路合成一路"（合法，例如参考融合器 2 入 1 出），
    /// 也可能是"两件事挤成一块"——框架判不了（"声明 ≠ 证明"）。该建议只出现在 **SDK 自检**
    /// （`ModuleContract` 的 `Advisories`，与 `Problems` 分通道），**不进运行期 Issue 中心**。
    /// </remarks>
    private static List<Sdk.Contracts.RoleDeclaration> BuildRoles(
        Context.ModuleContext context,
        IReadOnlyList<PortInstance> ports,
        IReadOnlyList<DataTypeDeclaration> dataTypes,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var roles = new List<Sdk.Contracts.RoleDeclaration>(context.DeclaredRoles.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);   // 口名 → 已占用它的角色 Id
        var knownTypes = new HashSet<DataTypeRef>(dataTypes.Select(t => t.Reference));

        foreach (var role in context.DeclaredRoles)
        {
            if (string.IsNullOrWhiteSpace(role.Id))
            {
                Report("角色的标识不能为空（已忽略该条）");
                continue;
            }

            if (!ids.Add(role.Id))
            {
                Report($"角色标识重复：'{role.Id}'（模块内唯一；已忽略重复声明）");
                continue;
            }

            if (!knownTypes.Contains(role.Type))
            {
                Report($"角色 '{role.Id}' 引用了本模块未声明的数据类型 {role.Type}（已忽略该条）");
                continue;
            }

            if (role.Inputs.Count == 0 && role.Outputs.Count == 0)
            {
                Report($"角色 '{role.Id}' 既没有输入口也没有输出口（一个分块至少要有一个口；已忽略该条）");
                continue;
            }

            if (!ValidatePorts(role.Id, role.Type, role.Inputs, Sdk.Ports.PortDirection.Receive)
                || !ValidatePorts(role.Id, role.Type, role.Outputs, Sdk.Ports.PortDirection.Send))
            {
                continue;
            }

            // 口归属唯一：先声明者优先（同一口被两个角色声明 ⇒ 后者被忽略）
            var taken = role.Inputs.Concat(role.Outputs)
                .FirstOrDefault(name => claimed.ContainsKey(name));

            if (taken is not null)
            {
                Report($"口 '{taken}' 已属于角色 '{claimed[taken]}'（一个口最多属于一个角色；已忽略角色 '{role.Id}'）");
                continue;
            }

            foreach (var name in role.Inputs.Concat(role.Outputs))
            {
                claimed[name] = role.Id;
            }

            // **不对口数设限，也不在运行期提示**（ADR-0052 Q3 的落地口径）：框架分不清"2 路合成 1 路"
            //（多源融合器，合法且已裁定）与"两件不相关的事"（"声明 ≠ 证明"）—— 在自家正确模块的 **Issue 中心**
            // 里报警，用户会以为模块写错了。该"建议"只存在于 **SDK 自检**（`ModuleContract` 的 `Advisories`，
            // 与 `Problems` 分通道），作者写模块时就能看到，现场不留噪。
            roles.Add(role);
        }

        return roles;

        bool ValidatePorts(
            string roleId,
            Sdk.Contracts.DataTypeRef roleType,
            IReadOnlyList<string> names,
            Sdk.Ports.PortDirection expected)
        {
            foreach (var name in names)
            {
                if (names.Count(n => string.Equals(n, name, StringComparison.Ordinal)) > 1)
                {
                    Report($"角色 '{roleId}' 的口 '{name}' 重复出现（已忽略该条）");
                    return false;
                }

                var port = ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                if (port is null)
                {
                    Report($"角色 '{roleId}' 引用了不存在的口 '{name}'（已忽略该条）");
                    return false;
                }

                if (port.Direction != expected)
                {
                    Report($"角色 '{roleId}' 的{(expected == Sdk.Ports.PortDirection.Receive ? "输入" : "输出")}口 '{name}' " +
                        $"实际方向是 {port.Direction}（已忽略该条）");
                    return false;
                }

                // 【2026-09 补充裁定 α】角色的**输入口与输出口必须承载同一种数据类型**（= `roleType`）。
                // ⇒ 类型转换 = **两个数据角色**（"收 A" + "发 B"）+ DeclareFlow（叠加层上是两个方块）。
                // 口**未声明类型**（旧模块形态）⇒ 无可比对象，跳过（与"可声明 ≠ 必须声明"一致）。
                if (port.Declaration.Type is { } portType && portType != roleType)
                {
                    Report($"角色 '{roleId}' 的{(expected == Sdk.Ports.PortDirection.Receive ? "输入" : "输出")}口 '{name}' " +
                        $"类型是 {portType}，与角色的类型 {roleType} 不一致（一个角色的输入口与输出口必须同一种数据类型；" +
                        "类型转换请拆成两个角色 + DeclareFlow；已忽略该条）");
                    return false;
                }
            }

            return true;
        }

        void Report(string message) => issues.Report(objectPath, new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Warning,
            message));
    }

    /// <summary>
    /// 校验并实例化**内部处理顺序**（`角色 → 角色`）【ADR-0052】：两端角色必须已声明，且不得自环；
    /// 同（起点, 终点）去重。不合法的**报 Issue 并忽略该条**。
    /// </summary>
    private static List<Sdk.Ports.FlowDeclaration> BuildFlows(
        Context.ModuleContext context,
        IReadOnlyList<Sdk.Contracts.RoleDeclaration> roles,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var flows = new List<Sdk.Ports.FlowDeclaration>(context.DeclaredFlows.Count);
        var known = new HashSet<string>(roles.Select(r => r.Id), StringComparer.Ordinal);
        var seen = new HashSet<(string From, string To)>();

        foreach (var flow in context.DeclaredFlows)
        {
            if (string.IsNullOrWhiteSpace(flow.FromRoleId) || string.IsNullOrWhiteSpace(flow.ToRoleId))
            {
                Report($"内部处理关系的角色标识不能为空：{flow}（已忽略该条）");
                continue;
            }

            if (string.Equals(flow.FromRoleId, flow.ToRoleId, StringComparison.Ordinal))
            {
                Report($"内部处理关系不得自环：{flow}（已忽略该条）");
                continue;
            }

            if (!known.Contains(flow.FromRoleId) || !known.Contains(flow.ToRoleId))
            {
                Report($"内部处理关系引用了未声明的角色：{flow}（已忽略该条）");
                continue;
            }

            if (!seen.Add((flow.FromRoleId, flow.ToRoleId)))
            {
                Report($"内部处理关系重复声明：{flow}（已忽略重复项）");
                continue;
            }

            flows.Add(flow);
        }

        return flows;

        void Report(string message) => issues.Report(objectPath, new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Warning,
            message));
    }

    /// <summary>
    /// 建立数据类型的**机械镜像**（标识/版本/尺寸/语义/计量/解码器句柄）：同（标识, 版本）重复即报 Issue 并跳过。
    /// </summary>
    /// <remarks>
    /// 框架**不解释**类型语义——只登记"有什么类型、多大、怎么解"，供兼容判定与呈现期回调使用。
    /// </remarks>
    private static List<DataTypeDeclaration> BuildDataTypeMirror(
        Context.ModuleContext context,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var types = new List<DataTypeDeclaration>(context.DeclaredDataTypes.Count);
        var seen = new HashSet<DataTypeRef>();

        foreach (var declaration in context.DeclaredDataTypes)
        {
            if (!seen.Add(declaration.Reference))
            {
                issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                    Sdk.Reporting.IssueCategory.InterfaceError,
                    Sdk.Reporting.IssueSeverity.Error,
                    $"数据类型重复声明：{declaration.Reference}（同一（标识, 版本）只能声明一次；重复声明已忽略）"));
                continue;
            }

            types.Add(declaration);
        }

        // 口引用了未声明的类型 ⇒ 明确报 Issue（不静默当作"没声明"）
        foreach (var port in context.DeclaredPorts)
        {
            if (port.Type is { } reference && !seen.Contains(reference))
            {
                issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                    Sdk.Reporting.IssueCategory.InterfaceError,
                    Sdk.Reporting.IssueSeverity.Warning,
                    $"口 '{port.Name}' 引用了未声明的数据类型 {reference}——该口按**结构**参与兼容判定",
                    port.Name));
            }
        }

        return types;
    }

    private static List<PortInstance> BuildPortInstances(Context.ModuleContext context, string objectPath, Diagnostics.IIssueSink issues)
    {
        var ports = new List<PortInstance>(context.DeclaredPorts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in context.DeclaredPorts)
        {
            if (!seen.Add(declaration.Name))
            {
                // 口名唯一性：重复即报 Issue 并**跳过**该声明（不静默改名）
                issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                    Sdk.Reporting.IssueCategory.InterfaceError,
                    Sdk.Reporting.IssueSeverity.Error,
                    $"口名重复：'{declaration.Name}'（口名必须在模块内唯一；重复声明已忽略）",
                    declaration.Name));
                continue;
            }

            ports.Add(new PortInstance(declaration));
        }

        return ports;
    }
}
