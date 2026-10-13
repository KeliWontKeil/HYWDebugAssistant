using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Hosting;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **容器控件的公共底座** —— 一个"标题 + 子排布面板"的框。
/// </summary>
/// <remarks>
/// <para>
/// 三种容器（分组框 / 折叠区 / 纯排布容器）共享同一件事：**子项由内层
/// <see cref="ModuleSurfacePanel"/> 排布**，容器自己只管外框（标题、边框、留白）。
/// 「容器不展平」这条语义因此落在这里：内层面板是**另一个排布作用域**，子项的几何相对它算。
/// </para>
/// <para>
/// <b>它不认识具体控件类型</b>，也不取数：子项的可视化由 <see cref="ISurfaceHost.Visual"/> 给。
/// </para>
/// </remarks>
public abstract class SurfaceContainerControl : ContentControl
{
    /// <summary>造一个容器控件。</summary>
    /// <param name="node">容器自己的声明节点（标题、留白、排布键都取自它）。</param>
    /// <param name="children">子项（按声明顺序）。</param>
    /// <param name="host">表面宿主（造子控件用）。</param>
    /// <param name="populate">
    /// 是否**用本节点的声明**把 <paramref name="children"/> 装进 <see cref="Panel"/>（默认是）。
    /// <c>tabs</c> 传 <see langword="false"/>：它的页内容按**页节点**的排布键排。
    /// </param>
    protected SurfaceContainerControl(
        ViewNode node,
        IReadOnlyList<ISurfaceLayoutItem> children,
        ISurfaceHost host,
        bool populate = true)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(host);
        Node = node;
        Panel = new ModuleSurfacePanel();

        if (populate)
        {
            Panel.Populate(node, children, host.Visual);
        }
    }

    /// <summary>容器自己的声明节点。</summary>
    public ViewNode Node { get; }

    /// <summary>内层排布面板（容器的子项在这一层里摆）。</summary>
    protected ModuleSurfacePanel Panel { get; }

    /// <summary>
    /// 容器内容的四周留白（声明面的 <c>padding</c>）。
    /// </summary>
    /// <remarks>
    /// 容器的边框与标题厚度是**外观**（主题给），内容留白是**声明的**（<c>padding</c>）——
    /// 两者不互相推导：模块说留多少就留多少，框画多粗由主题定。
    /// </remarks>
    protected Thickness ContentPadding => new(
        Math.Max(0, Node.Padding),
        Math.Max(0, Node.Padding),
        Math.Max(0, Node.Padding),
        Math.Max(0, Node.Padding));

    /// <summary>造一个带标题的容器外框（标题在上、内容在下；标题为空则不占位置）。</summary>
    /// <param name="title">标题文本（空 = 不画标题）。</param>
    /// <returns>可直接作为 <see cref="ContentControl.Content"/> 的控件。</returns>
    protected Control BuildFramedContent(string title)
    {
        var content = new Border
        {
            Padding = ContentPadding,
            Child = Panel,
        };

        // 边框与圆角由主题给（`SurfaceContainerBorder` / `SurfaceContainerCornerRadius`），
        // 这里只挂类名：换主题不必改控件。
        content.Classes.Add(SurfaceClasses.ContainerFrame);

        if (title.Length == 0)
        {
            return content;
        }

        var header = new TextBlock
        {
            Text = title,
            [!TextBlock.FontSizeProperty] = new DynamicResourceExtension(SurfaceTheme.TitleFontSizeKey),
        };
        header.Classes.Add(SurfaceClasses.ContainerTitle);

        // **两行网格，不是竖排 `StackPanel`**：`StackPanel` 量孩子时给的是**无限高**，
        // 于是内容那一层永远只按内容高 —— 里面任何 `fill` 都吃不到高度，整条链静默失效
        // （实测：`tabs` 声明 `height: fill`、页内文本框声明 `height: fill`，页内容面板仍只有 21 高，
        // 屏幕上就是"文本框塌成一行"）。换成 `Auto` + `*` 两行，内容那一行才真的拿到剩余高。
        // 与 `SurfaceElementControl.BuildChrome` 里"标题 + 主体"同一口径、同一个理由。
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(content, 1);
        grid.Children.Add(header);
        grid.Children.Add(content);

        return grid;
    }
}

