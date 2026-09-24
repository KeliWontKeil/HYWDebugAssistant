using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Hosting.Diagnostics;

/// <summary>
/// 框架侧问题上报口（L2/L3 内部使用）。
/// </summary>
/// <remarks>
/// 只定义"上报口"（依赖倒置）：**实现由 L4 的 Issue 中心提供**（M1-6）；
/// 在 Issue 中心就绪前，App/测试可注入控制台或收集式实现。
/// <para>实现**不抛出**：上报失败只记诊断计数，不得让框架因上报失败而崩。</para>
/// </remarks>
public interface IIssueSink
{
    /// <summary>上报一个问题。</summary>
    /// <param name="objectPath">归属对象路径（如 <c>package:D:\mods\foo</c>、<c>module:inst-1</c>）。</param>
    /// <param name="issue">问题内容。</param>
    void Report(string objectPath, IssueReport issue);
}

/// <summary>丢弃全部问题的空实现（用于"问题不重要"的场合，如单元测试构造）。</summary>
public sealed class NullIssueSink : IIssueSink
{
    /// <summary>共享实例。</summary>
    public static readonly NullIssueSink Instance = new();

    /// <inheritdoc />
    public void Report(string objectPath, IssueReport issue)
    {
        // 有意丢弃（Null 对象模式）
    }
}

/// <summary>收集式实现（测试与早期启动阶段使用）。</summary>
public sealed class CollectingIssueSink : IIssueSink
{
    private readonly List<(string ObjectPath, IssueReport Issue)> _issues = [];

    /// <summary>已收集的问题（快照）。</summary>
    public IReadOnlyList<(string ObjectPath, IssueReport Issue)> Issues => [.. _issues];

    /// <inheritdoc />
    public void Report(string objectPath, IssueReport issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _issues.Add((objectPath, issue));
    }
}
