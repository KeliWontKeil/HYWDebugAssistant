using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>Issue 列表行。</summary>
/// <param name="Sequence">序号。</param>
/// <param name="ObjectPath">归属对象路径。</param>
/// <param name="Category">类别。</param>
/// <param name="Severity">严重度。</param>
/// <param name="Source">来源（框架判定 / 模块报告）。</param>
/// <param name="Message">描述。</param>
/// <param name="Timestamp">发生时间（本地时间显示）。</param>
public sealed record IssueRowViewModel(
    long Sequence,
    string ObjectPath,
    string Category,
    string Severity,
    string Source,
    string Message,
    DateTimeOffset Timestamp)
{
    /// <summary>本地时间文本。</summary>
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// Issue 面板：汇总列表 + **按归属对象 / 类别 / 来源 / 严重度过滤**。
/// </summary>
/// <remarks>
/// 默认轻量不打扰：本面板只做"登记与呈现"，不弹模态框、不阻断运行。
/// </remarks>
public sealed partial class IssuesPanelViewModel : ViewModelBase
{
    /// <summary>"全部"选项（过滤下拉的默认值）。</summary>
    public const string All = "全部";

    /// <summary>单次拉取的最大条数（面板是汇总视图，不做全量无限增长）。</summary>
    public const int MaxIssues = 500;

    private readonly IRuntimeQuery _query;

    /// <summary>创建 Issue 面板视图模型。</summary>
    /// <param name="query">查询面。</param>
    public IssuesPanelViewModel(IRuntimeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    /// <summary>过滤后的列表（最新在前）。</summary>
    public ObservableCollection<IssueRowViewModel> Issues { get; } = [];

    /// <summary>归属对象过滤（子串匹配；空 = 不限）。</summary>
    [ObservableProperty]
    public partial string ObjectFilter { get; set; } = string.Empty;

    /// <summary>类别过滤。</summary>
    [ObservableProperty]
    public partial string CategoryFilter { get; set; } = All;

    /// <summary>来源过滤。</summary>
    [ObservableProperty]
    public partial string SourceFilter { get; set; } = All;

    /// <summary>严重度过滤。</summary>
    [ObservableProperty]
    public partial string SeverityFilter { get; set; } = All;

    /// <summary>拉取到的总条数（未过滤）。</summary>
    [ObservableProperty]
    public partial int TotalCount { get; set; }

    /// <summary>错误级条数（面板徽标用）。</summary>
    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    /// <summary>过滤后的条数。</summary>
    public int FilteredCount => Issues.Count;

    /// <summary>类别选项。</summary>
    public IReadOnlyList<string> Categories { get; } = BuildOptions<HYWDebugAssistant.Sdk.Reporting.IssueCategory>();

    /// <summary>来源选项。</summary>
    public IReadOnlyList<string> Sources { get; } = BuildOptions<HYWDebugAssistant.Sdk.Reporting.IssueSource>();

    /// <summary>严重度选项。</summary>
    public IReadOnlyList<string> Severities { get; } = BuildOptions<HYWDebugAssistant.Sdk.Reporting.IssueSeverity>();

    /// <summary>
    /// 按刷新节拍拉取并过滤。
    /// </summary>
    /// <remarks>用增量同步（见 <see cref="CollectionSync"/>）：每帧重建列表会让悬停等瞬时视觉状态反复重置（闪烁）。</remarks>
    public void Refresh()
    {
        var all = _query.GetIssues(MaxIssues);
        TotalCount = all.Count;
        ErrorCount = all.Count(i => string.Equals(i.Severity, "Error", StringComparison.Ordinal));

        var rows = all.Where(Matches)
            .Select(issue => new IssueRowViewModel(
                issue.Sequence,
                issue.ObjectPath,
                issue.Category,
                issue.Severity,
                issue.Source,
                issue.Message,
                issue.Timestamp))
            .ToList();

        _ = CollectionSync.Sync(Issues, rows);

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>汇总文本（弹窗顶部；含过滤后的条数）。</summary>
    public string Summary => FilteredCount == TotalCount
        ? $"共 {TotalCount} 条" + (ErrorCount > 0 ? $"，其中错误 {ErrorCount} 条" : string.Empty)
        : $"共 {TotalCount} 条，当前过滤显示 {FilteredCount} 条" + (ErrorCount > 0 ? $"，其中错误 {ErrorCount} 条" : string.Empty);

    /// <summary>清除全部过滤条件。</summary>
    [RelayCommand]
    private void ClearFilter()
    {
        ObjectFilter = string.Empty;
        CategoryFilter = All;
        SourceFilter = All;
        SeverityFilter = All;
    }

    private bool Matches(IssueView issue)
    {
        if (!string.IsNullOrWhiteSpace(ObjectFilter)
            && !issue.ObjectPath.Contains(ObjectFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesOption(CategoryFilter, issue.Category)
            && MatchesOption(SourceFilter, issue.Source)
            && MatchesOption(SeverityFilter, issue.Severity);
    }

    private static bool MatchesOption(string filter, string value) =>
        string.Equals(filter, All, StringComparison.Ordinal) || string.Equals(filter, value, StringComparison.Ordinal);

    private static IReadOnlyList<string> BuildOptions<TEnum>()
        where TEnum : struct, Enum =>
        [All, .. Enum.GetNames<TEnum>()];
}
