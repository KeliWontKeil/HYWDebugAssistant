using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 模块**实例工厂**：按 Definition 创建实例与成员实例表——成员统一路径的 ②实例化（基线 §4.10.2）。
/// </summary>
/// <remarks>
/// 流程：<c>new 模块对象</c> → <c>OnRegistered(context)</c>（模块在此声明口、数据类型、角色与内部关系）→ **成员实例化**（口实例 + 视图宿主）。
/// 钩子异常不逸出：捕获后转 Issue，实例仍返回（其活动此后不再启动）。
/// <para>本步不创建线程与队列（属数据面 M1-5）；实例状态为 <see cref="ModuleInstanceState.Registered"/>。</para>
/// </remarks>
public static class ModuleInstanceFactory
{
    /// <summary>创建实例。</summary>
    /// <param name="instanceId">实例 ID（工作现场内唯一、稳定）。</param>
    /// <param name="definition">模块定义。</param>
    /// <param name="issues">框架侧上报口。</param>
    /// <param name="viewState">
    /// **按实例绑定的视图状态读取面**；`null` ⇒ 模块读到「全部未知」
    /// （不影响声明与生命周期，老调用点零改动）。
    /// </param>
    /// <param name="display">
    /// **按实例绑定的显示请求面**；`null` ⇒ 模块的「清空显示源」请求被忽略（老调用点零改动）。
    /// </param>
    public static ModuleInstance Create(
        string instanceId,
        Registry.ModuleDefinition definition,
        Diagnostics.IIssueSink issues,
        Sdk.Context.IViewStateReader? viewState = null,
        Sdk.Context.IViewDisplayAccess? display = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(issues);

        var module = (Sdk.Lifecycle.IModule)Activator.CreateInstance(definition.EntryType)!;
        var context = new Context.ModuleContext(instanceId, definition.Id, issues);
        var objectPath = $"module:{instanceId}";

        if (viewState is not null)
        {
            context.AttachViewState(viewState);
        }

        if (display is not null)
        {
            context.AttachDisplay(display);
        }

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

        // ② 成员实例化：口实例（含口名唯一性校验）+ **数据类型镜像**
        var ports = BuildPortInstances(context, objectPath, issues);
        var dataTypes = BuildDataTypeMirror(context, objectPath, issues);

        // ②b **角色**（功能分块）：在口与类型都就绪之后校验（口存在且方向相符、类型可解析、口归属唯一）
        var roles = BuildRoles(context, ports, dataTypes, objectPath, issues);

        // ②c **内部处理顺序**（角色 → 角色）：两端角色必须已声明
        var flows = BuildFlows(context, roles, objectPath, issues);

        // ③ 视图声明（包内 `view.json`）：**界面形态的唯一事实源**
        var view = ReadView(definition, objectPath, ports, issues);
        var instance = new ModuleInstance(
            instanceId, definition, module, ports, dataTypes, roles, flows, view, context)
        {
            State = ModuleInstanceState.Registered,
        };

        return instance;
    }

    /// <summary>
    /// 框架视图 schema（进程内只读一次：它是**框架资产**，不是每实例的输入）。
    /// </summary>
    private static readonly Lazy<(Packages.ViewSchema? Schema, string? Error)> SchemaSlot = new(() =>
        Packages.ViewSchema.TryLoad(out var schema, out var error)
            ? (schema, null)
            : (null, error ?? "界面声明面 schema 加载失败"));

    /// <summary>
    /// **整份声明不可用时的兜底文档**：窗口取框架地板（= schema 里 <c>window.minWidth</c> / <c>minHeight</c>
    /// 的缺省值，见 <c>ViewSchema.WindowFloor</c>）—— 框架对"实例要开窗但没有任何可用声明"的口径是
    /// **给一个能用的最小窗口**，而不是 0×0（零尺寸窗口只会把"声明写错"伪装成"窗口不见了"）。
    /// </summary>
    /// <remarks>
    /// 地板值只在这里取一次：schema 读不到（框架资产坏了，已另有 Issue）时退到 0 —— 那是"框架自己坏了"
    /// 的退化场景，不再为它造第二份地板字面量。
    /// </remarks>
    private static readonly Lazy<Sdk.Views.ViewDocument> FallbackView = new(() =>
    {
        var (schema, _) = SchemaSlot.Value;
        var floor = schema?.WindowFloor;
        var window = floor is null
            ? Sdk.Views.ViewWindowDeclaration.None
            : Sdk.Views.ViewWindowDeclaration.Floor(floor.MinWidth, floor.MinHeight);
        return Sdk.Views.ViewDocument.Fallback(window);
    });

