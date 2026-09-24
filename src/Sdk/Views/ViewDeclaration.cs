using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// 视图**预置元素**目录（v1 = 档 3 完整）——ADR-0028 C3-6。
/// </summary>
/// <remarks>
/// 模块**不提供 View 类型**（C1-3）：视图只能由框架按本目录中的元素组合渲染。
/// 新增元素属**兼容扩展**；删除或改语义属破坏性变更（须 ADR）。
/// </remarks>
public enum ViewElementKind
{
    // ---- 数据呈现类（A） ----

    /// <summary>A1 数值显示（单/多值 + 格式单位）。</summary>
    Numeric = 0,

    /// <summary>A2 状态灯/指示器。</summary>
    StatusLight = 1,

    /// <summary>A3 曲线/波形（多通道、时间轴）。</summary>
    Curve = 2,

    /// <summary>A4 表格（行集）。</summary>
    Table = 3,

    /// <summary>A5 文本日志窗口。</summary>
    TextLog = 4,

    /// <summary>A6 十六进制/字节视图。</summary>
    HexView = 5,

    /// <summary>A7 仪表/进度条。</summary>
    Gauge = 6,

    /// <summary>A8 图像/位图帧。</summary>
    Image = 7,

    // ---- 交互控制类（B） ----

    /// <summary>B1 配置项编辑（按配置 Schema 自动生成）。</summary>
    ConfigurationEditor = 8,

    /// <summary>B2 命令按钮（触发模块定义的动作）。</summary>
    CommandButton = 9,

    /// <summary>B3 开关/滑块。</summary>
    ToggleSlider = 10,

    /// <summary>B4 枚举/下拉选择。</summary>
    EnumSelector = 11,

    /// <summary>B5 文本输入（如 AT 指令）。</summary>
    TextInput = 12,

    /// <summary>B6 文件/路径选择。</summary>
    FileSelector = 13,

    // ---- 结构容器类（C） ----

    /// <summary>C1 分组/分栏。</summary>
    Group = 14,

    /// <summary>C2 标签页。</summary>
    TabPages = 15,

    /// <summary>C3 折叠面板。</summary>
    Collapsible = 16,

    /// <summary>C4 滚动容器。</summary>
    Scroll = 17,
}

/// <summary>
/// 可暴露数据源的**类型**（05 §5.3：名称 + 类型）——决定订阅缓冲形态与呈现方式。
/// </summary>
/// <remarks>
/// 与元素类型的分工：**数据源类型**描述"模块往里写的是什么"，**元素类型**描述"用户看到什么"
/// （04 §4.3 的交接形态表按元素类型判定：A1/A2/A7 = 最新值槽，A3/A4/A5/A6/A8 = 环形缓冲）。
/// </remarks>
public enum ViewDataSourceKind
{
    /// <summary>单值 / 离散状态（数值）。</summary>
    Value = 0,

    /// <summary>序列（曲线 / 波形）。</summary>
    Sequence = 1,

    /// <summary>行集（表格）。</summary>
    RowSet = 2,

    /// <summary>字节块（十六进制视图）。</summary>
    ByteBlock = 3,

    /// <summary>文本（日志）。</summary>
    Text = 4,

    /// <summary>帧（图像 / 位图）。</summary>
    Frame = 5,
}

/// <summary>
/// 模块**可暴露数据源**（视图元素绑定的目标）——ADR-0031 Q5-3：声明数据源 + 框架拉取。
/// </summary>
/// <remarks>
/// 模块只声明"哪些数据可暴露"，不知道也不关心被谁展示、怎么展示；
/// 框架按刷新节拍从订阅缓冲拉取，并在 UI 侧抽样。
/// </remarks>
public sealed record ViewDataSource
{
    /// <summary>创建数据源声明。</summary>
    /// <param name="name">数据源名（模块内唯一；<c>Publish</c> 时使用）。</param>
    /// <param name="unit">可选单位文本（单位属模块显示层，**不入契约**——主文档 §5.4.2）。</param>
    /// <param name="format">可选格式化串（如 <c>F2</c>）。</param>
    /// <param name="kind">数据源类型（值 / 序列 / 行集 / 字节块 / 文本 / 帧）。</param>
    /// <param name="valueType">
    /// 单样本的**契约原语**（数值类数据源用；<see langword="null"/> = 非数值，由框架按字节块/文本呈现）。
    /// </param>
    public ViewDataSource(
        string name,
        string? unit = null,
        string? format = null,
        ViewDataSourceKind kind = ViewDataSourceKind.Value,
        ContractPrimitive? valueType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Unit = unit;
        Format = format;
        Kind = kind;
        ValueType = valueType;
    }

    /// <summary>数据源名（模块内唯一）。</summary>
    public string Name { get; }

    /// <summary>单位文本（显示用；契约不含单位语义）。</summary>
    public string? Unit { get; }

    /// <summary>格式化串（显示用）。</summary>
    public string? Format { get; }

    /// <summary>数据源类型（决定订阅缓冲形态与呈现方式）。</summary>
    public ViewDataSourceKind Kind { get; }

