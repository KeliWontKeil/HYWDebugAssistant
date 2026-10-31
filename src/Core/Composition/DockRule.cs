using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Core.Composition;

/// <summary>参与建网判定的一端（模块 + 模块矩形 + 端口 + 端口几何）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Layout">模块矩形（屏幕坐标；**预览时可以是拖动中的临时位置**）。</param>
/// <param name="PortName">口名。</param>
/// <param name="IsSend">是否发送口。</param>
/// <param name="Geometry">端口几何（含标签与边缘偏移）。</param>
/// <param name="Shape">端口契约形状。</param>
/// <param name="Semantics">端口传输语义。</param>
/// <param name="Edge">
/// 端口的**有效挂载边**（已按"用户覆盖 &gt; 包内声明"解析，没有惯例回退；见 <see cref="PortEdgeResolution"/>）。
/// </param>
public readonly record struct DockEnd(
    string InstanceId,
    ModuleLayoutView Layout,
    string PortName,
    bool IsSend,
    PortGeometryView Geometry,
    HYWDebugAssistant.Sdk.Contracts.ContractShape Shape,
    HYWDebugAssistant.Sdk.Contracts.TransferSemantics Semantics,
    HYWDebugAssistant.Sdk.Ports.PortEdge Edge);

/// <summary>一个**建网候选**（发送端 → 接收端）。</summary>
/// <param name="Source">发送端。</param>
/// <param name="Target">接收端。</param>
public readonly record struct DockPair(DockEnd Source, DockEnd Target)
{
    /// <summary>发送端口字面量（<c>实例ID.口名</c>）。</summary>
    public string SourceLiteral => $"{Source.InstanceId}.{Source.PortName}";

    /// <summary>接收端口字面量（<c>实例ID.口名</c>）。</summary>
    public string TargetLiteral => $"{Target.InstanceId}.{Target.PortName}";
}

/// <summary>
/// **"接触且对齐"建网判定**——纯几何 + 契约判定，**没有副作用**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只有一份实现</b>：网络行为（L4 推导）与 UI 预览（拖动中高亮"即将建网"）**共用本类**，
///     因此"预览会连上"与"松手后真的连上"永远一致；</item>
///   <item><b>与吸附无关</b>：吸附是 UI 行为（让用户容易对上），本判定只看几何事实——
///     吸附对齐后自然满足条件，满足条件即可建网；</item>
///   <item><b>严格以防误建</b>：接触（相邻边间隙 ≤ <see cref="EdgeContactTolerance"/>）+
///     接触面投影重叠 + 标记中心对齐（≤ <see cref="MarkerAlignTolerance"/>）+
///     方向（发送↔接收）+ 契约兼容（形状**完全一致**）+ **双方均未打标签**（打了标签的口只参与命名网络）。</item>
/// </list>
/// </remarks>
public static class DockRule
{
    /// <summary>接触容差（px）：相邻边间隙在此范围内视为"接触"（吸附贴合后为 0）。</summary>
    public const double EdgeContactTolerance = 2;

    /// <summary>标记对齐容差（px）：两端口标记中心沿接触面的距离上限。</summary>
    public const double MarkerAlignTolerance = 12;

