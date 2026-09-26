using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Persistence;

/// <summary>
/// 组合件的保存与加载服务（L4 持久化面）：文件 IO ↔ 运行时 ↔ Issue 报告。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>保存</b>：捕获现场 → 合并未知字段 → **原子写**；</item>
///   <item><b>加载</b>：读文件（大小/深度上限）→ 解析（记录级容错）→ **Schema 版本判定** → 降级式应用；</item>
///   <item><b>拒绝即保护</b>：不可解析 / 版本过新 → 只报 Issue，**不写任何内容**（§9.3）；</item>
///   <item><b>组合件永不自动执行代码</b>：加载只重建声明结构（§14）。</item>
/// </list>
/// </remarks>
public sealed class CompositeService
{
    private readonly ModuleRuntime _runtime;
    private readonly ModuleRegistry _registry;
    private readonly IIssueSink _issues;

    /// <summary>创建服务。</summary>
    /// <param name="runtime">运行时（捕获与加载的目标）。</param>
    /// <param name="registry">模块注册表（加载时按标识 + 版本重锚）。</param>
    /// <param name="issues">框架侧上报口（缺省用运行时自带的 Issue 中心）。</param>
    public CompositeService(ModuleRuntime runtime, ModuleRegistry registry, IIssueSink? issues = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(registry);

        _runtime = runtime;
        _registry = registry;
        _issues = issues ?? runtime.Issues;
    }

    // ---------------------------------------------------------------- 保存

    /// <summary>保存组合件（物化选中范围为 1 个文件）。</summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="name">组合件名（空 = 取文件名）。</param>
    /// <param name="instanceIds">选中实例（为空 = **当前全部**）。</param>
    public CommandResult Save(string filePath, string? name = null, IReadOnlyList<string>? instanceIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var compositeName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(filePath)
            : name;

        var composite = _runtime.CaptureComposite(compositeName, instanceIds, out var notices);

        if (!CompositeStore.TryWrite(filePath, CompositeSerializer.ToJsonObject(composite), out var error))
        {
            var failure = $"组合件保存失败：{error}";
            _issues.Report($"composite:{compositeName}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Error, failure));

            return CommandResult.Fail([failure]);
        }

        return CommandResult.Ok(notices);
    }

    // ---------------------------------------------------------------- 加载

    /// <summary>加载组合件（实例化进当前工作区；异常加载为**降级式**）。</summary>
    /// <param name="filePath">组合件文件路径。</param>
    public CommandResult Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!CompositeStore.TryRead(filePath, out var root, out var outcome, out var readError))
        {
            // 语法非法 = **不可解析**（与"读不到"区分开：面向用户的原因必须准确）
            var prefix = outcome == CompositeReadOutcome.Malformed ? "组合件不可解析" : "无法读取组合件";
            return Reject(filePath, $"{prefix}：{readError}");
        }

        if (!CompositeSerializer.TryParse(root!, out var composite, out var warnings, out var parseFailure))
        {
            return Reject(filePath, $"组合件不可解析：{parseFailure}");
        }

        if (composite!.SchemaVersion > Composite.CurrentSchemaVersion)
        {
            // 能迁移则自动迁移，不能则**拒绝并保护原文件**（§9.3）
            return Reject(filePath, $"组合件 Schema 版本过新（文件 v{composite.SchemaVersion} > 框架 v{Composite.CurrentSchemaVersion}）");
        }

        Migrate(composite);

        foreach (var warning in warnings)
        {
            _issues.Report($"composite:{Describe(composite)}", new IssueReport(
                IssueCategory.ConfigurationError, IssueSeverity.Warning, $"{warning}（其余部分已照常加载）"));
        }

        var result = _runtime.LoadComposite(composite, ResolveDefinition);

        var notices = new List<string>(result.Notices) { Summarize(result) };
        return CommandResult.Ok(notices);
    }

    /// <summary>为占位实例指定替代模块（"可手动指定替代版本"，§9.3）。</summary>
    /// <param name="instanceId">占位实例 ID。</param>
    /// <param name="moduleIdLiteral">替代模块标识字面量。</param>
    public CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleIdLiteral);

        if (!ResolveDefinition(moduleIdLiteral, out var definition, out var reason) || definition is null)
        {
            return CommandResult.Fail([reason ?? $"模块未注册：{moduleIdLiteral}"]);
        }

        if (!_runtime.TryResolvePlaceholder(instanceId, definition, out var notices, out var failure))
        {
            return CommandResult.Fail([failure ?? "占位实例解析失败"]);
        }

        return CommandResult.Ok(notices);
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>拒绝加载：报 Issue 并**保持原文件不变**（绝不写入）。</summary>
    private CommandResult Reject(string filePath, string reason)
    {
        var message = $"{reason}；已拒绝加载并保持原文件不变：{Path.GetFullPath(filePath)}";
        _issues.Report("composite:load", new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Error, message));
        return CommandResult.Fail([message]);
    }

    /// <summary>
    /// Schema 迁移（**逐级升版，不猜测字段语义**）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>v1 → v2</b>【ADR-0050 M2/R3】：网络源成员由单数 <c>source</c> 改为数组 <c>sources</c>。
    ///     字段形态的归一在**解析期**完成（<c>CompositeSerializer.ReadSources</c> 读旧字段即写进
    ///     <c>record.Sources</c>）⇒ 此处只需升版号；写回时旧字段被移除、新字段写出。</item>
    ///   <item>版本号**过新**由调用方拒绝（见 <see cref="LoadComposite"/>），不回退猜测。</item>
    /// </list>
    /// </remarks>
    private static void Migrate(Composite composite)
    {
        if (composite.SchemaVersion < Composite.CurrentSchemaVersion)
        {
            composite.SchemaVersion = Composite.CurrentSchemaVersion;
        }
    }

    /// <summary>按标识 + 版本解析定义（含框架兼容范围判定；不满足 → 缺失态 + 原因）。</summary>
    private bool ResolveDefinition(string moduleIdLiteral, out ModuleDefinition? definition, out string? reason)
    {
        definition = null;

        if (!ModuleId.TryParse(moduleIdLiteral, out var moduleId))
        {
            reason = $"模块标识非法：'{moduleIdLiteral}'";
            return false;
        }

        if (!_registry.TryResolve(moduleId, out definition) || definition is null)
        {
            reason = $"模块定义缺失（该标识/版本未注册）：{moduleIdLiteral}";
            return false;
        }

        if (!FrameworkCompatibility.IsSatisfied(
                definition.FrameworkCompatibility,
                FrameworkCompatibility.CurrentFrameworkVersion,
                out var compatibilityError))
        {
            var incompatible = definition;
            definition = null;
            reason = $"模块 {moduleIdLiteral} 的框架兼容范围不满足：{compatibilityError}"
                + $"（模块声明 '{incompatible.FrameworkCompatibility}'）";
            return false;
        }

        reason = null;
        return true;
    }

    private static string Summarize(CompositeLoadResult result)
    {
        var summary = $"加载完成：实例 {result.InstancesAdded} · 占位 {result.PlaceholdersCreated} · "
            + $"网络 {result.NetworksRestored} · 悬空成员 {result.UnresolvedMembers}";

        return result.PlaceholdersCreated == 0 && result.UnresolvedMembers == 0
            ? summary
            : summary + "（占位/悬空项见 Issue 面板，补齐模块包后可重新锚定）";
    }

    private static string Describe(Composite composite) =>
        composite.Name.Length > 0 ? composite.Name : "(未命名)";
}
