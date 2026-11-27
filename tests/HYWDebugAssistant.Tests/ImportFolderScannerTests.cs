using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>外层文件夹扫描：2–3 层、包边界、组合优先、隐藏目录与无包。</summary>
[Collection(nameof(FileSystemImportTests))]
public class ImportFolderScannerTests
{
    [Fact]
    public void 扫描最多三层_命中包后不再下钻()
    {
        var root = NewRoot();

        try
        {
            CreatePackage(Path.Combine(root, "level1", "modA"), "module.json");
            CreatePackage(Path.Combine(root, "level1", "level2", "modB"), "module.json");

            var boundary = Path.Combine(root, "boundary");
            CreatePackage(boundary, "module.json");
            CreatePackage(Path.Combine(boundary, "inner"), "module.json");

            var mixed = Path.Combine(root, "mixed");
            CreatePackage(mixed, "module.json");
            CreatePackage(mixed, "composite.json");

            var scan = FolderImportScanner.Scan(root);

            scan.LimitReached.ShouldBeFalse();
            scan.ModuleDirectories.Select(Path.GetFullPath).ShouldBe(
                [
                    Path.GetFullPath(Path.Combine(root, "level1", "modA")),
                    Path.GetFullPath(Path.Combine(root, "level1", "level2", "modB")),
                    Path.GetFullPath(boundary),
                ],
                ignoreOrder: true);

            scan.ModuleDirectories.ShouldNotContain(Path.GetFullPath(Path.Combine(boundary, "inner")));
            scan.CompositeDirectories.ShouldBe([Path.GetFullPath(mixed)]);
            scan.ModuleDirectories.ShouldNotContain(Path.GetFullPath(mixed), "同一目录里组合包优先");

            // **分类深度 = 扫描深度**（唯一口径）：分类说"有包"，批量导入就必须找得到包。
            // 分类若自定一套更浅的规则，多层级文件夹会被判成"不认识"，
            // 而冲突查询按扫描器列得出包 —— 问的与做的不是同一批。
            using var runtime = new ModuleRuntime();
            var services = new RuntimeServices(runtime);

            services.ClassifyImportPath(root).ShouldBe(ImportPathKind.Folder);
            services.ClassifyImportPath(boundary).ShouldBe(ImportPathKind.ModulePackage);
            services.ClassifyImportPath(mixed).ShouldBe(ImportPathKind.CompositePackage);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void 跳过隐藏目录_空目录返回无包()
    {
        var root = NewRoot();

        try
        {
            var empty = Path.Combine(root, "empty");
            _ = Directory.CreateDirectory(empty);

            FolderImportScanner.Scan(empty).ModuleDirectories.ShouldBeEmpty();
            FolderImportScanner.Scan(empty).CompositeDirectories.ShouldBeEmpty();

            if (OperatingSystem.IsWindows())
            {
                var hidden = Path.Combine(root, "hidden");
                CreatePackage(hidden, "module.json");
                File.SetAttributes(hidden, FileAttributes.Hidden);

                FolderImportScanner.Scan(root).ModuleDirectories.ShouldNotContain(Path.GetFullPath(hidden));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hyw-folder-scan-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        return root;
    }

    private static void CreatePackage(string directory, string fileName)
    {
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "{}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
