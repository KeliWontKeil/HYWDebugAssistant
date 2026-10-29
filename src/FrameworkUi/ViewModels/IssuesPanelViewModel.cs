using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>本文件各视图模型的文本表入口（拥有者 = FrameworkUi 程序集）。</summary>
internal static class IssuesText
{
    /// <summary>取一条文案。</summary>
    public static string Get(string key) => TextCatalog.Get(typeof(IssuesText).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(IssuesText).Assembly, key, args);
}

/// <summary>Issue 过滤器里的一项（值 + 中文显示名）。</summary>
/// <param name="Value">过滤值（“全部”或枚举名）。</param>
/// <param name="Label">显示名。</param>
public sealed record IssueFilterOption(string Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>严重度过滤按钮（全部 / 错误 / 警告 / 信息）及其计数。</summary>
public sealed partial class IssueSeverityFilterViewModel : ViewModelBase
{
    /// <summary>创建严重度过滤项。</summary>
    /// <param name="key">过滤键（<c>All</c> 或枚举名）。</param>
    /// <param name="label">显示名。</param>
    public IssueSeverityFilterViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>过滤键。</summary>
    public string Key { get; }

    /// <summary>显示名。</summary>
    public string Label { get; }

    /// <summary>当前筛选条件下的条数。</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>是否当前选中。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Issue 列表的一行（**稳定身份 = Sequence**）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>行对象只在序号首次出现时创建；刷新只更新未读状态，不重建列表 ⇒ 选中与悬停不闪；</item>
///   <item>列表只放“能扫一眼”的信息；完整消息 / 原始对象路径 / 原始枚举名放详情与复制文本；</item>
///   <item>未读是**面板会话状态**，不进 Issue 中心、不落盘。</item>
/// </list>
/// </remarks>
public sealed partial class IssueRowViewModel : ViewModelBase
{
    /// <summary>创建一行。</summary>
    /// <param name="issue">Issue 视图。</param>
    /// <param name="objectLabel">友好对象名。</param>
    public IssueRowViewModel(IssueView issue, string objectLabel)
    {
        ArgumentNullException.ThrowIfNull(issue);

        Sequence = issue.Sequence;
        ObjectPath = issue.ObjectPath;
        ObjectLabel = objectLabel;
        CategoryKey = issue.Category;
        CategoryLabel = IssueDisplay.CategoryLabel(issue.Category);
        SeverityKey = issue.Severity;
        SeverityLabel = IssueDisplay.SeverityLabel(issue.Severity);
        SourceKey = issue.Source;
        SourceLabel = IssueDisplay.SourceLabel(issue.Source);
        Message = issue.Message;
        Timestamp = issue.Timestamp;
    }

    /// <summary>序号（稳定身份；删除/已读都用它）。</summary>
    public long Sequence { get; }

    /// <summary>原始对象路径（诊断用；详情中可见）。</summary>
    public string ObjectPath { get; }

    /// <summary>友好对象名（列表用）。</summary>
    public string ObjectLabel { get; }

    /// <summary>类别原始值（过滤用）。</summary>
    public string CategoryKey { get; }

    /// <summary>类别中文名。</summary>
    public string CategoryLabel { get; }

    /// <summary>严重度原始值（过滤用）。</summary>
    public string SeverityKey { get; }

    /// <summary>严重度中文名。</summary>
    public string SeverityLabel { get; }

    /// <summary>来源原始值（过滤用）。</summary>
    public string SourceKey { get; }

    /// <summary>来源中文名。</summary>
    public string SourceLabel { get; }

    /// <summary>完整消息。</summary>
    public string Message { get; }

    /// <summary>发生时间（UTC；显示时转本地）。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>列表时间：今天只显示时分秒，非今天带日期。</summary>
    public string TimeText
    {
        get
        {
            var local = Timestamp.ToLocalTime();

            return local.Date == DateTimeOffset.Now.Date
                ? local.ToString("HH:mm:ss", CultureInfo.CurrentCulture)
                : local.ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>详情用完整本地时间。</summary>
    public string FullTimeText =>
        Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.CurrentCulture);

    /// <summary>列表消息预览（换行压成空格；完整内容在详情）。</summary>
    public string MessagePreview => Message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>错误级。</summary>
    public bool IsError => string.Equals(SeverityKey, nameof(IssueSeverity.Error), StringComparison.Ordinal);

    /// <summary>警告级。</summary>
    public bool IsWarning => string.Equals(SeverityKey, nameof(IssueSeverity.Warning), StringComparison.Ordinal);

    /// <summary>信息级。</summary>
    public bool IsInfo => string.Equals(SeverityKey, nameof(IssueSeverity.Info), StringComparison.Ordinal);

    /// <summary>是否未读。</summary>
    [ObservableProperty]
    public partial bool IsUnread { get; set; } = true;

    /// <summary>是否已读（用于界面弱化）。</summary>
    public bool IsRead => !IsUnread;

    partial void OnIsUnreadChanged(bool value) => OnPropertyChanged(nameof(IsRead));

    /// <summary>复制消息用的文本。</summary>
    public string ToMessageText() => Message;

    /// <summary>复制完整诊断用的文本。</summary>
    public string ToDetailText() =>
        $"[{FullTimeText}] {SeverityLabel}（{SeverityKey}） · {CategoryLabel}（{CategoryKey}） · {SourceLabel}（{SourceKey}）"
        + "\n" + IssuesText.Get("issues.copy.object", ObjectLabel, ObjectPath)
        + "\n" + IssuesText.Get("issues.copy.message", Message)
        + "\n" + IssuesText.Get("issues.copy.sequence", Sequence);
}

/// <summary>
/// Issue 面板：**列表 + 详情**、四维过滤、已读/未读、复制、删除与清空。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>默认轻量不打扰：本面板只做“登记与呈现”，不弹模态框、不阻断运行；</item>
///   <item>删除/清空只影响 Issue 列表；不改模块、网络或配置；</item>
///   <item>刷新每帧调用，但行对象按 Sequence 稳定复用，不重建列表。</item>
/// </list>
/// </remarks>
public sealed partial class IssuesPanelViewModel : ViewModelBase
{
    /// <summary>"全部"选项的**过滤键**（不是显示文案；显示名走文本表）。</summary>
    public const string All = "all";

    /// <summary>单次拉取的最大条数（列表显示“最近 N 条”；总数看 IssueSummary）。</summary>
    public const int MaxIssues = 500;

    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    /// <summary>已读序号（**本会话**状态）。</summary>
    private readonly HashSet<long> _read = [];

    /// <summary>最近拉取的全部 Issue 行（未过滤；按最新在前）。</summary>
    private readonly List<IssueRowViewModel> _allIssues = [];

    private bool _syncingSelection;
    private readonly bool _suppressFilterChanges;

    /// <summary>创建 Issue 面板视图模型。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="command">命令面（删除/清空 Issue）。</param>
    public IssuesPanelViewModel(IRuntimeQuery query, IRuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;

        // 构造期先不要触发过滤：SeverityFilters 尚未赋值，SelectedCategory 的 setter 会调用 ApplyFilter。
        _suppressFilterChanges = true;

        CategoryOptions = [new IssueFilterOption(All, IssuesText.Get("issues.filter.all")), .. Enum.GetNames<IssueCategory>().Select(name => new IssueFilterOption(name, IssueDisplay.CategoryLabel(name)))];
        SourceOptions = [new IssueFilterOption(All, IssuesText.Get("issues.filter.all")), .. Enum.GetNames<IssueSource>().Select(name => new IssueFilterOption(name, IssueDisplay.SourceLabel(name)))];
        SelectedCategory = CategoryOptions[0];
        SelectedSource = SourceOptions[0];

        SeverityFilters =
        [
            new IssueSeverityFilterViewModel(All, IssuesText.Get("issues.filter.all")),
            new IssueSeverityFilterViewModel(nameof(IssueSeverity.Error), IssuesText.Get("issues.severity.error")),
            new IssueSeverityFilterViewModel(nameof(IssueSeverity.Warning), IssuesText.Get("issues.severity.warning")),
            new IssueSeverityFilterViewModel(nameof(IssueSeverity.Info), IssuesText.Get("issues.severity.info")),
        ];
        SeverityFilters[0].IsSelected = true;
        _suppressFilterChanges = false;
    }

    /// <summary>过滤后的列表（最新在前）。</summary>
    public ObservableCollection<IssueRowViewModel> Issues { get; } = [];

    /// <summary>类别下拉选项。</summary>
    public IReadOnlyList<IssueFilterOption> CategoryOptions { get; }

    /// <summary>来源下拉选项。</summary>
    public IReadOnlyList<IssueFilterOption> SourceOptions { get; }

    /// <summary>严重度过滤按钮（带计数）。</summary>
    public ObservableCollection<IssueSeverityFilterViewModel> SeverityFilters { get; }

    /// <summary>归属对象搜索（对象路径 / 友好名 / 消息；空 = 不限）。</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>类别过滤。</summary>
    [ObservableProperty]
    public partial IssueFilterOption? SelectedCategory { get; set; }

    /// <summary>来源过滤。</summary>
    [ObservableProperty]
    public partial IssueFilterOption? SelectedSource { get; set; }

    /// <summary>严重度过滤键（<see cref="All"/> 或枚举名）。</summary>
    [ObservableProperty]
    public partial string SeverityFilter { get; set; } = All;

    /// <summary>只看未读。</summary>
    [ObservableProperty]
    public partial bool OnlyUnread { get; set; }

    /// <summary>选中行（列表与详情共用）。</summary>
    [ObservableProperty]
    public partial IssueRowViewModel? SelectedIssue { get; set; }

    /// <summary>全量总数（来自 IssueSummary，不受显示上限影响）。</summary>
    [ObservableProperty]
    public partial int TotalCount { get; set; }

    /// <summary>全量错误数。</summary>
    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    /// <summary>全量警告数。</summary>
    [ObservableProperty]
    public partial int WarningCount { get; set; }

    /// <summary>全量信息数。</summary>
    [ObservableProperty]
    public partial int InfoCount { get; set; }

    /// <summary>未读条数（当前已加载集合）。</summary>
    [ObservableProperty]
    public partial int UnreadCount { get; set; }

    /// <summary>过滤后的条数。</summary>
    public int FilteredCount => Issues.Count;

    /// <summary>过滤后条数的显示文本（"当前显示 {0} 条"）。</summary>
    public string FilteredCountText => IssuesText.Get("issues.filter.count", FilteredCount);

    /// <summary>是否有任何 Issue（全量）。</summary>
    public bool HasIssues => TotalCount > 0;

    /// <summary>过滤后是否有结果。</summary>
    public bool HasFilteredIssues => Issues.Count > 0;

    /// <summary>是否有选中项。</summary>
    public bool HasSelection => SelectedIssue is not null;

    /// <summary>是否只显示最近 N 条。</summary>
    public bool IsTruncated => TotalCount > _allIssues.Count;

    /// <summary>列表截断说明。</summary>
    public string TruncationText => IsTruncated
        ? IssuesText.Get("issues.summary.truncated", _allIssues.Count, TotalCount)
        : string.Empty;

    /// <summary>汇总文本。</summary>
    public string Summary =>
        IssuesText.Get("issues.summary", TotalCount, ErrorCount, WarningCount, InfoCount, UnreadCount)
        + (IsTruncated ? IssuesText.Get("issues.summary.truncatedSuffix", _allIssues.Count) : string.Empty);

    /// <summary>操作反馈（复制/删除/清空）。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>是否处于“清空全部”的内联确认态。</summary>
    [ObservableProperty]
    public partial bool IsConfirmingClear { get; set; }

    /// <summary>当前选中的消息文本（复制用）。</summary>
    public string? SelectedMessageText => SelectedIssue?.ToMessageText();

    /// <summary>当前选中的完整诊断文本（复制用）。</summary>
    public string? SelectedDetailText => SelectedIssue?.ToDetailText();

    /// <summary>
    /// 按刷新节拍拉取并更新（不重建行；不自动标已读）。
    /// </summary>
    public void Refresh()
    {
        var summary = _query.GetIssueSummary();
        TotalCount = summary.Total;
        ErrorCount = summary.Error;
        WarningCount = summary.Warning;
        InfoCount = summary.Info;

        var instances = _query.GetInstances()
            .GroupBy(instance => instance.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.Ordinal);

        var existing = _allIssues.ToDictionary(row => row.Sequence);
        var nextRows = new List<IssueRowViewModel>();

        foreach (var issue in _query.GetIssues(MaxIssues))
        {
            var row = existing.TryGetValue(issue.Sequence, out var current)
                ? current
                : new IssueRowViewModel(issue, IssueDisplay.ObjectLabel(issue.ObjectPath, instances));

            row.IsUnread = !_read.Contains(issue.Sequence);
            nextRows.Add(row);
        }

        _allIssues.Clear();
        _allIssues.AddRange(nextRows);

        // 已读集合只保留当前仍存在的序号（删除/清空/滚出上限后不产生死数据）。
        var alive = nextRows.Select(row => row.Sequence).ToHashSet();
        _read.RemoveWhere(sequence => !alive.Contains(sequence));

        ApplyFilter();

        if (SelectedIssue is { } selected)
        {
            var stillThere = nextRows.FirstOrDefault(row => row.Sequence == selected.Sequence);
            SetSelectedIssueSilently(stillThere is not null && Issues.Contains(stillThere) ? stillThere : null);
        }
    }

    /// <summary>按“搜索 + 类别 + 来源 + 严重度 + 只看未读”过滤。</summary>
    private void ApplyFilter()
    {
        if (_suppressFilterChanges)
        {
            return;
        }

        var baseRows = _allIssues.Where(MatchesNonSeverity).ToList();

        foreach (var filter in SeverityFilters)
        {
            filter.Count = string.Equals(filter.Key, All, StringComparison.Ordinal)
                ? baseRows.Count
                : baseRows.Count(row => string.Equals(row.SeverityKey, filter.Key, StringComparison.Ordinal));
            filter.IsSelected = string.Equals(filter.Key, SeverityFilter, StringComparison.Ordinal);
        }

        var filtered = baseRows
            .Where(row => string.Equals(SeverityFilter, All, StringComparison.Ordinal)
                || string.Equals(row.SeverityKey, SeverityFilter, StringComparison.Ordinal))
            .Where(row => !OnlyUnread || row.IsUnread)
            .ToList();

        _ = CollectionSync.Sync(Issues, filtered);

        if (SelectedIssue is { } selected && !filtered.Contains(selected))
        {
            SetSelectedIssueSilently(null);
        }

        UpdateUnreadCounts();
        NotifySummary();
    }

    /// <summary>匹配非严重度条件（搜索 / 类别 / 来源）。</summary>
    private bool MatchesNonSeverity(IssueRowViewModel row)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();

            if (!row.ObjectPath.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !row.ObjectLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !row.Message.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return MatchesOption(SelectedCategory?.Value ?? All, row.CategoryKey)
            && MatchesOption(SelectedSource?.Value ?? All, row.SourceKey);
    }

    private static bool MatchesOption(string filter, string value) =>
        string.Equals(filter, All, StringComparison.Ordinal)
        || string.Equals(filter, value, StringComparison.Ordinal);

    private void UpdateUnreadCounts() => UnreadCount = _allIssues.Count(row => row.IsUnread);

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(FilteredCountText));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasFilteredIssues));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsTruncated));
        OnPropertyChanged(nameof(TruncationText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SelectedMessageText));
        OnPropertyChanged(nameof(SelectedDetailText));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(IssueFilterOption? value) => ApplyFilter();

    partial void OnSelectedSourceChanged(IssueFilterOption? value) => ApplyFilter();

    partial void OnSeverityFilterChanged(string value) => ApplyFilter();

    partial void OnOnlyUnreadChanged(bool value) => ApplyFilter();

    partial void OnSelectedIssueChanged(IssueRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedMessageText));
        OnPropertyChanged(nameof(SelectedDetailText));

        if (!_syncingSelection && value is not null)
        {
            MarkRead(value, read: true);
        }
    }

    /// <summary>切换严重度过滤（按钮命令）。</summary>
    /// <param name="filter">被点击的严重度项。</param>
    [RelayCommand]
    private void SelectSeverity(IssueSeverityFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        SeverityFilter = filter.Key;
    }

    /// <summary>标记当前选中项为已读/未读。</summary>
    [RelayCommand]
    private void ToggleSelectedRead()
    {
        if (SelectedIssue is { } selected)
        {
            MarkRead(selected, !selected.IsUnread);
        }
    }

    /// <summary>全部已读（当前已加载集合；总数超过上限时说明只覆盖已加载部分）。</summary>
    [RelayCommand]
    private void MarkAllRead()
    {
        foreach (var row in _allIssues)
        {
            _read.Add(row.Sequence);
            row.IsUnread = false;
        }

        UpdateUnreadCounts();
        ApplyFilter();
        StatusMessage = IssuesText.Get("issues.status.markedAllRead");
    }

    /// <summary>删除当前选中项（只影响 Issue 列表）。</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedIssue is not { } selected)
        {
            return;
        }

        var index = Issues.IndexOf(selected);
        var result = _command.RemoveIssues([selected.Sequence]);

        if (!result.Succeeded)
        {
            StatusMessage = IssuesText.Get("issues.status.deleteFailed", string.Join("；", result.Failures));
            return;
        }

        _read.Remove(selected.Sequence);
        StatusMessage = IssuesText.Get("issues.status.deleted");
        Refresh();

        if (Issues.Count > 0)
        {
            SelectedIssue = Issues[Math.Clamp(index, 0, Issues.Count - 1)];
        }
    }

    /// <summary>进入“清空全部”的内联确认态。</summary>
    [RelayCommand]
    private void BeginClear() => IsConfirmingClear = true;

    /// <summary>取消“清空全部”。</summary>
    [RelayCommand]
    private void CancelClear() => IsConfirmingClear = false;

    /// <summary>确认清空全部 Issue（不影响模块运行）。</summary>
    [RelayCommand]
    private void ConfirmClear()
    {
        var result = _command.ClearIssues();

        if (!result.Succeeded)
        {
            StatusMessage = IssuesText.Get("issues.status.clearFailed", string.Join("；", result.Failures));
            return;
        }

        _read.Clear();
        SelectedIssue = null;
        IsConfirmingClear = false;
        StatusMessage = IssuesText.Get("issues.status.cleared");
        Refresh();
    }

    private void MarkRead(IssueRowViewModel row, bool read)
    {
        if (read)
        {
            _read.Add(row.Sequence);
        }
        else
        {
            _read.Remove(row.Sequence);
        }

        row.IsUnread = !read;
        UpdateUnreadCounts();
        NotifySummary();
    }

    private void SetSelectedIssueSilently(IssueRowViewModel? value)
    {
        _syncingSelection = true;
        SelectedIssue = value;
        _syncingSelection = false;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedMessageText));
        OnPropertyChanged(nameof(SelectedDetailText));
    }
}

