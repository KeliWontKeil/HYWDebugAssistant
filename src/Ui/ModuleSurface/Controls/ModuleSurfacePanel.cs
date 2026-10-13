using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **模块表面排布面板** —— 一次排布作用域（模块内容根，或一个容器内部）。
/// </summary>
/// <remarks>
/// <para>
/// 它读的东西只有两样：**声明节点**（<see cref="ISurfaceLayoutItem.Node"/>）与
/// **宿主给出的块轴事实**（<see cref="ISurfaceLayoutItem.Metrics"/>）。
/// 它<b>不认识任何控件类型</b> —— 派发、绑定、取数都在别处；这里只算矩形。
/// 这一条是"加一种控件不需要改布局引擎"的落点。
/// </para>
/// <para>
/// **排布口径全部来自声明**（<c>layout</c> / <c>overflow</c> / <c>padding</c> / <c>spacing</c> /
/// <c>columns</c> / <c>rows</c> / <c>columnSpacing</c> / <c>rowSpacing</c>）——
/// 面板自己**不带几何缺省**：只做"没声明时取哪个枚举值"的解析，而这两个枚举缺省与 schema 一致。
/// 量到的高度一律向上报，由外层滚动条接管；<c>overflow: clip</c> 时改为不外报。
/// </para>
/// <para>
/// 填满性与最小高**不问控件类型**，一律走 <see cref="SurfaceMetrics"/> 的投影 ——
/// 面板只看得见 <see cref="ISurfaceLayoutItem.Node"/> 与 <see cref="ISurfaceLayoutItem.Metrics"/>。
/// </para>
/// </remarks>
public sealed class ModuleSurfacePanel : Panel
{
    /// <summary>这一层要排的东西（按声明顺序；每项各对应一个子控件）。</summary>
    public IReadOnlyList<ISurfaceLayoutItem> Items { get; private set; } = [];

    /// <summary>本层的排布声明（<see langword="null"/> = 模块内容根，量不到任何声明键）。</summary>
    private ViewNode? Declaration { get; set; }

    /// <summary>项 → 子控件。</summary>
    private readonly Dictionary<ISurfaceLayoutItem, Control> _visuals = new(ReferenceEqualityComparer.Instance);

    /// <summary>流式这一遍量到的块轴口径（顺序 = <see cref="VisibleItems"/> 的顺序）。</summary>
    /// <remarks>
    /// 它是"测量这一遍的产物、排布这一遍的输入"，**不是跨遍缓存**：测量一遍就把整份覆盖写一次，
    /// 排布只用**同一次测量**写下的那一份。它与高度分配的**决定**无关（决定在
    /// <see cref="SurfaceBlockSolver.Plan"/>，输入是实得高度）—— 所以不存在"测量按 A 算、排布按 B 算"。
    /// </remarks>
    private BlockSpec[] _flowSpecs = [];

    /// <summary>排布模式（声明面的值；缺省流式）。</summary>
    private ViewLayoutMode Mode => Declaration is { } node
        ? ViewLayoutModeExtensions.Parse(node.Layout, ViewLayoutMode.Flow)
        : ViewLayoutMode.Flow;

    /// <summary>越界处理（声明面的值；缺省交给外层滚动）。</summary>
    private ViewLayoutOverflow Overflow => Declaration is { } node
        ? ViewLayoutOverflowExtensions.Parse(node.Overflow, ViewLayoutOverflow.Scroll)
        : ViewLayoutOverflow.Scroll;

    /// <summary>
    /// 子控件之间的间距（DIP）—— **就是这一层声明里的 <c>spacing</c>**（缺省由 schema 给）。
    /// </summary>
    /// <remarks>
    /// 面板自己不补第二个默认值：声明没写这个键时，读取器已按 schema 的容器缺省填好；
    /// 面板再补一个（或换一个主题里的数）就会让"同一份声明在两处得到不同的间距"。
    /// </remarks>
    private double Spacing { get; set; }

    /// <summary>网格列间距（DIP；<see cref="double.NaN"/> ⇒ 用 <see cref="Spacing"/>）。</summary>
    private double ColumnSpacing { get; set; } = double.NaN;

    /// <summary>网格行间距（DIP；<see cref="double.NaN"/> ⇒ 用 <see cref="Spacing"/>）。</summary>
    private double RowSpacing { get; set; } = double.NaN;

    /// <summary>网格列规格（缺省 <see langword="null"/> ⇒ 单列 <c>*</c>）。</summary>
    private IReadOnlyList<GridTrack>? Columns { get; set; }

    /// <summary>网格行规格（缺省 <see langword="null"/> ⇒ 行数由元素推断、每行按内容）。</summary>
    private IReadOnlyList<GridTrack>? Rows { get; set; }

    /// <summary>实际列规格（没声明 ⇒ 单列 <c>*</c>）。</summary>
    private IReadOnlyList<GridTrack> EffectiveColumns =>
        Declaration is { Columns.Count: > 0 } node ? node.Columns : DefaultColumns;

    /// <summary>实际行规格（没声明 ⇒ 空 ⇒ 行数由元素推断、每行按内容）。</summary>
    private IReadOnlyList<GridTrack> EffectiveRows =>
        Declaration is { Rows.Count: > 0 } node ? node.Rows : [];

    /// <summary>实际列间距（声明未给 ⇒ 用间距）。</summary>
    private double EffectiveColumnSpacing => double.IsNaN(ColumnSpacing) ? Spacing : Math.Max(0, ColumnSpacing);

    /// <summary>实际行间距（声明未给 ⇒ 用间距）。</summary>
    private double EffectiveRowSpacing => double.IsNaN(RowSpacing) ? Spacing : Math.Max(0, RowSpacing);

    /// <summary>单列缺省规格。</summary>
    private static readonly GridTrack[] DefaultColumns = [GridTrack.Star(1)];

    /// <summary>
    /// 把这一层的子项装进来（重装会先清干净）。
    /// </summary>
    /// <param name="declaration">
    /// 本层的排布声明（容器 = 它自己的节点；`tabs` 的页内容 = **页节点**；模块内容根 = 视图根节点）。
    /// </param>
    /// <param name="items">按声明顺序的子项（它们的 <see cref="ISurfaceLayoutItem.Node"/> 就是本层的孩子）。</param>
    /// <param name="visual">子项 → 子控件（本面板只问一次；编排层决定怎么造）。</param>
    /// <remarks>
    /// <para>
    /// **本面板只认两样东西**：<see cref="ISurfaceLayoutItem.Node"/>（几何的唯一来源）
    /// 与 <see cref="ISurfaceLayoutItem.Metrics"/>（块轴事实）。它不认识"分组框""标签条"这些概念 ——
    /// 那是编排层的事。**留白（<c>padding</c>）由外层容器负责**，面板不管。
    /// </para>
    /// <para>
    /// <b>有子项就必须有声明</b>：排布模式、间距、轨道全从它读 —— 少了它就会静默退化成一堆枚举缺省，
    /// 而"同一份页内容第一页一套、其它页另一套"正是这么来的。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">有子项却没有声明（调用点的层序装错了）。</exception>
    public void Populate(
        ViewNode? declaration,
        IReadOnlyList<ISurfaceLayoutItem> items,
        Func<ISurfaceLayoutItem, Control> visual)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(visual);

        if (declaration is null && items.Count > 0)
        {
            throw new InvalidOperationException(
                "排布面板拿到了子项却没有任何声明 —— 这一层必须传它自己的声明节点"
                + "（`tabs` 的页内容要传**页节点**，不是页签条自己的节点）。");
        }

        Declaration = declaration;
        Spacing = declaration is { } node ? Math.Max(0, node.Spacing) : 0;
        ColumnSpacing = declaration is { ColumnSpacing: > 0 } columnNode ? columnNode.ColumnSpacing : double.NaN;
        RowSpacing = declaration is { RowSpacing: > 0 } rowNode ? rowNode.RowSpacing : double.NaN;

