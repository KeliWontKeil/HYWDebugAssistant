using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **B6 文件选择器**的模式（G4 · 2026-09）：打开 / 保存。
/// </summary>
/// <remarks>打开用 <c>OpenFilePickerAsync</c>，保存用 <c>SaveFilePickerAsync</c>。属 SDK 声明面（只增）。</remarks>
public enum FilePickerMode
{
    /// <summary>打开文件。</summary>
    Open = 0,

    /// <summary>保存文件。</summary>
    Save = 1,
}

/// <summary>
/// **文本输入的"文本类型"**（2026-09 · 用户裁定"不使用新控件而是扩展它"）。
/// </summary>
/// <remarks>
/// 决定框架在输入时**放行哪些字符**（纯格式）；**范围与业务合法性不在此列** —— 那是模块的事
/// （用输入反馈通道回报 ⇒ 红框），与"可声明 ≠ 可判定"同一纪律。详见 <see cref="ViewElement.InputKind"/>。
/// <para>
/// 三档：<see cref="Text"/>（任意字符）· <see cref="Number"/>（数字写法）· <see cref="Hex"/>（十六进制 + 空白排版）。
/// </para>
/// </remarks>
public enum TextInputKind
{
    /// <summary>普通文本（默认；不限制字符）。</summary>
    Text = 0,

    /// <summary>数值写法：数字 · 一个小数点 · 首位负号（范围/合法性归模块）。</summary>
    Number = 1,

    /// <summary>
    /// **十六进制写法**（2026-09 · 用户裁定"HEX 输入框只接受 HEX"）：只放行 `0-9 A-F a-f`
    /// **与空白**（空格 / 制表 / 换行）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>空白是排版</b>（用户 2026-09 裁定："HEX 的**换行不作为字符，仅作为排版**"）⇒
    ///     框架只保证"用户敲进去的每个非空白字符都是十六进制数字"；**空白怎么分组、是否忽略，由模块解析时定**
    ///     （用户可按字节、按 16 字节一行随意排版）；</item>
    ///   <item>**每字节两位十六进制**（用户裁定）；缺位/奇数字符/非法字符的处置属**模块语义**（回报失败 ⇒ 红框）；</item>
    ///   <item>**只增**（新枚举值）⇒ 老模块行为不变。</item>
    /// </list>
    /// </remarks>
    Hex = 2,
}

/// <summary>
/// 元素的**对齐**（2026-09 · 由模块声明 —— 对齐属**视觉组织**，不归框架限死）。
/// </summary>
/// <remarks>
/// **当前只有 <see cref="ViewElementKind.TextInput"/> 消费**；其它元素声明了不生效（渲染子集逐步补齐）。
/// 单位**不在**本枚举职责内：输入框不显示框内单位后缀，单位用**框外标签**（取 <c>ViewDataSource.Unit</c>）。
/// </remarks>
public enum ViewAlignment
{
    /// <summary>跟随框架默认（当前 = 左对齐）。</summary>
    Default = 0,

    /// <summary>左对齐。</summary>
    Left = 1,

    /// <summary>居中。</summary>
    Center = 2,

