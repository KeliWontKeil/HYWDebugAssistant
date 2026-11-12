using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Runtime;

/// <summary>
/// **建网推导**——框架派生的网络由本文件一次性重算。
/// </summary>
/// <remarks>
/// <para>
/// <b>一条链路（唯一）</b>：<b>候选</b>（用户意图来源）→ <b>判定</b>（方向 + 契约，唯一入口
/// <c>CheckCompatibility</c>）→ <b>落地</b>（按来源分组成 <see cref="Network"/>，成员只收**被接受**的对）。
/// 候选有两种来源：<b>命名网络</b>（端口标签同名）与<b>对齐网络</b>（未打标签、两模块接触且标记对齐）。
/// 它们只是"用户怎么表达要连"的两种方式，**判定与落地是同一份实现**——历史教训：两条路径各判各的，
/// 结果"贴合能连、打标签不能连"，且贴合出来的"已连接"端口可能一条绑定都没有。
/// </para>
/// <para><b>差量落地</b>：每次重算把现场应有的网络与既有网络按"身份 + 在场成员"比对，
/// **相同的原地保留**（不拆不建、计数不清零）；只移除真的消失的、只新增真的出现的。
/// **例外**：带未锚定成员的网络（加载降级态 / 占位实例）**不在这里删**——那是"声明还在、口还没回来"
/// 的事实，删掉就等于把用户意图一起丢了；等占位解析或口补齐后由重锚接回。</para>
/// <para><b>触发时机</b>：布局提交（拖动/缩放结束）· 模块增删启停 · 端口标签变更 · 组合件加载。</para>
/// <para><b>与吸附的关系</b>：吸附是 UI 行为（让用户容易对上），本推导只看几何事实——吸附对齐后自然满足条件；
/// 预览走 <see cref="CollectAcceptedDockPairs"/>（与真正建网同一判定），不会"预告能连、松手不连"。</para>
/// </remarks>
public sealed partial class ModuleRuntime
{
    /// <summary>连接候选的**来源**（两种用户意图来源，不是两套规则）。</summary>
    private enum ConnectionOrigin
    {
        /// <summary>端口标签同名（命名网络）。</summary>
        Label,

        /// <summary>两模块接触且标记对齐（匿名 / 直连）。</summary>
        Contact,
    }

    /// <summary>一条连接候选：谁是源、谁是宿、属于哪种来源。</summary>
    private readonly record struct ConnectionCandidate(
        ConnectionOrigin Origin,
        string Label,
        NetworkMember Source,
        NetworkMember Target);

    /// <summary>一条被拒绝的候选（带可读原因，用于上报）。</summary>
    private readonly record struct ConnectionRejection(ConnectionCandidate Candidate, string Reason);

    /// <summary>同网络名但契约不兼容的一对端口（内部只读事实）。</summary>
    internal readonly record struct PortContractConflict(
        string InstanceId,
        string PortName,
        string PeerInstanceId,
        string PeerPortName,
        string? NetworkName,
        string Reason);

    /// <summary>
    /// 已上报过的拒绝键（**同一事实只报一次**）：拒绝是"持续状态"，而 Issue 是追加式列表——
    /// 每次拖动都重算一次，不按集合去重就会把同一条灌满面板。
    /// </summary>
    private readonly HashSet<string> _reportedRejections = new(StringComparer.Ordinal);

