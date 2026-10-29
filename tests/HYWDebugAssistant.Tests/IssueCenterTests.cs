using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>Issue 中心：汇总、删除、清空与“序号稳定”的回归。</summary>
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

        var recent = center.Recent(3);
        recent.Count.ShouldBe(3);
        recent[0].Sequence.ShouldBe(3, "最新在前");
        recent[^1].Sequence.ShouldBe(1);
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
    }
}
