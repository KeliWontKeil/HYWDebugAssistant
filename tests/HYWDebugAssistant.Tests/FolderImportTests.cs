using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>外层文件夹批量导入：嵌套组合包、无包汇总。</summary>
[Collection(nameof(FileSystemImportTests))]
public class FolderImportTests
{
    [Fact]
    public void 批量导入_应导入两层内的多个组合包()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var root = Path.Combine(Path.GetTempPath(), "hyw-folder-import-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "a", "comp-a");
        var second = Path.Combine(root, "b", "deep", "comp-b");
        _ = Directory.CreateDirectory(first);
        _ = Directory.CreateDirectory(second);

        var before = services.GetCompositeFiles().Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            services.SaveComposite(first, "scan-a", ["s1"]).Succeeded.ShouldBeTrue();
            services.SaveComposite(second, "scan-b", ["s1"]).Succeeded.ShouldBeTrue();

            var result = services.ImportFolder(root);

            result.ModuleCount.ShouldBe(0);
            result.CompositeCount.ShouldBe(2);
            result.Failures.ShouldBeEmpty(string.Join("；", result.Failures));

            var imported = services.GetCompositeFiles()
                .Where(file => !before.Contains(file.Path))
                .Select(file => file.Path)
                .ToList();
            imported.Count.ShouldBe(2);
            imported.ShouldAllBe(path => File.Exists(path));
        }
        finally
        {
            TryDeleteDirectory(root);

            foreach (var path in services.GetCompositeFiles()
                         .Where(file => !before.Contains(file.Path))
                         .Select(file => Path.GetDirectoryName(file.Path))
                         .Where(directory => directory is { Length: > 0 })
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(path!);
            }
        }
    }

    [Fact]
    public void 批量导入_没有包时返回零计数且不产生失败()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var root = Path.Combine(Path.GetTempPath(), "hyw-folder-empty-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);

        try
        {
            var result = services.ImportFolder(root);

            result.ModuleCount.ShouldBe(0);
            result.CompositeCount.ShouldBe(0);
            result.Failures.ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
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
