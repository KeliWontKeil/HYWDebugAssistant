using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Sdk;

namespace HYWDebugAssistant.Hosting.Registry;

/// <summary>
/// 模块定义（**运行时可用的不可变元数据**）——由包清单 + 代码级声明合成，注册进 <see cref="ModuleRegistry"/>。
/// </summary>
/// <remarks>
/// 对应基线 §4.4 的"模块提供者的不可变能力发布物"：用户不可修改、不感知底层实现；
/// 内置模块与外部模块**共用同一模型**。
/// <para>声明在**加载期读取一次**，此后只读；运行期热路径不使用本类型。</para>
/// </remarks>
public sealed class ModuleDefinition
{
    /// <summary>构造模块定义（通常经 <see cref="FromPackage"/> 由已校验的包产生）。</summary>
    public ModuleDefinition(
        ModuleId id,
        string displayName,
        string description,
        IReadOnlyList<string> tags,
        ModuleCapability capabilities,
        IReadOnlyList<string> dependsOn,
        string? frameworkCompatibility,
        bool participatesInDataFlow,
        Type entryType,
        string packageDirectory,
        string assemblyPath,
        Type? viewFactory = null,
        double windowWidth = 0,
        double windowHeight = 0,
        double minWidth = 0,
        double minHeight = 0)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Tags = tags;
        Capabilities = capabilities;
        DependsOn = dependsOn;
        FrameworkCompatibility = frameworkCompatibility;
        ParticipatesInDataFlow = participatesInDataFlow;
        EntryType = entryType;
        PackageDirectory = packageDirectory;
        AssemblyPath = assemblyPath;
        ViewFactory = viewFactory;
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;
        MinWidth = minWidth;
        MinHeight = minHeight;
    }

    /// <summary>模块标识（结构化三元组，含版本）。</summary>
    public ModuleId Id { get; }

    /// <summary>目录展示名。</summary>
    public string DisplayName { get; }

    /// <summary>描述。</summary>
    public string Description { get; }

    /// <summary>标签（仅供检索，**不是框架分类**）。</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>所需能力（加载时透明提示，不拦截）。</summary>
    public ModuleCapability Capabilities { get; }

    /// <summary>依赖的其他模块标识字面量。</summary>
    public IReadOnlyList<string> DependsOn { get; }

    /// <summary>框架兼容范围（为空 = 不限制）。</summary>
    public string? FrameworkCompatibility { get; }

    /// <summary>是否参与数据活动。</summary>
    public bool ParticipatesInDataFlow { get; }

    /// <summary>模块入口类型（实现 <c>IModule</c>）。</summary>
    public Type EntryType { get; }

    /// <summary>包目录。</summary>
    public string PackageDirectory { get; }

    /// <summary>实现程序集路径。</summary>
    public string AssemblyPath { get; }

    /// <summary>
    /// 本模块的**视图工厂类型**（实现 <c>Hyw…Sdk.Ui.IModuleViewFactory</c>）；<see langword="null"/> = 无自绘界面。
    /// </summary>
    /// <remarks>
    /// 用类型而不是实例：视图每个实例各造一次，而模块对象本身有状态、不能为造视图再实例化一份。
    /// </remarks>
    public Type? ViewFactory { get; }

    /// <summary>窗口默认宽（DIP）；<c>0</c> = 框架缺省。</summary>
    public double WindowWidth { get; }

    /// <summary>窗口默认高（DIP）；<c>0</c> = 框架缺省。</summary>
    public double WindowHeight { get; }

    /// <summary>窗口最小宽（DIP）；<c>0</c> = 框架地板。</summary>
    public double MinWidth { get; }

    /// <summary>窗口最小高（DIP）；<c>0</c> = 框架地板。</summary>
    public double MinHeight { get; }

    /// <summary>由已校验的包构造定义。</summary>
    public static ModuleDefinition FromPackage(ModulePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var attribute = package.DefinitionAttribute;
        return new ModuleDefinition(
            package.Id,
            attribute.DisplayName,
            attribute.Description,
            attribute.Tags,
            attribute.Capabilities,
            attribute.DependsOn.Length > 0 ? attribute.DependsOn : package.Manifest.DependsOn,
            attribute.FrameworkCompatibility.Length > 0 ? attribute.FrameworkCompatibility : package.Manifest.FrameworkCompatibility,
            attribute.ParticipatesInDataFlow,
            package.EntryType,
            package.PackageDirectory,
            package.AssemblyPath,
            attribute.ViewFactory,
            attribute.WindowWidth,
            attribute.WindowHeight,
            attribute.MinWidth,
            attribute.MinHeight);
    }

    /// <inheritdoc />
    public override string ToString() => Id.ToString();
}
