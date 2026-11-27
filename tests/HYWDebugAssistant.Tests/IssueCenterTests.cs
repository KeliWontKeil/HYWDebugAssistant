using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>Issue 中心：汇总、已读、删除、清空与“序号稳定”的回归。</summary>
/// <remarks>
/// 已读归条目自己：入口角标（未读错误数）与面板列表读同一份事实。
/// 这里断言"集合与计数始终一致"，不钉具体控件的呈现。
/// </remarks>
public class IssueCenterTests
{
    [Fact]
    public void 上报_应按严重度汇总且最近在前()
    {
        var center = new IssueCenter();
        center.Report("module:a", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Error, "错误 1"));
        center.Report("module:a", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "警告 1"));
        center.Report("module:b", new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Info, "信息 1"));

        var summary = center.Summary();
        summary.Total.ShouldBe(3);
        summary.Error.ShouldBe(1);
        summary.Warning.ShouldBe(1);
        summary.Info.ShouldBe(1);

        // 新上报都是未读：角标（未读错误数）与列表必须同源。
        summary.UnreadTotal.ShouldBe(3);
        summary.UnreadError.ShouldBe(1);
        summary.UnreadWarning.ShouldBe(1);
        summary.UnreadInfo.ShouldBe(1);
        center.Snapshot().ShouldAllBe(entry => entry.Unread);

        var recent = center.Recent(3);
        recent.Count.ShouldBe(3);
        recent[0].Sequence.ShouldBe(3, "最新在前");
        recent[^1].Sequence.ShouldBe(1);

        // 标为已读只影响未读计数，不影响分档总数。
        center.MarkRead([1], read: true).ShouldBe(1);
        center.MarkRead([1], read: true).ShouldBe(0, "已经读过的不重复计数");
        var afterRead = center.Summary();
        afterRead.Error.ShouldBe(1);
        afterRead.UnreadTotal.ShouldBe(2);
        afterRead.UnreadError.ShouldBe(0, "读掉那条错误后角标应归零");
        center.Snapshot().Single(entry => entry.Sequence == 1).Unread.ShouldBeFalse();

        // 再标回未读：角标重新出现。
        center.MarkRead([1], read: false).ShouldBe(1);
        center.Summary().UnreadError.ShouldBe(1);

        // 全部已读：未读归零（含不在显示窗口内的那些）。
        center.MarkAllRead().ShouldBe(3);
        center.Summary().UnreadTotal.ShouldBe(0);
        center.Summary().UnreadError.ShouldBe(0);
        center.Summary().Total.ShouldBe(3, "全部已读不改条目数");
        center.MarkAllRead().ShouldBe(0);
    }

    [Fact]
    public void 删除指定序号_应只移除命中的条目且序号不回退()
    {
        var center = new IssueCenter();
        center.Report("module:a", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "1"));
        center.Report("module:b", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "2"));
        center.Report("module:c", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "3"));

        center.Remove([2]).ShouldBe(1);
        center.Remove([99]).ShouldBe(0);
        center.Count.ShouldBe(2);
        center.Snapshot().Select(entry => entry.Sequence).ShouldBe([1L, 3L]);

        center.Report("module:d", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Info, "4"));
        center.Recent(1)[0].Sequence.ShouldBe(4, "删除不应重置自增序号");

        // 删除已读条目只减总数、不减未读；删除未读条目两者都减。
        center.MarkRead([1], read: true).ShouldBe(1);
        var beforeRemove = center.Summary();
        center.Remove([1]).ShouldBe(1);
        var afterReadRemoved = center.Summary();
        afterReadRemoved.Total.ShouldBe(beforeRemove.Total - 1);
        afterReadRemoved.UnreadTotal.ShouldBe(beforeRemove.UnreadTotal, "删掉已读的不该改变未读数");

        center.Remove([3]).ShouldBe(1);
        center.Summary().UnreadTotal.ShouldBe(afterReadRemoved.UnreadTotal - 1, "删掉未读的要扣未读数");
    }

    [Fact]
    public void 清空_应清掉全部条目()
    {
        var center = new IssueCenter();
        center.Report("module:a", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Error, "1"));
        center.Report("module:b", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "2"));

        center.Clear();

        center.Count.ShouldBe(0);
        center.Snapshot().ShouldBeEmpty();
        center.Summary().Total.ShouldBe(0);
        center.Summary().Error.ShouldBe(0);
        center.Summary().Warning.ShouldBe(0);
        center.Summary().UnreadTotal.ShouldBe(0);
        center.Summary().UnreadError.ShouldBe(0);
    }

    [Fact]
    public void 条目数应有上限_且分档计数同步扣减()
    {
        var center = new IssueCenter();

        // 上限是"意外地按数据频率报问题"时的止损线：超出后丢最旧的，计数必须跟着减。
        const int overflow = 200;

        for (var i = 0; i < 4096 + overflow; i++)
        {
            var severity = i % 3 == 0 ? IssueSeverity.Error : IssueSeverity.Info;
            center.Report("module:a", new IssueReport(IssueCategory.ModuleError, severity, $"问题 {i}"));
        }

        center.Count.ShouldBe(4096);
        center.Summary().Total.ShouldBe(4096);
        center.Snapshot()[^1].Sequence.ShouldBe(4096L + overflow, "丢最旧、留最新");

        var snapshot = center.Snapshot();
        snapshot.Count(entry => entry.Issue.Severity == IssueSeverity.Error).ShouldBe(center.Summary().Error);
        snapshot.Count(entry => entry.Issue.Severity == IssueSeverity.Warning).ShouldBe(center.Summary().Warning);
        snapshot.Count(entry => entry.Issue.Severity == IssueSeverity.Info).ShouldBe(center.Summary().Info);

        // 未读计数同样必须与表一致（淘汰掉的最旧条目要连未读数一起扣）。
        snapshot.Count(entry => entry.Unread).ShouldBe(center.Summary().UnreadTotal);
        snapshot.Count(entry => entry.Unread && entry.Issue.Severity == IssueSeverity.Error)
            .ShouldBe(center.Summary().UnreadError);
    }
}
