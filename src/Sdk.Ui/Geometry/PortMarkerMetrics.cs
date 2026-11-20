namespace HYWDebugAssistant.Sdk.Ui.Geometry;

/// <summary>
/// **端口标记（网络标签）的规格** —— 标记尺寸的**唯一来源**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么放在 Sdk.Ui</b>：运行时窗口、IDE 预览外壳与快照都读同一份；
///     放在 Ui 会让预览器（只引 Sdk.Ui）读不到，从而出现第二套尺寸。</item>
///   <item><b>长度是固定的</b>：给名称/速率/箭头/灯留够空间（信息变化不引起排版抖动）；缩长会丢信息，
///     所以放不下时由 <see cref="PortMarkerLayout.FitEdge"/> 把标记**换到放得下的边**，而不是缩小。</item>
///   <item><b>跨轴宽 = 预留带厚度</b>：**同一块地方**，不是相加（内容区加同宽内边距 ⇒ 标记与内容永不重叠）。</item>
/// </list>
/// </remarks>
public static class PortMarkerMetrics
{
    /// <summary>标记的**跨轴宽**（左右边缘标记的宽 / 上下边缘标记的高），= 预留带厚度。</summary>
    public const double Width = 20;

    /// <summary>标记的**固定沿边长度**（竖排标记的高 / 横排标记的宽）。</summary>
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
    /// **网络名列的最大宽**（= 首选长度 − 固定槽位）——"名称优先"的直接体现：空间不够时**先裁速率**。
    /// </summary>
    public const double NameMaxWidth = Length - FixedChrome;

    /// <summary>
    /// **模块窗口的最小宽度**（DIP）：比这更窄，标记与角手柄连"缩短的标签"都放不下。
    /// </summary>
    /// <remarks>
    /// 来历：竖排按钮簇（20 + 边距）与竖排标题徽标（约 16）并排 ≈ 50，加上两侧角手柄与描边余量 ≈ 74；取 96 留出运行指示与贴边余量。
    /// **不再由标记长度决定**：标记尺寸固定，放不下的边会被换掉（左右边在高度地板 160 下总能放下）。
    /// **高度地板**另见 <see cref="MinWindowHeight"/>：竖排按钮簇与左右边缘的竖排标记比宽度更吃空间。
    /// </remarks>
    public const double MinWindowWidth = 96;

    /// <summary>
    /// **模块窗口的最小高度**（DIP）：竖排（左右边缘）标记与竖排按钮簇需要的沿边长度。
    /// </summary>
    /// <remarks>
    /// 这条是**推算出来的约束**，不是审美取值：窗口比标记短 ⇒ 标记会被夹到起点并溢出被裁。
    /// 包声明 schema 里 `window.minHeight` 的缺省值取自本常量，读取器把小于地板的声明夹到地板
    /// ⇒ 全框架只有一份地板数值。
    /// </remarks>
    public const double MinWindowHeight = 96;
}
