using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Hosting.Installation;

/// <summary>
/// 模块安装器：**加载 → 校验 → 注册**的一条链路（ADR-0020 的"显式加载"）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>显式加载</b>：由用户指定包目录（或来源目录扫描的结果）触发；不自动扫描任意位置。</item>
///   <item><b>校验</b>：包结构（<see cref="ModulePackageLoader"/>）→ **框架兼容范围** → **依赖存在性**。</item>
///   <item><b>失败</b>：返回原因并报 Issue，**不影响既有运行**（不阻断其他模块）。</item>
///   <item><b>多版本</b>：同 ID 不同版本共存注册；同 ID 同版本重复即拒绝。</item>
/// </list>
/// </remarks>
public sealed class ModuleInstaller
{
    private readonly ModuleRegistry _registry;
    private readonly IIssueSink _issues;

    /// <summary>创建安装器。</summary>
    /// <param name="registry">模块注册表。</param>
    /// <param name="issues">框架侧上报口。</param>
    public ModuleInstaller(ModuleRegistry registry, IIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(issues);

        _registry = registry;
        _issues = issues;
    }

    /// <summary>安装一个模块包目录。</summary>
    /// <param name="packageDirectory">包目录（含 <c>module.json</c> 与实现程序集）。</param>
    /// <param name="definition">成功时返回模块定义。</param>
    /// <returns>是否安装成功。</returns>
    public bool TryInstall(string packageDirectory, out ModuleDefinition? definition)
    {
        definition = null;
        var objectPath = $"package:{packageDirectory}";

        // ① 包结构与清单校验
        var loadResult = ModulePackageLoader.Load(packageDirectory);
        if (!loadResult.Succeeded)
        {
            return Fail(objectPath, loadResult.FailureReason!);
        }

        var candidate = ModuleDefinition.FromPackage(loadResult.Package!);

        // ② 框架兼容范围
        if (!FrameworkCompatibility.IsSatisfied(
                candidate.FrameworkCompatibility,
                FrameworkCompatibility.CurrentFrameworkVersion,
                out var compatibilityError))
        {
            return Fail(objectPath, $"{candidate.Id}：{compatibilityError}");
        }

        // ③ 依赖存在性（缺失依赖 → Issue，不静默降级）
        foreach (var dependency in candidate.DependsOn)
        {
            if (!ModuleId.TryParse(dependency, out var dependencyId))
            {
                return Fail(objectPath, $"{candidate.Id}：依赖标识非法 '{dependency}'");
            }

            if (!_registry.TryResolve(dependencyId, out _))
            {
                return Fail(objectPath, $"{candidate.Id}：缺少依赖模块 {dependencyId}（请先安装该模块）");
            }
        }

        // ④ 注册（同 ID 同版本重复即拒绝）
        if (!_registry.TryRegister(candidate, out var registerError))
        {
            return Fail(objectPath, registerError!);
        }

        definition = candidate;
        return true;
    }

    /// <summary>注销模块（已实例化的在用实例由上层置为缺失态，**不强制卸载**）。</summary>
    public bool TryUninstall(ModuleId id) => _registry.TryUnregister(id);

    private bool Fail(string objectPath, string reason)
    {
        _issues.Report(objectPath, new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Error,
            $"模块包加载失败：{reason}"));
        return false;
    }
}
