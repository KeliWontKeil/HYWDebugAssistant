namespace HYWDebugAssistant.Ui.ModuleSurface.Theme;

/// <summary>
/// **表面样式类名** —— 控件往自己身上挂的类名，与主题文件里的选择器一一对应。
/// </summary>
/// <remarks>
/// <para>
/// 类名是**外观的挂点**：控件只声明"我是什么形状的一块"，具体画成什么样由主题的选择器决定。
/// 把类名收在这里（而不是散在各控件里写字面量）是为了让"主题文件里有没有漏写选择器"这件事
/// 可以在一个地方核对。
/// </para>
/// <para>
/// 名字一律 <c>surface-</c> 前缀，避免与外壳和 Fluent 自带的类名撞车。
/// </para>
/// </remarks>
public static class SurfaceClasses
{
    /// <summary>模块内容区里一块元素的外框。</summary>
    public const string Element = "surface-element";

    /// <summary>容器（分组框 / 折叠区 / 排布容器）的外框。</summary>
    public const string ContainerFrame = "surface-container";

    /// <summary>分组框的标题。</summary>
    public const string ContainerTitle = "surface-container-title";

    /// <summary>
    /// 控件的**说明文字**（文字取 <c>title</c>，摆在哪由 <c>textPlacement</c> 决定）。
    /// </summary>
    /// <remarks>
    /// <b>一个类名管两种排布</b>：类名只说"这是说明文字"，**间距由排布就地给** ——
    /// 上下堆叠时要一点下边距把文字与主体拉开，左右并排时那点边距会把文字**顶高**半个边距
    /// （屏幕上就是"框与文字没对齐"）。分成两个类名时两者只差一条 `Margin`，
    /// 于是"改了一处忘了另一处"是必然；把差别交给排布，类名就只剩一个。
    /// </remarks>
    public const string Title = "surface-title";

    /// <summary>标签条（页签的横排容器）。</summary>
    public const string TabStrip = "surface-tab-strip";

    /// <summary>整组页签的**外框**（包住标签条的那一层；颜色与文本框那一圈同源）。</summary>
    public const string TabStripFrame = "surface-tab-strip-frame";

    /// <summary>标签条上的一个页签。</summary>
    public const string TabStripItem = "surface-tab-strip-item";

    /// <summary>页签的选中态（与 <see cref="TabStripItem"/> 同时挂着）。</summary>
    public const string TabStripSelected = "selected";

    /// <summary>
    /// 选中页签的**下沿**（一条独立元素，横在页签底部）。
    /// </summary>
    /// <remarks>
    /// 为什么不做成按钮自己的边框：按钮的边框归模板部件、由 Fluent 的指针态着色，
    /// 自写同优先级的规则压不住它 ⇒ 指针一移上去选中带就被换掉。独立元素不参与按钮的状态。
    /// </remarks>
    public const string TabStripUnderline = "surface-tab-strip-underline";

    /// <summary>下沿的"这一页是当前页"档（与 <see cref="TabStripUnderline"/> 同时挂着）。</summary>
    public const string TabStripUnderlineSelected = "selected";

    /// <summary>数值读数（等宽、右对齐的那一行）。</summary>
    public const string Numeric = "surface-numeric";

    /// <summary>读数块（基类给每个元素造的"当前值"那一块）。</summary>
    public const string Readout = "surface-readout";

    /// <summary>曲线绘图区。</summary>
    public const string Curve = "surface-curve";

    /// <summary>文本日志 / 文本框（分页窗口的那块编辑器）。</summary>
    public const string Text = "surface-text";

    /// <summary>命令按钮。</summary>
    public const string CommandButton = "surface-command";

    /// <summary>开关（复选框 / 滑块开关共用的外框）。</summary>
    public const string Toggle = "surface-toggle";

    /// <summary>滑块开关拨到"开"那一档（与 <see cref="Toggle"/> 同时挂在外框上）。</summary>
    public const string ToggleOn = "on";

    /// <summary>仅登记、尚未渲染的控件占位。</summary>
    public const string Placeholder = "surface-placeholder";

    /// <summary>丢弃提示（"已丢弃 N 行"）。</summary>
    public const string DroppedHint = "surface-dropped-hint";

    /// <summary>状态灯的"无值"档。</summary>
    public const string StatusUnknown = "status-unknown";

    /// <summary>状态灯的"正常"档。</summary>
    public const string StatusOk = "status-ok";

    /// <summary>状态灯的"告警"档。</summary>
    public const string StatusWarn = "status-warn";

    /// <summary>状态灯的"故障"档。</summary>
    public const string StatusError = "status-error";

    /// <summary>数值读数里的单位文本。</summary>
    public const string Unit = "surface-unit";

    /// <summary>读数 / 标签的**等宽**字体类（数值与文本一律用它，保证列对齐）。</summary>
    public const string Monospace = "surface-mono";

    /// <summary>下拉选择器（枚举 / 选项）。</summary>
    public const string Selector = "surface-selector";

    /// <summary>就地结果行的文本元素。</summary>
    public const string ResultText = "surface-result-text";

    /// <summary>纯文本说明（<c>label</c>）。</summary>
    public const string Label = "surface-label";

    /// <summary>弱化的纯文本说明（次要说明）。</summary>
    public const string LabelMuted = "surface-label-muted";

    /// <summary>滑块开关的**轨道**（胶囊）。</summary>
    public const string ToggleTrack = "surface-toggle-track";

    /// <summary>滑块开关的**圆钮**。</summary>
    public const string ToggleKnob = "surface-toggle-knob";

    /// <summary>把状态档位写成类名（先清旧档再挂新档 —— 档位互斥，不允许同时挂两个）。</summary>
    /// <param name="target">灯体。</param>
    /// <param name="statusClass">档位类名（<see cref="StatusUnknown"/> 等）。</param>
    public static void SetStatusClass(Avalonia.Controls.Control target, string statusClass)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Classes.Remove(StatusUnknown);
        target.Classes.Remove(StatusOk);
        target.Classes.Remove(StatusWarn);
        target.Classes.Remove(StatusError);

        if (!target.Classes.Contains(statusClass))
        {
            target.Classes.Add(statusClass);
        }
    }
}