    /// <summary>单样本的契约原语（数值类数据源用；null = 非数值）。</summary>
    public ContractPrimitive? ValueType { get; }
}

/// <summary>
/// 视图元素（预置元素的**组合**中的一个节点）。
/// </summary>
/// <remarks>
/// 元素类型属架构（本目录）；元素**视觉形态**（尺寸/样式/网格）属 Gate C（UX 原型）。
/// </remarks>
public sealed record ViewElement
{
    /// <summary>创建视图元素。</summary>
    /// <param name="kind">预置元素类型。</param>
    /// <param name="dataSource">绑定的数据源名（交互控制类与容器类可为空）。</param>
    /// <param name="title">可选标题。</param>
    /// <param name="children">子元素（仅容器类元素使用）。</param>
    public ViewElement(
        ViewElementKind kind,
        string? dataSource = null,
        string? title = null,
        IReadOnlyList<ViewElement>? children = null)
    {
        Kind = kind;
        DataSource = dataSource;
        Title = title;
        Children = children ?? [];
    }

    /// <summary>元素类型（预置目录中的一项）。</summary>
    public ViewElementKind Kind { get; }

    /// <summary>绑定的数据源名（可为空）。</summary>
    public string? DataSource { get; }

    /// <summary>标题（可为空）。</summary>
    public string? Title { get; }

    /// <summary>子元素（容器类元素使用；其余为空）。</summary>
    public IReadOnlyList<ViewElement> Children { get; }
}

/// <summary>
/// 模块**命令**声明【新增 · M1-16 / ADR-0040 D1】：框架把它渲染成按钮（<see cref="ViewElementKind.CommandButton"/>），
/// 点击后**投递到模块线程**执行（<c>IModuleContext.Inbox</c>）。
/// </summary>
/// <remarks>
/// 命令是"人的一次操作"，**不是**配置写入 —— 配置只承载"随组合件保存的参数"。
/// </remarks>
public sealed record ViewCommand
{
    /// <summary>创建命令声明。</summary>
    /// <param name="name">命令名（模块内唯一；<c>ViewElement.DataSource</c> 用它关联按钮）。</param>
    /// <param name="title">按钮标题。</param>
    /// <param name="icon">可选图标标识（框架按 token 映射；未知则用默认图标）。</param>
    /// <param name="description">可选说明（工具提示）。</param>
    public ViewCommand(string name, string title, string? icon = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Name = name;
        Title = title;
        Icon = icon;
        Description = description;
    }

    /// <summary>命令名。</summary>
    public string Name { get; }

    /// <summary>按钮标题。</summary>
    public string Title { get; }

    /// <summary>图标标识（可为空）。</summary>
    public string? Icon { get; }

    /// <summary>说明文本（可为空）。</summary>
    public string? Description { get; }
}

/// <summary>
/// 模块的视图声明：预置元素的组合 + 其依赖的数据源。
/// </summary>
/// <remarks>
/// 不声明视图的模块 = **无 UI 模块**，合法；其状态与配置经框架的管理视图兜底访问（主文档 §10.4）。
/// </remarks>
public sealed record ViewDeclaration
{
    /// <summary>创建视图声明。</summary>
    /// <param name="elements">根元素（按顺序自上而下排布）。</param>
    /// <param name="dataSources">本视图使用到的数据源。</param>
    /// <param name="commands">本视图声明的命令（按钮类元素引用其名字）。</param>
    public ViewDeclaration(
        IReadOnlyList<ViewElement> elements,
        IReadOnlyList<ViewDataSource>? dataSources = null,
        IReadOnlyList<ViewCommand>? commands = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Elements = elements;
        DataSources = dataSources ?? [];
        Commands = commands ?? [];
    }

    /// <summary>根元素序列。</summary>
    public IReadOnlyList<ViewElement> Elements { get; }

    /// <summary>数据源声明。</summary>
    public IReadOnlyList<ViewDataSource> DataSources { get; }

    /// <summary>命令声明【M1-16 / ADR-0040】。</summary>
    public IReadOnlyList<ViewCommand> Commands { get; }

    /// <summary>
    /// **首选窗口宽度**（DIP；0 = 框架默认）【新增 · 用户第十一轮反馈】：
    /// 模块作者**声明**自己界面合适的默认尺寸，框架照用——**不按元素类型猜**（那等于把布局写死在框架里）。
    /// 用户拖过/缩过之后以用户布局为准；模块窗口的「还原」回到这个尺寸。
    /// </summary>
    /// <remarks>
    /// **用 init 属性而不是构造器参数**：给 SDK 构造器加可选参数会**改变方法签名**，
    /// 已编译的第三方模块会在加载时抛 <c>MissingMethodException</c>（实测踩过）——
    /// 新增"可选声明"一律用 <c>init</c> 属性（二进制兼容）。
    /// 用法：<c>new ViewDeclaration(elements, dataSources) { PreferredWidth = 330, PreferredHeight = 330 }</c>
    /// </remarks>
    public double PreferredWidth { get; init; }

    /// <summary>首选窗口高度（DIP；0 = 框架默认；同上用 <c>init</c> 属性）。</summary>
    public double PreferredHeight { get; init; }
}