        foreach (var child in Children)
        {
            // 先断开逻辑父级：漏掉会让旧子树留在逻辑树里（Avalonia 的 SetParent(null) 是幂等的）。
            ((ISetLogicalParent)child).SetParent(null);
        }

        Children.Clear();
        Items = items;
        _visuals.Clear();

        foreach (var item in items)
        {
            // **重装复用同一个子控件**：编排层的"视觉"是按声明节点缓存的，同一个节点在两次
            // 装配之间拿到的是同一个控件实例。于是"切页 / 折叠 / 重建"只是重新排布，
            // 已有子树的焦点、滚动位置、缓冲都不会被重置 —— 这正是旧实现丢掉的东西。
            // （本面板自己不缓存：缓存属于编排层，"哪些控件还活着"是它的账。）
            var child = visual(item);
            _visuals[item] = child;

            if (!Children.Contains(child))
            {
                ((ISetLogicalParent)child).SetParent(this);
                Children.Add(child);
            }
        }

        InvalidateMeasure();
    }

    /// <summary>取一项的子控件（不在表里 ⇒ <see langword="null"/>）。</summary>
    /// <param name="item">项。</param>
    private Control? VisualOf(ISurfaceLayoutItem item) =>
        _visuals.TryGetValue(item, out var visual) ? visual : null;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var items = VisibleItems();
        for (var i = 0; i < items.Count; i++)
        {
            Measure(items[i].Item, items[i].Visual, availableSize, out _, out _);
        }

        return Mode switch
        {
            ViewLayoutMode.Panel => MeasurePanel(availableSize),
            ViewLayoutMode.Absolute => MeasureAbsolute(availableSize),
            ViewLayoutMode.Grid => MeasureGrid(availableSize),
            _ => MeasureFlow(availableSize),
        };
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize) => Mode switch
    {
        ViewLayoutMode.Panel => ArrangePanel(finalSize),
        ViewLayoutMode.Absolute => ArrangeAbsolute(finalSize),
        ViewLayoutMode.Grid => ArrangeGrid(finalSize),
        _ => ArrangeFlow(finalSize),
    };

    /// <summary>可见的（项，子控件）对，按声明顺序。</summary>
    private List<(ISurfaceLayoutItem Item, Control Visual)> VisibleItems()
    {
        var list = new List<(ISurfaceLayoutItem, Control)>(Items.Count);
        foreach (var item in Items)
        {
            if (VisualOf(item) is { IsVisible: true } visual)
            {
                list.Add((item, visual));
            }
        }

        return list;
    }

    /// <summary>流式（缺省）：单列满宽、自上而下；余量等分给"填满型"。</summary>
    /// <remarks>
    /// <para>
    /// 高度这件事**只问 <see cref="SurfaceBlockSolver"/>**：
    /// 测量这一遍量出每个孩子的"地板口径"（<see cref="SurfaceBlockSolver.SpecOf"/>）并把它们累加成最小高；
    /// 排布这一遍把**实得高度**交给 <see cref="SurfaceBlockSolver.Plan"/>，由它给出每个孩子实际占多高。
    /// </para>
    /// <para>
    /// 于是"测量按 A 算、排布按 B 算"在结构上不可能发生：两次调用是**同一个纯函数**，
    /// 而决定分配的那一次用的输入只有"实得高度"——不读任何上一遍留下的尺寸。
    /// </para>
    /// </remarks>
    private Size MeasureFlow(Size availableSize)
    {
        ClipToBounds = Overflow is ViewLayoutOverflow.Clip;

        var bandWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var items = VisibleItems();
        var specs = new BlockSpec[items.Count];
        var needed = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            var (item, visual) = items[i];

            // 填满型的提示给"这一层真正给的可用高"（有限视口）：它量到的是"在这个空间里真实的占位"，
            // 于是地板与排布一致；给 0 会让它报 0 高，那一刻起整条高度链就没有事实了。
            Measure(item, visual, new Size(bandWidth, availableSize.Height), out _, out _);

            specs[i] = SurfaceBlockSolver.SpecOf(item.Node, visual.DesiredSize.Height);

            needed += i > 0 ? Spacing : 0;
            needed += specs[i].Floor;
        }

        _flowSpecs = specs;

        // 面板的期望高度 = **最小布局高度**：窗口更大 ⇒ 排布把余量给填满型；更小 ⇒ 外层滚动条接管
        return new Size(bandWidth, ClampExtent(needed, availableSize.Height));
    }

    /// <summary>流式排布（缺省）：单列满宽、自上而下。</summary>
    /// <remarks>
    /// 每个孩子的实得高由 <see cref="SurfaceBlockSolver.Plan"/> 一次算出（统一分给每个孩子），
    /// 然后**按这个高再量一遍**：文本面这类控件要知道自己的最终高才能决定内部排布，
    /// 而"最终高"在测量那一遍还不存在。这一遍的输入只有 <paramref name="finalSize"/> —— 依旧是本次的事实。
    /// </remarks>
    private Size ArrangeFlow(Size finalSize)
    {
        var items = VisibleItems();

        // 测量那一遍没记下口径（面板刚装上、或项数变了）⇒ 现量一遍补齐（幂等，不引入跨遍状态）。
        var specs = _flowSpecs.Length == items.Count ? _flowSpecs : MeasureSpecs(items, finalSize);

        var plan = SurfaceBlockSolver.Plan(specs, Spacing, finalSize.Height);
        var y = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            var (item, visual) = items[i];
            var width = ExtentOf(item, visual, vertical: false);
            var actualHeight = plan.Heights[i];
            var actualWidth = width.Arranged(visual.DesiredSize.Width, finalSize.Width);

            // 按**实得高**再量一遍：内部排布（文本面的视口、容器的行）要在确定的高下才成立。
            visual.Measure(new Size(actualWidth, actualHeight));

            // 非填满的宽度按声明的停靠对齐（`stretch` 与"声明了宽度"冲突时以宽度为准 ⇒ 等同靠左）
            var x = OffsetOf(item.Node.Align, finalSize.Width, actualWidth);

            visual.Arrange(new Rect(x, y, actualWidth, actualHeight));
            y += actualHeight + (i + 1 < items.Count ? Spacing : 0);
        }

        // 实占高度可能超过 finalSize（窗口太小时各填满型仍守住地板）⇒ 让外层滚动知道真实高度
        return new Size(finalSize.Width, ClampExtent(Math.Max(finalSize.Height, y), finalSize.Height));
    }

    /// <summary>现量一遍并算出每个孩子的块轴口径（<see cref="ArrangeFlow"/> 的兜底路径）。</summary>
    /// <param name="items">这一层的可见项。</param>
    /// <param name="size">这一遍的尺寸（宽度与可用高都取自它）。</param>
    /// <returns>口径表。</returns>
    private static BlockSpec[] MeasureSpecs(List<(ISurfaceLayoutItem Item, Control Visual)> items, Size size)
    {
        var specs = new BlockSpec[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var (item, visual) = items[i];
            Measure(item, visual, size, out _, out _);
            specs[i] = SurfaceBlockSolver.SpecOf(item.Node, visual.DesiredSize.Height);
        }

        return specs;
    }

    /// <summary>
    /// 绝对坐标：**声明了几何**的元素按自己的 X/Y/宽/高摆放，**没声明几何**的元素按流式接在声明区之下
    /// （不隐形、不重叠 —— 让"漏了一条"表现为"排在下面"，而不是变成一个看不见的控件）。
    /// </summary>
    private Size MeasureAbsolute(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var box = DesignBox();
        var (scaleX, scaleY) = ScaleFactors(box, availableSize);
        var bottom = 0.0;

        foreach (var (item, visual) in VisibleItems())
        {
            if (HasGeometry(item.Node))
            {
                var size = ResolveSize(item.Node, scaleX, scaleY);
                var y = item.Node.Y * (item.Node.ScaleWithWindow ? scaleY : 1);

                visual.Measure(size);
                bottom = Math.Max(bottom, y + size.Height);
                continue;
            }

            Measure(item, visual, new Size(width, availableSize.Height), out _, out _);

            bottom += Spacing;
            bottom += SurfaceBlockSolver.SpecOf(item.Node, visual.DesiredSize.Height).Floor;
        }

        return new Size(width, ClampExtent(bottom, availableSize.Height));
    }

    /// <summary>绝对坐标排布（声明区的底边之下留给"没声明几何"的元素，按流式接上）。</summary>
    private Size ArrangeAbsolute(Size finalSize)
    {
        var box = DesignBox();
        var (scaleX, scaleY) = ScaleFactors(box, finalSize);
        var bottom = 0.0;

        foreach (var (item, visual) in VisibleItems())
        {
            if (HasGeometry(item.Node))
            {
                var scale = item.Node.ScaleWithWindow;
                var size = ResolveSize(item.Node, scaleX, scaleY);
                var x = item.Node.X * (scale ? scaleX : 1);
                var y = item.Node.Y * (scale ? scaleY : 1);

                visual.Arrange(new Rect(x, y, size.Width, size.Height));
                bottom = Math.Max(bottom, y + size.Height);
                continue;
            }

            var extent = ExtentOf(item, visual, vertical: true);
            var height = extent.IsFill ? Math.Max(extent.Min, 1) : visual.DesiredSize.Height;
            var top = bottom + Spacing;

            visual.Arrange(new Rect(0, top, finalSize.Width, height));
            bottom = top + height;
        }

        return new Size(finalSize.Width, ClampExtent(bottom, finalSize.Height));
    }

    /// <summary>
    /// **panel 量**：孩子各自按声明的像素量，本面板报"面板框"尺寸。
    /// </summary>
    /// <param name="availableSize">父层这一遍给的可用尺寸（格子）。</param>
    /// <returns>期望尺寸。</returns>
    /// <remarks>
    /// <para>
    /// <b>三级尺寸</b>（三者互不相同，是本模式最容易被混起来的地方）：
    /// <list type="number">
    ///   <item><b>格子</b> = <paramref name="availableSize"/>（父层分给本面板的空间）；</item>
    ///   <item><b>设计框</b> = 所有孩子的 <c>(x, y, x+宽, y+高)</c> 包围盒，**只由坐标决定**，
    ///     与窗口多大、格子多大**毫无关系** —— 这是"内部相对坐标恒定"的载体；</item>
    ///   <item><b>面板框</b> = 本面板真正占据的矩形，由面板自己的 <c>width</c>/<c>height</c> 定：
    ///     <c>pixels</c> ⇒ 声明值；<c>fill</c> ⇒ 格子；<c>adaptive</c>/未声明 ⇒ 设计框。</item>
    /// </list>
    /// "整块挪位"就是：**面板框**随格子变，**设计框**与孩子坐标一丝不变，设计框在面板框里按
    /// <see cref="ViewNode.Anchor"/>/<see cref="ViewNode.VAnchor"/> 停靠。
    /// </para>
    /// <para>
    /// 孩子**只按设计框**取尺寸（`fill` 的孩子也拿设计框尺寸），**绝不拿格子或面板框尺寸** ——
    /// 否则孩子内部的自适应容器会随窗口重排，"整块挪位"就退化成了"整块重排"。
    /// </para>
    /// </remarks>
    private Size MeasurePanel(Size availableSize)
    {
        var design = PanelDesignBox();

        // **`fill` 档必须自己兜底**：`Arranged` 对 `fill` 回的是 `available`，而"摸底"那几遍给的
        // 可用尺寸是**无穷**（流式与网格都要先问"你想多大"）⇒ 照抄会让面板向上报无穷，
        // 而 Avalonia 的 `Measure` 会因此抛 `Invalid size returned for Measure`（整窗白屏级的事故）。
        // 面板框 = 按自己的两轴声明解析（与其它元素同一套 `AxisExtent` 口径）；
        // 可用尺寸无穷时按"设计框"兜底（面板不会小于自己的内容，也不会大到没边），有限的可用尺寸照旧夹住。
        var boxWidth = PanelBoxExtent(vertical: false).Arranged(design.Width, PanelFallback(availableSize.Width, design.Width));
        var boxHeight = PanelBoxExtent(vertical: true).Arranged(design.Height, PanelFallback(availableSize.Height, design.Height));

        if (!double.IsInfinity(availableSize.Width))
        {
            boxWidth = Math.Min(boxWidth, availableSize.Width);
        }

        if (!double.IsInfinity(availableSize.Height))
        {
            boxHeight = Math.Min(boxHeight, availableSize.Height);
        }

        foreach (var (item, visual) in VisibleItems())
        {
            visual.Measure(new Size(
                Math.Max(0, item.Node.Width.Pixels ?? 0),
                Math.Max(0, item.Node.Height.Pixels ?? 0)));
        }

        return new Size(Math.Max(0, boxWidth), Math.Max(0, boxHeight));
    }

    /// <summary>
    /// **panel 排**：先算设计框在本面板框里的停靠偏移，再把每个孩子放到"偏移 + 孩子坐标"。
    /// </summary>
    /// <param name="finalSize">本面板实得的矩形（= 面板框）。</param>
    /// <returns>实占尺寸。</returns>
    /// <remarks>
    /// <para>
    /// 设计框**不做原点归一化**：孩子的坐标就是相对本面板原点的位移，直接相加。
    /// 归一化（把包围盒平移回原点）会让"我把元素往左挪 10px"在屏幕上**看不出变化**
    /// （整组又被移回来了）—— 那是静默抵消声明。
    /// </para>
    /// <para>
    /// <b>停靠偏移只用"余量"，不减去设计框原点</b>：<see cref="PanelDesignBox"/> 的原点是
    /// **孩子坐标包围盒的左上角**；若把它减掉，等于把"最小的那个 x"抵消成 0 ——
    /// 声明 <c>x: 50</c> 会排在 0，而 <c>x: 200</c> 会排在 150（整组被平移，间距却照旧，
    /// 于是表现为"绝对坐标被强制左对齐了"，且很难看出是平移而不是"x 没生效"）。
    /// </para>
    /// </remarks>
    private Size ArrangePanel(Size finalSize)
    {
        var design = PanelDesignBox();

        // 停靠偏移：设计框在面板框里的余量按 anchor/valign 分配（复用网格那一套 `OffsetOf`）。
        // 这两个键只有 `panel` 有（读取器已保证其余模式不会带进来），缺省 `Start` = 左上。
        var offsetX = OffsetOf(Declaration?.Anchor ?? CellAlign.Start, finalSize.Width, design.Width);
        var offsetY = OffsetOf(Declaration?.VAnchor ?? CellAlign.Start, finalSize.Height, design.Height);

        foreach (var (item, visual) in VisibleItems())
        {
            var node = item.Node;
            var width = Math.Max(0, node.Width.Pixels ?? 0);
            var height = Math.Max(0, node.Height.Pixels ?? 0);

            visual.Arrange(new Rect(offsetX + node.X, offsetY + node.Y, width, height));
        }

        return finalSize;
    }

    /// <summary>
    /// **设计框**：panel 的全部可见孩子的 <c>(x, y, x+宽, y+高)</c> 包围盒（无孩子 ⇒ 空矩形）。
    /// </summary>
    /// <remarks>
    /// 它是"内部相对坐标"的唯一载体，**不读面板尺寸、不读格子尺寸、不读任何缩放**。
    /// 与 <see cref="DesignBox"/>（`absolute` 用）的区别：那个只统计"声明了几何"的元素，
    /// 这个统计全部可见孩子（`panel` 里坐标是必写的，没有"没声明几何"这一档）。
    /// </remarks>
    private Rect PanelDesignBox()
    {
        var box = default(Rect);
        var any = false;

        foreach (var (item, visual) in VisibleItems())
        {
            var node = item.Node;
            var right = node.X + Math.Max(0, node.Width.Pixels ?? 0);
            var bottom = node.Y + Math.Max(0, node.Height.Pixels ?? 0);

            if (!any)
            {
                box = new Rect(node.X, node.Y, right - node.X, bottom - node.Y);
                any = true;
                continue;
            }

            var left = Math.Min(box.X, node.X);
            var top = Math.Min(box.Y, node.Y);
            box = new Rect(left, top, Math.Max(box.Right, right) - left, Math.Max(box.Bottom, bottom) - top);
        }

        return box;
    }

    /// <summary>`fill` 档的兜底：可用尺寸有限就用它，无穷就退到设计框尺寸（再退 0）。</summary>
    /// <param name="available">父层给的可用尺寸（可能为无穷）。</param>
    /// <param name="design">设计框在该轴上的尺寸。</param>
    /// <returns>给 <see cref="AxisExtent.Arranged"/> 用的"可用空间"。</returns>
    private static double PanelFallback(double available, double design) =>
        double.IsInfinity(available) ? design : available;

    /// <summary>面板自己那个轴的尺寸口径（`panel` 的"内容尺寸"= 设计框，不是内容各自期望尺寸之和）。</summary>
    private AxisExtent PanelBoxExtent(bool vertical)
    {
        var axis = Declaration is { } node ? (vertical ? node.Height : node.Width) : null;
        var min = axis?.Min ?? 0;
        var max = axis?.Max ?? double.PositiveInfinity;

        return axis?.ResolvedMode switch
        {
            AxisMode.Pixels => new AxisExtent(AxisMode.Pixels, Math.Max(axis.Pixels ?? 0, 0), min, max),
            AxisMode.Fill => new AxisExtent(AxisMode.Fill, 0, min, max),
            // `adaptive` 与"未声明"都取设计框尺寸：panel 的"内容尺寸"就是设计框。
            _ => new AxisExtent(AxisMode.Adaptive, 0, min, max),
        };
    }

    /// <summary>网格（`TableLayoutPanel` 语义）：列/行轨道 + 元素落在格子里。</summary>
    private Size MeasureGrid(Size availableSize)
    {
        // **只按内容量，`*` 轨道一分余量都不分** —— 与父层这一遍给多宽无关（给多宽都不影响"我要多宽"），
        // 于是"高 250 的网格"在视口 594 与视口 800 里量到的一样高，报给外层的期望尺寸才是可信的。
        // 只有轨道之和会超出父层给的宽度时才把轨道按比例收一收（见 `ClampTracks`）。
        var plan = BuildGrid(new Size(availableSize.Width, double.PositiveInfinity), final: false);

        return new Size(Math.Min(plan.Width, availableSize.Width), plan.Height);
    }

    /// <summary>把轨道尺寸按比例收进可用宽（收不动时不动手，让滚动条去处理）。</summary>
    /// <param name="tracks">轨道尺寸。</param>
    /// <param name="trackMinimums">每个轨道的下限（写死像素的格子就是它的声明值，其余认内容下限）。</param>
    /// <param name="spacing">轨道间距。</param>
    /// <param name="available">可用宽（无穷或非正数时不动）。</param>
    /// <remarks>
    /// 存在的理由：测量遍里的 `*` 轨道是按**父层这一遍给的**宽度分的，而真正排布时面板往往窄一圈
    /// （内容内缩带就吃掉一圈）。不夹的话轨道之和会大于面板宽，跨列格子又按"格宽之和 + 格间距"算，
    /// 于是最右边会压到端口标记的预留带上（实测：日志与三列按钮的右边都到 368，而面板只有 360）。
    /// </remarks>
    private static void ClampTracks(double[] tracks, double[] trackMinimums, double spacing, double available)
    {
        if (double.IsInfinity(available) || available <= 0)
        {
            return;
        }

        double Total()
        {
            var total = spacing * Math.Max(0, tracks.Length - 1);
            for (var i = 0; i < tracks.Length; i++)
            {
                total += tracks[i];
            }

            return total;
        }

        var total = Total();

        if (total <= available || total <= 0)
        {
            return;
        }

        var gaps = spacing * Math.Max(0, tracks.Length - 1);
        var budget = Math.Max(0, available - gaps);

        // 按比例收会把窄的轨道也一起收掉，而格子里的控件有下限（命令按钮就是），
        // 收不动的格子会反过来把总宽顶回去（实测：收完仍是 362，而面板只有 360）。
        // 所以每一轮都把"已经到下限的格子"摘出去，只在还能收的格子里按比例分剩下的额度，直到放下或没人可收。
        // 下限按**声明语义**给：写死像素的格子一点都不能动（动它是改声明），其余认内容下限。
        var minimums = new double[tracks.Length];

        for (var i = 0; i < tracks.Length; i++)
        {
            minimums[i] = trackMinimums[i];
        }

        for (var round = 0; round < tracks.Length; round++)
        {
            var fixedTotal = 0.0;
            var flexible = 0.0;

            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] <= minimums[i] + 0.001)
                {
                    fixedTotal += tracks[i];
                }
                else
                {
                    flexible += tracks[i];
                }
            }

            if (flexible <= 0.001)
            {
                return;
            }

            var room = budget - fixedTotal;

            if (room >= flexible)
            {
                return;
            }

            var scale = Math.Max(0, room) / flexible;

            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] > minimums[i] + 0.001)
                {
                    tracks[i] = Math.Max(minimums[i], tracks[i] * scale);
                }
            }
        }
    }

    /// <summary>网格排布（`*` 行在这里按实得高度撑开）。</summary>
    private Size ArrangeGrid(Size finalSize)
    {
        // **网格内容一律裁在自己的框里**（与流式一致，不看 `overflow` 声明）：
        // 跨列格子会按"格宽之和 + 格间距"算宽，而这个格间距是父层的，格子自己那几条间距比它窄时
        // 跨列项就会比面板宽出一段（实测：三列各 128、间距 8 的面板宽 360，`columnSpan: 3` 的日志却算出 368）
        // —— 不裁的话它会压到端口标记的预留带上。声明面靠 `overflow: clip` 表达的是**要不要滚动条**，
        // 不是"允不允许画到框外"。
        ClipToBounds = true;

        // **按父层这一遍给的尺寸排**：父层先量后摆时给的可以比"这一遍报的期望"宽一圈
        // （内容内缩带也会差一圈），而格子坐标必须与"面板真的被摆在哪"对齐，
        // 否则最右侧/最下侧那一格会排出面板，压到端口标记的预留带上。
        var plan = BuildGrid(finalSize, final: true);

        foreach (var (child, rect) in plan.Rects)
        {
            child.Measure(new Size(rect.Width, rect.Height));
            child.Arrange(rect);
        }

        // 报的是**这一遍真的占下的高度**：`*` 行拿到 finalSize 的余量后会比测量时高，
        // 只有如实上报，外层排布（与滚动条）才知道内容到底有多高。
        var height = finalSize.Height;
        foreach (var (_, rect) in plan.Rects)
        {
            height = Math.Max(height, rect.Bottom);
        }

        return new Size(finalSize.Width, ClampExtent(height, finalSize.Height));
    }

    /// <summary>算一遍网格（测量与排布共用；<paramref name="final"/> = 是否给出最终矩形）。</summary>
    private GridPlan BuildGrid(Size finalSize, bool final)
    {
        var columns = EffectiveColumns;

        // **行规格必须取 `EffectiveRows`，不能取 `Rows` 字段**：`Rows` 只在"声明里真的写了 rows"
        // 时才有值，而 `EffectiveRows`（缺省空 ⇒ 行数由元素推断、每行按内容）才是这一层的排布口径。
        // 取错字段的后果是静默的：`*` 行拿不到权重（`starRowWeight` 为 0）、余量一分都分不出去，
        // 于是"声明了 `*` 行"与"什么都没声明"在图上一模一样 —— 填满型永远停在自己的最小高度。
        var declaredRows = EffectiveRows;
        var columnCount = Math.Max(1, columns.Count);
        var rowCount = declaredRows.Count;
        var columnSpacing = EffectiveColumnSpacing;
        var rowSpacing = EffectiveRowSpacing;

        // **量的时候，`*` 轨道不许把父层给的可用空间算成自己的**（排布那一遍才许）。
        // 父层量一个"高 250 的网格"时给的是**父层自己的**可用高（实测 594），
        // 若照单全收，`*` 行就把 594 吃满、网格报出 594 的期望高，一路顶上去：
        // 页容器虚高 ⇒ 标签页把整页排在视口之外 ⇒ 同一页里的元素被后面的内容盖住。
        // 口径因此是：**测量 = 按内容、排布 = 按实得空间**（与流式那一层"报最小布局高度"同一条）。
        var availableWidth = double.IsInfinity(finalSize.Width) ? 0 : finalSize.Width;
        var availableHeight = double.IsInfinity(finalSize.Height) ? 0 : finalSize.Height;

        // ---- ① 分格：显式指定优先，其余按行优先自动放置（跳过已占用格） ----
        var pending = VisibleItems();
        var items = new List<GridItem>();
        var occupied = new HashSet<(int Column, int Row)>();

        foreach (var (item, visual) in pending)
        {
            var node = item.Node;
            if (node.Column is null && node.Row is null)
            {
                continue;
            }

            var column = Math.Clamp(node.Column ?? 0, 0, columnCount - 1);
            var row = Math.Max(0, node.Row ?? 0);
            var columnSpan = Math.Clamp(node.ColumnSpan, 1, columnCount - column);
            var rowSpan = Math.Max(1, node.RowSpan);

            rowCount = Math.Max(rowCount, row + rowSpan);
            items.Add(new GridItem(item, visual, column, row, columnSpan, rowSpan));
            OccupyCell(occupied, column, row, columnSpan, rowSpan);
        }

        var cursor = 0;
        foreach (var (item, visual) in pending)
        {
            var node = item.Node;
            if (node.Column is not null || node.Row is not null)
            {
                continue;
            }

            while (occupied.Contains((cursor % columnCount, cursor / columnCount)))
            {
                cursor++;
            }

            var column = cursor % columnCount;
            var row = cursor / columnCount;
            cursor++;

            rowCount = Math.Max(rowCount, row + 1);
            items.Add(new GridItem(item, visual, column, row, 1, 1));
            occupied.Add((column, row));
        }

        rowCount = Math.Max(1, rowCount);

        // ---- ② 轨道：固定值先定，其余按内容取下限，`*` 再分剩余 ----
        var columnWidths = new double[columnCount];
        var fixedColumns = 0.0;
        var starColumnWeight = 0.0;
        for (var i = 0; i < columnCount; i++)
        {
            if (TrackKind(columns, i, starDefault: true) == GridTrackKind.Fixed)
            {
                columnWidths[i] = Math.Max(0, TrackValue(columns, i));
                fixedColumns += columnWidths[i];
            }
            else if (TrackKind(columns, i, starDefault: true) == GridTrackKind.Star)
            {
                starColumnWeight += TrackWeight(columns, i);
            }
        }

        var rowHeights = new double[rowCount];
        var fixedRows = 0.0;
        var starRowWeight = 0.0;
        for (var i = 0; i < rowCount; i++)
        {
            if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Fixed)
            {
                rowHeights[i] = Math.Max(0, TrackValue(declaredRows, i));
                fixedRows += rowHeights[i];
            }
            else if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Star)
            {
                starRowWeight += TrackWeight(declaredRows, i);
            }
        }

        // ---- ③ 量一遍子控件（固定轨道给轨道尺寸；`*`/`auto` 给内容尺寸，填满型给有限的最小高度） ----
        // 这一遍是"**摸底**"：轨道尺寸此刻还没定（`*` 要等 ④ 给出各格的下限、再分余量），
        // 所以非固定轨道只能给"内容量得到多少"的机会 —— 填满型按自己的最小高度报。
        var desired = new Size[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            var widthExtent = ExtentOf(entry.Item, entry.Visual, vertical: false);
            var heightExtent = ExtentOf(entry.Item, entry.Visual, vertical: true);

            // **宽度提示一律给一个有限的数**：固定列给轨道宽，其余给面板这一遍的可用宽。
            // 给无穷的后果不是"量不出宽度"，而是**会折行的控件按整行文本报宽** ——
            // `TextBox` 的 `TextWrapping=Wrap` 只在宽度受约束时才折行，宽度不受约束时它一路长到
            // 整行文本的宽度（实测 200 字符 ⇒ 文本面 1946 宽，而视口只有 360），
            // 这个尺寸再顺"轨道内容下限"顶上去，就变成**内容把模块右边界挤到外面**。
            // 宽度这一轴不该由内容说了算（与 `SurfaceViewport` 同一条口径）：框多宽由声明与视口决定。
            var widthHint = widthExtent.GridHint(
                TrackKind(columns, entry.Column, starDefault: true) == GridTrackKind.Fixed
                    ? columnWidths[entry.Column]
                    : availableWidth > 0 ? availableWidth : 0);

            // 非固定行给 **0** 当填满提示（不是无穷）：这一遍是摸底，`auto` / `*` 都还没定尺寸。
            // 给 0 的效果分三档，恰好各有各的道理：
            //   · 声明了固定像素 ⇒ `GridHint` 直接用声明值（作者说了算）；
            //   · 填满型且**有下限**（声明了 `height.min`）⇒ 量到下限 —— 给无穷的话它会按内容虚报，
            //     把 `*` 行撑到整屏高，后面的 `auto` 行被挤出视口；
            //   · 按内容型 / 下限为 0 的填满型 ⇒ 量到内容高（这正是它们该报的）。
            var heightHint = heightExtent.GridHint(
                TrackKind(declaredRows, entry.Row, starDefault: false) == GridTrackKind.Fixed
                    ? rowHeights[entry.Row]
                    : 0);

            entry.Visual.Measure(new Size(widthHint, heightHint));
            desired[i] = entry.Visual.DesiredSize;
        }

        // ---- ④ 内容下限：单列/单行子元素决定 auto 轨道尺寸、也给 `*` 轨道一个下限 ----
        var autoColumns = 0.0;
        var starColumnMinimums = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            var kind = TrackKind(columns, entry.Column, starDefault: true);
            if (entry.ColumnSpan != 1 || kind == GridTrackKind.Fixed)
            {
                continue;
            }

            // 声明了宽度的元素**以声明值为下限**（显式尺寸是模块作者说了算）
            var minimum = ExtentOf(entry.Item, entry.Visual, vertical: false).Minimum(desired[i].Width);
            columnWidths[entry.Column] = Math.Max(columnWidths[entry.Column], minimum);
        }

        for (var i = 0; i < columnCount; i++)
        {
            if (TrackKind(columns, i, starDefault: true) == GridTrackKind.Auto)
            {
                autoColumns += columnWidths[i];
            }
            else if (TrackKind(columns, i, starDefault: true) == GridTrackKind.Star)
            {
                starColumnMinimums += columnWidths[i];
            }
        }

        var columnSpacings = columnSpacing * Math.Max(0, columnCount - 1);
        var columnRemaining = availableWidth > 0
            ? Math.Max(0, availableWidth - fixedColumns - autoColumns - columnSpacings - starColumnMinimums)
            : 0;

        // 收之前先记下每个轨道的下限：`columnWidths` 走到这里刚好就是"内容 + 声明下限"的值
        // （`*` 的余量还没分），所以它就是"这一列最少要占多宽"。写死像素的格子不动 —— 动它是改声明。
        var trackMinimums = new double[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            trackMinimums[i] = columnWidths[i];
        }

        if (starColumnWeight > 0)
        {
            for (var i = 0; i < columnCount; i++)
            {
                if (TrackKind(columns, i, starDefault: true) == GridTrackKind.Star)
                {
                    columnWidths[i] += columnRemaining * (TrackWeight(columns, i) / starColumnWeight);
                }
            }
        }

        ClampTracks(columnWidths, trackMinimums, columnSpacing, availableWidth);

        var autoRows = 0.0;
        var starRowMinimums = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            var kind = TrackKind(declaredRows, entry.Row, starDefault: false);
            if (entry.RowSpan != 1 || kind == GridTrackKind.Fixed)
            {
                continue;
            }

            var need = ExtentOf(entry.Item, entry.Visual, vertical: true).Minimum(desired[i].Height);
            rowHeights[entry.Row] = Math.Max(rowHeights[entry.Row], need);
        }

        for (var i = 0; i < rowCount; i++)
        {
            if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Auto)
            {
                autoRows += rowHeights[i];
            }
            else if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Star)
            {
                starRowMinimums += rowHeights[i];
            }
        }

        var rowSpacings = rowSpacing * Math.Max(0, rowCount - 1);
        var rowRemaining = availableHeight > 0
            ? Math.Max(0, availableHeight - fixedRows - autoRows - rowSpacings - starRowMinimums)
            : 0;

        // **`*` 行只许吃"扣掉其它行需要的高度之后剩下的"**。
        // 直接拿"父层可用高"来分是错的：`*` 行会把 `auto` 行的份额也吃掉，
        // 于是网格总高超出可用高，排在最后的那几行被推到视口之外、**一个像素都看不见**
        // （实测：数据显示器的两排开关声明了 30 高，实得 0 高 —— 面板只把 `*` 行那份画了出来）。
        // 其它行需要的高度 = 固定像素行 + `auto` 行的内容高 + 它们与 `*` 行之间的间距。
        if (starRowWeight > 0 && final)
        {
            // 其它行要占的：固定像素行 + `auto` 的内容高 + 全部行间距。剩下的才归 `*` 行。
            var reserved = fixedRows + autoRows + rowSpacings;
            var starRoom = availableHeight > 0 ? Math.Max(0, availableHeight - reserved) : 0;

            for (var i = 0; i < rowCount; i++)
            {
                if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Star)
                {
                    rowHeights[i] += starRoom * (TrackWeight(declaredRows, i) / starRowWeight);
                }
            }
        }

        // ---- ④′ 轨道定案后再量一遍（**只在排布那一遍**）----
        // 高度提示必须是"这一遍真的分到的高度"，否则填满型会一直按摸底时那个最小高度定死自己
        // （实测：`*` 行分到 382，填满型的日志却仍停在 88 —— 内容明明有空间却挤在顶部）。
        if (final)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                var trackHeight = rowSpacing * Math.Max(0, entry.RowSpan - 1);
                for (var r = entry.Row; r < entry.Row + entry.RowSpan && r < rowHeights.Length; r++)
                {
                    trackHeight += rowHeights[r];
                }

                // 跨列时按轨道合计给宽，同理：不给合计的话跨列元素的宽度也会停在摸底值。
                // **按轨道各自的起点算**（不能用"格宽之和 + 格间距"）：那样子元素会连**后面那条**
                // 格间距一起吃掉，而面板在最后一条间距之前就结束了（实测三列各 128、间距 8 的面板宽 360，
                // `columnSpan: 3` 的日志却算出 368 —— 压到端口标记的预留带上）。
                var trackWidth = columnSpacing * Math.Max(0, entry.ColumnSpan - 1);
                for (var c = entry.Column; c < entry.Column + entry.ColumnSpan && c < columnWidths.Length; c++)
                {
                    trackWidth += columnWidths[c];
                }

                var widthExtent = ExtentOf(entry.Item, entry.Visual, vertical: false);
                var heightExtent = ExtentOf(entry.Item, entry.Visual, vertical: true);

                // 宽度同样给有限的数：固定列给轨道宽，其余给**面板这一遍的可用宽**。
                //
                // **不给 `Math.Max(轨道宽, 可用宽)`**：那个 Max 会让量到的宽度反过来撑开列宽 ——
                // 子控件（尤其带视口的容器）在"比父层还宽"的提示下会自报一个更大的期望宽，
                // 而下一条把它当成 `*` 列的内容下界 ⇒ **排布时列宽被顶到比面板还宽**
                // （实测：面板 378、页签那一列被顶到 418，整列右侧被裁掉 40）。
                // `*` 轨道的余量由面板这一遍的可用宽决定，与"某个孩子想要多宽"无关。
                var widthHint = widthExtent.GridHint(
                    TrackKind(columns, entry.Column, starDefault: true) == GridTrackKind.Fixed
                        ? columnWidths[entry.Column]
                        : availableWidth);
                var heightHint = heightExtent.MeasureHint(
                    TrackKind(declaredRows, entry.Row, starDefault: false) == GridTrackKind.Fixed
                        ? rowHeights[entry.Row]
                        : trackHeight);

                entry.Visual.Measure(new Size(widthHint, heightHint));
                desired[i] = entry.Visual.DesiredSize;
            }
        }

        // ---- ⑤ 汇总尺寸（`*` 行没拿到余量时就是内容最小高度 ⇒ 交给外层滚动条） ----
        var contentWidth = columnWidths.Sum() + columnSpacings;
        var contentHeight = rowHeights.Sum() + rowSpacings;

        // **宽度一律夹在父层给的可用宽里**：轨道宽之和可以比视口大（声明了固定像素、或某个格子的
        // 内容下限量得偏大），但"内容要多宽"不该由格子里的东西说了算 —— 与高度那条不一样：
        // 高度超出时该出滚动条（内容真的排不下），而**宽度超出只说明有个控件报错了宽度**。
        // 不夹的后果实测过：一个长文本的 `TextBox` 报 1946 宽 ⇒ 面板把 1946 当"内容宽"往上报 ⇒
        // 整个模块的内容区变成 1946 宽，**文字把模块右边界挤到屏幕外面**。
        var width = double.IsInfinity(availableWidth) ? contentWidth : Math.Min(contentWidth, availableWidth);
        var height = contentHeight;

        if (!final)
        {
            return new GridPlan(width, height, []);
        }

        var offsetsX = new double[columnCount];
        var x = 0.0;
        for (var i = 0; i < columnCount; i++)
        {
            offsetsX[i] = x;
            x += columnWidths[i] + columnSpacing;
        }

        var offsetsY = new double[rowCount];
        var y = 0.0;
        for (var i = 0; i < rowCount; i++)
        {
            offsetsY[i] = y;
            y += rowHeights[i] + rowSpacing;
        }

        var rects = new List<(Control Child, Rect Rect)>();
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            var lastColumn = Math.Min(columnWidths.Length - 1, entry.Column + entry.ColumnSpan - 1);
            var lastRow = Math.Min(rowHeights.Length - 1, entry.Row + entry.RowSpan - 1);

            // 格子的范围按**轨道起点到轨道终点**算（`+1` 位是"下一条轨道的起点"，末条则以面板边收尾）。
            // 不能用"格宽之和 + 格间距"：那是在隐性假设"格间距比父层窄"，格子跨到最后一列时会把
            // **面板根本不存在的那条**末尾间距也算进去（实测三列各 128、间距 8 的面板宽 360，
            // 却在 272 处排出一个 96 宽的按钮 ⇒ 右边到 368，压到端口标记的预留带上）。
            var cellX = offsetsX[entry.Column];
            var cellY = offsetsY[entry.Row];

            // 格子 = "从这条轨道的起点，跨过它在跨度里占的那几条轨道"：
            // 宽度 = 跨度内各轨道之和 + 跨度内的格间距；高度同理。
            // **不要用"轨道之和 + 全部间距"**（那会把跨度之后那条格间距也算进去，
            // 跨到最后一列时排出面板外）；**也不要按面板边收尾**（最后一行会算出比行本身还小的高度，
            // 实测行高 30、行顶 444、面板高 454 ⇒ 格子只剩 10）。内容真的超出面板时由外层滚动条接住。
            var cellWidth = 0.0;
            for (var c = entry.Column; c <= lastColumn; c++)
            {
                cellWidth += columnWidths[c];
            }

            cellWidth += columnSpacing * Math.Max(0, lastColumn - entry.Column);

            var cellHeight = 0.0;
            for (var r = entry.Row; r <= lastRow; r++)
            {
                cellHeight += rowHeights[r];
            }

            cellHeight += rowSpacing * Math.Max(0, lastRow - entry.Row);

            // **项不许超出格子**（`fill` / `adaptive` 那两档）：声明尺寸是"我想要多大"，
            // 格子是"这一层真的分给我多大" —— 两者冲突时以格子为准（面板自己也会把超出的部分裁掉）。
            // 不夹的话，`fill` 型会把"三列各 128/128/96、间距 8"的格宽和顶成 368，而面板只有 360
            // ⇒ 右边压到端口预留带。
            // **`pixels` 那一档例外**：作者写死的尺寸就是唯一事实源，格子比它窄也**如实按它摆**
            // （夹了就等于静默改声明 —— 声明 96 的元素在 90 的格子里实得 90，界面上看不出是声明失效）。
            // 溢出交给外层滚动条（与"放不下不缩内容"同一条口径）。
            static double FitCell(double value, double cell, AxisMode mode) =>
                mode is AxisMode.Pixels ? value : Math.Min(value, cell);

            var widthExtent = ExtentOf(entry.Item, entry.Visual, vertical: false);
            var heightExtent = ExtentOf(entry.Item, entry.Visual, vertical: true);
            var itemWidth = FitCell(widthExtent.Arranged(desired[i].Width, cellWidth), cellWidth, widthExtent.Mode);
            var itemHeight = FitCell(heightExtent.Arranged(desired[i].Height, cellHeight), cellHeight, heightExtent.Mode);
            var offsetX = OffsetOf(entry.Item.Node.Align, cellWidth, itemWidth);
            var offsetY = OffsetOf(entry.Item.Node.VerticalAlign, cellHeight, itemHeight);

            var rect = new Rect(cellX + offsetX, cellY + offsetY, itemWidth, itemHeight);
            rects.Add((entry.Visual, rect));
        }

        return new GridPlan(width, height, rects);
    }

    private static void OccupyCell(HashSet<(int Column, int Row)> occupied, int column, int row, int columnSpan, int rowSpan)
    {
        for (var c = column; c < column + columnSpan; c++)
        {
            for (var r = row; r < row + rowSpan; r++)
            {
                occupied.Add((c, r));
            }
        }
    }

    private static GridTrackKind TrackKind(IReadOnlyList<GridTrack> tracks, int index, bool starDefault)
    {
        if (index >= 0 && index < tracks.Count)
        {
            return tracks[index].Kind;
        }

        return starDefault ? GridTrackKind.Star : GridTrackKind.Auto;
    }

    private static double TrackValue(IReadOnlyList<GridTrack> tracks, int index) =>
        index >= 0 && index < tracks.Count ? tracks[index].Value : 0;

    private static double TrackWeight(IReadOnlyList<GridTrack> tracks, int index) =>
        Math.Max(TrackValue(tracks, index), 0);

    /// <summary>在格子里按停靠方式算偏移（`stretch` 与显式尺寸冲突 ⇒ 等同 `start`）。</summary>
    private static double OffsetOf(CellAlign align, double available, double size)
    {
        var slack = Math.Max(0, available - size);
        return align switch
        {
            CellAlign.Center => slack / 2,
            CellAlign.End => slack,
            _ => 0,
        };
    }

    /// <summary>`overflow: clip` ⇒ 不向外报溢出高度（不产生滚动条）；缺省 `scroll` 照旧报真实高度。</summary>
    private double ClampExtent(double extent, double available) =>
        Overflow is ViewLayoutOverflow.Clip && !double.IsInfinity(available) && available > 0
            ? Math.Min(extent, available)
            : extent;

    /// <summary>测量一个子控件：按声明面解析两个轴，再按轴的测量提示给约束。</summary>
    /// <remarks>
    /// 纵轴的提示给的是"**这一层真正给的可用高**"，不是"填满型的下限"。
    /// 从前给的是下限（通常 0），于是填满型在测量里一律报 0 高 ——
    /// 而排布那一遍又按余量分给它一大块，两遍口径不同，是自适应高度一切漂移的入口。
    /// </remarks>
    private static void Measure(
        ISurfaceLayoutItem item,
        Control visual,
        Size available,
        out AxisExtent width,
        out AxisExtent height)
    {
        width = ExtentOf(item, visual, vertical: false);
        height = ExtentOf(item, visual, vertical: true);

        var bandWidth = double.IsInfinity(available.Width) ? 0 : available.Width;
        var fillHint = double.IsInfinity(available.Height) ? height.Min : Math.Max(height.Min, available.Height);

        visual.Measure(new Size(width.MeasureHint(bandWidth), height.MeasureHint(fillHint)));
    }

    /// <summary>
    /// 解析一个轴的尺寸口径：**声明优先**（<c>adaptive</c> &gt; <c>fill</c> &gt; <c>pixels</c>），
    /// 未声明 ⇒ 框架缺省（宽度满宽；高度按目录给出的填满档位）。
    /// </summary>
    private static AxisExtent ExtentOf(ISurfaceLayoutItem item, Control visual, bool vertical)
    {
        var axis = vertical ? item.Node.Height : item.Node.Width;
        var max = axis.Max ?? double.PositiveInfinity;
        var min = axis.Min ?? 0;
        var hostFloor = vertical ? item.Metrics.MinHeight : 0;
        var fill = vertical ? item.Metrics.IsFill : true;

        var mode = axis.ResolvedMode;
        if (!vertical && mode is AxisMode.Undeclared)
        {
            // 宽度未声明 ⇒ 框架缺省"满宽"，与引入声明面之前逐像素一致。
            return new AxisExtent(AxisMode.Fill, 0, min, max);
        }

        return mode switch
        {
            AxisMode.Adaptive => new AxisExtent(AxisMode.Adaptive, 0, min, max),
            AxisMode.Pixels => new AxisExtent(AxisMode.Pixels, Math.Max(axis.Pixels ?? 1, 1), min, max),
            AxisMode.Fill => new AxisExtent(AxisMode.Fill, 0, Math.Max(min, hostFloor), max),
            _ => fill
                ? new AxisExtent(AxisMode.Fill, 0, Math.Max(min, hostFloor), max)
                : new AxisExtent(AxisMode.Adaptive, 0, min, max),
        };
    }

    /// <summary>一个轴解析后的尺寸口径（声明优先；未声明 ⇒ 框架缺省）。</summary>
    /// <param name="Mode">口径（<c>pixels</c> / <c>adaptive</c> / <c>fill</c>）。</param>
    /// <param name="Pixels">固定像素（仅 <see cref="AxisMode.Pixels"/> 有意义）。</param>
    /// <param name="Min">下限（DIP）。</param>
    /// <param name="Max">上限（DIP；缺省正无穷 = 不夹）。</param>
    private readonly record struct AxisExtent(AxisMode Mode, double Pixels, double Min, double Max)
    {
        /// <summary>是否"填满型"。</summary>
        public bool IsFill => Mode is AxisMode.Fill;

        /// <summary>夹在这个轴的上下限里。</summary>
        /// <param name="value">待夹的值。</param>
        public double Clamp(double value) => Math.Max(Min, Math.Min(Max, value));

        /// <summary>测量时给子控件的约束：<c>pixels</c> 给像素值 · <c>adaptive</c> 给无穷（量内容）· <c>fill</c> 给这一层真给的可用高。</summary>
        /// <param name="fillHint">这一层真给的可用高（调用方按"可见空间"给；无穷才退回 <c>Min</c>）。</param>
        /// <remarks>
        /// <para>
        /// <b>填满型的提示必须是"真给的可用高"</b>，不能是"下限"：给下限（通常 0）会让它在测量里
        /// 一律报 0 高，而排布那一遍又按余量分给它一大块 —— 测量与排布两套口径，
        /// 于是"谁吃余量"只能去读上一次的 <c>DesiredSize</c> 缓存，形成跨遍漂移。
        /// </para>
        /// <para>
        /// 给可用高同时解决两件事：<b>①</b> 填满型量到的是"它在这个空间里真实的占位"，
        /// 与排布一致；<b>②</b> 需要虚拟化的列表（日志）不会以为空间无限而把内容全实例化 ——
        /// 它拿到的仍是一个有限的上限。
        /// </para>
        /// </remarks>
        public double MeasureHint(double fillHint) => Mode switch
        {
            AxisMode.Pixels => Pixels,
            AxisMode.Adaptive => double.PositiveInfinity,
            _ => double.IsInfinity(fillHint) ? double.PositiveInfinity : fillHint,
        };

        /// <summary>
        /// 网格里量某一格时的提示：**声明了固定像素就给那个像素**，否则交给 <see cref="MeasureHint"/>。
        /// </summary>
        /// <param name="fillHint">没有固定像素时的提示（固定轨道给轨道尺寸，非固定轨道给 0 —— 见调用处）。</param>
        /// <returns>测量约束。</returns>
        /// <remarks>
        /// 网格的轨道尺寸在摸底那一遍还没定，所以非固定轨道只能用"上不封顶"问元素想要多大 ——
        /// 但**声明了固定像素的元素不需要问**：它的尺寸是作者写死的，给无穷只会让"填满型"的兄弟
        /// 按内容虚报出一个巨大的高度，再把轨道撑开、把别的行挤出视口。
        /// </remarks>
        public double GridHint(double fillHint) => Mode is AxisMode.Pixels ? Clamp(Pixels) : MeasureHint(fillHint);

        /// <summary>
        /// 网格轨道在这一轴上的**下限**。
        /// </summary>
        /// <param name="content">内容量到的尺寸。</param>
        /// <remarks>
        /// <b>只有"按内容"档认内容</b>：<c>pixels</c> 认声明值，<c>fill</c> **只认声明的下限** ——
        /// 同一个声明在 `flow` / `grid` 下必须是**同一个地板**（"尺寸不由内容决定"这条纪律在两种容器里同等成立；
        /// 要让轨道跟着内容走，元素该写 `adaptive`，那是 `auto` 轨道的定义）。
        /// </remarks>
        public double Minimum(double content) => Mode switch
        {
            AxisMode.Pixels => Clamp(Pixels),
            AxisMode.Adaptive => Clamp(content),
            _ => Clamp(Min),
        };

        /// <summary>
        /// 排布时实际占的尺寸。
        /// </summary>
        /// <param name="content">内容量到的尺寸。</param>
        /// <param name="available">可用空间。</param>
        /// <remarks>
        /// <b>`pixels` 档只认声明值，绝不被 <paramref name="available"/> 覆盖。</b>
        /// 声明了固定像素就是"作者说了算"：`grid` 的某一列比它宽时（`auto` 列由同列最宽的孩子决定、
        /// 且跨行共享），若这里回退成 `available`，实得尺寸就变成**列宽**而不是声明值 ——
        /// 屏幕上表现为"声明 72 的元素占了 226"，而声明面里看不出任何异常。
        /// 与"`Align` 缺省 `Stretch`"也不冲突：`Stretch` 说的是"没声明尺寸时铺满格子"，
        /// 声明了 `pixels` 就不是"没声明"。
        /// </remarks>
        public double Arranged(double content, double available) => Mode switch
        {
            AxisMode.Pixels => Clamp(Pixels),
            AxisMode.Adaptive => Clamp(content),
            _ => Clamp(available),
        };
    }

    /// <summary>网格的一格：项 + 子控件 + 它落在哪个格子（跨几格）。</summary>
    private sealed record GridItem(ISurfaceLayoutItem Item, Control Visual, int Column, int Row, int ColumnSpan, int RowSpan);

    /// <summary>网格算完的结果（尺寸 + 每个子控件的最终矩形）。</summary>
    private sealed record GridPlan(double Width, double Height, IReadOnlyList<(Control Child, Rect Rect)> Rects);

    /// <summary>该节点是否声明了几何（绝对坐标模式据此分派）。</summary>
    private static bool HasGeometry(ViewNode node) =>
        node.X != 0 || node.Y != 0 || node.Width.Pixels is not null || node.Height.Pixels is not null;

    /// <summary>**声明区**：所有声明了几何的子元素构成的包围盒（没有 ⇒ 空矩形）。</summary>
    private Rect DesignBox()
    {
        var box = default(Rect);
        var any = false;

        foreach (var item in Items)
        {
            if (VisualOf(item) is not { IsVisible: true } || !HasGeometry(item.Node))
            {
                continue;
            }

            var node = item.Node;
            if (!any)
            {
                box = new Rect(node.X, node.Y, node.Width.Pixels ?? 0, node.Height.Pixels ?? 0);
                any = true;
                continue;
            }

            var left = Math.Min(box.X, node.X);
            var top = Math.Min(box.Y, node.Y);
            box = new Rect(
                left,
                top,
                Math.Max(box.Right, node.X + (node.Width.Pixels ?? 0)) - left,
                Math.Max(box.Bottom, node.Y + (node.Height.Pixels ?? 0)) - top);
        }

        return box;
    }

    /// <summary>缩放比例 = 面板实得尺寸 ÷ 声明包围盒（某一维无穷大或声明为 0 ⇒ 该维不缩放）。</summary>
    private static (double X, double Y) ScaleFactors(Rect box, Size target)
    {
        var scaleX = box.Right > 0 && !double.IsInfinity(target.Width) ? target.Width / box.Right : 1;
        var scaleY = box.Bottom > 0 && !double.IsInfinity(target.Height) ? target.Height / box.Bottom : 1;
        return (Math.Max(scaleX, 0), Math.Max(scaleY, 0));
    }

    /// <summary>解析元素的实际尺寸（只有声明了 `scaleWithWindow` 的元素按比例缩放），上下限夹住且不小于 1×1。</summary>
    private static Size ResolveSize(ViewNode node, double scaleX, double scaleY)
    {
        var scale = node.ScaleWithWindow;
        var width = (node.Width.Pixels ?? 0) * (scale ? scaleX : 1);
        var height = (node.Height.Pixels ?? 0) * (scale ? scaleY : 1);

        if (node.Width.Min is { } minWidth)
        {
            width = Math.Max(width, minWidth);
        }

        if (node.Width.Max is { } maxWidth)
        {
            width = Math.Min(width, maxWidth);
        }

        if (node.Height.Min is { } minHeight)
        {
            height = Math.Max(height, minHeight);
        }

        if (node.Height.Max is { } maxHeight)
        {
            height = Math.Min(height, maxHeight);
        }

        return new Size(Math.Max(width, 1), Math.Max(height, 1));
    }
}
