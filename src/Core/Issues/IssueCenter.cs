using HYWDebugAssistant.Core.Api;
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
/// <param name="Unread">
/// 用户是否还没看过这一条（新上报 = 未读；"已读/全部已读"改这里）。
/// </param>
public readonly record struct IssueEntry(
    long Sequence,
    string ObjectPath,
    IssueReport Issue,
    DateTimeOffset Timestamp,
    bool Unread);

/// <summary>
/// Issue 中心：框架判定与模块报告的**统一收集点**（归属 + 类别 + 来源）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>天然隔离</b>：Issue 不阻断其他部分运行，仅登记与呈现（§8.5）；</item>
///   <item><b>低频率锁</b>：模块线程可能并发上报；Issue 不在数据热路径上，故用一把私有锁同时保护
///     上报 / 快照 / 删除 / 清空 / 已读 / 汇总。任意删除（用户删单条）需要可重排集合，队列做不到；</item>
///   <item><b>两条语义</b>：设计/变更问题（Prepare 拒绝等）与运行报告（异常/悬空/超时）共用本模型，
///     语义由类别与描述区分；</item>
///   <item><b>已读归条目自己</b>：入口角标（活动栏图标上的红标）与列表、筛选器读的是同一份未读事实。
///     放在面板里会让两者分叉 —— 面板关着时角标根本看不到"用户已经读过"，于是读完了红标还挂着。</item>
/// </list>
/// </remarks>
public sealed class IssueCenter : IIssueSink
{
    /// <summary>
    /// 条目上限：超出后丢最旧的一条（并同步扣减分档与未读计数）。
    /// </summary>
    /// <remarks>
    /// 常规运行远到不了这个量级；它是"某条通路意外地按数据频率报问题"时的止损线 ——
    /// 没有上限时，既占内存，又让每拍的全量汇总与列表重建越来越慢。
    /// </remarks>
    private const int MaxEntries = 4096;

    private readonly object _gate = new();
    private readonly List<IssueEntry> _entries = [];
    private long _sequence;
    private int _error;
    private int _warning;
    private int _info;
    private int _unread;
    private int _unreadError;
    private int _unreadWarning;
    private int _unreadInfo;

