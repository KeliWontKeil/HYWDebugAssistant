using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace HYWDebugAssistant.Hosting.Packages;

/// <summary>
/// **模块包的依赖解析**：模块**私有依赖**仍从包目录解析；
/// `HYWDebugAssistant.*` 与宿主目录同名程序集**绝不从包目录加载**。
/// </summary>
/// <remarks>
/// <para>
/// 背景：模块由 <see cref="Assembly.LoadFrom(string)"/> 加载，依赖只在**包目录**里；而运行期解析
/// 只看宿主（应用）的目录与它的依赖清单 ⇒ 包里的第三方程序集找不到（表现为模块功能静默失效）。
/// </para>
/// <para>
/// 更隐蔽的一档：像 <c>System.IO.Ports</c> 这种带**平台实现分片**的包，平铺在包根的那份是
/// "平台无关的引用版"（方法体直接抛 <c>PlatformNotSupportedException</c>），真正的实现躺在
/// <c>runtimes/&lt;rid&gt;/lib/&lt;tfm&gt;/</c> 下 ⇒ 必须**按当前平台优先**选那一份，
/// 否则会出现"在 Windows 上报告只支持 Windows"这种自相矛盾的失败。
/// </para>
/// <para>
/// 注册是幂等的全局钩子：只在**默认加载上下文**上挂一次，逐个包目录按序探测。
/// 找不到就交还默认行为（<see langword="null"/>），不改变既有模块的解析结果。
/// </para>
/// </remarks>
internal static class ModuleDependencyResolver
{
    private static readonly List<string> PackageDirectories = [];

    /// <summary>宿主目录已提供的托管程序集（包目录里的同名副本绝不被选中）。</summary>
    private static readonly Lazy<HashSet<string>> HostProvidedAssemblies = new(CollectHostProvided);

    private static readonly Lock Gate = new();

    private static bool _hooked;

    /// <summary>把一个模块包目录登记为**依赖探测路径**。</summary>
    /// <param name="packageDirectory">包目录（含模块程序集与它自带的依赖）。</param>
    public static void Register(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var full = Path.GetFullPath(packageDirectory);

        lock (Gate)
        {
            if (!PackageDirectories.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                PackageDirectories.Add(full);
            }

            if (_hooked)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving += Resolve;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveUnmanaged;
            _hooked = true;
        }
    }

    /// <summary>托管程序集：**平台实现优先**，再退回包根那份平铺副本。</summary>
    /// <remarks>
    /// 宿主已提供的程序集（`HYWDebugAssistant.*` 与宿主目录里的同名 DLL）**绝不从包目录加载**：
    /// 否则旧包里的 Sdk/Access 副本会遮蔽宿主，造成类型同一性被破坏或版本混装。
    /// </remarks>
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is not { Length: > 0 } simpleName)
        {
            return null;
        }

        if (simpleName.StartsWith("HYWDebugAssistant.", StringComparison.Ordinal)
            || HostProvidedAssemblies.Value.Contains(simpleName))
        {
            return null;
        }

        foreach (var directory in Snapshot())
        {
            foreach (var candidate in ManagedCandidates(directory, simpleName))
            {
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>原生库：包内 <c>runtimes/&lt;rid&gt;/native/</c> 优先。</summary>
    private static IntPtr ResolveUnmanaged(Assembly? requesting, string libraryName)
    {
        if (libraryName is not { Length: > 0 })
        {
            return IntPtr.Zero;
        }

        foreach (var directory in Snapshot())
        {
            foreach (var candidate in NativeCandidates(directory, libraryName))
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                {
                    return handle;
                }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>托管程序集的候选路径（按优先级）。</summary>
    private static IEnumerable<string> ManagedCandidates(string directory, string simpleName)
    {
        foreach (var rid in RidCandidates())
        {
            // runtimes/<rid>/lib/<tfm>/ —— 真正的平台实现
            var libRoot = Path.Combine(directory, "runtimes", rid, "lib");
            if (Directory.Exists(libRoot))
            {
                foreach (var tfm in Directory.EnumerateDirectories(libRoot))
                {
                    yield return Path.Combine(tfm, simpleName + ".dll");
                }
            }
        }

        // 包根那份（有的包就只有这一份；平台分片包的那份是引用版，所以排在后面）
        yield return Path.Combine(directory, simpleName + ".dll");
    }

    /// <summary>原生库的候选路径（按优先级）。</summary>
    private static IEnumerable<string> NativeCandidates(string directory, string libraryName)
    {
        foreach (var rid in RidCandidates())
        {
            var native = Path.Combine(directory, "runtimes", rid, "native");
            if (!Directory.Exists(native))
            {
                continue;
            }

            // 各平台的命名习惯都要试：libX.so / X.dll / libX.dylib
            yield return Path.Combine(native, libraryName);
            yield return Path.Combine(native, libraryName + ".dll");
            yield return Path.Combine(native, "lib" + libraryName + ".so");
            yield return Path.Combine(native, "lib" + libraryName + ".dylib");
        }
    }

    /// <summary>当前平台的 RID 候选（精确 RID → 可移植 RID）。</summary>
    private static IEnumerable<string> RidCandidates()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;

        if (!string.IsNullOrEmpty(rid))
        {
            yield return rid;

            var portable = rid.Split('-')[0];
            if (!string.Equals(portable, rid, StringComparison.OrdinalIgnoreCase))
            {
                yield return portable;
            }
        }
    }

    private static string[] Snapshot()
    {
        lock (Gate)
        {
            return [.. PackageDirectories];
        }
    }

    /// <summary>收集宿主输出目录根部的托管程序集名（不看包目录）。</summary>
    private static HashSet<string> CollectHostProvided()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                _ = names.Add(Path.GetFileNameWithoutExtension(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 宿主目录不可读 ⇒ 只保留 `HYWDebugAssistant.*` 前缀保护，不改变既有解析。
        }

        return names;
    }
}