/// <summary>
/// **分组框**（<c>group</c>）：一个带标题的框，子项在里面流式排布。
/// </summary>
public sealed class SurfaceGroupControl : SurfaceContainerControl
{
    /// <summary>造一个分组框。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="children">子项。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceGroupControl(ViewNode node, IReadOnlyList<ISurfaceLayoutItem> children, ISurfaceHost host)
        : base(node, children, host)
    {
        Content = BuildFramedContent(node.Title);
    }
}

/// <summary>
/// **纯排布容器**（<c>container</c>）：不画框、不带标题，只提供一个排布作用域。
/// </summary>
/// <remarks>
/// 它存在的唯一理由是"另起一层排布"（<c>layout</c> / <c>columns</c> / <c>spacing</c> 与父层不同）
/// ——所以它**一个像素的装饰都不加**，内容留白就是声明面的 <c>padding</c>。
/// </remarks>
public sealed class SurfaceLayoutControl : SurfaceContainerControl
{
    /// <summary>造一个纯排布容器。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="children">子项。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceLayoutControl(ViewNode node, IReadOnlyList<ISurfaceLayoutItem> children, ISurfaceHost host)
        : base(node, children, host)
    {
        Content = new Border { Padding = ContentPadding, Child = Panel };
    }
}

/// <summary>
/// **折叠区**（<c>collapsible</c>）：可收起的分组框；收起时**不占高、也不向父层要高度**。
/// </summary>
/// <remarks>
/// 折叠态本身由 <see cref="SurfaceStateSlot.Collapsed"/> 存在框架状态里（用户拨过就以用户为准），
/// 而"折叠着不占高"是**排布**的事 —— 由 <see cref="ISurfaceLayoutItem.IsExpanded"/> 告诉引擎。
/// 宿主在 <see cref="ExpandedChanged"/> 时两件事都做。
/// </remarks>
public sealed class SurfaceCollapsibleControl : SurfaceContainerControl
{
    private readonly Expander _expander;

    /// <summary>造一个折叠区。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="children">子项。</param>
    /// <param name="host">表面宿主。</param>
    /// <param name="expanded">初始展开态（用户拨过则以状态存储里的值为准，由宿主给）。</param>
    public SurfaceCollapsibleControl(
        ViewNode node,
        IReadOnlyList<ISurfaceLayoutItem> children,
        ISurfaceHost host,
        bool expanded)
        : base(node, children, host)
    {
        _expander = new Expander
        {
            Header = node.Title,
            Content = new Border { Padding = ContentPadding, Child = Panel },
            IsExpanded = expanded,
        };

        _expander.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty)
            {
                ExpandedChanged?.Invoke(this, _expander.IsExpanded);
            }
        };

        Content = _expander;
    }

    /// <summary>展开态变了（用户拨的）。</summary>
    public event EventHandler<bool>? ExpandedChanged;

    /// <summary>当前展开态。</summary>
    public bool IsExpanded => _expander.IsExpanded;
}

/// <summary>
/// **标签页**（<c>tabs</c>）：一条页签 + 当前页内容。
/// </summary>
/// <remarks>
/// <para>
/// <b>切页只换内容，不重建控件树</b> —— 这是新机制相对旧实现的一处修正：旧实现"切页 = 重建整棵元素表"，
/// 于是别的元素的焦点与滚动位置会跟着丢。这里内层面板是**同一个实例**，只把它的子项换掉
/// （用户的滚动位置由外层滚动条保持）。
/// </para>
/// <para>
/// 页节点本身**不渲染**（它就是"一页的内容"），页标题只作页签文字。
/// </para>
/// </remarks>
public sealed class SurfaceTabsControl : SurfaceContainerControl
{
    private readonly StackPanel _strip;

