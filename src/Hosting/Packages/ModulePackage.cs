using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Hosting.Packages;

/// <summary>
/// 已加载并校验通过的模块包（manifest + 实现绑定 + 包目录）。
/// </summary>
/// <param name="Manifest">包清单。</param>
/// <param name="Id">解析后的模块标识（已与代码级声明校验一致）。</param>
/// <param name="PackageDirectory">包目录。</param>
/// <param name="AssemblyPath">实现程序集路径。</param>
/// <param name="EntryType">模块入口类型。</param>
/// <param name="DefinitionAttribute">代码级声明（<c>[ModuleDefinition]</c>）。</param>
/// <param name="PortSpecs">视图 XAML 声明的端口（空 = 使用 C# `DeclarePort` 兼容路径）。</param>
public sealed record ModulePackage(
    ModuleManifest Manifest,
    ModuleId Id,
    string PackageDirectory,
    string AssemblyPath,
    Type EntryType,
    ModuleDefinitionAttribute DefinitionAttribute,
    IReadOnlyList<PortDeclarationSpec> PortSpecs);

/// <summary>
/// 包加载结果：**失败是正常结果**（返回原因，不抛异常）——失败只影响该包，不影响既有运行。
/// </summary>
/// <param name="Package">成功时的包；失败为 <see langword="null"/>。</param>
/// <param name="FailureReason">失败原因（面向用户的可读描述）。</param>
public sealed record PackageLoadResult(ModulePackage? Package, string? FailureReason)
{
    /// <summary>是否加载成功。</summary>
    public bool Succeeded => Package is not null;

    /// <summary>构造成功结果。</summary>
    public static PackageLoadResult Success(ModulePackage package) => new(package, null);

    /// <summary>构造失败结果。</summary>
    public static PackageLoadResult Failure(string reason) => new(null, reason);
}