    /// <summary>
    /// 读取包内视图声明（<c>view.json</c>）：**没有这个文件是合法的**（全走 schema 缺省）；
    /// 有但读不动/校验不过 ⇒ 报一条 Issue 并整份忽略（半份生效的界面比不生效更难查）。
    /// </summary>
    /// <remarks>
    /// schema 加载失败说明框架自己坏了 ⇒ 报错误级 Issue 并按"没有声明"处理。
    /// 声明里写的口名与数据面已注册的口对不上只报警告：**通信事实源是数据面，不是声明面**。
    /// </remarks>
    private static Sdk.Views.ViewDocument ReadView(
        Registry.ModuleDefinition definition,
        string objectPath,
        IReadOnlyList<PortInstance> ports,
        Diagnostics.IIssueSink issues)
    {
        var (schema, schemaError) = SchemaSlot.Value;
        if (schema is null)
        {
            issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                Sdk.Reporting.IssueCategory.InterfaceError,
                Sdk.Reporting.IssueSeverity.Error,
                schemaError ?? "界面声明面 schema 加载失败"));
            return FallbackView.Value;
        }

        if (!Packages.ViewDeclarationReader.TryRead(
                definition.PackageDirectory, schema, out var document, out var warnings, out var error)
            || document is null)
        {
            ReportViewIssue(
                issues,
                objectPath,
                error ?? $"{Packages.ViewDeclarationReader.FileName} 读取失败（已按无控件处理）");
            return FallbackView.Value;
        }

        foreach (var warning in warnings)
        {
            ReportViewIssue(issues, objectPath, warning);
        }

        if (!Packages.ViewDeclarationReader.TryCheckLabels(
                document,
                ports.Select(port => port.Name),
                // **判据 = 读到了包内声明**：没有 `view.json` 的模块是无界面模块（没有窗口放端口），
                // 不做界面侧完备性判定；有声明文件却漏了某个口的边，才是"未声明就没有位置"。
                Packages.ViewDeclarationReader.HasDeclaration(definition.PackageDirectory),
                out var labelWarnings,
                out var labelError))
        {
            ReportViewIssue(
                issues,
                objectPath,
                labelError ?? "包内声明与数据面对不上（已按无控件处理）",
                Sdk.Reporting.IssueSeverity.Error);
            return FallbackView.Value;
        }

        foreach (var warning in labelWarnings)
        {
            ReportViewIssue(issues, objectPath, warning);
        }

        return document;
    }

    private static void ReportViewIssue(
        Diagnostics.IIssueSink issues,
        string objectPath,
        string message,
        Sdk.Reporting.IssueSeverity severity = Sdk.Reporting.IssueSeverity.Warning) =>
        issues.Report(objectPath, new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            severity,
            message));

    /// <summary>
    /// 校验并实例化**角色**（功能分块）：一种类型 + 输入口/输出口；口必须存在且方向相符；
    /// **一个口最多属于一个角色**；Id 唯一；类型必须能在本模块解析。
    /// </summary>
    /// <remarks>
    /// 不合法的声明**报 Issue 并忽略该条**（其余照常）；一个口被两个角色抢 ⇒ 只保留**先声明**的那条。
    /// <b>口数不设限、运行期也不提示</b>：&gt;2 口既可能是"多路合成一路"（合法，例如参考融合器 2 入 1 出），
    /// 也可能是「两件事挤成一块」——框架判不了（声明 ≠ 证明）。该建议只出现在 **SDK 自检**
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

            // **不对口数设限，也不在运行期提示**：框架分不清「2 路合成 1 路」
            //（多源融合器，合法）与「两件不相关的事」（声明 ≠ 证明）—— 在自家正确模块的 **Issue 中心**
            // 里报警，用户会以为模块写错了。该「建议」只存在于 **SDK 自检**（`ModuleContract` 的 `Advisories`，
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

                // 角色的**输入口与输出口必须承载同一种数据类型**（= `roleType`）。
                // ⇒ 类型转换 = **两个数据角色**（「收 A」+「发 B」）+ DeclareFlow（叠加层上是两个方块）。
                // 口**未声明类型**（旧模块形态）⇒ 无可比对象，跳过（与「可声明 ≠ 必须声明」一致）。
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
    /// 校验并实例化**内部处理顺序**（`角色 → 角色`）：两端角色必须已声明，且不得自环；
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
