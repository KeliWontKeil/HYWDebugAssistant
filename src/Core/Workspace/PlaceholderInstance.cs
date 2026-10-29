using System.Text.Json.Nodes;

namespace HYWDebugAssistant.Core.Workspace;

/// <summary>
/// **占位实例**：加载组合件时模块定义缺失或版本不兼容 → 保留声明而不活动（主文档 §9.3 PQ-PS-03）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>保留什么</b>：实例 ID、原模块标识字面量、活动面（启用/禁用）、**配置原文**、端口几何与位置；</item>
///   <item><b>不做什么</b>：不创建模块对象、不建线程、不建队列、不参与绑定——**其余部分照常运行**；</item>
///   <item><b>出路</b>：用户补齐模块包后经 <c>ResolvePlaceholder</c> 手动指定替代（可跨版本），
///     届时实例化并沿用本处保留的配置；</item>
///   <item><b>配置原文</b>：以 JSON 片段保留（含未识别字段），解析成功后再写入模块配置面——**无损往返**。</item>
/// </list>
/// </remarks>
public sealed class PlaceholderInstance
{
    /// <summary>创建占位。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="moduleIdLiteral">原模块标识字面量（作者/模块名/版本）。</param>
    /// <param name="reason">缺失原因（面向用户，进 Issue 面板）。</param>
    /// <param name="enabled">活动面（启用/禁用）原值。</param>
    /// <param name="configuration">配置原文片段（可为空 = 原记录无配置）。</param>
    public PlaceholderInstance(
        string instanceId,
        string moduleIdLiteral,
        string reason,
        bool enabled,
        JsonObject? configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleIdLiteral);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        InstanceId = instanceId;
        ModuleIdLiteral = moduleIdLiteral;
        Reason = reason;
        Enabled = enabled;
        Configuration = configuration;
    }

    /// <summary>实例 ID（工作现场内唯一、稳定——网络成员靠它重锚）。</summary>
    public string InstanceId { get; }

    /// <summary>原模块标识字面量（作者/模块名/版本）。</summary>
    public string ModuleIdLiteral { get; }

    /// <summary>缺失原因。</summary>
    public string Reason { get; }

    /// <summary>活动面原值（解析占位时沿用）。</summary>
    public bool Enabled { get; }

    /// <summary>配置原文片段（可为空）。</summary>
    public JsonObject? Configuration { get; }

    /// <summary>归属对象路径（Issue 与诊断用）。</summary>
    public string ObjectPath => $"module:{InstanceId}";

    /// <inheritdoc />
    public override string ToString() =>
        CoreText.Get("core.workspace.placeholder", InstanceId, ModuleIdLiteral, Reason);
}
