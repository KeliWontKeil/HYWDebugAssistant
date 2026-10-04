using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// 模块内容区里**元素列表**的排布面板。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：普通 `StackPanel`（只按内容高度排）有两个问题——
/// ① <b>显示区（A5）随行数变长</b>：内容一多就把模块内容撑长，外层滚动条动、显示区自己的滚动条不动
/// （应该是显示区自己出滚动条，而不是变布局）；
/// ② <b>多行输入框的高度不随窗口拉伸</b>：拉大窗口时只有宽度变，高度不跟着变。
/// </para>
/// <para>
/// <b>规则</b>（"要填满的" vs "按内容高"）：
/// <list type="bullet">
///   <item><b>填满型</b>：A5 文本日志 + **多行**文本输入 —— 它们分走"剩余高度"，内部出滚动条；</item>
///   <item><b>按内容型</b>：其余元素（数值 / 曲线 / 按钮 / 单行输入 / 开关 / 标签条…）—— 高度 = 内容高度；</item>
///   <item><b>最小高度</b>：所有填满型都有一个下限（日志 88 DIP = 既有视觉取值；多行输入 = 其
///     <c>EditorMinHeight</c>）⇒ 窗口太小时由**外层 ScrollViewer** 滚动，而不是把填满型压没。</item>
/// </list>
/// </para>
/// <para>
/// <b>间距</b>：<see cref="Spacing"/> 是**给开发者调的**（当前值写在 `ModuleContentView.axaml` 里）——
/// 位置观感问题改那里即可，不用改代码。
/// </para>
/// </remarks>
public sealed class ModuleElementPanel : Panel
{
    /// <summary>A5 文本日志的最小高度（DIP）—— 与既有视觉取值一致（`architecture/07`）。</summary>
    public const double LogMinHeight = 88;

    /// <summary>元素之间间距的**默认值**（DIP）—— 也是容器内部排布间距的默认值。</summary>
    public const double DefaultSpacing = 2;

    /// <summary>元素之间的间距（可调；默认 2）。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ModuleElementPanel, double>(nameof(Spacing), DefaultSpacing);

    /// <summary>元素之间的间距（DIP）。</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>排布模式的默认值（流式 = 引入 `<c>view.json</c>` 之前的唯一行为）。</summary>
    public const ViewLayoutMode DefaultLayoutMode = ViewLayoutMode.Flow;

    /// <summary>
    /// 本面板这一层的**排布模式**；由模板绑到作用域自己的声明上
    /// （容器内层面板绑容器视图模型，根面板绑模块视图模型）。
    /// </summary>
    public static readonly StyledProperty<ViewLayoutMode> LayoutModeProperty =
        AvaloniaProperty.Register<ModuleElementPanel, ViewLayoutMode>(nameof(LayoutMode), DefaultLayoutMode);

