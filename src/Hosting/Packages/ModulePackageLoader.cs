using System.Reflection;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Hosting.Packages;

/// <summary>
/// 模块包加载器：**显式加载**（指定路径/来源目录）——校验失败即返回原因，不影响既有运行。
/// </summary>
/// <remarks>
/// 加载步骤（任一步失败即终止并给出原因）：
/// <list type="number">
///   <item>包目录存在且含 <c>module.json</c>；</item>
///   <item>清单中的模块标识字面量合法（<see cref="ModuleId.TryParse"/>）；</item>
///   <item>实现程序集存在；</item>
///   <item>引用的宿主程序集版本不高于宿主（旧包引用模块侧 <c>Access</c> 直接拒绝）；</item>
///   <item>入口类型存在且实现 <see cref="IModule"/>；</item>
///   <item>入口类型带 <c>[ModuleDefinition]</c> 声明，且其标识与清单**一致**。</item>
/// </list>
/// <para>
/// <b>只做结构校验</b>：框架兼容范围与依赖存在性的判定需要注册表信息，由调用方（L4/注册流程）完成——
/// 见 <see cref="Registry.ModuleRegistry"/> 与 M1-4 后续步骤。
/// </para>
/// </remarks>
public static class ModulePackageLoader
{
    /// <summary>加载一个模块包目录。</summary>
    public static PackageLoadResult Load(string packageDirectory)
    {
        if (!ModuleManifestReader.TryRead(packageDirectory, out var manifest, out var readError))
        {
            return PackageLoadResult.Failure(readError!);
        }

        if (!ModuleId.TryParse(manifest!.Id, out var declaredId))
        {
            return PackageLoadResult.Failure(
                HostingText.Get("hosting.package.manifestIdInvalid", manifest.Id));
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            return PackageLoadResult.Failure(HostingText.Get("hosting.package.entryPointMissing"));
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(packageDirectory, manifest.EntryAssembly));
        if (!File.Exists(assemblyPath))
        {
            return PackageLoadResult.Failure(HostingText.Get(
                "hosting.package.assemblyMissing",
                assemblyPath,
                ConfigurationDirectoryName()));
        }

        Assembly assembly;
        try
        {
            // **包目录先成为依赖探测路径**：模块包自带依赖（含平台实现分片）——
            // 不登记这一步，包里的第三方程序集在运行期解析不到（症状是模块功能静默失效）。
            ModuleDependencyResolver.Register(packageDirectory);
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            return PackageLoadResult.Failure(HostingText.Get("hosting.package.assemblyLoadFailed", ex.Message));
        }

        if (ValidateAssemblyReferences(assembly) is { } referenceFailure)
        {
            return PackageLoadResult.Failure(referenceFailure);
        }

        var entryType = assembly.GetType(manifest.EntryType, throwOnError: false);
        if (entryType is null)
        {
            return PackageLoadResult.Failure(HostingText.Get("hosting.package.entryTypeMissing", manifest.EntryType));
        }
        if (!typeof(IModule).IsAssignableFrom(entryType))
        {
            return PackageLoadResult.Failure(HostingText.Get("hosting.package.entryTypeNotModule", manifest.EntryType));
        }

        var attribute = entryType.GetCustomAttribute<ModuleDefinitionAttribute>();
        if (attribute is null)
        {
            return PackageLoadResult.Failure(HostingText.Get("hosting.package.definitionAttributeMissing", manifest.EntryType));
        }

        if (attribute.Id != declaredId)
        {
            return PackageLoadResult.Failure(
                HostingText.Get("hosting.package.declarationMismatch", declaredId, attribute.Id));
        }

        var package = new ModulePackage(
            manifest,
            declaredId,
            Path.GetFullPath(packageDirectory),
            assemblyPath,
            entryType,
            attribute);

        return PackageLoadResult.Success(package);
    }

    /// <summary>当前构建配置名（用于把"实现程序集不在包目录里"的提示指向真实的产物目录）。</summary>
    private static string ConfigurationDirectoryName() =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private const string AccessAssemblyName = "HYWDebugAssistant.Access";
    private const string SdkAssemblyName = "HYWDebugAssistant.Sdk";
    private const string SdkUiAssemblyName = "HYWDebugAssistant.Sdk.Ui";
    private const string SdkAccessKitAssemblyName = "HYWDebugAssistant.Sdk.AccessKit";

    /// <summary>
    /// 校验模块对**宿主程序集**的引用：旧包引用模块侧 Access、或 SDK 版本高于宿主 ⇒ 拒绝。
    /// </summary>
    /// <remarks>
    /// 版本比较看 AssemblyRef 的**请求版本**：模块编译时绑定哪个 SDK，就要求宿主至少提供那个契约版本。
    /// 宿主程序集版本以**宿主目录里的文件**为准（不依赖它是否已被加载）。
    /// </remarks>
    private static string? ValidateAssemblyReferences(Assembly assembly)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            var name = reference.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (string.Equals(name, AccessAssemblyName, StringComparison.Ordinal))
            {
                return HostingText.Get("hosting.package.legacyAccessReference");
            }

            if (!string.Equals(name, SdkAssemblyName, StringComparison.Ordinal)
                && !string.Equals(name, SdkUiAssemblyName, StringComparison.Ordinal)
                && !string.Equals(name, SdkAccessKitAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            var hostVersion = HostVersionOf(name);
            if (hostVersion is null)
            {
                return HostingText.Get("hosting.package.hostAssemblyMissing", name);
            }

            if (reference.Version is { } requested && requested.CompareTo(hostVersion) > 0)
            {
                return HostingText.Get("hosting.package.sdkVersionTooHigh", name, requested, hostVersion);
            }
        }

        return null;
    }

    /// <summary>取宿主提供的程序集版本（先看宿主目录文件，再看已加载程序集）。</summary>
    private static Version? HostVersionOf(string assemblyName)
    {
        var sdkAssembly = typeof(IModule).Assembly;
        if (string.Equals(assemblyName, sdkAssembly.GetName().Name, StringComparison.Ordinal))
        {
            return sdkAssembly.GetName().Version;
        }

        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (File.Exists(path))
        {
            try
            {
                return AssemblyName.GetAssemblyName(path).Version;
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                // 落到"已加载程序集"兜底。
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name, assemblyName, StringComparison.Ordinal))
            ?.GetName().Version;
    }
}