    /// <summary>重算全部**派生网络**（命名 + 对齐）。</summary>
    /// <param name="notices">面向用户的提示（连通/待命名等）。</param>
    /// <param name="alignAutoNetwork">是否启用"对齐自动建网"（设置项；关 = 只做命名网络）。</param>
    /// <remarks>
    /// <b>缺陷修复 · 偶发静默丢单元</b>：旧写法是"**先把全部派生网络移除，再按现场重建**"——
    /// 而"移除网络"会**同时移除它的绑定**，"重建"才把绑定加回来 ⇒ 两步之间有一个**该网络没有绑定的空窗**，
    /// 模块线程恰在此刻发送 ⇒ 单元被**静默丢弃**（无丢弃计数、无 Issue）。
    /// 本推导**每次变更提交都跑**（拖动/输入/配置/增删启停）⇒ 窗口被反复打开。
    /// 现在改为**差量**：算出现场应有的派生网络后，**身份与在场成员都相同的原地保留**（不拆不建），
    /// 只移除真的消失的、只新增真的出现的 ⇒ ① 不留空窗；② 绑定对象（含**投递/丢弃计数**与**速率计**）
    /// 不再被每次提交清零（同一份现场事实不该因为"提交了一次"就换一批计数器）。
    /// </remarks>
    private void ReconcileNetworks(List<string> notices, bool alignAutoNetwork)
    {
        var desired = BuildDesiredNetworks(alignAutoNetwork, notices);

        var existingPairs = Content.Networks
            .Select(DescribeKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var existing in Content.Networks.ToList())
        {
            var same = desired.Any(d => IsSameNetwork(existing, d));
            if (same)
            {
                continue;
            }

            // **声明还在、口还没回来**（加载降级态 / 占位）：保留，等重锚接回。
            if (existing.UnresolvedMembers.Count > 0 || existing.UnresolvedSources.Count > 0)
            {
                continue;
            }

            // **来自组合件的声明网络**（显式连接）：端口没有标签 ⇒ 现场没有别的重建来源，
            // 删掉就是丢用户数据。一旦这些端口有了标签（标签成为事实源），它就会被下面这条规则删掉。
            if (existing.IsFromFile && !HasAnyLabeledMember(existing))
            {
                continue;
            }

            RemoveNetworkCore(existing);   // **按对象引用**移除（匿名网络没有唯一名）
        }

        var keptKeys = Content.Networks
            .Select(DescribeKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var network in desired)
        {
            if (!keptKeys.Contains(DescribeKey(network)))
            {
                AddNetworkCore(network);
            }
        }

        // 变化提示：新出现的连接说清"连了什么"（用户必须看得见）
        foreach (var network in Content.Networks)
        {
            if (existingPairs.Contains(DescribeKey(network)))
            {
                continue;
            }

            if (DescribeNetwork(network) is { Length: > 0 } description)
            {
                notices.Add(CoreText.Get("core.runtime.connected", description));
            }
        }
    }

    /// <summary>该网络的**在场成员**里是否至少有一个口带标签（= 标签已成为这张网的事实源）。</summary>
    private bool HasAnyLabeledMember(Network network) =>
        network.Sources.Concat(network.Targets).Any(member =>
            !string.IsNullOrWhiteSpace(Content.Layout.GetPort(member.InstanceId, member.PortName).Label));

    /// <summary>
    /// **候选 → 判定 → 落地**（唯一链路）。
    /// </summary>
    /// <param name="alignAutoNetwork">是否把"接触且对齐"也算作候选来源。</param>
    /// <param name="notices">提示收集（尚无对端等）。</param>
    /// <returns>现场应有的派生网络（只含**被接受**的对）。</returns>
    private List<Network> BuildDesiredNetworks(bool alignAutoNetwork, List<string> notices)
    {
        var candidates = new List<ConnectionCandidate>();
        CollectLabelCandidates(candidates, notices);

        if (alignAutoNetwork)
        {
            CollectContactCandidates(candidates);
        }

        var accepted = new List<ConnectionCandidate>(candidates.Count);
        var rejections = new List<ConnectionRejection>();

        foreach (var candidate in candidates)
        {
            var result = Evaluate(candidate);
            if (result.IsCompatible)
            {
                accepted.Add(candidate);
            }
            else
            {
                rejections.Add(new ConnectionRejection(candidate, result.Reason ?? string.Empty));
            }
        }

        ReportRejections(rejections);
        return Materialize(accepted);
    }

    /// <summary>按**端口标签**收集候选（同名标签的发送口 × 接收口，逐对成候选）。</summary>
    private void CollectLabelCandidates(List<ConnectionCandidate> candidates, List<string> notices)
    {
        var labeled = new List<(NetworkMember Member, string Label)>();

        foreach (var instance in Content.Instances)
        {
            foreach (var port in instance.Ports)
            {
                var label = Content.Layout.GetPort(instance.InstanceId, port.Name).Label;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labeled.Add((new NetworkMember(instance.InstanceId, port.Name, port.Direction), label.Trim()));
                }
            }
        }