    /// <summary>本面板这一层的排布模式。</summary>
    public ViewLayoutMode LayoutMode
    {
        get => GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    /// <summary>越界处理的默认值（交给外层滚动 —— 模块内容区始终在滚动）。</summary>
    public const ViewLayoutOverflow DefaultOverflow = ViewLayoutOverflow.Scroll;

    /// <summary>本面板这一层在绝对坐标越界时的处理（`scroll` 交外层 / `clip` 裁剪且不产生滚动范围）。</summary>
    public static readonly StyledProperty<ViewLayoutOverflow> OverflowProperty =
        AvaloniaProperty.Register<ModuleElementPanel, ViewLayoutOverflow>(nameof(Overflow), DefaultOverflow);

    /// <summary>本面板这一层的越界处理。</summary>
    public ViewLayoutOverflow Overflow
    {
        get => GetValue(OverflowProperty);
        set => SetValue(OverflowProperty, value);
    }

    /// <summary>网格模式的列规格（缺省 <see langword="null"/> ⇒ 单列 <c>*</c> = 满宽一列）。</summary>
    public static readonly StyledProperty<IReadOnlyList<GridTrack>?> ColumnsProperty =
        AvaloniaProperty.Register<ModuleElementPanel, IReadOnlyList<GridTrack>?>(nameof(Columns));

    /// <summary>网格模式的列规格（`*` / `2*` / `auto` / 固定 DIP）。</summary>
    public IReadOnlyList<GridTrack>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>网格模式的行规格（缺省 <see langword="null"/> ⇒ 行数由元素推断、每行按内容）。</summary>
    public static readonly StyledProperty<IReadOnlyList<GridTrack>?> RowsProperty =
        AvaloniaProperty.Register<ModuleElementPanel, IReadOnlyList<GridTrack>?>(nameof(Rows));

    /// <summary>网格模式的行规格。</summary>
    public IReadOnlyList<GridTrack>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>网格列间距（缺省 <see cref="double.NaN"/> ⇒ 用 <see cref="Spacing"/>）。</summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ModuleElementPanel, double>(nameof(ColumnSpacing), double.NaN);

    /// <summary>网格列间距（DIP）。</summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>网格行间距（缺省 <see cref="double.NaN"/> ⇒ 用 <see cref="Spacing"/>）。</summary>
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ModuleElementPanel, double>(nameof(RowSpacing), double.NaN);

    /// <summary>网格行间距（DIP）。</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>实际列规格（没声明 ⇒ 单列 <c>*</c>）。</summary>
    private IReadOnlyList<GridTrack> EffectiveColumns => Columns is { Count: > 0 } columns ? columns : DefaultColumns;

    /// <summary>实际行规格（没声明 ⇒ 空 ⇒ 行数由元素推断、每行按内容）。</summary>
    private IReadOnlyList<GridTrack> EffectiveRows => Rows ?? [];

    /// <summary>实际列间距。</summary>
    private double EffectiveColumnSpacing => double.IsNaN(ColumnSpacing) ? Spacing : Math.Max(0, ColumnSpacing);

    /// <summary>实际行间距。</summary>
    private double EffectiveRowSpacing => double.IsNaN(RowSpacing) ? Spacing : Math.Max(0, RowSpacing);

    /// <summary>单列缺省规格。</summary>
    private static readonly GridTrack[] DefaultColumns = [GridTrack.Star(1)];

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // `clip` ⇒ 面板自己裁掉超出部分；高度也不向外报（外层因此不会出现滚动条）。
        ClipToBounds = Overflow == ViewLayoutOverflow.Clip;

        return LayoutMode switch
        {
            ViewLayoutMode.Absolute => MeasureAbsolute(availableSize),
            ViewLayoutMode.Grid => MeasureGrid(availableSize),
            _ => MeasureFlow(availableSize),
        };
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize) => LayoutMode switch
    {
        ViewLayoutMode.Absolute => ArrangeAbsolute(finalSize),
        ViewLayoutMode.Grid => ArrangeGrid(finalSize),
        _ => ArrangeFlow(finalSize),
    };

    /// <summary>流式（缺省）：单列满宽、自上而下；余量等分给"填满型"。</summary>
    private Size MeasureFlow(Size availableSize)
    {
        var bandWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var needed = 0.0;
        var visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            // 填满型按**最小高度**测量（有限视口！）—— 这一点很关键：
            //   · 用无限高测量的话，日志的 `ListBox` 会以为空间无限 ⇒ **虚拟化失效**（把 2 000 行全实例化，
            //     实测抓到）；有限视口下它只实例化可见的几行；
            //   · 其余元素用无限高测量，报自己的内容高度。
            // 尺寸一律**按声明驱动**（`view.json` 的 width/height 三优先级）：`pixels` 给像素值、
            // `adaptive` 给无穷（量内容）、`fill` 给下限。
            var width = WidthOf(child);
            var height = HeightOf(child);

            child.Measure(new Size(width.MeasureHint(bandWidth), height.MeasureHint(height.Min)));

            needed += visible++ > 0 ? Spacing : 0;
            needed += height.Needed(child.DesiredSize.Height);
        }

