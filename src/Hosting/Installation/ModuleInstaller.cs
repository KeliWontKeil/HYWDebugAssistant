using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Hosting.Installation;

/// <summary>模块包安装结果分类（只有 <see cref="Failed"/> 是真正的失败）。</summary>
public enum ModuleInstallOutcome
{
    /// <summary>新装入（注册表里没有同 ID 的包）。</summary>
    Installed = 0,

    /// <summary>同包目录、内容未变：已载入。</summary>
    AlreadyLoaded = 1,

    /// <summary>同 ID 但来自另一个包目录：先到者胜。</summary>
    DuplicatePackage = 2,

    /// <summary>同包目录，但包文件在本次进程启动后被重新构建：需重启。</summary>
    ContentChanged = 3,

    /// <summary>真正的加载/校验/依赖/注册失败。</summary>
    Failed = 4,
}

/// <summary>一次模块包安装的结果。</summary>
/// <param name="Outcome">结果分类。</param>
/// <param name="Definition">可用的定义（失败时为 null；重复/已载入时为已注册的那个）。</param>
/// <param name="Message">面向用户的一句说明。</param>
public sealed record ModuleInstallResult(
    ModuleInstallOutcome Outcome,
    ModuleDefinition? Definition,
    string Message)
{
    /// <summary>是否得到了一个可用定义（已载入/内容变化/新装入）。</summary>
    public bool Usable => Outcome is ModuleInstallOutcome.Installed
        or ModuleInstallOutcome.AlreadyLoaded
        or ModuleInstallOutcome.ContentChanged;

    /// <summary>是否需要重启应用才能使用新代码。</summary>
    public bool NeedsRestart => Outcome == ModuleInstallOutcome.ContentChanged;
}

/// <summary>
/// 模块安装器：**加载 → 校验 → 注册**的一条链路（显式加载）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>显式加载</b>：由用户指定包目录（或来源目录扫描的结果）触发；不自动扫描任意位置。</item>
///   <item><b>校验</b>：包结构（<see cref="ModulePackageLoader"/>）→ **依赖存在性**。</item>
///   <item><b>失败</b>：只有结构/依赖/注册失败才报 Error；同包重复是正常状态。</item>
///   <item><b>一个 ID 一个定义</b>：同 ID 不共存、不覆盖；内容变化由"同目录重建"表达（需重启）。</item>
/// </list>
/// </remarks>
public sealed class ModuleInstaller
{
    private readonly ModuleRegistry _registry;
    private readonly IIssueSink _issues;
    private readonly DateTime _processStartUtc;

    /// <summary>创建安装器。</summary>
    /// <param name="registry">模块注册表。</param>
    /// <param name="issues">框架侧上报口。</param>
    /// <param name="processStartUtc">本进程启动时刻（判定“包在启动后重新构建过”；缺省 = 现在）。</param>
    public ModuleInstaller(ModuleRegistry registry, IIssueSink issues, DateTime? processStartUtc = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(issues);

        _registry = registry;
        _issues = issues;
        _processStartUtc = processStartUtc ?? DateTime.UtcNow;
    }

    /// <summary>安装一个模块包目录，并返回**分类结果**。</summary>
    /// <param name="packageDirectory">包目录（含 <c>module.json</c> 与实现程序集）。</param>
    public ModuleInstallResult Install(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var objectPath = $"package:{packageDirectory}";

        // ① 包结构与清单校验
        var loadResult = ModulePackageLoader.Load(packageDirectory);
        if (!loadResult.Succeeded)
        {
            return Fail(objectPath, loadResult.FailureReason!);
        }

        var candidate = ModuleDefinition.FromPackage(loadResult.Package!);

        // ② 依赖存在性（缺失依赖 → Issue，不静默降级）
        foreach (var dependency in candidate.DependsOn)
        {
            if (!ModuleId.TryParse(dependency, out var dependencyId))
            {
                return Fail(objectPath, HostingText.Get("hosting.install.dependencyIdInvalid", candidate.Id, dependency));
            }

            if (!_registry.TryResolve(dependencyId, out _))
            {
                return Fail(objectPath, HostingText.Get("hosting.install.dependencyMissing", candidate.Id, dependencyId));
            }
        }

        // ③ 同 ID 已在注册表：不覆盖、不报错；区分“同一包”与“另一个包”
        if (_registry.TryResolve(candidate.Id, out var existing) && existing is not null)
        {
            var samePackage = string.Equals(
                Path.GetFullPath(existing.PackageDirectory),
                Path.GetFullPath(candidate.PackageDirectory),
                StringComparison.OrdinalIgnoreCase);

            if (samePackage)
            {
                var changed = WasBuiltAfterProcessStart(candidate.PackageDirectory);
                return changed
                    ? new ModuleInstallResult(
                        ModuleInstallOutcome.ContentChanged,
                        existing,
                        HostingText.Get("hosting.install.contentChanged", candidate.Id))
                    : new ModuleInstallResult(
                        ModuleInstallOutcome.AlreadyLoaded,
                        existing,
                        HostingText.Get("hosting.install.loadedUnchanged", candidate.Id));
            }

            return new ModuleInstallResult(
                ModuleInstallOutcome.DuplicatePackage,
                existing,
                HostingText.Get("hosting.install.loadedOtherDir", candidate.Id, existing.PackageDirectory));
        }

        // ④ 注册（一个 ID 一个定义）
        if (!_registry.TryRegister(candidate, out var registerError))
        {
            return Fail(objectPath, registerError!);
        }

        return new ModuleInstallResult(ModuleInstallOutcome.Installed, candidate, HostingText.Get("hosting.install.installed", candidate.Id));
    }

    /// <summary>兼容入口：是否得到一个可用定义（新装入 / 已载入 / 内容变化）。</summary>
    /// <param name="packageDirectory">包目录。</param>
    /// <param name="definition">可用定义。</param>
    public bool TryInstall(string packageDirectory, out ModuleDefinition? definition)
    {
        var result = Install(packageDirectory);
        definition = result.Definition;
        return result.Usable;
    }

    /// <summary>注销模块（已实例化的在用实例由上层置为缺失态，**不强制卸载**）。</summary>
    public bool TryUninstall(ModuleId id) => _registry.TryUnregister(id);

    private ModuleInstallResult Fail(string objectPath, string reason)
    {
        _issues.Report(objectPath, new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Error,
            HostingText.Get("hosting.install.packageLoadFailed", reason)));
        return new ModuleInstallResult(ModuleInstallOutcome.Failed, null, reason);
    }

    /// <summary>该包是否在本进程启动后被重新构建过（只看包目录直接文件；与原有 NeedsRestart 口径一致）。</summary>
    /// <remarks>
    /// **封面不算"内容变化"**：它是展示资源，不承载任何代码，换一张图不需要重启应用
    /// （为此弹出「需重启」既吓人又没用：重启了也只是换了张图）。
    /// 其余包根文件（程序集、清单、依赖）仍然按最新修改时间判定。
    /// </remarks>
    private bool WasBuiltAfterProcessStart(string packageDirectory)
    {
        try
        {
            var newest = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    ModulePackageLayout.CoverFileName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return newest > _processStartUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
