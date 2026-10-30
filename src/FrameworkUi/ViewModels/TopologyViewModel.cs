using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui.Formatting;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>本文件各视图模型的文本表入口（拥有者 = FrameworkUi 程序集）。</summary>
internal static class TopologyText
{
    /// <summary>取一条文案。</summary>
    public static string Get(string key) => TextCatalog.Get(typeof(TopologyText).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TopologyText).Assembly, key, args);
}

/// <summary>拓扑状态过滤按钮（全部 / 异常 / 停流 / 悬空）及其计数。</summary>
public sealed partial class TopologyStateFilterViewModel : ViewModelBase
{
    /// <summary>创建状态过滤项。</summary>
    /// <param name="key">过滤键。</param>
    /// <param name="label">显示名。</param>
    public TopologyStateFilterViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>过滤键。</summary>
    public string Key { get; }

    /// <summary>显示名。</summary>
    public string Label { get; }

    /// <summary>当前搜索条件下的条数。</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>是否选中。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>拓扑里的一个成员（源或宿）。</summary>
/// <remarks>身份 = 网络键 + 角色 + 原始字面量；刷新只更新字段，不重建行对象。</remarks>
public sealed partial class TopologyMemberViewModel : ViewModelBase
{
    /// <summary>创建成员。</summary>
    /// <param name="raw">原始字面量（<c>实例.口名</c>）。</param>
    /// <param name="role">角色（源 / 宿）。</param>
    /// <param name="displayName">友好显示名。</param>
    public TopologyMemberViewModel(string raw, string role, string displayName)
    {
        Raw = raw;
        Role = role;
        DisplayName = displayName;
    }

    /// <summary>原始成员字面量（tooltip / 复制用）。</summary>
    public string Raw { get; }

    /// <summary>角色（源 / 宿）。</summary>
    public string Role { get; }

    /// <summary>友好显示名。</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>状态文本（正常 / 停流 / 悬空）。</summary>
    [ObservableProperty]
    public partial string StateText { get; set; } = TopologyText.Get("topology.state.normal");

    /// <summary>是否悬空。</summary>
    [ObservableProperty]
    public partial bool IsUnresolved { get; set; }

    /// <summary>是否停流。</summary>
    [ObservableProperty]
    public partial bool IsStalled { get; set; }

    /// <summary>是否有指标（悬空源没有）。</summary>
    [ObservableProperty]
    public partial bool HasMetrics { get; set; }

    /// <summary>投递数（仅源）。</summary>
    [ObservableProperty]
    public partial long Delivered { get; set; }

    /// <summary>丢弃数（仅源）。</summary>
    [ObservableProperty]
    public partial long Dropped { get; set; }

    /// <summary>有效载荷速率（仅源）。</summary>
    [ObservableProperty]
    public partial double RateValue { get; set; }

    /// <summary>速率文本。</summary>
    [ObservableProperty]
    public partial string RateText { get; set; } = "—";

    /// <summary>最后数据时间（仅源）。</summary>
    [ObservableProperty]
    public partial string LastDataText { get; set; } = "—";

    /// <summary>表格投递列。</summary>
    public string DeliveredText => HasMetrics
        ? Delivered.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>表格丢弃列。</summary>
    public string DroppedText => HasMetrics
        ? Dropped.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>按原因拆开的丢弃串（仅源；全零 ⇒ 空串）。</summary>
    [ObservableProperty]
    public partial string DropBreakdownText { get; set; } = string.Empty;

    /// <summary>队列水位串（仅宿；无队列或无压力 ⇒ 空串）。</summary>
    [ObservableProperty]
    public partial string QueueText { get; set; } = string.Empty;

    /// <summary>宿口队列是否持续为满（**只作文本判定**，不改颜色/不改列）。</summary>
    [ObservableProperty]
    public partial bool IsSaturated { get; set; }