    /// <summary>右对齐。</summary>
    Right = 3,
}

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

    // ---- 交互控制类（B · 续） ----

    /// <summary>
    /// **B7 开关式滑块**（**2026-09 用户裁定新增**）——与 B3 同为"开 / 关"语义（值 = `true`/`false`），
    /// 只是**外观**是滑块开关（比勾选框更美观）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>用户原话："目前开关类的控件全部用的勾选框，**我想添加一个开关式的滑块，更美观**"；</item>
    ///   <item><b>为什么新增而不改 B3</b>：B3 `ToggleSlider` = **勾选框**形态（保留给"复选 / 多选"语义），
    ///     两者**语义相同、外观不同** ⇒ 拆成两个元素类型，模块按场景自选；</item>
    ///   <item><b>枚举值追加在末尾</b>（`18`）⇒ 老模块的声明字节不变（**只增不改**，`02` §19）。</item>
    /// </list>
    /// </remarks>
    ToggleSwitch = 18,
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

    /// <summary>
    /// 本数据源样本所属的**数据类型引用**【ADR-0046 D2】：框架据此取该类型的**解码器**与计量块。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**推荐**：数据源引用一个已声明的类型 ⇒ 呈现期解码走**模块提供的解码器**（D1 ③，框架只回调）；</item>
    ///   <item>**旧形态**：只声明 <see cref="ValueType"/>（不引用类型）⇒ 框架按**参考实现**解码
    ///     （过渡回退，登记为历史兼容路径，不是机制）；</item>
    ///   <item>两者都缺 ⇒ UI 显示"未声明类型 + 字节数"，**不猜**。</item>
    /// </list>
    /// 用法：<c>new ViewDataSource("value", valueType: F32) { Type = new("hyw/f32", 1) }</c>
    /// </remarks>
    public DataTypeRef? Type { get; init; }
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

    /// <summary>
    /// **视图状态的寻址键**（2026-09 用户裁定 · `decisions.md` 7.28；可选，`init` 只增）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>用途</b>：模块用 <c>IModuleContext.ViewState</c> **回读**视图状态时，用它指认"哪一个元素" ——
    ///     输入源 / 显示源本来就有**数据源名**可用，**容器（如 <see cref="ViewElementKind.TabPages"/>）没有**
    ///     ⇒ 需要这个显式名字（典型用途：读"当前选中页"）；</item>
    ///   <item>**不用 <see cref="Title"/>**：标题是**给人看的展示文案**，改文案的人不该顺手改坏程序语义；</item>
    ///   <item>模块内唯一（重复时框架取**首次声明**者并报 Issue）；</item>
    ///   <item>**不参与**契约 / 兼容 / 建网 —— 纯视图状态寻址。</item>
    /// </list>
    /// 用法：<c>new ViewElement(ViewElementKind.TabPages, children: pages) { StateKey = "text-pages" }</c>
    /// </remarks>
    public string? StateKey { get; init; }

    /// <summary>
    /// **多行文本输入**（G2 · 2026-09；仅 <see cref="ViewElementKind.TextInput"/> 有意义）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>用 `init` 属性而不是构造器参数</b>：给 SDK 构造器加可选参数会**改变方法签名** ⇒
    ///     已编译模块加载时抛 <c>MissingMethodException</c>（实测踩过）—— 新增"可选声明"一律用 <c>init</c>；</item>
    ///   <item><b>提交键不在模块侧定</b>：回车是"换行"还是"提交"**因人而异**（用户 2026-09 裁决：
    ///     "换行是一个极其常见的行为，且不同用户习惯不一样"）⇒ 它是**框架级可配置项**
    ///     （设置键 `multilineSubmitKey`，见《应用级设置》分册）；**模块只声明"我要多行"**；</item>
    ///   <item>单行输入（默认）行为**不变**：回车 / 失焦一次提交（ADR-0040 D-5）。</item>
    /// </list>
    /// 用法：<c>new ViewElement(ViewElementKind.TextInput, "text") { Multiline = true }</c>
    /// </remarks>
    public bool Multiline { get; init; }

    /// <summary>
    /// **B6 文件选择器的模式**（G4 · 2026-09；仅 <see cref="ViewElementKind.FileSelector"/> 有意义）。
    /// </summary>
    /// <remarks>
    /// 打开 / 保存。**只增**（`init`）。模块不接触文件对话框 API —— 用户选/存的**路径**经既有输入通道
    /// （`SubmitInput`）投递到模块，模块在活动体里 `TryDequeueInput` 拿到。
    /// </remarks>
    public FilePickerMode FileMode { get; init; } = FilePickerMode.Open;

    /// <summary>
    /// **B6 文件选择器的类型过滤**（G4；仅 <see cref="ViewElementKind.FileSelector"/> 有意义）。
    /// </summary>
    /// <remarks>形如 <c>*.txt</c> 或 <c>文本文件(*.txt)|*.txt</c>；空 = 不过滤。只增（`init`）。</remarks>
    public string? FileFilter { get; init; }

    /// <summary>
    /// **B6 文件选择器的默认文件名**（2026-09；仅 <see cref="ViewElementKind.FileSelector"/> + 保存模式有意义）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>解决什么</b>（用户 2026-09 实测）：保存对话框的文件名**空着**会让用户每次都得现想一个名字；
    ///     模块声明一个默认名（如 <c>savetext1.txt</c>）⇒ 对话框预填它，用户直接确定即可；</item>
    ///   <item><b>只是一句"建议"</b>：框架把它交给系统保存对话框的 `SuggestedFileName`（用户可以改成任何名字，
    ///     也能换目录）；框架**不**校验、**不**代替用户决定路径 —— "限制在用户选择的路径内"这条纪律不变；</item>
    ///   <item>打开模式（<see cref="FilePickerMode.Open"/>）忽略它（"默认文件名"对打开无意义）；</item>
    /// </list>
    /// 用法：<c>new ViewElement(ViewElementKind.FileSelector, "savePath", "保存") { FileMode = FilePickerMode.Save, SuggestedFileName = "savetext1.txt" }</c>
    /// </remarks>
    public string? SuggestedFileName { get; init; }

    /// <summary>
    /// **文本输入的"文本类型"**（2026-09；仅 <see cref="ViewElementKind.TextInput"/> 有意义）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么是"给已有元素加字段"而不是新元素</b>（用户 2026-09 裁定）：数值输入与文本输入
    ///     **走同一条通道、同一套提交语义**（都是"一串文本"），差别只在**放行哪些字符**；
    ///     新建一个元素会把"元素目录"撑大而语义重复 ⇒ **扩展已有元素**；</item>
    ///   <item><b>框架只做"格式"判定</b>：<see cref="TextInputKind.Number"/> 时输入框**只放行**
    ///     数字 · 一个小数点 · 首位负号（构造"插入后文本"用 <c>^-?[0-9]*\.?[0-9]*$</c> 校验）；
    ///     **范围 / 业务合法性归模块**（模块用输入反馈通道回报 ⇒ 红框）—— 与"可声明 ≠ 可判定"同一纪律；</item>
    ///   <item><b>投递的仍是文本</b>：输入通道签名是 string，数值输入**不新增通道**，
    ///     框架只保证"传进来的是个数的写法"，解析与校验仍由模块做；</item>
    ///   <item>**只增**（`init`，默认 <see cref="TextInputKind.Text"/> ⇒ 老模块行为不变）。</item>
    /// </list>
    /// 用法：<c>new ViewElement(ViewElementKind.TextInput, "interval", "间隔") { InputKind = TextInputKind.Number }</c>
    /// </remarks>
    public TextInputKind InputKind { get; init; } = TextInputKind.Text;

    /// <summary>
    /// **元素对齐**（2026-09；**当前只有 <see cref="ViewElementKind.TextInput"/> 消费**）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>由模块声明、不由框架限死</b>（用户 2026-09 裁定）：对齐是**视觉组织**的一部分
    ///     （"数值右对齐、文本左对齐"这类选择随界面布局而定）⇒ 交回模块声明；</item>
    ///   <item>**单位不在这里**：输入框**不显示框内单位后缀**（会让用户误以为"单位可以改"），
    ///     单位用**框外标签**呈现（取 <c>ViewDataSource.Unit</c>）；</item>
    ///   <item>当前消费方只有 B5；其它元素**声明了不生效**（渲染子集逐步补齐 —— `02` 的"未渲染"占位纪律）；</item>
    ///   <item>**只增**（`init`，默认 <see cref="ViewAlignment.Default"/> ⇒ 跟随框架默认，老模块行为不变）。</item>
    /// </list>
    /// </remarks>
    public ViewAlignment Alignment { get; init; } = ViewAlignment.Default;
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
