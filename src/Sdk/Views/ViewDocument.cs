using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Sdk.Views;

/// <summary>包内界面声明的完整内容 —— 框架级段 + 控件级段的唯一事实源。</summary>
public sealed record ViewDocument
{
    public required int Version { get; init; }

    public required ViewFrame Frame { get; init; }

    public required IReadOnlyList<ViewSourceDeclaration> Sources { get; init; }

    public required IReadOnlyList<ViewDefinition> Views { get; init; }

    public static ViewDocument Empty { get; } = new()
    {
        Version = 1,
        Frame = ViewFrame.None,
        Sources = [],
        Views = [],
    };

    /// <summary>
    /// **没有任何声明时**的兜底文档 —— 唯一与 <see cref="Empty"/> 的差别是**窗口尺寸不为 0**。
    /// </summary>
    /// <param name="window">
    /// 兜底窗口声明：由**读得到 schema 的那一层**（宿主）按框架地板填（见
    /// <c>ViewSchema.WindowFloor</c>）—— 地板值不允许在渲染侧或 SDK 里另写一份字面量。
    /// </param>
    /// <remarks>
    /// 整份声明被忽略（硬伤 / schema 加载失败）时用它而不是 <see cref="Empty"/>：一个 0×0 的窗口等于
    /// "这个实例没有任何尺寸"，而框架对可开窗实例的口径是**永远按地板给一个能用的窗口**
    /// （零尺寸窗口只会把"声明写错"伪装成"窗口不见了"，更难查）。
    /// </remarks>
    public static ViewDocument Fallback(ViewWindowDeclaration window) => new()
    {
        Version = 1,
        Frame = new ViewFrame { Window = window, Labels = [] },
        Sources = [],
        Views = [],
    };
}

/// <summary>框架级段：模块窗口的默认尺寸与约束、网络标签的默认位置。</summary>
public sealed record ViewFrame
{
    public required ViewWindowDeclaration Window { get; init; }

    public required IReadOnlyList<ViewNetworkLabel> Labels { get; init; }

    public static ViewFrame None { get; } = new()
    {
        Window = ViewWindowDeclaration.None,
        Labels = [],
    };
}

/// <summary>
/// 窗口尺寸与约束 —— 读取时已按 schema 补齐缺省值、并已按**框架地板**夹过下限，
/// 渲染侧因此**不再自带缺省、也不再夹下限**（地板见 <c>ViewSchema.WindowFloor</c>）。
/// </summary>
public sealed record ViewWindowDeclaration
{
    public required double Width { get; init; }

    public required double Height { get; init; }

    public required double MinWidth { get; init; }

    public required double MinHeight { get; init; }

    public double? MaxWidth { get; init; }

    public double? MaxHeight { get; init; }

    public double EffectiveMinWidth => Math.Max(MinWidth, 0);

    public double EffectiveMinHeight => Math.Max(MinHeight, 0);

    public static ViewWindowDeclaration None { get; } = new()
    {
        Width = 0,
        Height = 0,
        MinWidth = 0,
        MinHeight = 0,
    };

    /// <summary>按框架地板构造一份"仅尺寸"的窗口声明（宿主在兜底文档里用；见 <see cref="ViewDocument.Fallback"/>）。</summary>
    /// <param name="minWidth">地板宽（DIP）。</param>
    /// <param name="minHeight">地板高（DIP）。</param>
    public static ViewWindowDeclaration Floor(double minWidth, double minHeight) => new()
    {
        Width = minWidth,
        Height = minHeight,
        MinWidth = minWidth,
        MinHeight = minHeight,
    };
}

/// <summary>网络标签的默认位置：落在哪条边、在边上的哪个地方。</summary>
public sealed record ViewNetworkLabel(string Name, PortEdge Edge, double Offset);

/// <summary>数据源条目 —— 元素靠名字引用它。</summary>
public sealed record ViewSourceDeclaration(
    string Name,
    ViewDataSourceKind Kind,
    ContractPrimitive? ValueType,
    string? Unit);

/// <summary>一个视图：标题 + 根节点。</summary>
public sealed record ViewDefinition(string Title, ViewNode Root);

