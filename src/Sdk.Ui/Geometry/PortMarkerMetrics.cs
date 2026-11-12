namespace HYWDebugAssistant.Sdk.Ui.Geometry;

/// <summary>
/// **端口标记（网络标签）的规格** —— 标记尺寸的**唯一来源**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么放在 Sdk.Ui</b>：运行时窗口、IDE 预览外壳与快照都读同一份；
///     放在 Ui 会让预览器（只引 Sdk.Ui）读不到，从而出现第二套尺寸。</item>
///   <item><b>长度</b>：给名称/速率/箭头/灯留够空间（信息变化不引起排版抖动）。</item>
///   <item><b>跨轴宽 = 预留带厚度</b>：**同一块地方**，不是相加（内容区加同宽内边距 ⇒ 标记与内容永不重叠）。</item>
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
    /// 色条 + 箭头列 + 指示灯 + 灯距端 + 名称边距 + 速率边距。
    /// </summary>
    /// <remarks>
    /// 与运行时标记模板里的实际留白**一一对应**（`ModuleWindow.axaml` 的标记模板：
    /// `Grid ColumnDefinitions="Auto,Auto,*,20,Auto"` 加各自的 `Margin`）—— 改动其中任何一处都要同步这里，
    /// 否则"名称可用宽"会算错（表现为又被截掉一截）。具体数值以本常量为准，注释不复制。
    /// </remarks>
    public const double FixedChrome = 49;

    /// <summary>
    /// **网络名列的最大宽**（= 长度 − 固定槽位）——"名称优先"的直接体现：空间不够时**先裁速率**。
    /// </summary>
    public const double NameMaxWidth = Length - FixedChrome;

    /// <summary>
    /// **模块窗口沿边的最小可行尺寸**（= 标记长度 + 余量）——「固定 / 解锁」按钮要在标记之外落脚，
    /// 窗口还得比标记长一点。
    /// </summary>
    /// <remarks>
    /// 这条是**推算出来的约束**，不是审美取值：窗口比标记短 ⇒ 标记被面板夹到起点并**溢出被裁**。
    /// 它**就是框架地板的来历**：包声明 schema 里 `window.minHeight` 的缺省值取自本常量，
    /// 读取器把小于地板的声明夹到地板 ⇒ 全框架只有一份地板数值。
    /// </remarks>
    public const double MinWindowExtent = 160;
}