    /// <summary>
    /// 计算**全部建网候选**（发送端 → 接收端）。
    /// </summary>
    /// <param name="ends">参与判定的端口端（调用方决定取哪些实例/端口、用真实还是临时矩形）。</param>
    /// <returns>候选列表（同一发送端可对应多个接收端；严格判定下通常一对一）。</returns>
    /// <remarks>
    /// <b>缺陷修复 · 方向不再由"谁在左/上"决定</b>（症状：网络标签解锁并换边拖动后吸附失效，
    /// 只有在默认边才能吸附）：旧实现把**左/上那个模块直接当成发送端**，
    /// 只在它里面找发送口 ⇒ 只要发送端不在左/上
    /// （恰恰是"把标签拖到另一条边 + 反着摆"的情形），候选**恒为空** ⇒ 吸附与建网同时静默失效。
    /// 现在改为：由**接触几何**算出"两侧各自朝向接触面的那条边"，然后**双向各试一次**
    /// （发送口在哪一侧都行）。物理事实不变：两个口必须各自冲着对方。
    /// </remarks>
    public static IReadOnlyList<DockPair> ComputeDockPairs(IReadOnlyList<DockEnd> ends)
    {
        ArgumentNullException.ThrowIfNull(ends);

        var pairs = new List<DockPair>();
        var seen = new HashSet<(string Source, string Target)>();

        var modules = ends
            .GroupBy(e => e.InstanceId, StringComparer.Ordinal)
            .Select(g => (InstanceId: g.Key, Layout: g.First().Layout, Ends: (IReadOnlyList<DockEnd>)[.. g]))
            .ToList();

        for (var i = 0; i < modules.Count; i++)
        {
            for (var j = i + 1; j < modules.Count; j++)
            {
                var a = modules[i];
                var b = modules[j];

                if (!TryGetContact(a.Layout, b.Layout, out var aIsLeftOrTop, out var horizontal))
                {
                    continue;
                }

                // **接触面朝向**（唯一的判据来源）：左/上那个模块朝外的是右/下边，右/下那个模块朝外的是左/上边。
                // 这两个边名**与谁是发送端无关** —— 方向由端口自己（`IsSend`）决定，不由位置决定。
                var lowFacing = horizontal ? PortEdge.Right : PortEdge.Bottom;
                var highFacing = horizontal ? PortEdge.Left : PortEdge.Top;

                var low = aIsLeftOrTop ? a : b;
                var high = aIsLeftOrTop ? b : a;

                // 双向各试一次：发送口在左/上侧，或发送口在右/下侧。
                Collect(low, lowFacing, high, highFacing, horizontal, pairs, seen);
                Collect(high, highFacing, low, lowFacing, horizontal, pairs, seen);
            }
        }

        return pairs;
    }

    /// <summary>从"发送侧 / 接收侧 + 各自朝向接触面的边"收集候选（两侧共用一条实现）。</summary>
    private static void Collect(
        (string InstanceId, ModuleLayoutView Layout, IReadOnlyList<DockEnd> Ends) senderSide,
        PortEdge senderFacing,
        (string InstanceId, ModuleLayoutView Layout, IReadOnlyList<DockEnd> Ends) receiverSide,
        PortEdge receiverFacing,
        bool horizontal,
        List<DockPair> pairs,
        HashSet<(string Source, string Target)> seen)
    {
        foreach (var source in senderSide.Ends.Where(e => e.IsSend && e.Edge == senderFacing))
        {
            foreach (var target in receiverSide.Ends.Where(e => !e.IsSend && e.Edge == receiverFacing))
            {
                if (!IsDockable(source, target, horizontal)
                    || !seen.Add((source.InstanceId + "." + source.PortName, target.InstanceId + "." + target.PortName)))
                {
                    continue;
                }

                pairs.Add(new DockPair(source, target));
            }
        }
    }