/// <summary>Issue 的显示映射（友好名 / 原始名 / 时间 / 对象路径）。</summary>
public static class IssueDisplay
{
    /// <summary>类别中文名。</summary>
    public static string CategoryLabel(string key) => key switch
    {
        nameof(IssueCategory.ConfigurationError) => IssuesText.Get("issues.category.configuration"),
        nameof(IssueCategory.CompositionError) => IssuesText.Get("issues.category.composition"),
        nameof(IssueCategory.InterfaceError) => IssuesText.Get("issues.category.interface"),
        nameof(IssueCategory.ModuleError) => IssuesText.Get("issues.category.module"),
        nameof(IssueCategory.CommunicationError) => IssuesText.Get("issues.category.communication"),
        nameof(IssueCategory.RuntimeError) => IssuesText.Get("issues.category.runtime"),
        nameof(IssueCategory.PresentationError) => IssuesText.Get("issues.category.presentation"),
        _ => key,
    };

    /// <summary>来源中文名。</summary>
    public static string SourceLabel(string key) => key switch
    {
        nameof(IssueSource.Framework) => IssuesText.Get("issues.source.framework"),
        nameof(IssueSource.Module) => IssuesText.Get("issues.source.module"),
        _ => key,
    };

    /// <summary>严重度中文名。</summary>
    public static string SeverityLabel(string key) => key switch
    {
        nameof(IssueSeverity.Error) => IssuesText.Get("issues.severity.error"),
        nameof(IssueSeverity.Warning) => IssuesText.Get("issues.severity.warning"),
        nameof(IssueSeverity.Info) => IssuesText.Get("issues.severity.info"),
        _ => key,
    };