    /// <summary>队列轴分项（过期 / 停止清尾 / 模块内部；仅宿，非零才出现）。</summary>
    [ObservableProperty]
    public partial string QueueDetailText { get; set; } = string.Empty;

    /// <summary>详情 / 复制用的一行（状态 + 队列轴分项）。</summary>
    public string DetailText => QueueDetailText.Length == 0
        ? StateText
        : $"{StateText} · {QueueDetailText}";

    /// <summary>指标摘要（复制用；源带按原因丢弃）。</summary>
    public string MetricsText => HasMetrics
        ? TopologyText.Get("topology.metrics.summary", DeliveredText, DroppedText, RateText, LastDataText)
          + (DropBreakdownText.Length > 0 ? " · " + DropBreakdownText : string.Empty)
        : StateText;

    partial void OnHasMetricsChanged(bool value) => NotifyMetrics();

    partial void OnDeliveredChanged(long value) => NotifyMetrics();

    partial void OnDroppedChanged(long value) => NotifyMetrics();

    partial void OnRateTextChanged(string value) => NotifyMetrics();

    partial void OnLastDataTextChanged(string value) => NotifyMetrics();

    partial void OnDropBreakdownTextChanged(string value) => NotifyMetrics();

    partial void OnStateTextChanged(string value)
    {
        OnPropertyChanged(nameof(MetricsText));
        OnPropertyChanged(nameof(DetailText));
    }

    partial void OnQueueDetailTextChanged(string value) => OnPropertyChanged(nameof(DetailText));

    private void NotifyMetrics()
    {
        OnPropertyChanged(nameof(DeliveredText));
        OnPropertyChanged(nameof(DroppedText));
        OnPropertyChanged(nameof(MetricsText));
    }
}

/// <summary>
/// 拓扑诊断的一条网络（列表行 + 详情数据），**不再承载头部汇总指标**。
/// </summary>
/// <remarks>标识 = <see cref="NetworkView.Key"/>（不能用网络名：匿名网络名是空串）。</remarks>
public sealed partial class TopologyNetworkViewModel : ViewModelBase
{
    /// <summary>创建网络。</summary>
    /// <param name="key">网络身份键。</param>
    public TopologyNetworkViewModel(string key) => Key = key;

    /// <summary>网络身份键。</summary>
    public string Key { get; }

    /// <summary>显示名（匿名 = 直连）。</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = TopologyText.Get("topology.network.direct");

    /// <summary>类型（命名 / 直连）。</summary>
    [ObservableProperty]
    public partial string KindLabel { get; set; } = TopologyText.Get("topology.kind.direct");

    /// <summary>是否停流。</summary>
    [ObservableProperty]
    public partial bool HasStalled { get; set; }

    /// <summary>是否有悬空成员。</summary>
    [ObservableProperty]
    public partial bool HasUnresolved { get; set; }

    /// <summary>悬空成员数（源 + 宿）。</summary>
    [ObservableProperty]
    public partial int UnresolvedMemberCount { get; set; }

    /// <summary>源数。</summary>
    [ObservableProperty]
    public partial int SourceCount { get; set; }

    /// <summary>宿数。</summary>
    [ObservableProperty]
    public partial int TargetCount { get; set; }

    /// <summary>诊断结论（一句事实）。</summary>
    [ObservableProperty]
    public partial string DiagnosticText { get; set; } = TopologyText.Get("topology.diagnostic.normal");

    /// <summary>列表分组标签（异常 / 正常；仅组首行有值）。</summary>
    [ObservableProperty]
    public partial string GroupLabel { get; set; } = string.Empty;

    /// <summary>是否有分组标签。</summary>
    public bool HasGroupLabel => GroupLabel.Length > 0;

    partial void OnGroupLabelChanged(string value) => OnPropertyChanged(nameof(HasGroupLabel));

    /// <summary>源成员。</summary>
    public ObservableCollection<TopologyMemberViewModel> Sources { get; } = [];

    /// <summary>宿成员。</summary>
    public ObservableCollection<TopologyMemberViewModel> Targets { get; } = [];