    /// <summary>
    /// 两模块是否**接触**；并给出接触面走向（<paramref name="horizontal"/> = true 表示左右相邻、接触面是竖直线）。
    /// </summary>
    /// <param name="a">模块 A 矩形。</param>
    /// <param name="b">模块 B 矩形。</param>
    /// <param name="aIsLeftOrTop">A 是否位于"左/上"侧（即 A 的发送面朝向 B）。</param>
    /// <param name="horizontal">接触面是否竖直（左右相邻）。</param>
    public static bool TryGetContact(ModuleLayoutView a, ModuleLayoutView b, out bool aIsLeftOrTop, out bool horizontal)
    {
        aIsLeftOrTop = true;
        horizontal = true;

        // **接触判定在物理像素空间做**：两个窗口可能在不同缩放的屏上，DIP 没有统一定义。
        var aScale = ScaleOf(a);
        var bScale = ScaleOf(b);
        var tolerance = EdgeContactTolerance * Math.Min(aScale, bScale);

        // ① 左右相邻：A 在左、B 在右（或反之）
        var (left, right) = (a.X * aScale) <= (b.X * bScale) ? (a, b) : (b, a);
        var leftScale = ScaleOf(left);
        var rightScale = ScaleOf(right);
        if (Math.Abs(((left.X + left.Width) * leftScale) - (right.X * rightScale)) <= tolerance
            && Overlap(left.Y * leftScale, left.Height * leftScale, right.Y * rightScale, right.Height * rightScale) > tolerance)
        {
            aIsLeftOrTop = ReferenceEquals(left, a);
            horizontal = true;
            return true;
        }

        // ② 上下相邻：A 在上、B 在下（或反之）
        var (top, bottom) = (a.Y * aScale) <= (b.Y * bScale) ? (a, b) : (b, a);
        var topScale = ScaleOf(top);
        var bottomScale = ScaleOf(bottom);
        if (Math.Abs(((top.Y + top.Height) * topScale) - (bottom.Y * bottomScale)) <= tolerance
            && Overlap(top.X * topScale, top.Width * topScale, bottom.X * bottomScale, bottom.Width * bottomScale) > tolerance)
        {
            aIsLeftOrTop = ReferenceEquals(top, a);
            horizontal = false;
            return true;
        }

        return false;
    }

    /// <summary>严格判定两端是否可建网（"朝向接触面"已由调用方按候选枚举筛过）。</summary>
    /// <param name="source">发送端（其有效边必须朝向接触面，见 <see cref="ComputeDockPairs"/>）。</param>
    /// <param name="target">接收端（其有效边必须朝向接触面）。</param>
    /// <param name="horizontal">接触面是否竖直（决定"标记中心"取哪个坐标轴）。</param>
    public static bool IsDockable(DockEnd source, DockEnd target, bool horizontal)
    {
        // ① 双方都不得打标签：打了标签的口只参与"命名网络"（两种机制不混用）
        if (!string.IsNullOrWhiteSpace(source.Geometry.Label) || !string.IsNullOrWhiteSpace(target.Geometry.Label))
        {
            return false;
        }

        // ② 契约必须兼容（接收 ⊇ 发送；不自动转换）
        if (!ContractCompatibility.Check(source.Shape, source.Semantics, target.Shape, target.Semantics).IsCompatible)
        {
            return false;
        }

        // ③ 标记中心沿接触面必须对齐（"标记对齐"；中心与容差都换到物理像素）
        var markerTolerance = MarkerAlignTolerance * Math.Min(ScaleOf(source.Layout), ScaleOf(target.Layout));
        return Math.Abs(MarkerCenter(source, horizontal) - MarkerCenter(target, horizontal)) <= markerTolerance;
    }

    /// <summary>端口标记中心在屏幕坐标下的位置（左右相邻取 Y、上下相邻取 X）。</summary>
    /// <param name="end">端口端。</param>
    /// <param name="horizontal">接触面是否竖直。</param>
    public static double MarkerCenter(DockEnd end, bool horizontal)
    {
        var offset = Math.Clamp(end.Geometry.Offset, 0, 1);
        var scale = ScaleOf(end.Layout);
        return horizontal
            ? (end.Layout.Y + (end.Layout.Height * offset)) * scale
            : (end.Layout.X + (end.Layout.Width * offset)) * scale;
    }

    private static double ScaleOf(ModuleLayoutView layout) => layout.Scale > 0 ? layout.Scale : 1;

    /// <summary>两段区间在某一轴上的重叠长度。</summary>
    private static double Overlap(double startA, double lengthA, double startB, double lengthB) =>
        Math.Min(startA + lengthA, startB + lengthB) - Math.Max(startA, startB);
}