        // 面板的期望高度 = **最小布局高度**：窗口更大 ⇒ Arrange 把余量给填满型；更小 ⇒ 外层滚动条接管
        return new Size(bandWidth, ClampExtent(needed, availableSize.Height));
    }

    /// <summary>流式排布（缺省）：单列满宽、自上而下。</summary>
    private Size ArrangeFlow(Size finalSize)
    {
        var fillCount = 0;
        var fixedHeight = 0.0;
        var visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            fixedHeight += visible++ > 0 ? Spacing : 0;

            var height = HeightOf(child);
            if (height.IsFill)
            {
                fillCount++;
            }
            else
            {
                fixedHeight += height.Needed(child.DesiredSize.Height);
            }
        }

        // 余量在"填满型"之间**等分**；没有填满型时余量留在底部（内容按内容高排，保持既有观感）
        var each = fillCount > 0 ? Math.Max(0, finalSize.Height - fixedHeight) / fillCount : 0;
        var y = 0.0;
        visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            y += visible++ > 0 ? Spacing : 0;

            var width = WidthOf(child);
            var height = HeightOf(child);
            var actualHeight = height.Arranged(child.DesiredSize.Height, each);
            var actualWidth = width.Arranged(child.DesiredSize.Width, finalSize.Width);

            // 非填满的宽度按声明的停靠对齐（`stretch` 与"声明了宽度"冲突时以宽度为准 ⇒ 等同靠左）
            var x = OffsetOf(BoundsOf(child)?.Align ?? CellAlign.Stretch, finalSize.Width, actualWidth);

            child.Arrange(new Rect(x, y, actualWidth, actualHeight));
            y += actualHeight;
        }

        // 实占高度可能超过 finalSize（窗口太小时各填满型仍守住最小高度）⇒ 让外层 ScrollViewer 知道真实高度
        return new Size(finalSize.Width, ClampExtent(Math.Max(finalSize.Height, y), finalSize.Height));
    }

    /// <summary>
    /// 绝对坐标：**声明了几何**的元素按自己的 X/Y/宽/高摆放，**没声明几何**的元素按流式接在声明区之下
    /// （不隐形、不重叠 —— 让"漏了一条"表现为"排在下面"，而不是变成一个看不见的控件）。
    /// </summary>
    /// <remarks>
    /// 位置单位是 DIP、相对本面板的客户区；<c>scaleWithWindow</c> 的元素按"面板实得尺寸 ÷ 声明包围盒"缩放，
    /// 其余元素钉死（窗口变大只是留白更多）。填满型在绝对坐标里**按最小高度**测量（同流式的纪律：
    /// 有限视口，日志的虚拟化不失效）。
    /// </remarks>
    private Size MeasureAbsolute(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var box = DesignBox();
        var (scaleX, scaleY) = ScaleFactors(box, availableSize);
        var bottom = 0.0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (BoundsOf(child) is { } bounds)
            {
                var size = ResolveSize(bounds, scaleX, scaleY);
                var y = bounds.Y * (bounds.ScaleWithWindow ? scaleY : 1);

                child.Measure(size);
                bottom = Math.Max(bottom, y + size.Height);
                continue;
            }

            child.Measure(IsFill(child)
                ? new Size(width, MinHeightOf(child))
                : new Size(width, double.PositiveInfinity));

            bottom += Spacing;
            bottom += IsFill(child) ? MinHeightOf(child) : child.DesiredSize.Height;
        }

        return new Size(width, ClampExtent(bottom, availableSize.Height));
    }

    /// <summary>绝对坐标排布（声明区的底边之下留给"没声明几何"的元素，按流式接上）。</summary>
    private Size ArrangeAbsolute(Size finalSize)
    {
        var box = DesignBox();
        var (scaleX, scaleY) = ScaleFactors(box, finalSize);
        var bottom = 0.0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            if (BoundsOf(child) is { } bounds)
            {
                var scale = bounds.ScaleWithWindow;
                var size = ResolveSize(bounds, scaleX, scaleY);
                var x = bounds.X * (scale ? scaleX : 1);
                var y = bounds.Y * (scale ? scaleY : 1);

                child.Arrange(new Rect(x, y, size.Width, size.Height));
                bottom = Math.Max(bottom, y + size.Height);
                continue;
            }

            var height = IsFill(child) ? Math.Max(MinHeightOf(child), 1) : child.DesiredSize.Height;
            var top = bottom + Spacing;

            child.Arrange(new Rect(0, top, finalSize.Width, height));
            bottom = top + height;
        }

        return new Size(finalSize.Width, ClampExtent(bottom, finalSize.Height));
    }

    /// <summary>
    /// 网格（`TableLayoutPanel` 语义）：列/行轨道 + 元素落在格子里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>轨道规格</b>：<c>*</c>（等分剩余）· <c>2*</c>（按权重分）· <c>auto</c>（按内容）· 固定 DIP
    /// （<c>columns</c>/<c>rows</c>）。元素用 <c>column</c>/<c>row</c>（0 起）指定格子；不写就按**行优先**
    /// 自动放置，并跳过已占用的格子。
    /// </para>
    /// <para>
    /// <b>余量只给 `*` 行</b>：测量时 `*` 行按**内容最小高度**报（填满型按自己的最小高度 ⇒ 有限视口，
    /// 日志/多行输入的虚拟化不失效），排布时按实得高度撑开 ⇒ 与流式同一条纪律：窗口太小由外层滚动条接管，
    /// 窗口更大则 `*` 行（= 显示区）跟着变高。
    /// </para>
    /// <para>
    /// <b>显式尺寸优先</b>：元素声明了 <c>width</c>/<c>height</c> 时按声明尺寸排，<c>align</c>/<c>verticalAlign</c>
    /// 决定它在格子里的停靠（`stretch` 与显式尺寸冲突时以尺寸为准 ⇒ 等同靠左/靠上）。
    /// </para>
    /// </remarks>
    private Size MeasureGrid(Size availableSize)
    {
        var plan = BuildGrid(availableSize, final: false);
        return new Size(plan.Width, ClampExtent(plan.Height, availableSize.Height));
    }

    /// <summary>网格排布（`*` 行在这里按实得高度撑开）。</summary>
    private Size ArrangeGrid(Size finalSize)
    {
        var plan = BuildGrid(finalSize, final: true);

        foreach (var (child, rect) in plan.Rects)
        {
            child.Measure(new Size(rect.Width, rect.Height));
            child.Arrange(rect);
        }

        return new Size(finalSize.Width, ClampExtent(plan.Height, finalSize.Height));
    }

    /// <summary>算一遍网格（测量与排布共用；<paramref name="final"/> = 是否给出最终矩形）。</summary>
    private GridPlan BuildGrid(Size availableSize, bool final)
    {
        var columns = EffectiveColumns;
        var declaredRows = EffectiveRows;
        var columnCount = Math.Max(1, columns.Count);
        var rowCount = declaredRows.Count;
        var columnSpacing = EffectiveColumnSpacing;
        var rowSpacing = EffectiveRowSpacing;
        var availableWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var availableHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        // ---- ① 分格：显式指定优先，其余按行优先自动放置（跳过已占用格） ----
        var pending = new List<(Control Child, ViewNode? Bounds)>();
        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                pending.Add((child, BoundsOf(child)));
            }
        }

        var items = new List<GridItem>();
        var occupied = new HashSet<(int Column, int Row)>();

        foreach (var (child, bounds) in pending)
        {
            if (bounds?.Column is null && bounds?.Row is null)
            {
                continue;
            }

            var column = Math.Clamp(bounds!.Column ?? 0, 0, columnCount - 1);
            var row = Math.Max(0, bounds.Row ?? 0);
            var columnSpan = Math.Clamp(bounds.ColumnSpan, 1, columnCount - column);
            var rowSpan = Math.Max(1, bounds.RowSpan);

            rowCount = Math.Max(rowCount, row + rowSpan);
            items.Add(new GridItem(child, bounds, column, row, columnSpan, rowSpan));
            OccupyCell(occupied, column, row, columnSpan, rowSpan);
        }

        var cursor = 0;
        foreach (var (child, bounds) in pending)
        {
            if (bounds?.Column is not null || bounds?.Row is not null)
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
            items.Add(new GridItem(child, bounds, column, row, 1, 1));
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

        // ---- ③ 量一遍子元素（固定轨道给轨道尺寸；`*`/`auto` 给内容尺寸，填满型给有限的最小高度） ----
        var desired = new Size[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var childWidth = WidthOf(item.Child);
            var childHeight = HeightOf(item.Child);
            var widthHint = childWidth.MeasureHint(
                TrackKind(columns, item.Column, starDefault: true) == GridTrackKind.Fixed
                    ? columnWidths[item.Column]
                    : double.PositiveInfinity);
            var heightHint = childHeight.MeasureHint(
                TrackKind(declaredRows, item.Row, starDefault: false) == GridTrackKind.Fixed
                    ? rowHeights[item.Row]
                    : childHeight.Min);

            item.Child.Measure(new Size(widthHint, heightHint));
            desired[i] = item.Child.DesiredSize;
        }

        // ---- ④ 内容下限：单列/单行子元素决定 auto 轨道尺寸、也给 `*` 轨道一个下限 ----
        var autoColumns = 0.0;
        var starColumnMinimums = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var kind = TrackKind(columns, item.Column, starDefault: true);
            if (item.ColumnSpan != 1 || kind == GridTrackKind.Fixed)
            {
                continue;
            }

            // 声明了宽度的元素**以声明值为下限**：它在测量时可能报出更宽的内容尺寸（如"标题 + 开关"那一行），
            // 那不该把 `*` 列撑开 —— 显式尺寸是模块作者说了算（显式声明优先）。
            var minimum = WidthOf(item.Child).Minimum(desired[i].Width);
            columnWidths[item.Column] = Math.Max(columnWidths[item.Column], minimum);
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

        var autoRows = 0.0;
        var starRowMinimums = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var kind = TrackKind(declaredRows, item.Row, starDefault: false);
            if (item.RowSpan != 1 || kind == GridTrackKind.Fixed)
            {
                continue;
            }

            // 同理：像素口径以声明值为准；填满/自适应仍要保证不小于下限与内容高度
            var need = HeightOf(item.Child).Minimum(desired[i].Height);
            rowHeights[item.Row] = Math.Max(rowHeights[item.Row], need);
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

        if (starRowWeight > 0)
        {
            for (var i = 0; i < rowCount; i++)
            {
                if (TrackKind(declaredRows, i, starDefault: false) == GridTrackKind.Star)
                {
                    rowHeights[i] += rowRemaining * (TrackWeight(declaredRows, i) / starRowWeight);
                }
            }
        }

        // ---- ⑤ 汇总尺寸（`*` 行没拿到余量时就是内容最小高度 ⇒ 交给外层滚动条） ----
        var contentWidth = columnWidths.Sum() + columnSpacings;
        var contentHeight = rowHeights.Sum() + rowSpacings;
        var width = final || availableWidth <= 0 ? contentWidth : availableWidth;
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
            var item = items[i];
            var cellWidth = columnSpacing * (item.ColumnSpan - 1);
            for (var c = item.Column; c < item.Column + item.ColumnSpan; c++)
            {
                cellWidth += columnWidths[c];
            }

            var cellHeight = rowSpacing * (item.RowSpan - 1);
            for (var r = item.Row; r < item.Row + item.RowSpan; r++)
            {
                cellHeight += rowHeights[r];
            }

            var itemWidth = WidthOf(item.Child).Arranged(desired[i].Width, cellWidth);
            var itemHeight = HeightOf(item.Child).Arranged(desired[i].Height, cellHeight);
            var offsetX = OffsetOf(item.Bounds?.Align ?? CellAlign.Stretch, cellWidth, itemWidth);
            var offsetY = OffsetOf(item.Bounds?.VerticalAlign ?? CellAlign.Stretch, cellHeight, itemHeight);

            rects.Add((item.Child, new Rect(
                offsetsX[item.Column] + offsetX,
                offsetsY[item.Row] + offsetY,
                itemWidth,
                itemHeight)));
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

    /// <summary>网格的一格：子控件 + 它落在哪个格子（跨几格）。</summary>
    private sealed record GridItem(Control Child, ViewNode? Bounds, int Column, int Row, int ColumnSpan, int RowSpan);

    /// <summary>网格算完的结果（尺寸 + 每个子控件的最终矩形）。</summary>
    private sealed record GridPlan(double Width, double Height, IReadOnlyList<(Control Child, Rect Rect)> Rects);

    /// <summary>`overflow: clip` ⇒ 不向外报溢出高度（不产生滚动条）；缺省 `scroll` 照旧报真实高度。</summary>
    private double ClampExtent(double extent, double available) =>
        Overflow == ViewLayoutOverflow.Clip && !double.IsInfinity(available) && available > 0
            ? Math.Min(extent, available)
            : extent;

    /// <summary>子元素的声明节点（没有视图模型 ⇒ <see langword="null"/>；节点自带全部布局事实）。</summary>
    private static ViewNode? BoundsOf(Control child) =>
        (child.DataContext as ViewElementViewModel)?.Node;

    /// <summary>
    /// 解析**宽度**轴的口径：**声明优先**（`adaptive` &gt; `fill` &gt; `pixels`），未声明 ⇒ 框架缺省"满宽"。
    /// </summary>
    private static AxisExtent WidthOf(Control child)
    {
        var axis = BoundsOf(child)?.Width;
        var max = axis?.Max ?? double.PositiveInfinity;
        var min = axis?.Min ?? 0;

        return axis?.ResolvedMode switch
        {
            AxisMode.Adaptive => new AxisExtent(AxisMode.Adaptive, 0, min, max),
            AxisMode.Pixels => new AxisExtent(AxisMode.Pixels, Math.Max(axis!.Pixels ?? 1, 1), min, max),
            AxisMode.Fill => new AxisExtent(AxisMode.Fill, 0, min, max),
            _ => new AxisExtent(AxisMode.Fill, 0, 0, max),
        };
    }

    /// <summary>
    /// 解析**高度**轴的口径：**声明优先**，未声明 ⇒ 框架缺省（日志/多行输入/内含填满型的容器"填满"并带
    /// 框架下限 <see cref="MinHeightOf(Control)"/>，其余随内容）。
    /// </summary>
    private static AxisExtent HeightOf(Control child)
    {
        var axis = BoundsOf(child)?.Height;
        var mode = axis?.ResolvedMode ?? AxisMode.Undeclared;
        var max = axis?.Max ?? double.PositiveInfinity;

        return mode switch
        {
            AxisMode.Adaptive => new AxisExtent(AxisMode.Adaptive, 0, axis?.Min ?? 0, max),
            AxisMode.Pixels => new AxisExtent(AxisMode.Pixels, Math.Max(axis!.Pixels ?? 1, 1), axis.Min ?? 0, max),
            AxisMode.Fill => new AxisExtent(AxisMode.Fill, 0, axis!.Min ?? MinHeightOf(child), max),
            _ => IsFill(child)
                ? new AxisExtent(AxisMode.Fill, 0, MinHeightOf(child), max)
                : new AxisExtent(AxisMode.Adaptive, 0, 0, max),
        };
    }

    /// <summary>一个轴解析后的尺寸口径（声明优先；未声明 ⇒ 框架缺省）。</summary>
    /// <param name="Mode">口径（<c>pixels</c> / <c>adaptive</c> / <c>fill</c>）。</param>
    /// <param name="Pixels">固定像素（仅 <see cref="AxisMode.Pixels"/> 有意义）。</param>
    /// <param name="Min">下限（DIP）。</param>
    /// <param name="Max">上限（DIP；缺省正无穷 = 不夹）。</param>
    private readonly record struct AxisExtent(AxisMode Mode, double Pixels, double Min, double Max)
    {
        /// <summary>是否"填满型"（网格里填满格子、流式里分走剩余高度）。</summary>
        public bool IsFill => Mode == AxisMode.Fill;

        /// <summary>夹在这个轴的上下限里。</summary>
        public double Clamp(double value) => Math.Max(Min, Math.Min(Max, value));

        /// <summary>测量时给子控件的约束：<c>pixels</c> 给像素值 · <c>adaptive</c> 给无穷（量内容）· <c>fill</c> 给指定值。</summary>
        public double MeasureHint(double fillHint) => Mode switch
        {
            AxisMode.Pixels => Pixels,
            AxisMode.Adaptive => double.PositiveInfinity,
            _ => fillHint,
        };

        /// <summary>"需要多少空间"里的占位：<c>pixels</c> 给像素值 · <c>adaptive</c> 给内容 · <c>fill</c> 只认下限。</summary>
        public double Needed(double content) => Mode switch
        {
            AxisMode.Pixels => Clamp(Pixels),
            AxisMode.Adaptive => Clamp(content),
            _ => Min,
        };

        /// <summary>网格轨道的内容下限：<c>pixels</c> 认声明值（显式尺寸说了算）· 其余认内容、至少到下限。</summary>
        public double Minimum(double content) =>
            Mode == AxisMode.Pixels ? Clamp(Pixels) : Clamp(Math.Max(Min, content));

        /// <summary>排布时实际占的尺寸：<c>pixels</c> 固定 · <c>adaptive</c> 随内容 · <c>fill</c> 填满给定空间。</summary>
        public double Arranged(double content, double available) => Mode switch
        {
            AxisMode.Pixels => Clamp(Pixels),
            AxisMode.Adaptive => Clamp(content),
            _ => Clamp(available),
        };
    }

    /// <summary>**声明区**：所有声明了几何的子元素构成的包围盒（没有 ⇒ 空矩形）。</summary>
    private Rect DesignBox()
    {
        var box = default(Rect);
        var any = false;

        foreach (var child in Children)
        {
            if (!child.IsVisible || BoundsOf(child) is not { } bounds)
            {
                continue;
            }

            if (!any)
            {
                box = new Rect(bounds.X, bounds.Y, bounds.Width.Pixels ?? 0, bounds.Height.Pixels ?? 0);
                any = true;
                continue;
            }

            var left = Math.Min(box.X, bounds.X);
            var top = Math.Min(box.Y, bounds.Y);
            box = new Rect(
                left,
                top,
                Math.Max(box.Right, bounds.X + (bounds.Width.Pixels ?? 0)) - left,
                Math.Max(box.Bottom, bounds.Y + (bounds.Height.Pixels ?? 0)) - top);
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
    private static Size ResolveSize(ViewNode bounds, double scaleX, double scaleY)
    {
        var scale = bounds.ScaleWithWindow;
        var width = (bounds.Width.Pixels ?? 0) * (scale ? scaleX : 1);
        var height = (bounds.Height.Pixels ?? 0) * (scale ? scaleY : 1);

        if (bounds.Width.Min is { } minWidth)
        {
            width = Math.Max(width, minWidth);
        }

        if (bounds.Width.Max is { } maxWidth)
        {
            width = Math.Min(width, maxWidth);
        }

        if (bounds.Height.Min is { } minHeight)
        {
            height = Math.Max(height, minHeight);
        }

        if (bounds.Height.Max is { } maxHeight)
        {
            height = Math.Min(height, maxHeight);
        }

        return new Size(Math.Max(width, 1), Math.Max(height, 1));
    }

    /// <summary>
    /// 是否"填满型"（A5 文本日志 · 多行文本输入 · **内含填满型的容器**）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **填满性要"透传"**：容器自己不做填满，
    /// 但它内部（含任意层嵌套）有填满型时，**容器必须代替那个内部元素**参与父层的余量分配 ——
    /// 否则多行输入框会被"内容高度"钉死、拉大窗口不再变高（视觉回退，既有断言会红）。
    /// </para>
    /// <para>
    /// **折叠区收起时不透传**：收起后内容不可见，容器只报标题行的高度（由 `Expander` 的 `DesiredSize` 给出）。
    /// </para>
    /// </remarks>
    private static bool IsFill(Control child) => child.DataContext switch
    {
        TextLogElementViewModel => true,
        TextInputElementViewModel { IsMultiline: true } => true,
        ContainerElementViewModel container => container.IsExpanded && ContainsFill(container),
        _ => false,
    };

    /// <summary>容器内部（含任意层嵌套）是否存在填满型元素。</summary>
    private static bool ContainsFill(ContainerElementViewModel container)
    {
        foreach (var child in container.Children)
        {
            if (child is TextLogElementViewModel
                || child is TextInputElementViewModel { IsMultiline: true }
                || (child is ContainerElementViewModel nested && ContainsFill(nested)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>填满型的**最小高度**。</summary>
    private static double MinHeightOf(Control child) => child.DataContext switch
    {
        TextLogElementViewModel => LogMinHeight,
        TextInputElementViewModel input => Math.Max(input.EditorMinHeight, 1),
        ContainerElementViewModel container => MinHeightOf(container),
        _ => 1,
    };

    /// <summary>
    /// 容器的**最小高度** = 内部各元素最小高度之和 + 内部间距（容器边框/标题的厚度由模板自己报，这里只给下限）。
    /// </summary>
    /// <remarks>
    /// 这个下限有两个作用：① 窗口被压小时容器不塌成 0；② 容器拿到的是**有限高**，
    /// 内层填满型（日志的 `ListBox`）因此仍按有限视口测量，**虚拟化不失效**。
    /// </remarks>
    private static double MinHeightOf(ContainerElementViewModel container)
    {
        var needed = 0.0;
        var first = true;

        foreach (var child in container.Children)
        {
            needed += first ? 0 : DefaultSpacing;
            first = false;

            needed += child switch
            {
                TextLogElementViewModel => LogMinHeight,
                TextInputElementViewModel input => Math.Max(input.EditorMinHeight, 1),
                ContainerElementViewModel nested => MinHeightOf(nested),
                _ => 1,
            };
        }

        return needed;
    }
}
