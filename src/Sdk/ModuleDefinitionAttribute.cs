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

    /// <summary>描述（目录展示用）。</summary>
    public string Description { get; init; } = string.Empty;

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
}