        foreach (var group in labeled.GroupBy(x => x.Label, StringComparer.Ordinal))
        {
            var label = group.Key;
            var senders = group.Where(x => x.Member.Direction == PortDirection.Send).Select(x => x.Member).ToList();
            var receivers = group.Where(x => x.Member.Direction == PortDirection.Receive).Select(x => x.Member).ToList();

            if (senders.Count == 0)
            {
                notices.Add(CoreText.Get("core.runtime.networkNoSender", label));
                continue;
            }

            if (receivers.Count == 0)
            {
                notices.Add(CoreText.Get("core.runtime.networkNoReceiver", label));
                continue;
            }

            // **逐 (源,宿) 对**成候选：兼容筛选只有一次（`Evaluate`），不在这里预先"看谁跟谁像"。
            foreach (var sender in senders)
            {
                foreach (var receiver in receivers)
                {
                    candidates.Add(new ConnectionCandidate(ConnectionOrigin.Label, label, sender, receiver));
                }
            }
        }
    }

    /// <summary>按**接触且对齐**收集候选（仅未打标签的口）。</summary>
    private void CollectContactCandidates(List<ConnectionCandidate> candidates)
    {
        foreach (var pair in CollectDockPairs(null))
        {
            candidates.Add(new ConnectionCandidate(
                ConnectionOrigin.Contact,
                string.Empty,
                new NetworkMember(pair.Source.InstanceId, pair.Source.PortName, PortDirection.Send),
                new NetworkMember(pair.Target.InstanceId, pair.Target.PortName, PortDirection.Receive)));
        }
    }

    /// <summary>
    /// **唯一的接受判定**：方向 + 契约（类型身份 + 版本 + 形状）。
    /// </summary>
    /// <param name="candidate">候选（两端字面量）。</param>
    /// <returns>判定结果（不兼容时带可读原因）。</returns>
    private CompatibilityResult Evaluate(ConnectionCandidate candidate)
    {
        var source = FindPort(candidate.Source.InstanceId, candidate.Source.PortName);
        var target = FindPort(candidate.Target.InstanceId, candidate.Target.PortName);

        if (source is null || target is null)
        {
            return CompatibilityResult.Incompatible(CoreText.Get("core.runtime.reason.portMissingOrDirection"));
        }

        if (source.Declaration.Direction != PortDirection.Send
            || target.Declaration.Direction != PortDirection.Receive)
        {
            return CompatibilityResult.Incompatible(CoreText.Get("core.runtime.reason.portMissingOrDirection"));
        }

        return CheckCompatibility(
            candidate.Source.InstanceId, source,
            candidate.Target.InstanceId, target);
    }

    /// <summary>把**被接受的候选**落成网络：标签来源按标签名聚合，接触来源按源聚合（匿名）。</summary>
    private static List<Network> Materialize(List<ConnectionCandidate> accepted)
    {
        var networks = new List<Network>();

        foreach (var group in accepted.GroupBy(GroupKeyOf, StringComparer.Ordinal))
        {
            var sources = group.Select(candidate => candidate.Source).Distinct().ToList();
            var targets = group.Select(candidate => candidate.Target).Distinct().ToList();

            if (sources.Count == 0 || targets.Count == 0)
            {
                continue;   // 没有一条被接受的对 ⇒ 不建网络（标签保留在布局里，等对端出现再连）
            }

            networks.Add(new Network(
                group.First().Label,
                sources,
                targets));
        }

        return networks;
    }

    /// <summary>
    /// 分组的键：命名网络 = 标签名；对齐网络 = **源口**（一个源对它接触到的宿集合 = 一条匿名网络）。
    /// </summary>
    private static string GroupKeyOf(ConnectionCandidate candidate) =>
        candidate.Origin == ConnectionOrigin.Contact
            ? "contact|" + candidate.Source
            : "label|" + candidate.Label;

    /// <summary>
    /// **同网络名但契约不兼容的端口对**（只读；供界面标红提示）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>只处理命名网络候选</b>：未打标签的几何候选不属于“同网络名”这个语义；</item>
    ///   <item><b>判定复用唯一入口</b> <see cref="CheckCompatibility"/>：与实际建网/Issue 同一套规则；</item>
    ///   <item><b>只报契约原因</b>：口缺失/方向不符不在这里报（那不是“同名不兼容”）；</item>
    ///   <item><b>无副作用</b>：不写布局、不改网络；按 (源, 宿) 去重。</item>
    /// </list>
    /// </remarks>
    internal IReadOnlyList<PortContractConflict> DescribeLabelContractConflicts()
    {
        var candidates = new List<ConnectionCandidate>();
        CollectLabelCandidates(candidates, notices: []);
        var conflicts = new List<PortContractConflict>(candidates.Count);
        var seen = new HashSet<(string Source, string Target)>();

        foreach (var candidate in candidates)
        {
            var source = FindPort(candidate.Source.InstanceId, candidate.Source.PortName);
            var target = FindPort(candidate.Target.InstanceId, candidate.Target.PortName);

            if (source is null || target is null)
            {
                continue;   // 方向/口缺失不是契约冲突
            }

            var result = CheckCompatibility(
                candidate.Source.InstanceId, source,
                candidate.Target.InstanceId, target);

            if (result.IsCompatible)
            {
                continue;
            }

            var key = (candidate.Source.ToString(), candidate.Target.ToString());
            if (!seen.Add(key))
            {
                continue;
            }

            conflicts.Add(new PortContractConflict(
                candidate.Source.InstanceId,
                candidate.Source.PortName,
                candidate.Target.InstanceId,
                candidate.Target.PortName,
                candidate.Label,
                result.Reason ?? CoreText.Get("core.runtime.reason.contractIncompatible")));
        }

        return conflicts;
    }

    /// <summary>
    /// 上报**被拒绝**的候选：只报"结构条件都满足、只差契约"的对（几何不成立的根本不是候选，不报）。
    /// </summary>
    /// <remarks>
    /// 去重口径 = 来源 + 两端 + 原因；同一事实（拖动期间反复重算）只进 Issue 一次，
    /// 消失后再出现才会重新上报（拒绝是状态，Issue 是追加式事件）。
    /// </remarks>
    private void ReportRejections(List<ConnectionRejection> rejections)
    {
        var current = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rejection in rejections)
        {
            var candidate = rejection.Candidate;
            var key = $"{candidate.Origin}|{candidate.Label}|{candidate.Source}|{candidate.Target}|{rejection.Reason}";
            _ = current.Add(key);

            if (_reportedRejections.Contains(key))
            {
                continue;
            }

            var reason = rejection.Reason.Length > 0
                ? rejection.Reason
                : CoreText.Get("core.runtime.reason.contractIncompatible");

            if (candidate.Origin == ConnectionOrigin.Label)
            {
                Issues.Report($"network:{candidate.Label}", new IssueReport(
                    IssueCategory.InterfaceError,
                    IssueSeverity.Warning,
                    CoreText.Get("core.runtime.networkExcluded", candidate.Label, candidate.Source, candidate.Target, reason)));
            }
            else
            {
                Issues.Report($"network:{candidate.Source}→{candidate.Target}", new IssueReport(
                    IssueCategory.InterfaceError,
                    IssueSeverity.Warning,
                    CoreText.Get("core.runtime.contactRejected", candidate.Source, candidate.Target, reason)));
            }
        }

        _reportedRejections.Clear();
        _reportedRejections.UnionWith(current);
    }

    /// <summary>
    /// 两条网络是否**同一条连接**（⇒ 现场保有者原地复用，不拆不建）。
    /// </summary>
    /// <remarks>
    /// 判据必须**同时**看两件事，缺一会留隐藏状态：
    /// <list type="number">
    ///   <item><b>声明身份</b>（<see cref="DescribeKey"/> = 声明源集合 → 宿集合）；</item>
    ///   <item><b>在场（已锚定）成员集合</b> —— 身份键用**声明源**（两条都停流的网络不得撞同一个键），
    ///     所以只比键会把"声明相同但**在场成员变了**"（如某口重新锚定/消失）误判为没变
    ///     ⇒ 保留旧对象就等于保留**过期的锚定结果**（旧对象仍 <c>IsStalled</c> 或仍指向旧成员）。</item>
    /// </list>
    /// </remarks>
    private static bool IsSameNetwork(Network existing, Network desired) =>
        string.Equals(DescribeKey(existing), DescribeKey(desired), StringComparison.Ordinal)
        && existing.Sources.Count == desired.Sources.Count
        && existing.Sources.All(desired.Sources.Contains)
        && existing.Targets.Count == desired.Targets.Count
        && existing.Targets.All(desired.Targets.Contains);

    /// <summary>
    /// 收集当前现场的**建网候选**（发送端 → 接收端；几何 + 标签资格，**不含契约**）。
    /// </summary>
    /// <param name="provisionalLayouts">
    /// 临时布局（拖动中的预览用）：给出的实例用这里的矩形，其余用工作现场里的布局。
    /// </param>
    internal List<DockPair> CollectDockPairs(IReadOnlyList<Api.ModuleLayoutView>? provisionalLayouts)
    {
        var ends = new List<DockEnd>();
        var overridden = provisionalLayouts?.ToDictionary(l => l.InstanceId, StringComparer.Ordinal) ?? [];

        foreach (var instance in Content.Instances)
        {
            Api.ModuleLayoutView? layout;
            if (overridden.TryGetValue(instance.InstanceId, out var provisional))
            {
                layout = provisional;
            }
            else
            {
                layout = Content.Layout.TryGetModule(instance.InstanceId, out var storedLayout) ? storedLayout : null;
            }

            if (layout is null || !layout.IsViewVisible)
            {
                continue;
            }

            foreach (var port in instance.Ports)
            {
                var isSend = port.Direction == PortDirection.Send;
                // **端口几何**：用户覆盖 > 模块代码里声明的默认位置（没有惯例回退）
                // （与呈现同源 ⇒ 一次解析出有效边/偏移/标签）。
                var recorded = Content.Layout.TryGetPort(instance.InstanceId, port.Name, out var stored);
                var geometry = PortGeometryResolution.Resolve(
                    port.Declaration, stored, recorded);

                ends.Add(new DockEnd(
                    instance.InstanceId,
                    layout,
                    port.Name,
                    isSend,
                    geometry,
                    // 有效边：上面那一次解析已经算完（`PortEdgeResolution` 仍是唯一实现）。
                    geometry.Edge));
            }
        }

        return [.. DockRule.ComputeDockPairs(ends)];
    }

    /// <summary>
    /// **预览用的候选**：几何候选再经**唯一接受判定**过滤（与落地同一条规则）。
    /// </summary>
    /// <remarks>
    /// 拖动中的"即将自动建网"提示与标记描边都读这里：预览若比落地宽（或窄），用户看到的
    /// 就是"预告骗人"——这比不预告更糟。
    /// </remarks>
    /// <param name="provisionalLayouts">临时布局（拖动中的位置）。</param>
    internal List<DockPair> CollectAcceptedDockPairs(IReadOnlyList<Api.ModuleLayoutView>? provisionalLayouts) =>
        [.. CollectDockPairs(provisionalLayouts).Where(IsDockPairAccepted)];

    /// <summary>一个几何候选是否被**唯一接受判定**接受（方向 + 契约）。</summary>
    private bool IsDockPairAccepted(DockPair pair)
    {
        var source = FindPort(pair.Source.InstanceId, pair.Source.PortName);
        var target = FindPort(pair.Target.InstanceId, pair.Target.PortName);

        return source is not null
            && target is not null
            && CheckCompatibility(pair.Source.InstanceId, source, pair.Target.InstanceId, target).IsCompatible;
    }

    /// <summary>
    /// **拖动网络标签时的"连接对齐"建议**。
    /// </summary>
    /// <param name="instanceId">本口所属实例。</param>
    /// <param name="portName">本口名。</param>
    /// <param name="edge">本口**预览**所在边（拖动中可能已换边）。</param>
    /// <param name="offset">本口**预览**归一化偏移。</param>
    /// <returns>建议（对端口 + 应对齐到的偏移）；无则 null。</returns>
    /// <remarks>
    /// <b>做法</b>：把本口的几何**按预览值覆盖**后跑一遍既有的建网判定（<see cref="DockRule.ComputeDockPairs"/>），
    /// 再经**唯一接受判定**过滤（<see cref="IsDockPairAccepted"/>）；若本口出现在某个候选里，就说明
    /// "按这个位置松手就会连上" ⇒ 返回"把标记吸到与对端口中心对齐"所需的偏移。
    /// <list type="bullet">
    ///   <item><b>为什么这样做</b>：判定必须与真正建网**同源**（几何 + 标签 + 方向 + 契约全部一致），
    ///     否则 UI 会吸到一个"看着能连、松手却不连"的位置 —— 预览骗人比不吸更糟；</item>
    ///   <item>**无副作用**：只构造临时判定数据，不写布局、不触发重算。</item>
    /// </list>
    /// </remarks>
    internal Api.AnchorDockHintView? SuggestAnchorDock(string instanceId, string portName, Sdk.Ports.PortEdge edge, double offset)
    {
        var ends = new List<DockEnd>();
        var mine = default(DockEnd?);

        foreach (var instance in Content.Instances)
        {
            if (!Content.Layout.TryGetModule(instance.InstanceId, out var layout) || !layout.IsViewVisible)
            {
                continue;
            }

            foreach (var port in instance.Ports)
            {
                var isSend = port.Direction == PortDirection.Send;
                var recorded = Content.Layout.TryGetPort(instance.InstanceId, port.Name, out var stored);
                var geometry = PortGeometryResolution.Resolve(
                    port.Declaration, stored, recorded);

                var isMine = string.Equals(instance.InstanceId, instanceId, StringComparison.Ordinal)
                    && string.Equals(port.Name, portName, StringComparison.Ordinal);

                if (isMine)
                {
                    // **预览值**：边与偏移都用拖动中的位置（其余字段照旧）
                    geometry = geometry with { Edge = edge, Offset = Math.Clamp(offset, 0, 1) };
                }

                var end = new DockEnd(
                    instance.InstanceId,
                    layout,
                    port.Name,
                    isSend,
                    geometry,
                    isMine
                        ? edge
                        : geometry.Edge);

                if (isMine)
                {
                    mine = end;
                }

                ends.Add(end);
            }
        }

        if (mine is not { } self)
        {
            return null;
        }

        foreach (var pair in DockRule.ComputeDockPairs(ends))
        {
            var isSource = string.Equals(pair.Source.InstanceId, instanceId, StringComparison.Ordinal)
                && string.Equals(pair.Source.PortName, portName, StringComparison.Ordinal);
            var isTarget = string.Equals(pair.Target.InstanceId, instanceId, StringComparison.Ordinal)
                && string.Equals(pair.Target.PortName, portName, StringComparison.Ordinal);

            if (!isSource && !isTarget)
            {
                continue;
            }

            // **预览不预告"不会被接受"的连接**：契约不兼容的对不产生吸附建议。
            if (!IsDockPairAccepted(pair))
            {
                continue;
            }

            var peer = isSource ? pair.Target : pair.Source;

            // 接触面走向：由**本口所在边**决定（口在左右边 ⇒ 左右相邻 ⇒ 沿 Y 对齐）
            var horizontal = PortEdgeGeometry.IsVertical(self.Edge);

            // 把本口中心对齐到对端口中心 ⇒ 松手即可建网。
            // `MarkerCenter` 是**物理像素**；换算到本窗口 DIP 后再求归一化偏移。
            var selfScale = self.Layout.Scale > 0 ? self.Layout.Scale : 1;
            var othersCenter = DockRule.MarkerCenter(peer, horizontal) / selfScale;
            var origin = horizontal ? self.Layout.Y : self.Layout.X;
            var span = horizontal ? self.Layout.Height : self.Layout.Width;

            if (span <= 0)
            {
                return null;
            }

            return new Api.AnchorDockHintView(
                instanceId,
                portName,
                peer.InstanceId,
                peer.PortName,
                Math.Clamp((othersCenter - origin) / span, 0, 1));
        }

        return null;
    }

    /// <summary>
    /// 把网络压成"**源集合** → 宿集合"的键（用于判断重算前后是否同一条连接 / 网络身份）。
    /// </summary>
    /// <remarks>
    /// 【P40】**对外也用同一个键**：<c>RuntimeServices.GetNetworks()</c> 把它填进 `NetworkView.Key`。
    /// 键含**全部源**（多源网络的身份 = 源集合 + 宿集合）。
    /// **用"声明的源"而非"在场的源"**：身份是**声明**的属性——两条都停流的网络不得因"在场源为空"
    /// 而撞同一个键。**诊断/绑定**的键用 <see cref="DescribeSourceKey"/>（逐源，按源归属）。
    /// </remarks>
    internal static string DescribeKey(Network network) =>
        string.Join(",", network.DeclaredSources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal))
        + "→"
        + string.Join(",", network.Targets.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal));

    /// <summary>把**单个源**与其宿集合压成键（绑定/诊断的身份：`源 → 宿集合`）。</summary>
    /// <param name="source">源成员。</param>
    /// <param name="targets">该源的宿集合。</param>
    /// <returns>身份键。</returns>
    internal static string DescribeSourceKey(NetworkMember source, IReadOnlyList<NetworkMember> targets) =>
        source + "→" + string.Join(",", targets.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal));

    /// <summary>把网络描述成面向用户的短句（区别于持久化文件里的 Describe 重载）。</summary>
    private static string DescribeNetwork(Network network)
    {
        if (network.Targets.Count == 0)
        {
            return string.Empty;
        }

        var sources = network.Sources.Where(s => s.InstanceId.Length > 0).ToList();
        if (sources.Count == 0)
        {
            return string.Empty;
        }

        var name = network.IsNamed ? $"「{network.Name}」" : string.Empty;
        return $"{name}{string.Join("、", sources.Select(s => s.ToString()))} → "
            + string.Join("、", network.Targets.Select(t => $"{t.InstanceId}.{t.PortName}"));
    }
}
