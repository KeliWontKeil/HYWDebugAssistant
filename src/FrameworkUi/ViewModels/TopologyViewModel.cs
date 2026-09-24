using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// 拓扑诊断的一行（**只读调试信息**，R-4）：网络 → 成员（<c>实例ID.口名</c>）· 方向 · 状态 ·
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
/// **拓扑诊断弹窗**（R-4 / `11` §3）：只读列出网络与成员，替代"跨窗口长连线"的 v1 表达。
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

    /// <summary>摘要（网络数 / 成员数 / 停流数）。</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "网络 0";

    /// <summary>
    /// 按刷新节拍同步表格。
    /// </summary>
    /// <remarks>用增量同步（见 <see cref="CollectionSync"/>）：每帧重建列表会让悬停等瞬时视觉状态反复重置（闪烁）。</remarks>
    public void Refresh()
    {
        var diagnostics = _query.GetDiagnostics().ToDictionary(d => d.NetworkName, StringComparer.Ordinal);
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

            _ = diagnostics.TryGetValue(network.Name, out var diagnostic);

            rows.Add(new TopologyRowViewModel(
                name,
                kind,
                network.Source,
                "源",
                state,
                diagnostic?.Delivered ?? 0,
                diagnostic?.Dropped ?? 0,
                diagnostic?.LastDataTime?.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.CurrentCulture) ?? "—"));

            members++;

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
}
