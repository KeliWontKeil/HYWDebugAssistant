namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// 控件绑定角色 —— 一个控件**只允许一个**角色键。
/// </summary>
/// <remarks>
/// 「只允许一个」由读取器判定为硬伤；本枚举只表达"这一条描述吃哪种绑定"。
/// <see cref="None"/> 是合法状态（纯展示控件没有 <c>data</c> 时显示空）。
/// </remarks>
public enum ControlRole
{
    /// <summary>无绑定。</summary>
    None,

    /// <summary>吃显示源（<c>data</c>）。</summary>
    Data,

    /// <summary>吃输入源（<c>input</c>）。</summary>
    Input,

    /// <summary>吃命令名（<c>command</c>）。</summary>
    Command,
}

/// <summary>
/// **呈现状态来源** —— 只读性 / 可见性 / 可用性 / 校验态**从哪里推出**。
/// </summary>
/// <remarks>
/// 声明面**不写**这些：它们由绑定角色、数据源状态与框架规则推出。
/// 本枚举是"框架持有状态、模块只读快照"这一口径的载体。
/// </remarks>
public enum PresentationSource
{
    /// <summary>不适用（容器 / 无状态控件）。</summary>
    None,

    /// <summary>由绑定数据源的接线与新鲜度推出（空值、未接线、过期）。</summary>
    SourceAvailability,

    /// <summary>由数据源取值推出（例如状态灯按取值选态，含未知态）。</summary>
    SourceValue,

    /// <summary>由框架的输入过滤规则推出（按键放行、范围合法性归模块）。</summary>
    InputPolicy,

    /// <summary>由**模块设定的外观**推出（边框色 / 文字色）—— 框架只存颜色，不解释它的含义。</summary>
    Appearance,

    /// <summary>由容器内容推出。</summary>
    Content,

    /// <summary>由框架的运行时状态存储推出（折叠态、选中页）。</summary>
    StateStore,
}

/// <summary>
/// **格式能力** —— 显示格式**从哪里来**。
/// </summary>
/// <remarks>
/// 模块不能在声明面指定格式，只能在**数据源**上给单位与小数位；其余格式归框架格式表。
/// </remarks>
public enum FormatCapability
{
    /// <summary>无格式能力（容器 / 布尔 / 文本原样）。</summary>
    None,

    /// <summary>数值格式化（小数位来自框架格式表）。</summary>
    Numeric,

    /// <summary>可带单位（单位来自数据源声明）。</summary>
    Unit,

    /// <summary>时间轴（时间格式来自框架格式表）。</summary>
    TimeAxis,

    /// <summary>多行文本排版（换行与空行标记）。</summary>
    TextLayout,
}

/// <summary>
/// **块轴尺寸策略** —— 本控件在**块轴（纵轴）**上按哪一档参与排布，以及有没有**有限高下限**。
/// </summary>
/// <remarks>
/// <para>
/// 这是**渲染维度的唯一事实源**：排布引擎按这里的档位决定"这一块吃多少纵向余量"，
/// 而不是按控件的 C# 类型做特判（那会让"加一种控件"变成"改布局引擎"）。
/// </para>
/// <para>
/// 档位回答的是"吃不吃余量"，**与"窗口压小时最少留多高"无关** ——
/// 后者是**声明**的事（<c>height.min</c>），本记录不带那个数值：
/// 几何字面量散进代码之后，作者看不出、也改不了自己的界面。
/// </para>
/// </remarks>
/// <param name="Axis">块轴档位。</param>
public sealed record SurfaceGeometryStrategy(SurfaceAxisStrategy Axis)
{
    /// <summary>按内容（自适应）—— 不吃余量、不透传、无下限。</summary>
    public static readonly SurfaceGeometryStrategy Adaptive = new(SurfaceAxisStrategy.Adaptive);

    /// <summary>填满 —— 吃掉容器剩余纵向余量。</summary>
    public static SurfaceGeometryStrategy Fill() => new(SurfaceAxisStrategy.Fill);

    /// <summary>是否吃容器剩余纵向余量。</summary>
    public bool IsFill => Axis is SurfaceAxisStrategy.Fill;
}

/// <summary>块轴档位（<see cref="SurfaceGeometryStrategy"/> 的一半）。</summary>
public enum SurfaceAxisStrategy
{
    /// <summary>按内容（自适应）。</summary>
    Adaptive,

    /// <summary>吃掉容器剩余纵向余量。</summary>
    Fill,
}