    /// <summary>当前条目数（已删除/已清空的不计）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public void Report(string objectPath, IssueReport issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        lock (_gate)
        {
            _entries.Add(new IssueEntry(
                ++_sequence,
                objectPath ?? string.Empty,
                issue,
                DateTimeOffset.UtcNow,
                Unread: true));

            CountLocked(issue.Severity, +1);
            UnreadCountLocked(issue.Severity, +1);

            if (_entries.Count > MaxEntries)
            {
                var excess = _entries.Count - MaxEntries;

                for (var index = 0; index < excess; index++)
                {
                    CountLocked(_entries[index].Issue.Severity, -1);
                    if (_entries[index].Unread)
                    {
                        UnreadCountLocked(_entries[index].Issue.Severity, -1);
                    }
                }

                _entries.RemoveRange(0, excess);
            }
        }
    }

    /// <summary>按发生顺序取快照。</summary>
    public IReadOnlyList<IssueEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }

    /// <summary>取最近 <paramref name="maxCount"/> 条（最新在前）。</summary>
    public IReadOnlyList<IssueEntry> Recent(int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        lock (_gate)
        {
            var take = Math.Min(maxCount, _entries.Count);
            var result = new IssueEntry[take];

            for (var index = 0; index < take; index++)
            {
                result[index] = _entries[_entries.Count - 1 - index];
            }

            return result;
        }
    }

    /// <summary>全量汇总（总数 + 按严重度计数 + 按严重度的未读数）。</summary>
    /// <remarks>
    /// 分档计数随上报 / 删除 / 清空 / 已读增量维护 ⇒ 汇总不再每拍全表扫描。
    /// 未读数在这里给出，是为了让入口角标与列表用**同一个数**（面板关着时也一样）。
    /// </remarks>
    public IssueSummaryView Summary()
    {
        lock (_gate)
        {
            return new IssueSummaryView(_entries.Count, _error, _warning, _info)
            {
                UnreadTotal = _unread,
                UnreadError = _unreadError,
                UnreadWarning = _unreadWarning,
                UnreadInfo = _unreadInfo,
            };
        }
    }

    /// <summary>标记一批条目为已读 / 未读（不存在的序号忽略）。</summary>
    /// <param name="sequences">要改的序号。</param>
    /// <param name="read"><see langword="true"/> = 已读，<see langword="false"/> = 未读。</param>
    /// <returns>状态真的变了的条数。</returns>
    public int MarkRead(IReadOnlyCollection<long> sequences, bool read)
    {
        ArgumentNullException.ThrowIfNull(sequences);

        if (sequences.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var changed = 0;

            foreach (var sequence in sequences)
            {
                var index = IndexOfLocked(sequence);
                if (index < 0)
                {
                    continue;
                }

                var entry = _entries[index];
                if (entry.Unread == !read)
                {
                    continue;
                }

                _entries[index] = entry with { Unread = !read };
                UnreadCountLocked(entry.Issue.Severity, read ? -1 : +1);
                changed++;
            }

            return changed;
        }
    }

    /// <summary>全部标记为已读（含当前未加载/未显示的那些）。</summary>
    /// <returns>状态真的变了的条数。</returns>
    public int MarkAllRead()
    {
        lock (_gate)
        {
            var changed = 0;

            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (!entry.Unread)
                {
                    continue;
                }

                _entries[index] = entry with { Unread = false };
                changed++;
            }

            _unread = 0;
            _unreadError = 0;
            _unreadWarning = 0;
            _unreadInfo = 0;
            return changed;
        }
    }

    /// <summary>移除指定序号（不存在的序号忽略）。</summary>
    /// <param name="sequences">要移除的序号。</param>
    /// <returns>实际移除的条数。</returns>
    public int Remove(IReadOnlyCollection<long> sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);

        if (sequences.Count == 0)
        {
            return 0;
        }

        var targets = new HashSet<long>(sequences);

        lock (_gate)
        {
            var removed = 0;

            _ = _entries.RemoveAll(entry =>
            {
                if (!targets.Contains(entry.Sequence))
                {
                    return false;
                }

                CountLocked(entry.Issue.Severity, -1);
                if (entry.Unread)
                {
                    UnreadCountLocked(entry.Issue.Severity, -1);
                }

                removed++;
                return true;
            });

            return removed;
        }
    }

    /// <summary>清空（用户操作"清除问题列表"）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _error = 0;
            _warning = 0;
            _info = 0;
            _unread = 0;
            _unreadError = 0;
            _unreadWarning = 0;
            _unreadInfo = 0;
        }
    }

    /// <summary>按序号找条目下标（表按序号升序，二分）；找不到 ⇒ -1。</summary>
    private int IndexOfLocked(long sequence)
    {
        var low = 0;
        var high = _entries.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var current = _entries[mid].Sequence;

            if (current == sequence)
            {
                return mid;
            }

            if (current < sequence)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }

    /// <summary>维护分档计数（**调用方必须持有 <see cref="_gate"/>**，与条目表同拍落地）。</summary>
    private void CountLocked(IssueSeverity severity, int delta)
    {
        switch (severity)
        {
            case IssueSeverity.Error:
                _error += delta;
                break;

            case IssueSeverity.Warning:
                _warning += delta;
                break;

            default:
                _info += delta;
                break;
        }
    }

    /// <summary>维护未读分档计数（**调用方必须持有 <see cref="_gate"/>**）。</summary>
    private void UnreadCountLocked(IssueSeverity severity, int delta)
    {
        _unread += delta;

        switch (severity)
        {
            case IssueSeverity.Error:
                _unreadError += delta;
                break;

            case IssueSeverity.Warning:
                _unreadWarning += delta;
                break;

            default:
                _unreadInfo += delta;
                break;
        }
    }
}