    /// <summary>是否异常。</summary>
    public bool IsProblem => HasStalled || HasUnresolved;

    /// <summary>列表副标题：几源几宿。</summary>
    public string MemberSummary => TopologyText.Get("topology.memberSummary", SourceCount, TargetCount);

    /// <summary>更新字段（不重建卡；保留成员对象）。</summary>
    /// <param name="displayName">显示名。</param>
    /// <param name="kindLabel">类型。</param>
    /// <param name="stalled">是否停流。</param>
    /// <param name="unresolvedCount">悬空成员数。</param>
    /// <param name="sources">源成员。</param>
    /// <param name="targets">宿成员。</param>
    /// <param name="saturated">是否有宿口队列持续为满（只在"正常"之上覆盖，压不过停流/悬空）。</param>
    public void Update(
        string displayName,
        string kindLabel,
        bool stalled,
        int unresolvedCount,
        IReadOnlyList<TopologyMemberViewModel> sources,
        IReadOnlyList<TopologyMemberViewModel> targets,
        bool saturated = false)
    {
        DisplayName = displayName;
        KindLabel = kindLabel;
        HasStalled = stalled;
        UnresolvedMemberCount = unresolvedCount;
        HasUnresolved = unresolvedCount > 0;

        _ = CollectionSync.Sync(Sources, sources);
        _ = CollectionSync.Sync(Targets, targets);

        SourceCount = sources.Count;
        TargetCount = targets.Count;

        // 优先级：停流+悬空 > 停流 > 悬空 > **有宿口饱和** > 正常
        DiagnosticText = (stalled, unresolvedCount > 0) switch
        {
            (true, true) => TopologyText.Get("topology.diagnostic.stalledAndUnresolved", unresolvedCount),
            (true, false) => TopologyText.Get("topology.diagnostic.stalled"),
            (false, true) => TopologyText.Get("topology.diagnostic.unresolved", unresolvedCount),
            _ => saturated
                ? TopologyText.Get("topology.diagnostic.saturated")
                : TopologyText.Get("topology.diagnostic.normal"),
        };

        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(MemberSummary));
        OnPropertyChanged(nameof(HasGroupLabel));
    }

