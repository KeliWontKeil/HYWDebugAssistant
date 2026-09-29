using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>标记面板的**层**：同一套定位算法服务两层（标记自己 / 它的「固定」按钮）。</summary>
public enum PortMarkerLayer
{
    /// <summary>标记块本身（贴窗口外沿、沿边按归一化偏移分布）。</summary>
    Marker = 0,

    /// <summary>
    /// 标记的**「固定 / 解锁」图标按钮**（G5 · C5-7）——落在**同一条边的预留带里、标记长度之外**。
    /// </summary>
    AnchorButton = 1,
}

/// <summary>
/// 口标记的排版面板：标记块**只落在预留带内**（不覆盖模块内容），沿边缘按**归一化偏移**定位。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>预留带</b>：左右边缘各留 <see cref="BandThickness"/> px、上下边缘各留 <see cref="BandThickness"/> px；
///     内容区由视图侧加同宽内边距 —— 因此标记与内容**永不重叠**（用户反馈：标记"像覆盖了正常内容"）。
///     <b>注意这两个 20px 是同一块地方，不是相加</b>（实测：内容区右缘 298、标记带 [299,319]）
///     ⇒ **带内、标记长度之外**才是唯一真正空白的地方（「固定」按钮就放那儿，用户 2026-09 裁定）；</item>
///   <item><b>形态随边缘变化</b>（简单规则，不做过度设计）：左右边缘 = **竖排**（块窄而高、文字旋转 90°）；
///     上下边缘 = **横排**（块宽而扁、文字横排）。当前框架把发送口放右、接收口放左，上下分支为将来留口；</item>
///   <item><b>两层共用一份定位算法</b>：`AlongEdgeSpan` 是"标记沿边占哪一段"的**唯一**实现（标记层与按钮层都调它）
///     —— 曾另有一份 `PortAnchorCalculator`（只被测试用、生产不用）⇒ 同一规则两个来源，已删除；新增按钮层时
///     也**不新写第二份**（按钮位置由标记区间推出来）；</item>
///   <item><b>订阅视图模型</b>（G5 · 2026-09 缺陷修复）：本面板的位置来自 <c>PortMarkerViewModel.Offset/Edge</c>，
///     而它们是**普通绑定读值**（不是面板的可绑定属性）⇒ 值变了**不会**让布局失效。缺了下面的订阅，
///     **沿同一条边拖动时标记在屏幕上纹丝不动**（换边时因宽高绑定变化才"看起来能动"）——
///     用户实测报"解锁了但拖不动"，而当时的用例只断言了视图模型 ⇒ 漏掉。**排版规则仍只有这一份实现**。</item>
/// </list>
/// </remarks>
public sealed class PortMarkerPanel : Panel
{
    /// <summary>
    /// 预留带厚度（px）——内容区需加同宽内边距，否则标记会压住内容。
    /// </summary>
    /// <remarks>
    /// = <see cref="PortMarkerMetrics.Width"/>（**同一块地方**，不是相加）；两者若各写一份，
    /// 改了标记宽度而忘了内边距 ⇒ 标记压住内容（"尺寸只能有一个来源"）。
    /// </remarks>
    public const double BandThickness = PortMarkerMetrics.Width;

    /// <summary>「固定」按钮与标记之间的缝（px）——按钮落在**带内、标记长度之外**时留的间隙。</summary>
    public const double AnchorButtonGap = 2;

    /// <summary>面板层（标记 / 固定按钮）。</summary>
    public static readonly StyledProperty<PortMarkerLayer> LayerProperty =
        AvaloniaProperty.Register<PortMarkerPanel, PortMarkerLayer>(nameof(Layer));

    /// <summary>
    /// 标记与窗口外沿的间距（px）——**【ADR-0039 D3】-1 = 标记外沿与窗口外框完全重合**。
    /// </summary>
    /// <remarks>
    /// 本面板位于 1 px 边框**之内**，故取 −1 让标记压到窗口外框上：两个相邻模块的标记带因此**严丝合缝**
    /// （否则中间会留 2 px 边框缝，看起来像"没对齐" —— 用户第六轮反馈）。
    /// </remarks>
    private const double EdgeInset = -1;

    /// <summary>已挂上变更通知的子项 → 其当前的标记视图模型（用于解绑，防泄漏）。</summary>
    private readonly Dictionary<Control, PortMarkerViewModel> _hooked = [];

    /// <summary>本面板排布的是哪一层（默认 = 标记层）。</summary>
    public PortMarkerLayer Layer
    {
        get => GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Children.CollectionChanged += OnChildrenChanged;

        foreach (var child in Children)
        {
            HookChild(child);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Children.CollectionChanged -= OnChildrenChanged;

        foreach (var (child, marker) in _hooked)
        {
            child.PropertyChanged -= OnChildPropertyChanged;
            marker.PropertyChanged -= OnMarkerChanged;
        }

        _hooked.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var marker = child.DataContext as PortMarkerViewModel;
            var size = MarkerSize(marker, child.DesiredSize);

            child.Arrange(Layer == PortMarkerLayer.AnchorButton
                ? AnchorButtonRect(marker, size, child.DesiredSize, finalSize)
                : MarkerRect(marker, size, finalSize));
        }

        return finalSize;
    }

    /// <summary>标记的占地尺寸（**以视图模型为准**；模型缺失时回退到实测尺寸）。</summary>
    private static Size MarkerSize(PortMarkerViewModel? marker, Size fallback) =>
        marker is null ? fallback : new Size(marker.MarkerWidth, marker.MarkerHeight);

