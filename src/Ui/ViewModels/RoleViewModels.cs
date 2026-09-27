using System.Globalization;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **角色信息行**（纯文字，画在角色方块周围；**不画文本框**）【用户第三十四轮】。
/// </summary>
/// <param name="Text">文本（空串不参与绘制）。</param>
/// <param name="FontSize">字号（第一行 11px，其余 10px）。</param>
/// <param name="Primary">是否主行（速率）；主行用叠层主文字色，其余用次要色。</param>
public readonly record struct RoleInfoLine(string Text, double FontSize, bool Primary);

/// <summary>
/// **角色方块** = 模块的一个**功能分块**（左入右出的处理块）【ADR-0052】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>角色来自模块声明</b>（一种数据类型 + 输入口/输出口），**不是框架派生**；
///     数据经 `IRuntimeQuery.GetRoles` 取得（**Core 算、UI 只画**）；</item>
///   <item><b>两行文字</b>：功能名（无则类型短名）+ **`m/n`**（输入侧连了几个角色 / 输出侧连了几个角色；
///     无该方向的口 ⇒ `--`；有口未连 ⇒ `0`）；</item>
///   <item><b>填充 = 类型色</b>（按 <see cref="TypeKey"/>，跨模块一致）· **输入边 = 接收方向色**、
///     **输出边 = 发送方向色**（与端口标记同构）；**不带状态色**；</item>
///   <item><b>网络标签不进本层</b>：接口层（端口标记）才是网络标签的表达处 —— 两者不是同一层级。</item>
/// </list>
/// </remarks>
public sealed class RoleBoxViewModel
{
    /// <summary>创建角色方块视图模型。</summary>
    /// <param name="key">角色键（`实例ID/角色Id`）。</param>
    /// <param name="roleId">角色标识（模块内唯一）。</param>
    /// <param name="displayName">功能名（空 ⇒ 呈现层回退为类型短名）。</param>
    /// <param name="typeKey">类型身份（色板键）。</param>
    /// <param name="typeText">类型呈现文本（悬停/回退用）。</param>
    /// <param name="inputs">输入口名。</param>
    /// <param name="outputs">输出口名。</param>
    /// <param name="inboundRoleCount">输入侧连接的对端角色数（null = 无输入口）。</param>
    /// <param name="outboundRoleCount">输出侧连接的对端角色数（null = 无输出口）。</param>
    /// <param name="semanticText">类型语义标签（可读注记；空 = 未声明）。</param>
    /// <param name="nominalRateHz">类型标称速率（null = 未声明）。</param>
    public RoleBoxViewModel(
        string key,
        string roleId,
        string displayName,
        string typeKey,
        string typeText,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        int? inboundRoleCount,
        int? outboundRoleCount,
        string semanticText = "",
        double? nominalRateHz = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        Key = key;
        RoleId = roleId;
        DisplayName = displayName ?? string.Empty;
        TypeKey = typeKey ?? string.Empty;
        TypeText = string.IsNullOrWhiteSpace(typeText) ? "—" : typeText;
        Inputs = inputs ?? [];
        Outputs = outputs ?? [];
        InboundRoleCount = inboundRoleCount;
        OutboundRoleCount = outboundRoleCount;
        SemanticText = semanticText ?? string.Empty;
        NominalRateHz = nominalRateHz;
    }

    /// <summary>角色键（`实例ID/角色Id`）。</summary>
    public string Key { get; }

    /// <summary>角色标识（模块内唯一）。</summary>
    public string RoleId { get; }

    /// <summary>功能名（空 ⇒ 方块第一行回退为类型短名）。</summary>
    public string DisplayName { get; }

    /// <summary>类型身份（色板键，跨模块一致）。</summary>
    public string TypeKey { get; }

    /// <summary>类型呈现文本（`{seq:u64, value:f32}` 之类；悬停与回退用）。</summary>
    public string TypeText { get; }

    /// <summary>输入口名（空 = 纯源分块）。</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>输出口名（空 = 纯汇分块）。</summary>
    public IReadOnlyList<string> Outputs { get; }

    /// <summary>输入侧连接的对端**角色数**（`m`；null = 无输入口 ⇒ 显示 `--`）。</summary>
    public int? InboundRoleCount { get; }

    /// <summary>输出侧连接的对端**角色数**（`n`；null = 无输出口 ⇒ 显示 `--`）。</summary>
    public int? OutboundRoleCount { get; }

    /// <summary>类型语义标签（`样本含序号与值 / value 为无单位实数` 之类；空 = 未声明）。</summary>
    public string SemanticText { get; }

    /// <summary>类型标称速率（Hz；null = 未声明）。</summary>
    public double? NominalRateHz { get; }

    /// <summary>是否有输入侧（决定方块左边是否画接口边）。</summary>
    public bool HasInput => Inputs.Count > 0;

