using System.Collections.Concurrent;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Issues;

/// <summary>
/// Issue 条目：归属对象 + 类别 + 来源 + 描述 + 时间（主文档 §8.5 PQ-ER-01）。
/// </summary>
/// <param name="Sequence">自增序号（列表稳定排序用）。</param>
/// <param name="ObjectPath">归属对象路径（如 <c>module:gen-1</c>、<c>module:gen-1/out</c>、<c>network:net-1</c>）。</param>
/// <param name="Issue">问题内容（类别/严重度/描述）。</param>
/// <param name="Timestamp">发生时间。</param>
public readonly record struct IssueEntry(long Sequence, string ObjectPath, IssueReport Issue, DateTimeOffset Timestamp);

/// <summary>
/// Issue 中心：框架判定与模块报告的**统一收集点**（归属 + 类别 + 来源）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>天然隔离</b>：Issue 不阻断其他部分运行，仅登记与呈现（§8.5）；</item>
///   <item><b>无锁收集</b>：模块线程可能并发上报 → 无锁并发队列；读取用快照；</item>
///   <item><b>两条语义</b>：设计/变更问题（Prepare 拒绝等）与运行报告（异常/悬空/超时）共用本模型，语义由类别与描述区分。</item>
/// </list>
/// </remarks>
public sealed class IssueCenter : IIssueSink
{
    private readonly ConcurrentQueue<IssueEntry> _entries = new();
    private long _sequence;

    /// <summary>累计条目数（含已滚出的）。</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public void Report(string objectPath, IssueReport issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var entry = new IssueEntry(
            Interlocked.Increment(ref _sequence),
            objectPath ?? string.Empty,
            issue,
            DateTimeOffset.UtcNow);

        _entries.Enqueue(entry);
    }

    /// <summary>按发生顺序取快照。</summary>
    public IReadOnlyList<IssueEntry> Snapshot() => [.. _entries];

    /// <summary>取最近 <paramref name="maxCount"/> 条（最新在前）。</summary>
    public IReadOnlyList<IssueEntry> Recent(int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        return [.. _entries.Reverse().Take(maxCount)];
    }

    /// <summary>清空（用户操作"清除问题列表"）。</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
            // 清空
        }
    }
}