/// <summary>节点：容器（可含子节点）或原子控件（叶子）。</summary>
/// <remarks>专有键的取值全部来自声明文件、缺省由 schema 补齐，渲染侧只读不算。</remarks>
public sealed record ViewNode
{
    public required string Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>
    /// **每个页签的文字**（仅 <c>tabs</c>；声明面 <c>pageTitles</c>）。
    /// </summary>
    /// <remarks>
    /// 为什么不让页签去取"页节点自己的 <see cref="Title"/>"：页节点的 title **同时也是页内那块标题带**，
    /// 于是"给页签起个名字"就必然在页内容里多画一行 —— 页内容的可用高度因此比同组的其它块少一截
    /// （实测：同一个组里输入框 142、结果框 166，看起来就是"一高一低"）。
    /// 名义与画面分开之后，页签有名字、页内容没有多余的一行。
    /// **空数组 = 按旧口径**（退回页节点的 title），所以没写这个键的模块行为不变。
    /// </remarks>
    public IReadOnlyList<string> PageTitles { get; init; } = [];

    /// <summary>
    /// 控件**自己身上**的文字（目前只有 <c>commandButton</c> 用它）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Title"/> 分家的理由：<c>title</c> 在别处一律是"这个元素的说明文字"，
    /// 而按钮上的字**就是按钮本身**。同一个键承担两种语义，界面上就会出现"说明一行 + 按钮一行"
    /// 的双份文案（实测「发送」按钮上真的写了两个「发送」）。
    /// </remarks>
    public string Text { get; init; } = string.Empty;

    /// <summary>这一块文本面是否只读（声明面 <c>readonly</c>；输入与输出的唯一区别之一）。</summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// 文本层用哪一套滚动（声明面 <c>scrollBar</c>）。**必写** —— 机制不由框架从 <see cref="ReadOnly"/> 推。
    /// </summary>
    /// <remarks>只对手工构造的节点可见这个缺省；读取器（运行时的唯一来源）总是显式给出。</remarks>
    public ViewScrollMode ScrollBar { get; init; } = ViewScrollMode.Native;

    /// <summary>文本面的空框最小高（DIP；0 = 不设，框高只由声明与内容决定）。</summary>
    public double MinEditorHeight { get; init; }

    /// <summary>
    /// 这个控件**主文字**的字号（DIP；声明面 <c>fontSize</c>；<c>0</c> = 用主题那一档）。
    /// </summary>
    /// <remarks>
    /// 主文字 = 用户看这块控件的"内容"：`label` 的正文、`textBox` 的输入文本、`numeric`/`curve` 的读数、
    /// `commandButton` 按钮上的字。**说明文字**（<see cref="Title"/> 那一块）另有 <see cref="TitleFontSize"/>：
    /// 两块文字各由自己的键描述，别的控件长什么样与本控件无关。
    /// </remarks>
    public double FontSize { get; init; }

    /// <summary>
    /// 这个控件**说明文字**的字号（DIP；声明面 <c>titleFontSize</c>；<c>0</c> = 用主题那一档）。
    /// </summary>
    /// <remarks>与 <see cref="FontSize"/> 是两块不同的文字：只有声明了 <see cref="TextPlacement"/> 才有实体。</remarks>
    public double TitleFontSize { get; init; }

    /// <summary>
    /// 这个控件**说明文字**的默认颜色（`#RRGGBB` / `#AARRGGBB`；声明面 <c>titleColor</c>；空 = 用主题的）。
    /// </summary>
    /// <remarks>与 <see cref="TextColor"/> 的分工同 <see cref="TitleFontSize"/> 与 <see cref="FontSize"/>。</remarks>
    public string TitleColor { get; init; } = string.Empty;

    /// <summary>纯文本说明是否弱化显示（次要说明）。</summary>
    public bool Muted { get; init; }

    /// <summary>纯文本说明是否**不折行**（单行标签用；写死宽度由文字决定）。</summary>
    public bool Nowrap { get; init; }

