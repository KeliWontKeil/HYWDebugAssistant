using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Infrastructure.Storage;

namespace HYWDebugAssistant.Core.Persistence;

/// <summary>
/// 默认组合目录的**文件/包列表**（只读文件元数据；不解析组合内容）。
/// </summary>
/// <remarks>
/// 新格式 = 一个包目录（含 <c>composite.json</c>）；旧扁平 <c>*.json</c> 继续可读。
/// 目录不存在 / 不可读 ⇒ 返回空列表（首次运行是正常状态）；不抛异常。
/// </remarks>
public static class CompositeFileStore
{
    /// <summary>旧组合封面文件名：与组合同主名的 <c>.preview.png</c>。</summary>
    public const string LegacyCoverSuffix = ".preview.png";

    /// <summary>取得组合文档对应的封面路径（不判断是否存在）。</summary>
    /// <param name="compositePath">组合 JSON/文档路径。</param>
    public static string CoverPathFor(string compositePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositePath);
        var full = Path.GetFullPath(compositePath);
        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException(CoreText.Get("core.composite.coverPathMissingDirectory"), nameof(compositePath));

        if (string.Equals(Path.GetFileName(full), StoragePaths.CompositeDocumentFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(directory, StoragePaths.CompositeCoverFileName);
        }

        var stem = Path.GetFileNameWithoutExtension(compositePath);
        return Path.Combine(directory, stem + LegacyCoverSuffix);
    }

    /// <summary>包目录的文档路径。</summary>
    public static string DocumentPathFor(string packageDirectory) =>
        Path.Combine(packageDirectory, StoragePaths.CompositeDocumentFileName);

    /// <summary>扫描一个目录下的组合包与旧组合文件。</summary>
    /// <param name="directory">目录（通常 = 默认组合目录）。</param>
    public static IReadOnlyList<CompositeFileView> Scan(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            var result = new List<CompositeFileView>();
            var packageStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageDirectory in Directory.EnumerateDirectories(directory))
            {
                var document = DocumentPathFor(packageDirectory);
                if (!File.Exists(document))
                {
                    continue;
                }

                var info = new FileInfo(document);
                var name = Path.GetFileName(packageDirectory);
                packageStems.Add(name);
                var cover = CoverPathFor(document);
                result.Add(new CompositeFileView(
                    document,
                    StoragePaths.CompositeDocumentFileName,
                    new DateTimeOffset(info.LastWriteTime),
                    info.Length,
                    PreviewImagePath: File.Exists(cover) ? cover : null,
                    DirectoryPath: info.DirectoryName,
                    IsPackage: true,
                    DisplayName: name));
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (packageStems.Contains(name))
                {
                    continue;
                }

                var info = new FileInfo(path);
                var cover = CoverPathFor(path);
                result.Add(new CompositeFileView(
                    info.FullName,
                    info.Name,
                    new DateTimeOffset(info.LastWriteTime),
                    info.Length,
                    PreviewImagePath: File.Exists(cover) ? cover : null,
                    DirectoryPath: info.DirectoryName,
                    IsPackage: false,
                    DisplayName: name));
            }

            return [.. result
                .OrderByDescending(file => file.LastWriteTime)
                .ThenBy(file => file.DisplayName ?? file.FileName, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