    /// <summary>复制诊断文本（含原始标识）。</summary>
    public string ToDetailText()
    {
        var lines = new List<string>
        {
            TopologyText.Get("topology.copy.header", DisplayName, KindLabel,
                HasStalled ? TopologyText.Get("topology.state.stalled")
                    : HasUnresolved ? TopologyText.Get("topology.state.unresolved")
                    : TopologyText.Get("topology.state.normal")),
            TopologyText.Get("topology.copy.diagnostic", DiagnosticText),
            TopologyText.Get("topology.copy.counts", SourceCount, TargetCount),
        };

        foreach (var source in Sources)
        {
            lines.Add(TopologyText.Get("topology.copy.source", source.DisplayName, source.Raw, source.MetricsText));
        }

        foreach (var target in Targets)
        {
            lines.Add(TopologyText.Get("topology.copy.target", target.DisplayName, target.Raw, target.DetailText));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// **拓扑诊断弹窗**：网络列表 + 单网络诊断详情；只读，不提供建立/断开网络的操作。
/// </summary>
public sealed partial class TopologyViewModel : ViewModelBase
{
    /// <summary>“全部”状态过滤键。</summary>
    public const string All = "all";

    /// <summary>“异常”状态过滤键。</summary>
    public const string Abnormal = "abnormal";

    /// <summary>“停流”状态过滤键。</summary>
    public const string Stalled = "stalled";

    /// <summary>“悬空”状态过滤键。</summary>
    public const string Unresolved = "unresolved";

    private readonly IRuntimeQuery _query;
    private readonly Dictionary<string, TopologyNetworkViewModel> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopologyMemberViewModel> _members = new(StringComparer.Ordinal);
    private readonly List<TopologyNetworkViewModel> _allNetworks = [];

    /// <summary>创建拓扑诊断视图模型。</summary>
    /// <param name="query">查询面。</param>
    public TopologyViewModel(IRuntimeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        StateFilters =
        [
            new TopologyStateFilterViewModel(All, TopologyText.Get("topology.filter.all")),
            new TopologyStateFilterViewModel(Abnormal, TopologyText.Get("topology.filter.abnormal")),
            new TopologyStateFilterViewModel(Stalled, TopologyText.Get("topology.filter.stalled")),
            new TopologyStateFilterViewModel(Unresolved, TopologyText.Get("topology.filter.unresolved")),
        ];
        StateFilters[0].IsSelected = true;
    }

    /// <summary>过滤后的网络列表。</summary>
    public ObservableCollection<TopologyNetworkViewModel> Networks { get; } = [];

    /// <summary>状态过滤按钮。</summary>
    public ObservableCollection<TopologyStateFilterViewModel> StateFilters { get; }

    /// <summary>模块清单视图模型（占位实例属诊断）。</summary>
    public ModuleCatalogViewModel? Catalog { get; set; }

    /// <summary>当前选中的网络。</summary>
    [ObservableProperty]
    public partial TopologyNetworkViewModel? SelectedNetwork { get; set; }

    /// <summary>是否有选中项。</summary>
    public bool HasSelection => SelectedNetwork is not null;

    /// <summary>搜索（网络名 / 成员显示名 / 原始成员字面量）。</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>状态过滤键。</summary>
    [ObservableProperty]
    public partial string StateFilter { get; set; } = All;

    /// <summary>网络数。</summary>
    [ObservableProperty]
    public partial int NetworkCount { get; set; }

    /// <summary>异常网络数（停流或悬空）。</summary>
    [ObservableProperty]
    public partial int AbnormalCount { get; set; }

    /// <summary>停流网络数。</summary>
    [ObservableProperty]
    public partial int StalledCount { get; set; }

    /// <summary>含悬空成员的网络数。</summary>
    [ObservableProperty]
    public partial int UnresolvedCount { get; set; }

    /// <summary>占位实例数。</summary>
    [ObservableProperty]
    public partial int PlaceholderCount { get; set; }

    /// <summary>顶部摘要：只保留网络/异常维度。</summary>
    public string Summary =>
        TopologyText.Get("topology.summary", NetworkCount, AbnormalCount)
        + (StalledCount > 0 || UnresolvedCount > 0
            ? TopologyText.Get("topology.summary.detail", StalledCount, UnresolvedCount)
            : string.Empty);

    /// <summary>是否有网络。</summary>
    public bool HasNetworks => NetworkCount > 0;

    /// <summary>过滤后是否有网络。</summary>
    public bool HasFilteredNetworks => Networks.Count > 0;

    /// <summary>是否有占位实例。</summary>
    public bool HasPlaceholders => PlaceholderCount > 0;

    /// <summary>占位区标题。</summary>
    public string PlaceholderHeader => TopologyText.Get("topology.placeholder.header", PlaceholderCount);

    /// <summary>
    /// 按刷新节拍同步网络与成员（不重建列表/详情对象）。
    /// </summary>
    public void Refresh()
    {
        var networks = _query.GetNetworks();
        var instances = _query.GetInstances()
            .GroupBy(instance => instance.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.Ordinal);

        // 本拍各 (实例, 口) 的队列水位：**每轮只取一次**（键 = `实例.口名`，与成员字面量同形）
        var queues = new Dictionary<string, PortQueueDepthView>(StringComparer.Ordinal);
        foreach (var depth in _query.GetQueueDepths())
        {
            queues[$"{depth.InstanceId}.{depth.PortName}"] = depth;
        }

        var nextCards = new List<TopologyNetworkViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var network in networks)
        {
            var key = string.IsNullOrWhiteSpace(network.Key)
                ? $"anonymous:{network.Source}:{string.Join('|', network.Targets)}"
                : network.Key;

            if (!seen.Add(key))
            {
                continue;
            }

            if (!_cards.TryGetValue(key, out var card))
            {
                card = new TopologyNetworkViewModel(key);
                _cards[key] = card;
            }

            var unresolvedSources = new HashSet<string>(network.UnresolvedSources ?? [], StringComparer.Ordinal);
            var sources = new List<TopologyMemberViewModel>();

            foreach (var source in network.Sources.Count > 0 ? network.Sources : [network.Source])
            {
                var parts = TopologyDisplay.ParseMember(source, instances);
                var unresolved = unresolvedSources.Contains(source);
                var member = GetMember($"{key}|S|{source}", source, TopologyText.Get("topology.source"), parts.DisplayName);

                var metrics = unresolved ? new PortMetricsView(0, 0, 0) : PortMetricsOf(source);
                member.DisplayName = parts.DisplayName;
                member.StateText = unresolved
                    ? TopologyText.Get("topology.state.unresolved")
                    : network.IsStalled ? TopologyText.Get("topology.state.stalled") : TopologyText.Get("topology.state.normal");
                member.IsUnresolved = unresolved;
                member.IsStalled = network.IsStalled;
                member.HasMetrics = !unresolved;
                member.Delivered = metrics.Delivered;
                member.Dropped = metrics.Dropped;
                member.RateValue = metrics.BytesPerSecond;
                member.RateText = TopologyDisplay.FormatRate(metrics.BytesPerSecond);
                member.LastDataText = TopologyDisplay.FormatTime(metrics.LastDataTime);
                member.DropBreakdownText = DropBreakdownOf(metrics);

                sources.Add(member);
            }

            var targets = new List<TopologyMemberViewModel>();
            var unresolvedTargets = new HashSet<string>(network.UnresolvedMembers ?? [], StringComparer.Ordinal);

            foreach (var target in network.Targets.Concat(unresolvedTargets))
            {
                var parts = TopologyDisplay.ParseMember(target, instances);
                var unresolved = unresolvedTargets.Contains(target);
                var member = GetMember($"{key}|T|{target}", target, TopologyText.Get("topology.target"), parts.DisplayName);

                member.DisplayName = parts.DisplayName;
                member.StateText = unresolved
                    ? TopologyText.Get("topology.state.unresolved")
                    : network.IsStalled ? TopologyText.Get("topology.state.stalled") : TopologyText.Get("topology.state.normal");
                member.IsUnresolved = unresolved;
                member.IsStalled = network.IsStalled;
                member.HasMetrics = false;
                member.Delivered = 0;
                member.Dropped = 0;
                member.RateValue = 0;
                member.RateText = "—";
                member.LastDataText = "—";
                member.DropBreakdownText = string.Empty;

                // 队列轴（只有接收口有队列）：深度 / 饱和 / 过期 / 清尾 / 模块内部
                member.QueueText = string.Empty;
                member.QueueDetailText = string.Empty;
                member.IsSaturated = false;

                if (queues.TryGetValue(target, out var queue))
                {
                    var queueActive = queue.Depth > 0
                        || queue.Saturated
                        || queue.DroppedEvictedUnits + queue.DroppedRejectedUnits > 0
                        || queue.ExpiredUnits > 0;

                    if (queueActive)
                    {
                        member.QueueText = QueueTextOf(queue);
                        member.QueueDetailText = QueueDetailOf(queue);
                        member.IsSaturated = queue.Saturated;
                    }
                }

                targets.Add(member);
            }

            card.Update(
                network.IsAnonymous ? TopologyText.Get("topology.network.direct") : network.Name,
                network.IsAnonymous ? TopologyText.Get("topology.kind.direct") : TopologyText.Get("topology.kind.named"),
                network.IsStalled,
                unresolvedSources.Count + unresolvedTargets.Count,
                sources,
                targets,
                targets.Exists(member => member.IsSaturated));

            nextCards.Add(card);
        }

        foreach (var key in _cards.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _ = _cards.Remove(key);
        }

        _allNetworks.Clear();
        _allNetworks.AddRange(nextCards
            .OrderBy(card => card.HasStalled ? 0 : card.HasUnresolved ? 1 : 2)
            .ThenBy(card => card.DisplayName, StringComparer.CurrentCulture)
            .ThenBy(card => card.Key, StringComparer.Ordinal));

        NetworkCount = _allNetworks.Count;
        AbnormalCount = _allNetworks.Count(card => card.IsProblem);
        StalledCount = _allNetworks.Count(card => card.HasStalled);
        UnresolvedCount = _allNetworks.Count(card => card.HasUnresolved);
        PlaceholderCount = Catalog?.Placeholders.Count ?? 0;

        ApplyFilter();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasNetworks));
        OnPropertyChanged(nameof(HasPlaceholders));
        OnPropertyChanged(nameof(PlaceholderHeader));
    }

