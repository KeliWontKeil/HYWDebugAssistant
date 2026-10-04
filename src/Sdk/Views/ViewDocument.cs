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

    public bool Auto { get; init; }

    public CellAlign Align { get; init; }

    public CellAlign VerticalAlign { get; init; }

    public required string Layout { get; init; }

    public required string Overflow { get; init; }

    public double Padding { get; init; }

    public double Spacing { get; init; }

    public required IReadOnlyList<GridTrack> Columns { get; init; }

    public required IReadOnlyList<GridTrack> Rows { get; init; }

    public double ColumnSpacing { get; init; }

    public double RowSpacing { get; init; }

    public bool Expanded { get; init; }

    public bool ShowInlineResult { get; init; }

    public string? FeedbackSource { get; init; }

    /// <summary>数据绑定（data 角色键的取值）；与 <see cref="Input"/>、<see cref="Command"/> 三者互斥。</summary>
    public string? DataSource { get; init; }

    /// <summary>输入绑定（input 角色键的取值）。</summary>
    public string? Input { get; init; }

    /// <summary>命令绑定（command 角色键的取值）。</summary>
    public string? Command { get; init; }

    public required string Unit { get; init; }

    public int Capacity { get; init; }

    public bool Wrap { get; init; }

    public bool ShowNewlineMark { get; init; }

    public bool Multiline { get; init; }

    public required string InputKind { get; init; }

    public required string TextAlign { get; init; }

    public required string FileMode { get; init; }

    public string? FileFilter { get; init; }

    public string? SuggestedFileName { get; init; }

    public string? Icon { get; init; }

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