/// <summary>
/// **渲染状态** —— 已渲染 / 仅登记。
/// </summary>
/// <remarks>
/// <para>
/// <b>这个事实的权威在包声明 schema 的 <c>rendered</c> 字段</b>（<c>KindSpec.Rendered</c>）；
/// 本枚举是渲染侧对同一事实的**本地账位** —— 不是为了"再存一份"，而是因为方向禁令不允许
/// <c>Ui</c> 在运行期读 <c>Hosting</c> 的 schema（<c>Ui</c> 只引用 <c>Sdk</c> + <c>Core.Api</c>）。
/// </para>
/// <para>
/// <b>"两处同口径"不靠人记</b>：一致性由架构用例逐 kind 交叉守住（声明面只登记而渲染侧给
/// <c>Rendered</c>、或反过来，都会当场报红）⇒ 谁改了其中一处、忘了另一处，是<b>编译后立刻可见</b>的失败，
/// 不是"界面上少一种控件"这种静默后果。
/// </para>
/// <para>
/// <c>DeclarationOnly</c> 的 kind **声明合法、不拒绝加载**，框架给**一等公民占位**（统一外形 + 自己的主题槽）
/// 并记一条警告 —— 占位是一个**类型**，不是一个"别的控件兜底"。
/// </para>
/// </remarks>
public enum ControlRenderState
{
    /// <summary>已渲染（有对应的表面控件类型）。</summary>
    Rendered,

    /// <summary>仅登记（尚未实现；落统一占位，声明合法）。</summary>
    DeclarationOnly,
}

/// <summary>
/// 一个控件**专有语义键**在描述里的登记。
/// </summary>
/// <remarks>
/// <see cref="Default"/> 是 <see langword="null"/> 与"缺省即空字符串"的分界：
/// 前者表示无可陈述的字面缺省，后者是真实的空缺省。<b>缺省的唯一事实源是包声明 schema</b>
/// —— 这里写的是 <b>键的语义与归属</b>（读侧），schema 写的是缺省值（写侧）。两者**不互为副本**：
/// 本记录只被用来回答"这个键属于哪个控件、是不是必填"，不参与取值。
/// </remarks>
/// <param name="Name">声明键名（小驼峰）。</param>
/// <param name="JsonType">键的 JSON 类型（<c>string</c> / <c>bool</c> / <c>integer</c> / <c>number</c> / <c>array</c> / <c>object</c>）。</param>
/// <param name="Required">是否必填（必填而缺失 ⇒ 硬伤；由读取器判定）。</param>
/// <param name="Default">字面缺省；<see langword="null"/> 表示无可陈述的字面缺省。</param>
public sealed record ControlPropertySpec(
    string Name,
    string JsonType,
    bool Required = false,
    string? Default = null);

/// <summary>
/// 一个控件的**描述** —— 渲染侧关于"这个 kind 怎么长、要什么"的唯一登记。
/// </summary>
/// <remarks>
/// <para>
/// 构造参数全部必填、无默认值、无可选参数：漏填任何一个维度 **⇒ 编译失败**。
/// 因此"描述完备"由类型系统保证，不进测试。
/// </para>
/// <para>
/// <b>刻意不设"分类"与"编号"轴</b>：控件名称本身就是唯一的分类 —— 看到 <c>textInput</c> 就知道
/// 它是什么。它*用来干什么*（当输入框、当只读显示器、当触发件）由**模块开发者决定**，
/// 并据此配置对应属性；框架不按"用途"分桶。
/// </para>
/// <para>
/// 本类型是**数据**：不含行为、不含外观数值、不含几何缺省值字面量。外观全部落在
/// <see cref="ThemeSlot"/> 指到的主题资源里。
/// </para>
/// </remarks>
/// <param name="Kind">声明键名：小驼峰 <c>kind</c>，与包声明 schema 的 kind 名逐字一致。</param>
/// <param name="AcceptsChildren">是否接受 <c>children</c>。</param>
/// <param name="OwnsLayoutKeys">是否**自带排布键**（<c>layout</c>/<c>overflow</c>/<c>padding</c>/<c>spacing</c>…）。</param>
/// <param name="Roles">允许的绑定角色（至多一个；空集合 = 无绑定）。</param>
/// <param name="SemanticProperties">该控件专有的语义键（键名与归属；缺省值不在此）。</param>
/// <param name="GeometryStrategy">块轴尺寸策略（排布引擎按它算余量，不按 C# 类型）。</param>
/// <param name="PresentationSources">呈现状态从哪些来源推出。</param>
/// <param name="FormatCapabilities">格式能力。</param>
/// <param name="ThemeSlot">主题槽（外观落点）。</param>
/// <param name="RenderState">渲染状态（已渲染 / 仅登记）。</param>
public sealed record ControlDescriptor(
    string Kind,
    bool AcceptsChildren,
    bool OwnsLayoutKeys,
    IReadOnlyList<ControlRole> Roles,
    IReadOnlyList<ControlPropertySpec> SemanticProperties,
    SurfaceGeometryStrategy GeometryStrategy,
    IReadOnlyList<PresentationSource> PresentationSources,
    IReadOnlyList<FormatCapability> FormatCapabilities,
    string ThemeSlot,
    ControlRenderState RenderState)
{
    /// <summary>该控件的唯一绑定角色；无绑定 ⇒ <see cref="ControlRole.None"/>。</summary>
    public ControlRole Role => Roles.Count > 0 ? Roles[0] : ControlRole.None;

    /// <summary>是否已渲染（有对应的表面控件类型）。</summary>
    public bool IsRendered => RenderState is ControlRenderState.Rendered;

    /// <summary>是否接受子节点。</summary>
    public bool IsContainer => AcceptsChildren;
}