    /// <summary>是否有输出侧（决定方块右边是否画接口边）。</summary>
    public bool HasOutput => Outputs.Count > 0;

    /// <summary>
    /// **方块宽度**（按文本自适应）【ADR-0053 · 用户第三十四轮："中心块太大了……建议使用文本自适应宽度"】。
    /// </summary>
    /// <remarks>纯函数估算（<see cref="RoleTextMetrics"/>）⇒ 两端窗算出的宽度一致，连线锚点因而不会错位。</remarks>
    public double BoxWidth => RoleTextMetrics.BoxWidth(Title, RoleCountText);

    /// <summary>
    /// **角色自身的信息行**（画在方块**上方**的纯文字，**左对齐、不画文本框**）【ADR-0053 · 用户第三十四轮】：
    /// ①**速率**（大字，该角色自身的发送/接收速率）· ②`输入 m / 输出 n`（该角色的**口数**）· ③`类型：xxx`（**短标签**）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**这是角色维度的量**（"这块功能自己在跑多快"），与端口标记上的**通路维度**（"这条网络在跑多快"）
    ///     是两个维度：读同一份框架诊断，但表达对象不同（用户裁定：不能因为底层同源就当作一个维度）；</item>
    ///   <item>用户对排布的要求："文本**左对齐**、**字体更大**一些、**禁止 UI 外显过长描述**（当前第三行太长了）"
    ///     ⇒ 速率用大字号（<see cref="RoleTextMetrics.RateFontSize"/>），第三行只放**第一个语义标签**并截断；</item>
    ///   <item>②行的 `m/n` 是**口数**（与方块上的连接数 `m/n` 不同）：用户草图写的是 `输入0/输出1`。</item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<RoleInfoLine> InfoLines
    {
        get
        {
            var lines = new List<RoleInfoLine>(3)
            {
                new(RateText, RoleTextMetrics.RateFontSize, Primary: true),
                new($"输入 {Inputs.Count} / 输出 {Outputs.Count}", RoleTextMetrics.DetailFontSize, Primary: false),
            };

            if (MetaText.Length > 0)
            {
                lines.Add(new RoleInfoLine(MetaText, RoleTextMetrics.DetailFontSize, Primary: false));
            }

            return lines;
        }
    }

    /// <summary>信息行①：**该角色自身的速率**（无流量 ⇒ `---`；UI 不自算，取框架聚合口径）。</summary>
    public string RateText { get; set; } = "---";

    /// <summary>信息行③：`类型：<短标签>`（未声明 ⇒ 空，不占位；**只放第一个语义标签并截断** —— 禁止外显过长描述）。</summary>
    public string MetaText { get; set; } = string.Empty;

    /// <summary>方块第一行：功能名（无则类型短名，再退呈现文本）。</summary>
    public string Title => DisplayName.Length > 0 ? DisplayName : ShortType;

    /// <summary>
    /// 方块第二行：**`m/n`** —— 输入侧连接的角色数 / 输出侧连接的角色数（无该侧 ⇒ `--`）。
    /// </summary>
    public string RoleCountText => $"{Format(InboundRoleCount)}/{Format(OutboundRoleCount)}";

    /// <summary>类型短名（取呈现文本的"可读部分"；`{seq:u64, value:f32}` 这类结构描述会原样给出）。</summary>
    private string ShortType => TypeText.Length > 24 ? TypeText[..23] + "…" : TypeText;

    private static string Format(int? count) => count is { } value ? value.ToString(CultureInfo.InvariantCulture) : "--";

    /// <inheritdoc />
    public override string ToString() => $"{Title}（{RoleCountText}）";
}

/// <summary>
/// **角色间数据流**（叠加层的一条线 = 一次"角色 → 角色"的流动）【ADR-0052】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>已按（本端角色, 方向, 对端实例, 对端角色）聚合；<see cref="PortPairCount"/> = 口对数（**不在线上标注条数**、
///     **不标网络名** —— 接口层信息不进叠加层）；</item>
///   <item>线两端锚点 = **两个角色方块的中心**（用户第三十四轮裁定："连线从角色方块中心出发"），
///     两侧用同一套布局算，故两段**严格共线**；</item>
///   <item>线本身**不承载指标**（用户第三十四轮裁定）：角色维度信息画在**角色方块**上、
///     通路维度信息画在**端口标记**上 —— 两个维度各有一处出处，不在这条线上再写一遍。</item>
/// </list>
/// </remarks>
public sealed class RoleLinkViewModel
{
    /// <summary>创建角色间数据流视图模型。</summary>
    /// <param name="localRoleKey">本端角色键。</param>
    /// <param name="localIsSend">本端是否**发送侧**（数据从本端输出侧流出）。</param>
    /// <param name="peerInstanceId">对端实例 ID。</param>
    /// <param name="peerRoleKey">对端角色键。</param>
    /// <param name="portPairCount">聚合的口对数（该角色对之间实际建立投递的口对个数）。</param>
    public RoleLinkViewModel(
        string localRoleKey,
        bool localIsSend,
        string peerInstanceId,
        string peerRoleKey,
        int portPairCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(peerInstanceId);

        LocalRoleKey = localRoleKey;
        LocalIsSend = localIsSend;
        PeerInstanceId = peerInstanceId;
        PeerRoleKey = peerRoleKey ?? string.Empty;
        PortPairCount = portPairCount;
    }

