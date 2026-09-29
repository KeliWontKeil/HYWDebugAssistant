namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **端口标记（网络标签）的规格** —— 标记尺寸的**唯一来源**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要有这个类型</b>（2026-09 缺陷修复）：标记尺寸原先有**两个来源** ——
///     <c>PortMarkerViewModel.MarkerWidth/MarkerHeight</c> 里写死的 20/114，与 <c>Tokens.axaml</c> 的
///     <c>PortMarkerWidth</c>/<c>PortMarkerLength</c>；而**真正生效的只有前者**（模板的宽高绑定的就是视图模型，
///     面板排布也读它，两个 token 全仓库无人引用 = 死资源）⇒ 改了一处、另一处没改，"尺寸对不上"只会在
///     真机上以"标记压住内容 / 被裁"的形式出现。现在收敛到这里一处，token 侧只留注释指回来。</item>
///   <item><b>长度 114 → 150</b>（2026-09 用户实测）：长度 114 时槽位缺口约 63px，而速率文本（<c>0.00 KB/s</c>）
///     就要 ≈54px ⇒ 网络名只剩约 10px，「（直连）」必然被截（用户："括号太大显示不出来，一半的文字被
///     后面的速率遮住了"）⇒ 加长到 **150**。代价：窗口 <c>MinHeight</c> 必须 ≥ 标记长度 + 余量
///     （见 <see cref="MinWindowExtent"/>）。</item>
///   <item><b>跨轴宽 20</b> = 预留带厚度（<c>PortMarkerPanel.BandThickness</c>）：**同一块地方**，
///     不是相加（内容区加同宽内边距 ⇒ 标记与内容永不重叠）。</item>
/// </list>
/// </remarks>
public static class PortMarkerMetrics
{
    /// <summary>标记的**跨轴宽**（左右边缘标记的宽 / 上下边缘标记的高），= 预留带厚度。</summary>
    public const double Width = 20;

    /// <summary>标记的**沿边长度**（竖排标记的高 / 横排标记的宽）。</summary>
    public const double Length = 150;

    /// <summary>
    /// 标记内**固定槽位**的合计宽（不参与"谁让谁"的弹性分配）：
    /// 色条 6 + 箭头列 20 + 指示灯 8 + 灯距端 5 + 名称边距 6 + 速率边距 4 = **49**。
    /// </summary>
    /// <remarks>
    /// 与模板里的实际留白**一一对应**（`ModuleWindow.axaml` 的标记模板：`Grid ColumnDefinitions="Auto,Auto,*,20,Auto"`
    /// 加各自的 `Margin`）—— 改动其中任何一处都要同步这里，否则"名称可用宽"会算错
    /// （表现为又被截掉一截，正是本次要修的缺陷）。
    /// </remarks>
    public const double FixedChrome = 49;

    /// <summary>
    /// **网络名列的最大宽**（= 长度 − 固定槽位）——"名称优先"的直接体现：空间不够时**先裁速率**。
    /// </summary>
    /// <remarks>
    /// 【2026-09 用户裁定】用户选了"名称优先：空间不够时先裁速率而不是名称"——名字是"这是哪条网络"的
    /// 身份信息，速率的量纲/位置由槽位保证，被裁掉也不至于误读。
    /// </remarks>
    public const double NameMaxWidth = Length - FixedChrome;

    /// <summary>
    /// **模块窗口沿边的最小可行尺寸**（= 标记长度 + 余量）——「固定 / 解锁」按钮要在标记之外落脚，
    /// 窗口还得比标记长一点。
    /// </summary>
    /// <remarks>
    /// 这条是**推算出来的约束**，不是审美取值：窗口比标记短 ⇒ 标记被面板夹到起点并**溢出被裁**
    /// （`PortMarkerPanel.AlongEdgeSpan` 的 `Math.Clamp(start, 0, max(0, available - length))`），
    /// 表现为"标记被切掉一截"。`ModuleWindow.axaml` 的 `MinWidth/MinHeight` 与
    /// `ModuleViewViewModel` 的同名常量必须 ≥ 它。
    /// </remarks>
    public const double MinWindowExtent = 160;
}
