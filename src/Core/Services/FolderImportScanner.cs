using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Infrastructure.Storage;

namespace HYWDebugAssistant.Core.Services;

/// <summary>一个外层文件夹里发现的包目录。</summary>
/// <param name="ModuleDirectories">模块包目录（含 `module.json`）。</param>
/// <param name="CompositeDirectories">组合包目录（含 `composite.json`）。</param>
/// <param name="LimitReached">是否因为达到扫描上限而停止。</param>
internal sealed record FolderImportScan(
    IReadOnlyList<string> ModuleDirectories,
    IReadOnlyList<string> CompositeDirectories,
    bool LimitReached);

/// <summary>
/// **外层文件夹导入的扫描器**：向下最多 <see cref="MaxDepth"/> 层找模块包/组合包。
/// </summary>
/// <remarks>
/// 纯路径扫描，不复制、不改注册表，便于单测。包边界规则：
/// 目录含 <c>composite.json</c> ⇒ 组合包；否则含 <c>module.json</c> ⇒ 模块包；
/// 命中后不再向下（避免把包内部文件当成更多包）。隐藏/系统/重解析点目录不进入。
/// </remarks>
internal static class FolderImportScanner
{
    /// <summary>向下扫描的最大层数（根目录 = 0）。</summary>
    public const int MaxDepth = 3;

    /// <summary>一次扫描最多返回的包目录数。</summary>
    public const int MaxPackages = 200;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>扫描一个目录。</summary>
    /// <param name="root">外层文件夹。</param>
    /// <returns>模块包目录、组合包目录与是否达到上限。</returns>
    public static FolderImportScan Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new FolderImportScan([], [], false);
        }

        var modules = new List<string>();
        var composites = new List<string>();

        var full = Path.GetFullPath(root);
        if (AddPackage(full, modules, composites))
        {
            return new FolderImportScan(modules, composites, false);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { full };
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((full, 0));
        var limitReached = false;

        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();
            if (depth >= MaxDepth)
            {
                continue;
            }

            var children = EnumerateChildren(directory);
            foreach (var child in children)
            {
                if (modules.Count + composites.Count >= MaxPackages)
                {
                    limitReached = true;
                    break;
                }

                if (!seen.Add(child))
                {
                    continue;
                }

                if (AddPackage(child, modules, composites))
                {
                    continue;
                }

                if (depth + 1 < MaxDepth)
                {
                    pending.Enqueue((child, depth + 1));
                }
            }

            if (limitReached)
            {
                break;
            }
        }

        return new FolderImportScan(modules, composites, limitReached);
    }

    private static string[] EnumerateChildren(string directory)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(directory, "*", Options)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>目录自身是包 ⇒ 记下并返回 true（组合包优先）。</summary>
    private static bool AddPackage(string directory, List<string> modules, List<string> composites)
    {
        if (File.Exists(Path.Combine(directory, StoragePaths.CompositeDocumentFileName)))
        {
            composites.Add(directory);
            return true;
        }

        if (File.Exists(Path.Combine(directory, ModuleManifestReader.FileName)))
        {
            modules.Add(directory);
            return true;
        }

        return false;
    }
}
