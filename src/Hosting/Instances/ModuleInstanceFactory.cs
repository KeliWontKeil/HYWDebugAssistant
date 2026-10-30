using HYWDebugAssistant.Sdk.Access;
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
    /// <param name="files">
    /// **按实例绑定的文件选择请求面**；`null` ⇒ 模块的"弹一次系统选择器"请求被忽略
    /// （宿主没接线时用户看不到对话框，模块也收不到路径 —— 老调用点零改动）。
    /// </param>
    /// <param name="timing">全局计时面；`null` ⇒ 用进程共享的全局计时服务。</param>
    /// <param name="catalog">
    /// **外部标准目录**：按模块声明的标准标识解析适配器与参数类型；
    /// `null` ⇒ 有外部接入声明的模块得到 Error 并以未接线状态继续（老调用点零改动）。
    /// </param>
    public static ModuleInstance Create(
        string instanceId,
        Registry.ModuleDefinition definition,
        Diagnostics.IIssueSink issues,
        Sdk.Context.IViewStateReader? viewState = null,
        Sdk.Context.IViewDisplayAccess? display = null,
        Sdk.Context.IFileAccess? files = null,
        Sdk.Timing.IModuleTiming? timing = null,
        IExternalAccessCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(issues);

        var module = (Sdk.Lifecycle.IModule)Activator.CreateInstance(definition.EntryType)!;
        var context = new Context.ModuleContext(instanceId, definition.Id, issues, timing);
        var objectPath = $"module:{instanceId}";

        // 模块自有资源包：只读、只读自己的 `resources/`（模块目录 = 入口程序集所在目录）。
        context.AttachResources(Context.ModuleResourceAccess.ForModule(definition.EntryType.Assembly));

        if (viewState is not null)
        {
            context.AttachViewState(viewState);
        }

        if (display is not null)
        {
            context.AttachDisplay(display);
        }

        if (files is not null)
        {
            context.AttachFiles(files);
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
                HostingText.Get("hosting.instance.onRegisteredThrew", ex.GetType().Name, ex.Message)));
        }

        // ② 成员实例化：口实例（含口名唯一性校验）+ **数据类型镜像**
        var ports = BuildPortInstances(context, objectPath, issues);
        var dataTypes = BuildDataTypeMirror(context, objectPath, issues);

        // ②b **角色**（功能分块）：在口与类型都就绪之后校验（口存在且方向相符、类型可解析、口归属唯一）
        var roles = BuildRoles(context, ports, dataTypes, objectPath, issues);

        // ②c **内部处理顺序**（角色 → 角色）：两端角色必须已声明
        var flows = BuildFlows(context, roles, objectPath, issues);

        // ②d **界面契约**（显示源 / 输入源 / 命令）：框架据此建缓冲与闸门，与"画在哪"无关
        var displaySources = BuildDisplaySources(context, objectPath, issues);
        var inputs = BuildInputs(context, objectPath, issues);
        var persistentInputs = new HashSet<string>(context.PersistentInputs, StringComparer.Ordinal);
        var commands = BuildCommands(context, objectPath, issues);

        // ②e **外部接入**（名唯一 + 标准可解析 + 参数类型可实例化 + 单元上限合法）
        var externalAccesses = BuildExternalAccesses(context, objectPath, issues, catalog);

        var instance = new ModuleInstance(
            instanceId, definition, module, ports, dataTypes, roles, flows,
            displaySources, inputs, persistentInputs, commands, externalAccesses, context)
        {
            State = ModuleInstanceState.Registered,
        };

        // 两种消费方式不能同时生效：活动体优先，交互回调不会被投递（提醒模块作者，不阻断）。
        if (module is Sdk.Lifecycle.IActivitySource && module is Sdk.Lifecycle.IModuleInteractionSink)
        {
            issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                Sdk.Reporting.IssueCategory.ModuleError,
                Sdk.Reporting.IssueSeverity.Warning,
                HostingText.Get("hosting.instance.dualConsumption")));
        }

        return instance;
    }

    /// </summary>
    private static List<Sdk.Views.DisplaySourceDeclaration> BuildDisplaySources(
        Context.ModuleContext context,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var list = new List<Sdk.Views.DisplaySourceDeclaration>(context.DeclaredDisplaySources.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in context.DeclaredDisplaySources)
        {
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                Report(HostingText.Get("hosting.instance.displaySourceEmptyName"));
                continue;
            }

            if (!seen.Add(source.Name))
            {
                Report(HostingText.Get("hosting.instance.displaySourceDuplicate", source.Name));
                continue;
            }

            list.Add(source);
        }

        return list;

        void Report(string message) => issues.Report(objectPath, new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Warning,
            message));
    }

    /// <summary>校验**输入源**声明：名字唯一（空名已由上下文丢弃）。重复 ⇒ 报 Issue 并跳过。</summary>
    private static List<string> BuildInputs(
        Context.ModuleContext context,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var list = new List<string>(context.DeclaredInputs.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in context.DeclaredInputs)
        {
            if (!seen.Add(name))
            {
                issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                    Sdk.Reporting.IssueCategory.InterfaceError,
                    Sdk.Reporting.IssueSeverity.Warning,
                    HostingText.Get("hosting.instance.inputDuplicate", name)));
                continue;
            }

            list.Add(name);
        }

        return list;
    }

    /// <summary>
    /// 校验**外部接入**声明：名字非空/唯一、标准标识可解析、参数类型可实例化、单元上限合法。
    /// </summary>
    /// <remarks>
    /// 适配器与参数类型归框架所有：标准标识经 <paramref name="catalog"/> 解析成 descriptor，
    /// 参数类型随 descriptor 登记进配置快照（表单与持久化复用）。
    /// 未知标准 / 缺 catalog / 参数类型非法 ⇒ 报 Error 并**忽略该条**（模块以未接线状态继续）。
    /// </remarks>
    private static List<ExternalAccessDeclaration> BuildExternalAccesses(
        Context.ModuleContext context,
        string objectPath,
        Diagnostics.IIssueSink issues,
        IExternalAccessCatalog? catalog)
    {
        const int maxUnitBytes = 1024 * 1024;
        var accesses = new List<ExternalAccessDeclaration>(context.DeclaredExternalAccesses.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var access in context.DeclaredExternalAccesses)
        {
            if (string.IsNullOrWhiteSpace(access.Name))
            {
                Report(HostingText.Get("hosting.instance.accessEmptyName"));
                continue;
            }

            if (!names.Add(access.Name))
            {
                Report(HostingText.Get("hosting.instance.accessDuplicateName", access.Name));
                continue;
            }

            if (access.MaxUnitBytes <= 0 || access.MaxUnitBytes > maxUnitBytes)
            {
                Report(HostingText.Get(
                    "hosting.instance.accessUnitLimit",
                    access.Name,
                    maxUnitBytes,
                    access.MaxUnitBytes));
                continue;
            }

            if (string.IsNullOrWhiteSpace(access.StandardId))
            {
                Report(HostingText.Get("hosting.instance.accessStandardEmpty", access.Name));
                continue;
            }

            if (catalog is null)
            {
                Report(HostingText.Get(
                    "hosting.instance.accessCatalogMissing",
                    access.Name,
                    access.StandardId));
                continue;
            }

            if (!catalog.TryResolve(access.StandardId, out var descriptor) || descriptor is null)
            {
                Report(HostingText.Get("hosting.instance.accessUnknownStandard", access.StandardId, access.Name));
                continue;
            }

            var parametersType = descriptor.ParametersType;
            if (parametersType is null
                || !parametersType.IsClass
                || parametersType.IsAbstract
                || !parametersType.IsPublic)
            {
                Report(HostingText.Get(
                    "hosting.instance.accessParametersNotPublicClass",
                    access.StandardId,
                    access.Name));
                continue;
            }

            if (parametersType.GetConstructor(Type.EmptyTypes) is null)
            {
                Report(HostingText.Get(
                    "hosting.instance.accessParametersNoCtor",
                    access.StandardId,
                    parametersType.Name,
                    access.Name));
                continue;
            }

            if (descriptor.CreateAdapter is null)
            {
                Report(HostingText.Get("hosting.instance.accessAdapterFactoryMissing", access.StandardId, access.Name));
                continue;
            }

            if (context.TryEnsureConfiguration(parametersType) is null)
            {
                Report(HostingText.Get(
                    "hosting.instance.accessParametersInstantiateFailed",
                    access.StandardId,
                    parametersType.Name,
                    access.Name));
                continue;
            }

            accesses.Add(access);
        }

        return accesses;

        void Report(string message) => issues.Report(objectPath, new Sdk.Reporting.IssueReport(
            Sdk.Reporting.IssueCategory.InterfaceError,
            Sdk.Reporting.IssueSeverity.Error,
            message));
    }

    /// <summary>校验**命令**声明：名字唯一。重复 ⇒ 报 Issue 并跳过（标题以先声明者为准）。</summary>
    private static List<(string Name, string Title)> BuildCommands(
        Context.ModuleContext context,
        string objectPath,
        Diagnostics.IIssueSink issues)
    {
        var list = new List<(string Name, string Title)>(context.DeclaredCommands.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, title) in context.DeclaredCommands)
        {
            if (!seen.Add(name))
            {
                issues.Report(objectPath, new Sdk.Reporting.IssueReport(
                    Sdk.Reporting.IssueCategory.InterfaceError,
                    Sdk.Reporting.IssueSeverity.Warning,
                    HostingText.Get("hosting.instance.commandDuplicate", name)));
                continue;
            }

            list.Add((name, title));
        }

        return list;
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
                Report(HostingText.Get("hosting.instance.roleIdEmpty"));
                continue;
            }

            if (!ids.Add(role.Id))
            {
                Report(HostingText.Get("hosting.instance.roleIdDuplicate", role.Id));
                continue;
            }

            if (!knownTypes.Contains(role.Type))
            {
                Report(HostingText.Get("hosting.instance.roleTypeUndeclared", role.Id, role.Type));
                continue;
            }

            if (role.Inputs.Count == 0 && role.Outputs.Count == 0)
            {
                Report(HostingText.Get("hosting.instance.roleNoPorts", role.Id));
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
                Report(HostingText.Get("hosting.instance.portAlreadyOwned", taken, claimed[taken], role.Id));
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
                    Report(HostingText.Get("hosting.instance.rolePortDuplicate", roleId, name));
                    return false;
                }

                var port = ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                if (port is null)
                {
                    Report(HostingText.Get("hosting.instance.rolePortMissing", roleId, name));
                    return false;
                }

                if (port.Direction != expected)
                {
                    Report(HostingText.Get(
                        "hosting.instance.rolePortDirection",
                        roleId,
                        name,
                        expected == Sdk.Ports.PortDirection.Receive
                            ? HostingText.Get("hosting.instance.direction.input")
                            : HostingText.Get("hosting.instance.direction.output"),
                        port.Direction));
                    return false;
                }

                // 角色的**输入口与输出口必须承载同一种数据类型**（= `roleType`）。
                // ⇒ 类型转换 = **两个数据角色**（「收 A」+「发 B」）+ DeclareFlow（叠加层上是两个方块）。
                // 口**未声明类型**（旧模块形态）⇒ 无可比对象，跳过（与「可声明 ≠ 必须声明」一致）。
                if (port.Declaration.Type is { } portType && portType != roleType)
                {
                    Report(HostingText.Get(
                        "hosting.instance.rolePortTypeMismatch",
                        roleId,
                        name,
                        expected == Sdk.Ports.PortDirection.Receive
                            ? HostingText.Get("hosting.instance.direction.input")
                            : HostingText.Get("hosting.instance.direction.output"),
                        portType,
                        roleType));
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
                Report(HostingText.Get("hosting.instance.flowRoleIdEmpty", flow));
                continue;
            }

            if (string.Equals(flow.FromRoleId, flow.ToRoleId, StringComparison.Ordinal))
            {
                Report(HostingText.Get("hosting.instance.flowSelfLoop", flow));
                continue;
            }

            if (!known.Contains(flow.FromRoleId) || !known.Contains(flow.ToRoleId))
            {
                Report(HostingText.Get("hosting.instance.flowUndeclaredRole", flow));
                continue;
            }

            if (!seen.Add((flow.FromRoleId, flow.ToRoleId)))
            {
                Report(HostingText.Get("hosting.instance.flowDuplicate", flow));
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
                    HostingText.Get("hosting.instance.dataTypeDuplicate", declaration.Reference)));
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
                    HostingText.Get("hosting.instance.portUndeclaredType", port.Name, reference),
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
                    HostingText.Get("hosting.instance.portNameDuplicate", declaration.Name),
                    declaration.Name));
                continue;
            }

            ports.Add(new PortInstance(declaration));
        }

        return ports;
    }
}