    /// <summary>对象路径 → 列表友好名（原始路径仍在详情里保留）。</summary>
    /// <param name="objectPath">原始对象路径。</param>
    /// <param name="instances">实例 ID → 显示名（可为空表）。</param>
    public static string ObjectLabel(string objectPath, IReadOnlyDictionary<string, string> instances)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return IssuesText.Get("issues.object.unassigned");
        }

        if (objectPath.StartsWith("module:", StringComparison.Ordinal))
        {
            var rest = objectPath["module:".Length..];
            var slash = rest.IndexOf('/');
            var instanceId = slash > 0 ? rest[..slash] : rest;
            var port = slash > 0 ? rest[(slash + 1)..] : null;
            var name = instances.TryGetValue(instanceId, out var display) && !string.IsNullOrWhiteSpace(display)
                ? display
                : instanceId;

            return port is { Length: > 0 }
                ? IssuesText.Get("issues.object.modulePort", name, instanceId, port)
                : IssuesText.Get("issues.object.module", name, instanceId);
        }

        if (objectPath.StartsWith("network:", StringComparison.Ordinal))
        {
            var name = objectPath["network:".Length..];
            return string.IsNullOrWhiteSpace(name)
                ? IssuesText.Get("issues.object.networkUnnamed")
                : IssuesText.Get("issues.object.network", name);
        }

        if (objectPath.StartsWith("package:", StringComparison.Ordinal))
        {
            return IssuesText.Get("issues.object.package", objectPath["package:".Length..]);
        }

        return objectPath;
    }
}
