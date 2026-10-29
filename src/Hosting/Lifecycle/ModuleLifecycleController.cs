using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Hosting.Lifecycle;

/// <summary>
/// 生命周期控制器：驱动框架状态机 <c>Registered → Active ⇄ Disabled → Removed</c> 并触发模块钩子。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>权威</b>：状态由框架维护（模块不得自定状态）；</item>
///   <item><b>异常边界</b>：钩子异常一律捕获 → Issue，**不中断框架流程**、不影响其他模块（AC-2）；</item>
///   <item><b>非法转换</b>：按状态机拒绝并报 Issue（例如对已移除的实例再激活）；</item>
///   <item><b>线程</b>：由调用方（L4 变更流程）在管理线程上调用；M1-5 起模块活动体在实例专属线程上运行。</item>
/// </list>
/// </remarks>
public sealed class ModuleLifecycleController
{
    private readonly IIssueSink _issues;

    /// <summary>创建控制器。</summary>
    public ModuleLifecycleController(IIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues = issues;
    }

    /// <summary><c>Registered → Active</c>：加入即运行。</summary>
    public bool Activate(ModuleInstance instance) => Transition(
        instance,
        allowedFrom: ModuleInstanceState.Registered,
        target: ModuleInstanceState.Active,
        hook: module => module.OnActivated(),
        hookName: nameof(IModule.OnActivated));

    /// <summary><c>Active → Disabled</c>：保留实例/拓扑/配置、暂停数据流（内部处置自决）。</summary>
    public bool Disable(ModuleInstance instance) => Transition(
        instance,
        allowedFrom: ModuleInstanceState.Active,
        target: ModuleInstanceState.Disabled,
        hook: module => module.OnDisabled(),
        hookName: nameof(IModule.OnDisabled));

    /// <summary><c>Disabled → Active</c>：恢复。</summary>
    public bool Enable(ModuleInstance instance) => Transition(
        instance,
        allowedFrom: ModuleInstanceState.Disabled,
        target: ModuleInstanceState.Active,
        hook: module => module.OnEnabled(),
        hookName: nameof(IModule.OnEnabled));

    /// <summary>配置变更通知（状态不变）：热生效或请求重建由模块自决。</summary>
    public void NotifyConfigurationChanged(ModuleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.State == ModuleInstanceState.Removed)
        {
            _issues.Report(ObjectPath(instance), Issue(IssueSeverity.Warning,
                HostingText.Get("hosting.lifecycle.configChangedOnRemoved")));
            return;
        }

        InvokeHook(instance, module => module.OnConfigurationChanged(), nameof(IModule.OnConfigurationChanged));
    }

    /// <summary>
    /// 移除：调用 <c>OnRemoved</c> 释放模块内部资源后置为 <c>Removed</c>（**不可逆**）。
    /// </summary>
    /// <remarks>
    /// 连接/绑定与成员资源的清理由 L4 的 ChangeSet 负责级联（M1-6）；本方法只驱动模块侧钩子与状态。
    /// </remarks>
    public bool Remove(ModuleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.State == ModuleInstanceState.Removed)
        {
            _issues.Report(ObjectPath(instance), Issue(IssueSeverity.Warning, HostingText.Get("hosting.lifecycle.alreadyRemoved")));
            return false;
        }

        InvokeHook(instance, module => module.OnRemoved(), nameof(IModule.OnRemoved));
        instance.State = ModuleInstanceState.Removed;
        return true;
    }

    private bool Transition(
        ModuleInstance instance,
        ModuleInstanceState allowedFrom,
        ModuleInstanceState target,
        Action<IModule> hook,
        string hookName)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.State != allowedFrom)
        {
            _issues.Report(ObjectPath(instance), Issue(IssueSeverity.Warning,
                HostingText.Get("hosting.lifecycle.illegalTransition", instance.State, target, allowedFrom)));
            return false;
        }

        InvokeHook(instance, hook, hookName);
        instance.State = target;
        return true;
    }

    private void InvokeHook(ModuleInstance instance, Action<IModule> hook, string hookName)
    {
        try
        {
            hook(instance.Module);
        }
        catch (Exception ex)
        {
            // 钩子异常不逸出边界（AC-2）：转 Issue，框架继续
            _issues.Report(ObjectPath(instance), new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Error,
                HostingText.Get("hosting.lifecycle.hookThrew", hookName, ex.GetType().Name, ex.Message)));
        }
    }

    private static string ObjectPath(ModuleInstance instance) => $"module:{instance.InstanceId}";

    private static IssueReport Issue(IssueSeverity severity, string message) =>
        new(IssueCategory.RuntimeError, severity, message);
}
