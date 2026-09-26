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

        // ③ 视图宿主（M1-9 由 UI 层按声明渲染；此处仅保留声明）
        var instance = new ModuleInstance(instanceId, definition, module, ports, dataTypes, context.DeclaredView, context)
        {
            State = ModuleInstanceState.Registered,
        };

        return instance;
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
