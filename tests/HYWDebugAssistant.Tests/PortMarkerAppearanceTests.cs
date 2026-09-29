using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **网络标签的外观规则**（箭头方向 · 尺寸 · 中线吸附）—— 2026-09 用户实测后补的回归网。
/// </summary>
/// <remarks>
/// <para>
/// 用户报的三个外观问题各自对应下面一组用例：
/// ① **箭头方向写死**（"在上下两条边时，箭头方向仍为左/右式，希望箭头方向不要写死，而是根据
///    网络标签位置和输入输出关系推"）；
/// ② **标记太短**（"（直连）括号太大显示不出来，一半的文字被后面的速率遮住了"）；
/// ③ **要与中线对齐**（"希望添加网络标签与中线对齐能力"）。
/// </para>
/// <para>
/// 这里断言的是**规则本身**（纯值），真实排版由 `ModuleWindowVisualTests` 的无头真布局用例兜底。
/// </para>
/// </remarks>
public class PortMarkerAppearanceTests
{
    /// <summary>
    /// **箭头 = 由「所在边」+「发送/接收」推出**（发送朝外、接收朝内）。
    /// </summary>
    /// <param name="edge">标签所在边。</param>
    /// <param name="isSend">是否发送口。</param>
    /// <param name="expected">期望字形（**内容坐标系**里的字形；竖排分支外面还套着 −90° 旋转，
    /// 但这套映射下"低侧边朝外 / 高侧边朝内"恰好都对应 `↑` ⇒ 两个分支共用同一张表）。</param>
    /// <remarks>
    /// 旧实现是 <c>IsVertical ? "↓" : "→"</c> —— **不看边、不看发送/接收** ⇒ 上下边时永远是"左右式"箭头，
    /// 且同一模块上发送口与接收口的箭头一模一样（只能靠颜色区分）。本用例逐格钉死新规则。
    /// </remarks>
    [Theory]
    // 上边：朝外是"向上"
    [InlineData(PortEdge.Top, true, "↑")]
    [InlineData(PortEdge.Top, false, "↓")]
    // 下边：朝外是"向下"
    [InlineData(PortEdge.Bottom, true, "↓")]
    [InlineData(PortEdge.Bottom, false, "↑")]
    // 左边：朝外是"向左"（竖排分支旋转 −90° ⇒ 内容 ↑ 在屏幕上才是 ←）
    [InlineData(PortEdge.Left, true, "↑")]
    [InlineData(PortEdge.Left, false, "↓")]
    // 右边：朝外是"向右"（同上 ⇒ 内容 ↓ 在屏幕上呈 →）
    [InlineData(PortEdge.Right, true, "↓")]
    [InlineData(PortEdge.Right, false, "↑")]
    public void 箭头方向_应由所在边与发送接收推出(PortEdge edge, bool isSend, string expected)
    {
        var marker = Marker(edge, isSend);

        marker.Arrow.ShouldBe(
            expected,
            $"{edge}+{(isSend ? "发送" : "接收")}：发送=朝外、接收=朝内 ⇒ 屏幕方向唯一 ⇒ 字形 {expected}");
    }

    /// <summary>**换边之后箭头必须跟着变**（否则用户看到的是"箭头还指着上一条边"）。</summary>
    [Fact]
    public void 箭头方向_换边后应跟着变()
    {
        var marker = Marker(PortEdge.Bottom, isSend: true);
        marker.Arrow.ShouldBe("↓", "下边发送 ⇒ 朝外向下");

        marker.Edge = PortEdge.Top;
        marker.Arrow.ShouldBe("↑", "换到上边 ⇒ 朝外向上（同一张表，不是写死的字形）");

        marker.Edge = PortEdge.Right;
        marker.Arrow.ShouldBe("↓", "换到右边 ⇒ 朝外向右（竖排分支：内容 ↓ 在屏幕上呈 →）");
    }

