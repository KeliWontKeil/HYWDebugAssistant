using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Ui.Design;
using NSubstitute;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>Issue 面板：未读、严重度/未读过滤与详情文本。</summary>
public class IssuesPanelViewModelTests
{
    [Fact]
    public void 选中即已读_全部已读与过滤_应同步()
    {
        var issues = new IssuesPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
        issues.Refresh();

        issues.Issues.Count.ShouldBe(4);
        issues.UnreadCount.ShouldBe(4);

        var first = issues.Issues[0];
        issues.SelectedIssue = first;
        issues.UnreadCount.ShouldBe(3, "选中一行应标记已读");
        issues.SelectedDetailText.ShouldNotBeNullOrWhiteSpace("详情必须给出归属对象等事实");

        issues.OnlyUnread = true;
        issues.Issues.Count.ShouldBe(3, "只看未读时不应再显示刚读过的那行");

        issues.OnlyUnread = false;
        issues.SeverityFilter = nameof(IssueSeverity.Error);
        issues.Issues.Count.ShouldBe(1);

        issues.MarkAllReadCommand.Execute(null);
        issues.UnreadCount.ShouldBe(0);
    }

    [Fact]
    public void 只刷汇总时不应拉取完整Issue列表()
    {
        var query = Substitute.For<IRuntimeQuery>();
        query.GetIssueSummary().Returns(new IssueSummaryView(Total: 7, Error: 3, Warning: 2, Info: 2));

        var issues = new IssuesPanelViewModel(query, Substitute.For<IRuntimeCommand>());
        issues.RefreshSummary();

        issues.TotalCount.ShouldBe(7);
        issues.ErrorCount.ShouldBe(3);
        issues.WarningCount.ShouldBe(2);
        issues.InfoCount.ShouldBe(2);
        query.DidNotReceive().GetIssues(Arg.Any<int>());
    }

    [Fact]
    public void 搜索_应匹配对象与消息且清除能恢复()
    {
        var issues = new IssuesPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
        issues.Refresh();

        issues.SearchText = "sender-1";
        issues.Issues.Count.ShouldBe(1);

        issues.SearchText = "保存路径";
        issues.Issues.Count.ShouldBe(1);

        issues.SearchText = string.Empty;
        issues.Issues.Count.ShouldBe(4);
        issues.SearchText.ShouldBeEmpty();
    }
}
