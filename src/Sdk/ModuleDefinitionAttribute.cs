namespace HYWDebugAssistant.Sdk;

/// <summary>
/// 模块声明特性：承载 Definition **六项声明**中可在编译期静态表达的部分。
/// </summary>
/// <remarks>
/// 声明仅在**模块包加载/注册期读取一次**，随后转为不可变元数据缓存；运行期热路径零反射。
/// 未在此表达的两项（成员声明、视图声明）由模块类型在实例化时通过成员注册表达。
/// </remarks>
/// <example>
/// <code>
/// [ModuleDefinition("hyw", "serial-port", "1.0.0",
///     DisplayName = "串口收发",
///     Description = "串口数据的收发与解析入口",
///     Capabilities = ModuleCapability.SerialPort)]
/// public sealed class SerialPortModule : IModule { /* ... */ }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ModuleDefinitionAttribute : Attribute
{
    /// <summary>创建模块声明。</summary>
    /// <param name="author">作者命名空间（<c>[a-z0-9-]</c>）。</param>
    /// <param name="name">模块名（<c>[a-z0-9-]</c>）。</param>
    /// <param name="version">版本（SemVer 三段式）。</param>
    public ModuleDefinitionAttribute(string author, string name, string version)
    {
        Id = ModuleId.Create(author, name, version);
    }

    /// <summary>模块标识（结构化三元组）。</summary>
    public ModuleId Id { get; }

    /// <summary>目录展示名（不构成框架业务分类，P16）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 目录展示名的**文本表键**（拥有者 = 模块程序集；命中则优先于 <see cref="DisplayName"/>）。
    /// </summary>
    /// <remarks>空 = 不用表，回退到 <see cref="DisplayName"/> / 入口类型名。</remarks>
    public string DisplayNameKey { get; init; } = string.Empty;

    /// <summary>描述（目录展示用）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 描述的**文本表键**（拥有者 = 模块程序集；命中则优先于 <see cref="Description"/>）。
    /// </summary>
    /// <remarks>空 = 不用表，回退到 <see cref="Description"/>。</remarks>
    public string DescriptionKey { get; init; } = string.Empty;

    /// <summary>可选标签（仅供用户检索，**不是框架分类**）。</summary>
    public string[] Tags { get; init; } = [];

    /// <summary>所需能力（加载时透明提示，不拦截）。</summary>
    public ModuleCapability Capabilities { get; init; } = ModuleCapability.None;

    /// <summary>依赖的其他模块标识字面量（如 <c>hyw/protocol-core/1.0.0</c>）；缺失依赖 → Issue，不静默降级。</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>
    /// 框架兼容范围（如 <c>&gt;=1.0.0 &lt;2.0.0</c>）；加载期校验，不满足即拒绝注册。
    /// </summary>
    public string FrameworkCompatibility { get; init; } = string.Empty;

    /// <summary>是否参与数据活动（供 L2/L3 决定承载方式；不构成框架分类）。</summary>
    public bool ParticipatesInDataFlow { get; init; } = true;

    /// <summary>
    /// 模块窗口的**默认尺寸**（DIP）。<c>0</c> = 用框架缺省。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 窗口尺寸是**模块的形态事实**（这个模块天生多大），因此写在代码声明里，
    /// 与口、类型、角色同处 —— 它是编译期可校验的常量，不是运行期读来的数据。
    /// </para>
    /// <para>
    /// 内容排不下时由内容区滚动，框架**不按内容反过来算窗口尺寸**。
    /// </para>
    /// </remarks>
    public double WindowWidth { get; init; }

    /// <summary>窗口默认高（DIP）；<c>0</c> = 框架缺省。</summary>
    /// <remarks>
    /// <para>
    /// 有视图的模块，实际初始高度由视图根 `.axaml` 的 `MinHeight` 加窗口 chrome 决定；
    /// 这里的值只是无视图时的兜底（架构规则要求尺寸声明可被加载期读取）。
    /// </para>
    /// <para>兜底值应与视图内容一致，避免视图加载失败时把紧凑模块拉成统一高度。</para>
    /// </remarks>
    public double WindowHeight { get; init; }

    /// <summary>窗口最小尺寸（DIP）；<c>0</c> = 用框架地板。</summary>
    /// <remarks>
    /// <para>地板存在的理由是端口标记的沿边几何：窗口比它窄会把标记裁掉一截。</para>
    /// <para>
    /// 用 <c>0</c> 当"未声明"：特性参数的有效类型里没有可空数（<c>double?</c> 不能做命名参数），
    /// 而 0 宽高的窗口没有意义 —— 于是它天然是一个不可能被真正想要的哨兵值。
    /// </para>
    /// </remarks>
    public double MinWidth { get; init; }

    /// <inheritdoc cref="MinWidth"/>
    public double MinHeight { get; init; }

    /// <summary>
    /// 本模块的**视图工厂类型**（实现 <c>HYWDebugAssistant.Sdk.Ui.IModuleViewFactory</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 框架按这个类型**每个实例造一次视图**（类型需有公共无参构造）。
    /// 用类型而非实例：模块对象本身是有状态、受生命周期约束的，视图不能把它的第二份实例造出来。
    /// </para>
    /// <para>
    /// 不声明 = 这个模块没有自绘界面，窗口只显示身份与状态（合法形态，不是错误）。
    /// </para>
    /// </remarks>
    public Type? ViewFactory { get; init; }
}