    /// <summary>按搜索 + 状态过滤；保持当前选中网络。</summary>
    private void ApplyFilter()
    {
        var baseRows = _allNetworks.Where(MatchesSearch).ToList();

        foreach (var filter in StateFilters)
        {
            filter.Count = filter.Key switch
            {
                All => baseRows.Count,
                Abnormal => baseRows.Count(card => card.IsProblem),
                Stalled => baseRows.Count(card => card.HasStalled),
                Unresolved => baseRows.Count(card => card.HasUnresolved),
                _ => 0,
            };
            filter.IsSelected = string.Equals(filter.Key, StateFilter, StringComparison.Ordinal);
        }

        var filtered = baseRows.Where(card => StateFilter switch
        {
            All => true,
            Abnormal => card.IsProblem,
            Stalled => card.HasStalled,
            Unresolved => card.HasUnresolved,
            _ => true,
        }).ToList();

        _ = CollectionSync.Sync(Networks, filtered);
        ApplyGroupLabels(filtered);

        if (SelectedNetwork is { } selected && filtered.Contains(selected))
        {
            OnPropertyChanged(nameof(HasSelection));
        }
        else
        {
            SelectedNetwork = filtered.FirstOrDefault(card => card.IsProblem) ?? filtered.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasFilteredNetworks));
        OnPropertyChanged(nameof(Summary));
    }

    private static void ApplyGroupLabels(IReadOnlyList<TopologyNetworkViewModel> filtered)
    {
        foreach (var card in filtered)
        {
            card.GroupLabel = string.Empty;
        }

        var firstAbnormal = filtered.FirstOrDefault(card => card.IsProblem);
        if (firstAbnormal is not null)
        {
            firstAbnormal.GroupLabel = TopologyText.Get("topology.group.abnormal");
        }

        var firstNormal = filtered.FirstOrDefault(card => !card.IsProblem);
        if (firstNormal is not null)
        {
            firstNormal.GroupLabel = TopologyText.Get("topology.group.normal");
        }
    }

    private bool MatchesSearch(TopologyNetworkViewModel card)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();

        return card.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || card.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
            || card.Sources.Concat(card.Targets).Any(member =>
                member.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || member.Raw.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>切换状态过滤（按钮命令）。</summary>
    /// <param name="filter">被点击的状态项。</param>
    [RelayCommand]
    private void SelectState(TopologyStateFilterViewModel? filter)
    {
        if (filter is not null)
        {
            StateFilter = filter.Key;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnStateFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedNetworkChanged(TopologyNetworkViewModel? value) =>
        OnPropertyChanged(nameof(HasSelection));

    private TopologyMemberViewModel GetMember(string key, string raw, string role, string displayName)
    {
        if (!_members.TryGetValue(key, out var member))
        {
            member = new TopologyMemberViewModel(raw, role, displayName);
            _members[key] = member;
        }

        return member;
    }

    /// <summary>取某个成员字面量（`实例.口名`）的端口指标。</summary>
    private PortMetricsView PortMetricsOf(string literal)
    {
        var separator = literal.LastIndexOf('.');
        if (separator <= 0)
        {
            return new PortMetricsView(0, 0, 0);
        }

        return _query.GetPortMetrics(literal[..separator], literal[(separator + 1)..]);
    }

    /// <summary>
    /// 源成员的**按原因丢弃串**：固定顺序、零值不出现、全零 ⇒ 空串。
    /// </summary>
    /// <param name="metrics">该口的统一指标（框架算好的分项）。</param>
    private static string DropBreakdownOf(PortMetricsView metrics)
    {
        var parts = new List<string>(6);

        Add(parts, "topology.drop.evicted", metrics.DropQueueFullEvicted);
        Add(parts, "topology.drop.rejected", metrics.DropQueueFullRejected);
        Add(parts, "topology.drop.noCarrier", metrics.DropNoCarrier);
        Add(parts, "topology.drop.carrierStopped", metrics.DropCarrierStopped);
        Add(parts, "topology.drop.invalidPayload", metrics.DropInvalidPayload);
        Add(parts, "topology.drop.stalled", metrics.DropStalled);

        return string.Join(" · ", parts);

        static void Add(List<string> target, string key, long value)
        {
            if (value > 0)
            {
                target.Add(TopologyText.Get(key, value));
            }
        }
    }

    /// <summary>宿成员的队列水位串（饱和后缀**在这里生成一次**，界面上不再拼）。</summary>
    /// <param name="queue">该口的队列视图。</param>
    private static string QueueTextOf(PortQueueDepthView queue) =>
        TopologyText.Get("topology.queue.state", queue.Depth, queue.Capacity)
        + (queue.Saturated ? TopologyText.Get("topology.queue.saturatedSuffix") : string.Empty);

    /// <summary>宿成员的队列轴分项（过期 / 停止清尾 / 模块内部；非零才出现）。</summary>
    /// <param name="queue">该口的队列视图。</param>
    private static string QueueDetailOf(PortQueueDepthView queue)
    {
        var parts = new List<string>(3);

        Add(parts, "topology.drop.expired", queue.ExpiredUnits);
        Add(parts, "topology.drop.discardedOnStop", queue.DiscardedOnStopUnits);
        Add(parts, "topology.drop.moduleInternal", queue.ModuleInternalDroppedUnits);

        return string.Join(" · ", parts);

        static void Add(List<string> target, string key, long value)
        {
            if (value > 0)
            {
                target.Add(TopologyText.Get(key, value));
            }
        }
    }
}

/// <summary>拓扑的显示格式化（友好成员名 / 时间 / 速率）。</summary>
public static class TopologyDisplay
{
    /// <summary>解析成员字面量并生成友好显示名。</summary>
    /// <param name="literal">`实例.口名`。</param>
    /// <param name="instances">实例 ID → 显示名。</param>
    public static MemberParts ParseMember(string literal, IReadOnlyDictionary<string, string> instances)
    {
        var separator = literal.LastIndexOf('.');
        var instanceId = separator > 0 ? literal[..separator] : literal;
        var portName = separator > 0 ? literal[(separator + 1)..] : string.Empty;
        var display = instances.TryGetValue(instanceId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : instanceId;

        return new MemberParts(
            instanceId,
            portName,
            string.IsNullOrEmpty(portName) ? display : $"{display} › {portName}");
    }

    /// <summary>速率文本（有效载荷字节/秒）。</summary>
    public static string FormatRate(double bytesPerSecond) => bytesPerSecond <= 0
        ? "—"
        : ByteQuantity.Format(bytesPerSecond, perSecond: true);

    /// <summary>时间文本（无数据 = 破折号）。</summary>
    public static string FormatTime(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
}

/// <summary>成员解析结果。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">口名。</param>
/// <param name="DisplayName">友好显示名。</param>
public sealed record MemberParts(string InstanceId, string PortName, string DisplayName);