    /// <summary>
    /// 控件的**说明文字**摆在哪（声明面 <c>textPlacement</c>）：<c>none</c> / <c>above</c> / <c>below</c> /
    /// <c>left</c> / <c>right</c>；文字取 <see cref="Title"/>。
    /// </summary>
    /// <remarks>
    /// 缺省值逐 kind 写在 schema 的 <c>defaultText</c> 段（与本属性同名的键），渲染侧不写"没声明就这样"。
    /// <c>none</c> 表示这个控件**不画**说明文字 —— 位置由模块自己摆一个 <c>label</c> 元素定。
    /// </remarks>
    public string TextPlacement { get; init; } = "none";

    /// <summary>
    /// 说明文字在**剩下那一轴**上靠哪边（声明面 <c>textAnchor</c>）：<c>start</c> / <c>center</c> / <c>end</c>。
    /// </summary>
    /// <remarks>
    /// <b>只作用于副轴</b>：文字与主体上下堆叠时管横向，左右并排时管纵向。另一轴由
    /// <see cref="TextPlacement"/> 本身决定，再给一次就是自相矛盾（读取器按白名单报硬伤）。
    /// </remarks>
    public string TextAnchor { get; init; } = "center";

    public string? StateKey { get; init; }

    public required AxisLayout Width { get; init; }

    public required AxisLayout Height { get; init; }

    public bool ScaleWithWindow { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public int? Column { get; init; }

    public int? Row { get; init; }

    public int ColumnSpan { get; init; }

    public int RowSpan { get; init; }


    public CellAlign Align { get; init; }

    public CellAlign VerticalAlign { get; init; }

    /// <summary>
    /// **容器级**横向停靠（仅 `layout: "panel"` 有意义）：内部"设计框"在**面板框**里靠哪边。
    /// </summary>
    /// <remarks>
    /// 与元素级 <see cref="Align"/> **不是一回事**，所以是两个键：
    /// <see cref="Align"/> 管"**这一项**在**它的格子**里靠哪边"；
    /// 本属性管"**这一组**（设计框）在**面板框**里靠哪边"。
    /// </remarks>
    public CellAlign Anchor { get; init; }

    /// <summary>**容器级**纵向停靠（仅 `layout: "panel"` 有意义；同理见 <see cref="Anchor"/>）。</summary>
    public CellAlign VAnchor { get; init; }

    public required string Layout { get; init; }

    public required string Overflow { get; init; }

    public double Padding { get; init; }

    public double Spacing { get; init; }

    public required IReadOnlyList<GridTrack> Columns { get; init; }

    public required IReadOnlyList<GridTrack> Rows { get; init; }

    public double ColumnSpacing { get; init; }

    public double RowSpacing { get; init; }

    public bool Expanded { get; init; }

    /// <summary>
    /// 这块文字的**默认颜色**（`#RRGGBB` / `#AARRGGBB`；空 = 用主题的）。
    /// </summary>
    /// <remarks>
    /// 它是**内容语义**（"这一块文字该是什么颜色"），与主题分工：主题给字体与色板，
    /// 声明给"这一块出来瞬间是什么样"。运行时还能由模块改（见 `IViewAppearanceAccess`）。
    /// </remarks>
    public string TextColor { get; init; } = string.Empty;

    /// <summary>数据绑定（data 角色键的取值）；与 <see cref="Input"/>、<see cref="Command"/> 三者互斥。</summary>
    public string? DataSource { get; init; }

    /// <summary>输入绑定（input 角色键的取值）。</summary>
    public string? Input { get; init; }

    /// <summary>命令绑定（command 角色键的取值）。</summary>
    public string? Command { get; init; }

    public required string Unit { get; init; }


    public bool Wrap { get; init; }

    public bool ShowNewlineMark { get; init; }

    public bool Multiline { get; init; }


    public required string TextAlign { get; init; }


    public required IReadOnlyList<ViewNode> Children { get; init; }

    /// <summary>读取该节点时记下的警告（已登记未渲染、该条忽略之类）。</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public bool HasChildren => Children.Count > 0;
}

/// <summary>声明文件里角色键的名字 —— 读取与渲染两侧共用同一份字面量。</summary>
public static class ViewBindingRoles
{
    public const string Data = "data";

    public const string Input = "input";

    public const string Command = "command";
}