    /// <summary>本端角色键（= <see cref="RoleBoxViewModel.Key"/>）。</summary>
    public string LocalRoleKey { get; }

    /// <summary>本端是否发送侧。</summary>
    public bool LocalIsSend { get; }

    /// <summary>对端实例 ID。</summary>
    public string PeerInstanceId { get; }

    /// <summary>对端角色键。</summary>
    public string PeerRoleKey { get; }

    /// <summary>聚合的口对数（1 = 最常见；>1 表示该角色对之间有多条口对投递）。</summary>
    public int PortPairCount { get; }

    /// <summary>
    /// 线的**瞄准点**（本窗局部坐标）= **对端角色方块的中心**【ADR-0053 · 用户第三十四轮】。
    /// </summary>
    /// <remarks>
    /// 用户裁定："**连线从角色方块中心出发**"。本端那端也以**本端方块中心**为端点，
    /// 绘制时裁掉落在方块内部的那一段（见 <c>ConnectionOverlay.LinkSegment</c>）⇒
    /// 视觉上从方块中心方向出发，且不会在方块里留下一道淡线、也不会横穿方块。
    /// </remarks>
    public Avalonia.Point Target { get; internal set; }

    /// <summary>对端是否与本模块**拼接贴合**（接触）——未贴合时线末端画断口 + 标对端名。</summary>
    public bool PeerAdjacent { get; internal set; }

    /// <summary>对端名（`实例`；仅未贴合时绘制）。</summary>
    public string? PeerText { get; internal set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{LocalRoleKey} {(LocalIsSend ? "→" : "←")} {PeerInstanceId}/{PeerRoleKey} ×{PortPairCount}";
}

/// <summary>
/// **模块内部处理顺序**（`角色 → 角色`）【ADR-0052】：细实线 + 中性色 + **不流动**（结构 ≠ 流量）。
/// </summary>
public sealed class RoleOrderViewModel
{
    /// <summary>创建内部顺序视图模型。</summary>
    /// <param name="fromRoleKey">起点角色键。</param>
    /// <param name="toRoleKey">终点角色键。</param>
    /// <param name="label">可选注记（v1 不绘制）。</param>
    public RoleOrderViewModel(string fromRoleKey, string toRoleKey, string? label = null)
    {
        FromRoleKey = fromRoleKey ?? string.Empty;
        ToRoleKey = toRoleKey ?? string.Empty;
        Label = label;
    }

    /// <summary>起点角色键。</summary>
    public string FromRoleKey { get; }

    /// <summary>终点角色键。</summary>
    public string ToRoleKey { get; }

    /// <summary>可选注记（v1 不绘制）。</summary>
    public string? Label { get; }

    /// <inheritdoc />
    public override string ToString() => $"{FromRoleKey} ⇒ {ToRoleKey}";
}

/// <summary>
/// 叠加层**信息区**的内容【ADR-0049 D18 · ADR-0052】。
/// </summary>
/// <param name="Identity">左上：`实例 · 模块名`（12px）。</param>
/// <param name="Notice">
/// 左上第二行（10px，仅在**需要说明**时非空）：无数据口 ⇒ `无数据口`；有数据口但**未声明角色** ⇒ 提示作者补声明。
/// （**不再**用文字重复"几个角色/几个口"——那是图上可见的事实，用户第三十三轮明确要求去掉。）
/// </param>
public readonly record struct RoleOverlayInfo(string Identity, string Notice);

/// <summary>叠加层信息区的构造（纯函数 ⇒ 可单独断言）。</summary>
public static class RoleOverlayInfoBuilder
{
    /// <summary>构造信息区。</summary>
    /// <param name="identity">`实例 · 模块名`。</param>
    /// <param name="roleCount">声明了几个角色。</param>
    /// <param name="portCount">模块有几个数据口。</param>
    /// <returns>信息区内容。</returns>
    public static RoleOverlayInfo Build(string identity, int roleCount, int portCount)
    {
        if (roleCount > 0)
        {
            return new RoleOverlayInfo(identity ?? string.Empty, string.Empty);
        }

        return new RoleOverlayInfo(
            identity ?? string.Empty,
            portCount == 0 ? "无数据口" : $"未声明角色（{portCount} 个数据口）");
    }
}
