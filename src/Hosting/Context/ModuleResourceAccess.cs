using System.Reflection;
using HYWDebugAssistant.Sdk.Resources;

namespace HYWDebugAssistant.Hosting.Context;

/// <summary>
/// **模块自有资源包的只读访问**：根固定为模块目录下的 `resources/`，只读、只读自己的包。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不越界</b>：类别必须是**单段目录名**，相对路径不得含 `..` / 盘符 / 前导分隔符；
///     拼接后的绝对路径还必须落在 `resources/` 之内（双保险，防符号链接式的绕过）；</item>
///   <item><b>失败不抛</b>：文件不存在 / 路径非法 / IO 出错都返回 <see langword="false"/>；</item>
///   <item><b>标准 kind 不经过这里</b>：`i18n` / `icons` 由框架的文本 / 图标目录按拥有者读，
///     模块用本访问面读的应当是**自定义 kind**。</item>
/// </list>
/// </remarks>
public sealed class ModuleResourceAccess : IResourceAccess
{
    private readonly string _root;

    /// <summary>创建访问面。</summary>
    /// <param name="resourcesRoot">模块目录下的 `resources/` 绝对路径。</param>
    public ModuleResourceAccess(string resourcesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesRoot);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resourcesRoot));
    }

    /// <summary>按模块程序集定位它的 `resources/`（模块包 / 开发与测试输出同一形态）。</summary>
    /// <param name="assembly">模块入口程序集。</param>
    public static ModuleResourceAccess ForModule(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var directory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Module assembly has no on-disk location; resources/ cannot be resolved.", nameof(assembly));
        }

        return new ModuleResourceAccess(Path.Combine(directory, ResourceLayout.ResourcesDirectoryName));
    }

    /// <inheritdoc />
    public bool TryOpen(string kind, string relativePath, out Stream? stream)
    {
        stream = null;

        if (!IsSingleSegment(kind) || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, kind, Path.Combine(segments)));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (!File.Exists(candidate))
            {
                return false;
            }

            stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSingleSegment(string kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && !kind.Contains('/', StringComparison.Ordinal)
        && !kind.Contains('\\', StringComparison.Ordinal)
        && !kind.Contains(':', StringComparison.Ordinal)
        && kind is not "." and not "..";
}
