using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// 拓扑诊断的一行（**只读调试信息**）：网络 → 成员（<c>实例ID.口名</c>）· 方向 · 状态 ·
/// 投递/丢弃计数 · 最后数据时间。
/// </summary>
/// <remarks>
/// 这里是**唯一**允许出现代码化标识（成员字面量）的界面：它的用途就是诊断"端口唯一 ID 与连接关系"。
/// </remarks>
/// <param name="NetworkName">网络显示名（匿名 = （直连））。</param>
/// <param name="Kind">类型（匿名 / 命名 / 自动）。</param>
/// <param name="Member">成员字面量（<c>实例ID.口名</c>）。</param>
/// <param name="Direction">方向（源 / 宿）。</param>
/// <param name="State">状态（正常 / 停流 / 悬空）。</param>
/// <param name="Delivered">该网络累计投递数（仅源行显示）。</param>
/// <param name="Dropped">该网络累计丢弃数（仅源行显示）。</param>
/// <param name="LastDataTime">最后数据时间（仅源行显示）。</param>
public sealed record TopologyRowViewModel(
    string NetworkName,
    string Kind,
    string Member,
    string Direction,
    string State,
    long Delivered,
    long Dropped,
    string LastDataTime);

/// <summary>
/// **拓扑诊断弹窗**：只读列出网络与成员，替代"跨窗口长连线"的 v1 表达。
/// </summary>
/// <remarks>本视图只读：不提供建立/断开网络的操作（建网唯一机制 = 口上的网络标签）。</remarks>
public sealed partial class TopologyViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;

    /// <summary>创建拓扑诊断视图模型。</summary>
    /// <param name="query">查询面。</param>
    public TopologyViewModel(IRuntimeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    /// <summary>拓扑行（按网络分组，源在前、宿在后）。</summary>
    public ObservableCollection<TopologyRowViewModel> Rows { get; } = [];

    /// <summary>
    /// **模块清单视图模型**（占位实例属**诊断**，其列表与"指定替代"命令由它提供）。
    /// </summary>
    /// <remarks>占位实例归本弹窗所有（不属「模块清单」页），故这里引用它而不复制数据。</remarks>
    public ModuleCatalogViewModel? Catalog { get; set; }

    /// <summary>摘要（网络数 / 成员数 / 停流数）。</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "网络 0";

    /// <summary>
    /// 按刷新节拍同步表格。
    /// </summary>
    /// <remarks>用增量同步（见 <see cref="CollectionSync"/>）：每帧重建列表会让悬停等瞬时视觉状态反复重置（闪烁）。</remarks>
    public void Refresh()
    {
        var networks = _query.GetNetworks();

        var rows = new List<TopologyRowViewModel>();
        var members = 0;

        foreach (var network in networks)
        {
            var name = network.IsAnonymous ? "（直连）" : network.Name;
            // 类型：匿名（对齐派生 / 显式直连）或 命名（标签派生 / 显式命名）——框架派生与否不再单列一档，
            // 避免"标签派生"被误标成"自动"（用户看到的是自己起的名字）
            var kind = network.IsAnonymous ? "匿名" : "命名";
            var state = network.IsStalled
                ? "停流"
                : (network.UnresolvedMembers?.Count ?? 0) > 0 ? "悬空" : "正常";

            // 统一取数口径：源行的指标 = **该网络各源端口指标之和**
            // （端口指标由框架的**唯一**查询 `GetPortMetrics` 给出：该口参与的全部"流"之和）。
            //
            // **旧写法错在哪**（本行就是缺陷现场）：它拿 `network.Key`（= `源集合 → 宿集合`）去查
            // `GetDiagnostics()` 的 `Key` 字典 —— 而后者是**逐源键**（= `单源 → 宿子集`）。
            // 多源网络下两个键**永不相等** ⇒ **源行恒显示 0 / 0 / —**（1:1 单源单宿时两个键恰好字面相同，
            // 所以这个缺陷一直没被看见）。现在改为向框架要"该口参与了哪些流"，不再猜键。
            var sourceMetrics = SourceMetricsOf(network);

            rows.Add(new TopologyRowViewModel(
                name,
                kind,
                network.Source,
                "源",
                state,
                sourceMetrics.Delivered,
                sourceMetrics.Dropped,
                FormatTime(sourceMetrics.LastDataTime)));

            members++;

            // 宿行按既有设计**不显示计数**（见 `TopologyRowViewModel` 的文档："仅源行显示"）
            foreach (var target in network.Targets)
            {
                rows.Add(new TopologyRowViewModel(name, kind, target, "宿", state, 0, 0, "—"));
                members++;
            }

            foreach (var unresolved in network.UnresolvedMembers ?? [])
            {
                rows.Add(new TopologyRowViewModel(name, kind, unresolved, "宿", "悬空", 0, 0, "—"));
                members++;
            }
        }

        _ = CollectionSync.Sync(Rows, rows);

        var stalled = networks.Count(n => n.IsStalled);
        Summary = $"网络 {networks.Count} · 成员 {members}" + (stalled > 0 ? $" · 停流 {stalled}" : string.Empty);
    }

    /// <summary>
    /// 该网络**源行的指标** = 它的**各源端口**指标之和（源行是**网络级**行；单源网络下 = 该源口自己的数）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>端口字面量一律按**最后一个 `.`** 拆成（实例, 口）——与本仓库其余字面量解析同一约定；</item>
    ///   <item><see cref="PortMetricsView.Dropped"/> 只含**传输丢弃**（停流未投递是合法状态，
    ///     不得混算），故本表的「丢弃」列不把停流未投递算进去；</item>
    ///   <item>悬空/停流的源口由框架给出全 0（它不在任何流上）——这正是本表该显示的降级事实。</item>
    /// </list>
    /// </remarks>
    /// <param name="network">网络视图。</param>
    private (long Delivered, long Dropped, DateTimeOffset? LastDataTime) SourceMetricsOf(NetworkView network)
    {
        long delivered = 0;
        long dropped = 0;
        DateTimeOffset? last = null;

        foreach (var literal in network.Sources)
        {
            var separator = literal.LastIndexOf('.');
            if (separator <= 0)
            {
                continue;   // 非法字面量：不猜，跳过（宁可少显示，不让界面崩）
            }

            var metrics = _query.GetPortMetrics(literal[..separator], literal[(separator + 1)..]);

            delivered += metrics.Delivered;
            dropped += metrics.Dropped;

            if (metrics.LastDataTime is { } time && (last is null || time > last))
            {
                last = time;
            }
        }

        return (delivered, dropped, last);
    }

    /// <summary>最后数据时间的呈现文本（无数据 = 破折号）。</summary>
    /// <param name="time">时刻。</param>
    private static string FormatTime(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.CurrentCulture) ?? "—";
}
