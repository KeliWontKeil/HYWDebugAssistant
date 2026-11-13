using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Values;

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
///   <item>引用的宿主程序集**在宿主里存在**（旧包引用模块侧 <c>Access</c> 直接拒绝；不比较版本）；</item>
///   <item>入口类型存在且实现 <see cref="IModule"/>；</item>
///   <item>入口类型带 <c>[ModuleDefinition]</c> 声明，且其标识与清单**一致**。</item>
/// </list>
/// <para>
/// <b>只做结构校验</b>：依赖存在性的判定需要注册表信息，由调用方（L4/注册流程）完成——
/// 见 <see cref="Registry.ModuleRegistry"/>。
/// </para>
/// <para>
/// <b>不做版本门禁</b>：模块与宿主之间没有"契约版本区间"。按旧 SDK 编译的模块在加载/实例化失败时，
/// 错误文案带上**它编译时引用的 SDK 版本**，用于判断"是不是该重编"（见 <see cref="TryReadReferencedSdkVersion"/>）。
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

        // **清单声明的资源目录必须真的存在**（这条字段过去只被构建期审计读、运行期完全忽略——
        // 模块作者写错路径时，界面只在"少一个图标"时才暴露，且没有任何提示）。
        foreach (var resource in manifest.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource)
                || Path.IsPathRooted(resource)
                || resource.Split('\\', '/').Any(segment => segment == ".."))
            {
                return PackageLoadResult.Failure(HostingText.Get("hosting.package.resourceInvalid", resource));
            }

            if (!Directory.Exists(Path.Combine(packageDirectory, resource)))
            {
                return PackageLoadResult.Failure(HostingText.Get("hosting.package.resourceMissing", resource));
            }
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
            var message = HostingText.Get("hosting.package.assemblyLoadFailed", ex.Message);
            if (TryReadReferencedSdkVersion(assemblyPath) is { } sdkVersion)
            {
                message += HostingText.Get("hosting.package.sdkReference", SdkAssemblyName, sdkVersion);
            }

            return PackageLoadResult.Failure(message);
        }

        if (ValidateHostAssemblies(assembly) is { } referenceFailure)
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

        // 端口声明来自模块视图 XAML 的**源文件**（构建时随包复制到 `views/`）。
        // 没有 views/ ⇒ 空（旧模块仍走 C# `DeclarePort`）；有错 ⇒ 拒绝该包并给可读原因。
        var portSpecs = PortSpecReader.ReadDirectory(
            Path.Combine(packageDirectory, "views"),
            assembly);

        if (portSpecs.Errors.Count > 0)
        {
            var first = portSpecs.Errors[0];
            return PackageLoadResult.Failure(HostingText.Get(
                "hosting.package.portSpecsInvalid",
                Path.GetFileName(first.FilePath ?? packageDirectory),
                first.Code.ToString(),
                first.Detail));
        }

        var valueSpecs = ValueSpecReader.ReadDirectory(
            Path.Combine(packageDirectory, "views"),
            assembly);

        if (valueSpecs.Errors.Count > 0)
        {
            var first = valueSpecs.Errors[0];
            return PackageLoadResult.Failure(HostingText.Get(
                "hosting.package.valueSpecsInvalid",
                Path.GetFileName(first.FilePath ?? packageDirectory),
                first.Code.ToString(),
                first.Detail));
        }

        var package = new ModulePackage(
            manifest,
            declaredId,
            Path.GetFullPath(packageDirectory),
            assemblyPath,
            entryType,
            attribute,
            portSpecs.Specs,
            valueSpecs.Specs);

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
    private const string SdkTestingAssemblyName = "HYWDebugAssistant.Sdk.Testing";

    /// <summary>
    /// 校验模块对**宿主程序集**的引用：引用模块侧的旧 <c>Access</c> ⇒ 拒绝；
    /// 引用**作者包**（契约自检套件）⇒ 拒绝；引用本应随宿主提供却缺席的 SDK 程序集 ⇒ 拒绝。
    /// **不比较版本**。
    /// </summary>
    private static string? ValidateHostAssemblies(Assembly assembly)
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

            // 作者包只服务测试项目；模块运行时引用它 = 包会把它带进模块目录、且宿主不提供 ⇒ 明确拒绝。
            if (string.Equals(name, SdkTestingAssemblyName, StringComparison.Ordinal))
            {
                return HostingText.Get("hosting.package.authorKitReference");
            }

            if (!IsHostAssemblyName(name))
            {
                continue;
            }

            if (!IsHostAssemblyPresent(name))
            {
                return HostingText.Get("hosting.package.hostAssemblyMissing", name);
            }
        }

        return null;
    }

    private static bool IsHostAssemblyName(string name) =>
        string.Equals(name, SdkAssemblyName, StringComparison.Ordinal)
        || string.Equals(name, SdkUiAssemblyName, StringComparison.Ordinal)
        || string.Equals(name, SdkAccessKitAssemblyName, StringComparison.Ordinal);

    private static bool IsHostAssemblyPresent(string assemblyName)
    {
        if (string.Equals(assemblyName, typeof(IModule).Assembly.GetName().Name, StringComparison.Ordinal))
        {
            return true;
        }

        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (File.Exists(path))
        {
            return true;
        }

        return AppDomain.CurrentDomain.GetAssemblies().Any(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal));
    }

    /// <summary>
    /// 读模块程序集**编译时引用的 SDK 版本**（只作加载失败时的诊断信息，不参与任何判定）。
    /// </summary>
    private static string? TryReadReferencedSdkVersion(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return null;
            }

            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                if (!string.Equals(reader.GetString(reference.Name), SdkAssemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                return reference.Version.ToString();
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            // 诊断信息读不到就算了：失败原因本身仍然返回。
        }

        return null;
    }
}