    /// <summary>每页的**声明节点**（页内容按它排 —— 页签条自己的排布键绝不套到页内容上）。</summary>
    private readonly IReadOnlyList<ViewNode> _pageNodes;

    private readonly IReadOnlyList<IReadOnlyList<ISurfaceLayoutItem>> _pages;
    private readonly IReadOnlyList<string> _titles;
    private readonly ISurfaceHost _host;
    private readonly List<Button> _buttons = [];
    private readonly List<Border> _underlines = [];
    private int _selected;

    /// <summary>造一个标签页控件。</summary>
    /// <param name="node">声明节点（标签条自己：页签条的行高、状态键取它）。</param>
    /// <param name="pageNodes">每页的声明节点（页内容的排布键取它）。</param>
    /// <param name="pages">每页的子项（顺序 = 声明顺序）。</param>
    /// <param name="titles">每页的标题（页签文字）。</param>
    /// <param name="host">表面宿主（造子控件用）。</param>
    /// <param name="selected">当前页号（用户拨过则以状态存储里的值为准，由宿主给）。</param>
    /// <remarks>
    /// <b>页内容与页签条是两层声明</b>：页签条那一层回答"这一块高度怎么分给页签条 / 页内容"
    /// （<c>tabs.rows</c>），页节点那一层回答"页里面的元素怎么排"。
    /// 用页签条的节点装页内容 ⇒ 第一页与其它页拿到两套排布规则，而屏幕上只表现为"切页后间距变了"。
    /// </remarks>
    public SurfaceTabsControl(
        ViewNode node,
        IReadOnlyList<ViewNode> pageNodes,
        IReadOnlyList<IReadOnlyList<ISurfaceLayoutItem>> pages,
        IReadOnlyList<string> titles,
        ISurfaceHost host,
        int selected)
        : base(node, PageItems(pages, selected), host, populate: false)
    {
        ArgumentNullException.ThrowIfNull(pageNodes);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(titles);
        _pageNodes = pageNodes;
        _pages = pages;
        _titles = titles;
        _host = host;
        _selected = pages.Count > 0 ? Math.Clamp(selected, 0, pages.Count - 1) : 0;

        // 页内容按**页节点**装（页面板自己的声明 = 这一页的排布口径）。
        Panel.Populate(PageNode(pageNodes, node, _selected), PageItems(pages, _selected), host.Visual);

        _strip = new StackPanel { Orientation = Orientation.Horizontal };
        _strip.Classes.Add(SurfaceClasses.TabStrip);

        for (var i = 0; i < _titles.Count; i++)
        {
            var index = i;
            var button = new Button { Content = _titles[i] };
            button.Classes.Add(SurfaceClasses.TabStripItem);
            button.Click += (_, _) => Select(index);

            // **只压掉"高度地板"，绝不压横向内边距**：页签的宽度是"文字 + 左右内边距"，
            // 把内边距一起压掉会让页签只剩文字那么宽（实测整条从 139 掉到 29）——
            // 屏幕上表现为"页签突然很窄、很难点到"，而声明与排布全都合格。
            // 全局 `Button` 的 `MinHeight = 27`（给独立按钮的地板）才是要压的那一条：
            // 它把条撑到 19 ⇒ 声明 22 的那一行不再需要拉伸 ⇒ 声明失效。
            button.MinHeight = 0;
            button.Padding = new Thickness(11, 0);

            // **选中下沿是独立的一条元素**，不借 `Button` 自己的边框：
            // `Button` 的边框归模板部件、由 Fluent 的指针态按主题资源着色 —— 自写一条同优先级的
            // `BorderBrush` 压不住它，鼠标一移上去那圈主色就被换掉（实测：悬停时选中带消失）。
            // 独立元素不参与按钮的任何状态 ⇒ "选中标记一定还在"是**结构上保证**的。
            var underline = new Border();
            underline.Classes.Add(SurfaceClasses.TabStripUnderline);

            var cell = new Grid();
            cell.Children.Add(button);
            cell.Children.Add(underline);

            _buttons.Add(button);
            _underlines.Add(underline);
            _strip.Children.Add(cell);
        }

        // **整组的外框**：颜色与文本框那一圈同一个色源 —— 页签因此读起来是"贴在同一套框线里的一块控件"。
        // 画在外面而不是每个按钮各画一条：按钮数量、标题长短怎么变，框线都不会错位。
        // **方角**：与分组框 / 文本面那一圈同一套几何语言；圆角只会让首末页签与中间的观感分家。
        // **纵向铺满**：它所在那一行多高就多高（行高来自 `tabs.rows` 的声明）—— 居中的话条会回到
        // "自己内容那么高"，声明 22 的条实测只剩 19（"声明了却没生效"）。
        // 横向仍按内容靠左：整组页签不该铺满整块宽度。
        var stripFrame = new Border
        {
            Child = _strip,
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        stripFrame.Classes.Add(SurfaceClasses.TabStripFrame);
        stripFrame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderStrong");

        // **页签条固定、页内容占满剩下的高度** —— 用两行网格，不能用竖排 StackPanel：
        // `StackPanel` 量孩子时给的是无限高，于是页内容永远"要多高给多高"，
        // 页比视口高的时候整块被排到视口之外（实测：视口 594，页要 679，最后一段落在窗口外看不见）。
        // 网格第二行是 `*`，页内容拿到的是"视口减页签条"这个**确定**高度。
        // 页内容外面套"滚动条 + `SurfaceViewport`"这两层，与整个表面用的是同一对壳体：
        // 滚动条的量内容那一遍必然是无限高，靠 `SurfaceViewport` 把"量到的内容高"与"实得视口高"分开，
        // 页内容因此既能拿满视口、又能在真的更高时被滚出来。
        // **不要再直接套裸 `ScrollViewer`**：那等于什么都没改（它量内容用的还是无限高）。
        var content = new ScrollViewer
        {
            Content = new SurfaceViewport { Child = Panel },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        // **页签条与页内容的分法来自声明**（`tabs.rows`）：第 0 行页签条、第 1 行页内容。
        // 不写 = `Auto,*`（旧行为：页签条按内容、页内容吃余量）。
        //
        // **为什么页签条高度归声明而不是主题**：它是这个控件自己的几何。放在主题里就会变成
        // "主题去抵消另一个主题"—— 全局 `Button` 有 `MinHeight`（那是给独立按钮的地板），
        // 页签条要更矮就得在主题里再写一条把它压下来；而"页签条多高"根本不是色板/圆角那类外观决定，
        // 不同模块对它的需求可以完全不同。所以：**行高是唯一权威**，按钮自身不再设最小高
        // （见主题里 `Button.surface-tab-strip-item` 的注释），行写 22 就真的是 22。
        var layout = new Grid { RowDefinitions = RowsOf(node.Rows) };

        Grid.SetRow(stripFrame, 0);
        layout.Children.Add(stripFrame);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        Content = layout;

        ApplySelectionClasses();
    }

    /// <summary>
    /// 把声明的行规格翻成网格的行定义（<c>rows</c> 的写法与容器完全一致）。
    /// </summary>
    /// <param name="rows">声明里的行规格；空表 ⇒ 缺省分法。</param>
    /// <returns>行定义。</returns>
    /// <remarks>
    /// 页签至少要有两行（第 0 行页签条、第 1 行页内容），这一点在**读取器**那里就报硬伤了；
    /// 这里只负责把合法的写法翻过来。多写的行会留空（不用），不静默丢掉。
    /// </remarks>
    private static RowDefinitions RowsOf(IReadOnlyList<GridTrack> rows)
    {
        if (rows.Count < 2)
        {
            return new RowDefinitions("Auto,*");
        }

        var definitions = new RowDefinitions();

        foreach (var track in rows)
        {
            definitions.Add(track.Kind switch
            {
                GridTrackKind.Auto => new RowDefinition(GridLength.Auto),
                GridTrackKind.Fixed => new RowDefinition(new GridLength(Math.Max(track.Value, 1), GridUnitType.Pixel)),
                _ => new RowDefinition(new GridLength(Math.Max(track.Value, 1), GridUnitType.Star)),
            });
        }

        return definitions;
    }

    /// <summary>取某一页的子项（越界 ⇒ 夹到合法页；没有页 ⇒ 空）。</summary>
    /// <param name="pages">每页的子项。</param>
    /// <param name="selected">页号。</param>
    /// <returns>这一页的子项。</returns>
    private static IReadOnlyList<ISurfaceLayoutItem> PageItems(
        IReadOnlyList<IReadOnlyList<ISurfaceLayoutItem>> pages,
        int selected) =>
        pages.Count == 0 ? [] : pages[Math.Clamp(selected, 0, pages.Count - 1)];

    /// <summary>取某一页的**声明节点**（没有页这一档不存在：读取器要求容器必须有子节点；兜底给页签条自己的节点）。</summary>
    /// <param name="pageNodes">每页的声明节点。</param>
    /// <param name="fallback">没有页时的兜底（此时面板里没有任何项，用不到它的排布键）。</param>
    /// <param name="selected">页号。</param>
    /// <returns>这一页的声明节点。</returns>
    private static ViewNode PageNode(IReadOnlyList<ViewNode> pageNodes, ViewNode fallback, int selected) =>
        pageNodes.Count == 0 ? fallback : pageNodes[Math.Clamp(selected, 0, pageNodes.Count - 1)];

    /// <summary>用户切了页（宿主据此把选中页记进状态存储）。</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>当前页号。</summary>
    public int SelectedIndex => _selected;

    /// <summary>页数（宿主与体检用例要按页遍历）。</summary>
    public int PageCount => _pages.Count;

    /// <summary>切到某一页（越界即忽略；同一页重复调不重建）。</summary>
    /// <param name="index">页号。</param>
    public void Select(int index)
    {
        if (index < 0 || index >= _pages.Count || index == _selected)
        {
            return;
        }

        _selected = index;
        ApplySelectionClasses();

        // **只换内容**：同一个面板实例重新装一次子项（页节点自己不进面板）。
        // 排布声明给**页节点** —— 页节点那一层才是"页里面的元素怎么排"的出处；
        // 给页签条自己的节点会把 `layout`/`columns`/`rows`/`padding`/`spacing` 全套套在页内容上，
        // 页内容就按一套本该管页签条的规则排（实测：排布演示页因此虚高到 687、第五节被切在视口外）。
        Panel.Populate(PageNode(_pageNodes, Node, _selected), PageItems(_pages, _selected), _host.Visual);
        SelectionChanged?.Invoke(this, _selected);
    }

    /// <summary>
    /// 把选中态写成"按钮上的类名 + 下沿的显隐"。
    /// </summary>
    /// <remarks>
    /// 下沿是**独立元素**（见构造处）：它只认"这一页是不是当前页"，与指针状态无关 ——
    /// 所以悬停到已选中的页签上时，标记不会消失。
    /// </remarks>
    private void ApplySelectionClasses()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            var selected = i == _selected;

            if (selected)
            {
                if (!button.Classes.Contains(SurfaceClasses.TabStripSelected))
                {
                    button.Classes.Add(SurfaceClasses.TabStripSelected);
                }
            }
            else
            {
                button.Classes.Remove(SurfaceClasses.TabStripSelected);
            }

            _underlines[i].IsVisible = selected;
            _underlines[i].Classes.Set(SurfaceClasses.TabStripUnderlineSelected, selected);
        }
    }
}