    /// <summary>
    /// **标记沿边占的那一段**（起点 + 可用长度）——标记层与按钮层**共用这一份算法**
    /// （同一规则只许有一处实现，见类注释）。
    /// </summary>
    private static (double Start, double Available) AlongEdgeSpan(
        PortSide side,
        double offset,
        Size panel,
        Size marker)
    {
        var vertical = side is PortSide.Left or PortSide.Right;
        var available = vertical ? panel.Height : panel.Width;
        var length = vertical ? marker.Height : marker.Width;

        var start = (available * Math.Clamp(offset, 0, 1)) - (length / 2);
        start = Math.Clamp(start, 0, Math.Max(0, available - length));
        return (start, available);
    }

    /// <summary>标记层的矩形：沿边按偏移分布，跨轴贴窗口外沿。</summary>
    private static Rect MarkerRect(PortMarkerViewModel? marker, Size size, Size panel)
    {
        var side = marker?.Side ?? PortSide.Left;
        var offset = marker?.Offset ?? 0.5;
        var (start, available) = AlongEdgeSpan(side, offset, panel, size);

        return side switch
        {
            PortSide.Left => new Rect(EdgeInset, start, size.Width, size.Height),
            PortSide.Right => new Rect(
                Math.Max(0, panel.Width - size.Width - EdgeInset), start, size.Width, size.Height),
            PortSide.Top => new Rect(start, EdgeInset, size.Width, size.Height),
            _ => new Rect(
                start, Math.Max(0, panel.Height - size.Height - EdgeInset), size.Width, size.Height),
        };
    }

    /// <summary>
    /// **「固定 / 解锁」按钮**的矩形（G5 · C5-7）：落在**同一条边的预留带里、标记长度之外**。
    /// </summary>
    /// <remarks>
    /// 规则（用户 2026-09 裁定"要落在边框留白处，而不是压内容"）：
    /// <list type="number">
    ///   <item>先看标记**之后**（沿边正方向）放不放得下按钮 + 缝 ⇒ 放那儿；</item>
    ///   <item>否则看标记**之前** ⇒ 放那儿；</item>
    ///   <item>两端都放不下（窗口接近最小尺寸：竖排标记 114 长、窗口最小高 120 ⇒ 只剩 6px）⇒
    ///     **贴到"色条端"**并允许压住标记的那一头 —— 色条只是一根实色窄条，损失最小；
    ///     **永不压指示灯那一头**（连接状态是用户最在意的信息）。</item>
    /// </list>
    /// 「色条端」在同一条边上对竖排/横排**方向相反**：竖排内容整体旋转 −90°（内容 x 小的一端落到屏幕
    /// **下方**）⇒ 色条端 = 屏幕**下**（沿边坐标更大）；横排不旋转 ⇒ 色条端 = 屏幕**左**（坐标更小）。
    /// </remarks>
    private static Rect AnchorButtonRect(
        PortMarkerViewModel? marker,
        Size markerSize,
        Size button,
        Size panel)
    {
        var side = marker?.Side ?? PortSide.Left;
        var offset = marker?.Offset ?? 0.5;
        var vertical = side is PortSide.Left or PortSide.Right;

        var (start, available) = AlongEdgeSpan(side, offset, panel, markerSize);
        var markerLength = vertical ? markerSize.Height : markerSize.Width;

        // 按钮的沿边长度取**实测尺寸**（模板里已固定 20×20；不在这里再写一份常量，免得两处会漂）
        var alongLength = vertical ? button.Height : button.Width;
        var need = alongLength + AnchorButtonGap;
        var end = start + markerLength;

        double along;
        if (available - end >= need)
        {
            along = end + AnchorButtonGap;                        // ① 标记之后
        }
        else if (start >= need)
        {
            along = start - AnchorButtonGap - alongLength;        // ② 标记之前
        }
        else
        {
            // ③ 都不够：贴"色条端"（竖排 = 下端；横排 = 左端），允许压住色条
            along = vertical ? available - alongLength : 0;
        }

        along = Math.Clamp(along, 0, Math.Max(0, available - alongLength));

        return vertical
            ? new Rect(
                side == PortSide.Right
                    ? Math.Max(0, panel.Width - button.Width - EdgeInset)
                    : EdgeInset,
                along,
                button.Width,
                button.Height)
            : new Rect(
                along,
                side == PortSide.Bottom
                    ? Math.Max(0, panel.Height - button.Height - EdgeInset)
                    : EdgeInset,
                button.Width,
                button.Height);
    }

    /// <summary>子项集合变化：新子项也要挂上变更通知（数量与端口数同阶，全量重挂即可）。</summary>
    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var child in Children)
        {
            HookChild(child);
        }
    }

    /// <summary>挂上"子项 DataContext 变化"的通知（`ItemsControl` 是先加子项、后赋 DataContext）。</summary>
    private void HookChild(Control child)
    {
        child.PropertyChanged -= OnChildPropertyChanged;
        child.PropertyChanged += OnChildPropertyChanged;
        Rebind(child);
    }

    private void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == StyledElement.DataContextProperty && sender is Control child)
        {
            Rebind(child);
        }
    }

    /// <summary>把订阅切到子项**当前**的标记视图模型上。</summary>
    private void Rebind(Control child)
    {
        if (_hooked.Remove(child, out var previous))
        {
            previous.PropertyChanged -= OnMarkerChanged;
        }

        if (child.DataContext is PortMarkerViewModel marker)
        {
            marker.PropertyChanged += OnMarkerChanged;
            _hooked[child] = marker;
        }
    }

    /// <summary>
    /// **位置或边缘变了 ⇒ 让排布失效**（缺这一段：同边拖动的预览在屏幕上不动，见类注释）。
    /// 两层都要：按钮的落位也由标记区间推出来。
    /// </summary>
    private void OnMarkerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PortMarkerViewModel.Offset) or nameof(PortMarkerViewModel.Edge))
        {
            InvalidateArrange();
        }
    }
}
