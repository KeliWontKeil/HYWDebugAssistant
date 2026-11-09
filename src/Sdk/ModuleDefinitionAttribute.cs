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
/// [ModuleDefinition("hyw", "serial-port",
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
    public ModuleDefinitionAttribute(string author, string name)
    {
        Id = ModuleId.Create(author, name);
    }

    /// <summary>模块标识（作者/模块名，两段）。</summary>
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

    /// <summary>依赖的其他模块标识字面量（如 <c>hyw/protocol-core</c>）；缺失依赖 → Issue，不静默降级。</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>是否参与数据活动（供 L2/L3 决定承载方式；不构成框架分类）。</summary>
    public bool ParticipatesInDataFlow { get; init; } = true;

    /// <summary>
    /// 旧窗口默认宽（DIP）。**已废弃**：尺寸唯一事实是视图根 `.axaml` 的 `MinWidth/MinHeight`（纯内容区）。
    /// </summary>
    /// <remarks>
    /// 保留属性只为二进制兼容；框架不再读取，内置模块不得声明。
    /// 新模块请只在视图根声明内容尺寸，chrome 由框架加。
    /// </remarks>
    public double WindowWidth { get; init; }

    /// <summary>旧窗口默认高（DIP）。**已废弃**，语义同 <see cref="WindowWidth"/>。</summary>
    public double WindowHeight { get; init; }

    /// <summary>旧窗口最小宽（DIP）。**已废弃**，语义同 <see cref="WindowWidth"/>。</summary>
    public double MinWidth { get; init; }

    /// <summary>旧窗口最小高（DIP）。**已废弃**，语义同 <see cref="WindowWidth"/>。</summary>
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