    /// <summary>
    /// **标记长度 = 150，且"名称优先"的可用宽是推出来的**（不是模板里再写一个数字）。
    /// </summary>
    /// <remarks>
    /// 旧实现长度 114：固定槽位占 49 ⇒ 名称可用宽约 63px，而速率文本（`0.00 KB/s`，字号 10 的等宽字体）
    /// 就要 ≈54px ⇒ 「（直连）」只剩约 10px（用户看到的"被速率遮住一半"）。
    /// </remarks>
    [Fact]
    public void 标记尺寸_长度与名称可用宽应自洽()
    {
        var vertical = Marker(PortEdge.Right, isSend: true);
        var horizontal = Marker(PortEdge.Top, isSend: true);

        vertical.MarkerWidth.ShouldBe(PortMarkerMetrics.Width, "左右边缘：窄而长");
        vertical.MarkerHeight.ShouldBe(PortMarkerMetrics.Length);
        horizontal.MarkerWidth.ShouldBe(PortMarkerMetrics.Length, "上下边缘：宽而扁");
        horizontal.MarkerHeight.ShouldBe(PortMarkerMetrics.Width);

        // 名称优先：名称列的可用宽 = 沿边长度 − 固定槽位（槽位合计见 PortMarkerMetrics.FixedChrome 的说明）
        vertical.NameMaxWidth.ShouldBe(
            PortMarkerMetrics.Length - PortMarkerMetrics.FixedChrome,
            "名称列的可用宽必须由标记长度推出来（写死会出现「加长了标签、名称列还是老宽度」的半修状态）");

        vertical.NameMaxWidth.ShouldBeGreaterThan(
            44,
            "至少要让「（直连）」（4 个全角字 × 字号 11 ≈ 44px）排得下 —— 这正是用户报的那个缺陷");

        // 窗口最小边长必须容得下标记（否则标记会被面板夹到起点并溢出被裁）
        PortMarkerMetrics.MinWindowExtent.ShouldBeGreaterThanOrEqualTo(
            PortMarkerMetrics.Length,
            "窗口比标记短 ⇒ 标记被裁（`ModuleWindow.axaml` 的 MinHeight 由它推出来）");
    }

    /// <summary>
    /// **中线吸附**：离模块边缘中点够近 ⇒ 吸到 0.5；离得远 ⇒ 原样返回（不抢用户的手感）。
    /// </summary>
    /// <remarks>
    /// 容差取设置里的「吸附距离」（默认 4px），按**像素**判定 —— 归一化容差会让"窗口越小越吸不住"。
    /// </remarks>
    [Theory]
    [InlineData(0.51, 200, 4, 0.5)]      // 偏 2px（= 0.01 × 200）⇒ 吸
    [InlineData(0.519, 200, 4, 0.5)]     // 偏 3.8px ⇒ 仍在容差内 ⇒ 吸
    [InlineData(0.53, 200, 4, 0.53)]     // 偏 6px ⇒ 不吸（原样）
    [InlineData(0.5, 200, 4, 0.5)]       // 本来就在中线上
    public void 中线吸附_够近才吸(double offset, double extent, double tolerance, double expected)
    {
        AnchorSnap.ToEdgeCenter(offset, extent, tolerance).ShouldBe(expected, 1e-9);
    }

    /// <summary>退化输入不得让吸附变成"乱吸"（边长未知 / 容差为 0）。</summary>
    [Fact]
    public void 中线吸附_退化输入应原样返回()
    {
        AnchorSnap.ToEdgeCenter(0.5, extentDip: 0, toleranceDip: 4).ShouldBe(0.5);
        AnchorSnap.ToEdgeCenter(0.51, extentDip: 200, toleranceDip: 0).ShouldBe(0.51);
    }

    /// <summary>造一个指定边与方向的最小标记（只测规则，不涉及运行时）。</summary>
    private static PortMarkerViewModel Marker(PortEdge edge, bool isSend) =>
        new("m1", "p1", isSend, label: null, offset: 0.5, typeText: "f32", typeKey: "test/f32@1", edge: edge);
}
