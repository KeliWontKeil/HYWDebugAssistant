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

        // ② 成员实例化：口实例（含口名唯一性校验）
        var ports = BuildPortInstances(context, objectPath, issues);

        // ③ 视图宿主（M1-9 由 UI 层按声明渲染；此处仅保留声明）
        var instance = new ModuleInstance(instanceId, definition, module, ports, context.DeclaredView, context)
        {
            State = ModuleInstanceState.Registered,
        };

        return instance;
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
