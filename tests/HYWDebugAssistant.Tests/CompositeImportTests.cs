using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 组合文件检查/导入：默认目录扫描、缺失模块标注、导入复制与同名冲突的替换/跳过。
/// </summary>
[Collection(nameof(FileSystemImportTests))]
public class CompositeImportTests
{
    [Fact]
    public void 检查_应标注组合缺少的模块()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var path = NewDefaultFile();

        try
        {
            File.WriteAllText(path, """
                {
                  "name": "missing",
                  "instances": [
                    { "instanceId": "m1", "moduleId": "hyw/missing", "enabled": true }
                  ],
                  "networks": [],
                  "layout": {}
                }
                """);

            var file = services.GetCompositeFiles().Single(f => f.Path == path);
            file.ModuleCount.ShouldBe(1);
            file.ReadError.ShouldBeNull();
            file.MissingModules.ShouldNotBeNull().ShouldContain("hyw/missing");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void 导入同名组合_内容一字不差也必须询问而不是静默复用()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var sourceRoot = Path.Combine(Path.GetTempPath(), "hyw-import-same-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(sourceRoot, "same");
        _ = Directory.CreateDirectory(source);

        string? targetDocument = null;
        try
        {
            services.SaveComposite(source, "same", ["s1"]).Succeeded.ShouldBeTrue();

            var first = services.ImportComposite(source);
            first.Succeeded.ShouldBeTrue(string.Join("；", first.Failures));
            targetDocument = first.TargetPath;
            targetDocument.ShouldNotBeNull();
            var beforeBytes = File.ReadAllBytes(targetDocument!);

            // 源目录与默认目录里的那份**内容一字不差**、名字也相同：仍然必须问。
            // （按内容"复用"会让用户看不到任何提示，等于询问机制失效。）
            var conflicts = services.GetImportConflicts(source);
            conflicts.ShouldHaveSingleItem("同名同内容也是冲突");
            conflicts[0].TargetName.ShouldBe("same");

            var again = services.ImportComposite(source);

            again.Succeeded.ShouldBeFalse("没有决定时不得静默复用");
            again.Failures.ShouldNotBeEmpty();
            File.ReadAllBytes(targetDocument!).ShouldBe(beforeBytes, "失败时旧内容不得被改动");
            File.Exists(Path.Combine(Path.GetDirectoryName(targetDocument!)!, "same-2", "composite.json"))
                .ShouldBeFalse("不得静默改成 -2");
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            if (targetDocument is { Length: > 0 } && Path.GetDirectoryName(targetDocument) is { Length: > 0 } directory)
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    [Fact]
    public void 导入外层文件夹_只装组合包也应列出冲突()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var outerRoot = Path.Combine(Path.GetTempPath(), "hyw-import-outer-" + Guid.NewGuid().ToString("N"));
        var clashing = Path.Combine(outerRoot, "clash");
        var fresh = Path.Combine(outerRoot, "fresh");
        _ = Directory.CreateDirectory(clashing);
        _ = Directory.CreateDirectory(fresh);

        string? importedDirectory = null;
        try
        {
            services.SaveComposite(clashing, "clash", ["s1"]).Succeeded.ShouldBeTrue();
            services.SaveComposite(fresh, "fresh", ["s1"]).Succeeded.ShouldBeTrue();

            importedDirectory = Path.GetDirectoryName(services.ImportComposite(clashing).TargetPath!);

            // 分类与扫描同一口径：只装组合包的文件夹是"外层文件夹"，不是"不认识"。
            services.ClassifyImportPath(outerRoot).ShouldBe(ImportPathKind.Folder);

            // 外层文件夹（不含 module.json）走的是批量导入：同样要先给出冲突。
            var conflicts = services.GetImportConflicts(outerRoot);
            conflicts.ShouldHaveSingleItem();
            Path.GetFileName(conflicts[0].SourcePath).ShouldBe("composite.json");
            conflicts[0].TargetName.ShouldBe("clash");

            // **范围决定问什么**：本页只导组合包时不得问模块包冲突。
            services.GetImportConflicts(outerRoot, ImportScope.Modules).ShouldBeEmpty();
            services.GetImportConflicts(outerRoot, ImportScope.Composites).Count.ShouldBe(1);

            var result = services.ImportFolder(outerRoot);

            result.CompositeCount.ShouldBe(1, "不撞名的那个照常导入");
            result.Failures.ShouldNotBeEmpty("撞名那个在没人回答时必须失败");

            // **组合页入口对外层文件夹必须给同一个结论**：
            // 冲突列表里列过的项，答复之后必须真的能导进来 —— 只认"路径自身就是组合包"的
            // 入口做不到这点（它会报"组合文件不存在"）。
            // 重新问一次：上一步刚把 fresh 也导了，此刻两个名字都被占用。
            var pending = services.GetImportConflicts(outerRoot, ImportScope.Composites);
            pending.Count.ShouldBe(2);

            var decisions = pending.ToDictionary(
                conflict => conflict.SourcePath,
                _ => ImportConflictChoice.Replace,
                StringComparer.OrdinalIgnoreCase);

            var folder = services.ImportComposite(outerRoot, decisions);

            folder.Succeeded.ShouldBeTrue(string.Join("；", folder.Failures));
            folder.ImportedCount.ShouldBe(2, "文件夹里两个组合包都应落地");
            folder.TargetPath.ShouldBeNull("一次落地多个时不硬造一个代表项");
        }
        finally
        {
            TryDeleteDirectory(outerRoot);
            if (importedDirectory is { Length: > 0 })
            {
                TryDeleteDirectory(importedDirectory);
            }

            TryDeleteDirectory(Path.Combine(StoragePaths.CompositesRoot, "fresh"));
            TryDeleteDirectory(Path.Combine(StoragePaths.CompositesRoot, "clash"));
        }
    }

    [Fact]
    public void 导入组合_源就是默认目录里的那个包时应早退且不自我覆盖()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var sourceRoot = Path.Combine(Path.GetTempPath(), "hyw-import-self-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(sourceRoot, "self");
        _ = Directory.CreateDirectory(source);

        string? targetDocument = null;
        try
        {
            services.SaveComposite(source, "self", ["s1"]).Succeeded.ShouldBeTrue();
            targetDocument = services.ImportComposite(source).TargetPath;
            targetDocument.ShouldNotBeNull();
            var beforeBytes = File.ReadAllBytes(targetDocument!);
            var directory = Path.GetDirectoryName(targetDocument!)!;

            // 把默认目录里那个包再"导入"一次 = 什么都不该发生（尤其不能把自己删了）。
            var again = services.ImportComposite(directory);

            again.Succeeded.ShouldBeTrue(string.Join("；", again.Failures));
            again.TargetPath.ShouldBe(targetDocument);
            Directory.Exists(directory).ShouldBeTrue("早退不得删掉来源目录");
            File.ReadAllBytes(targetDocument!).ShouldBe(beforeBytes);
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            if (targetDocument is { Length: > 0 } && Path.GetDirectoryName(targetDocument) is { Length: > 0 } directory)
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    [Fact]
    public void 导入同名组合_选择替换应覆盖为目标内容且不产生副本()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var sourceRoot = Path.Combine(Path.GetTempPath(), "hyw-import-replace-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(sourceRoot, "pkg");
        _ = Directory.CreateDirectory(source);

        string? targetDocument = null;
        try
        {
            services.SaveComposite(source, "旧内容", ["s1"]).Succeeded.ShouldBeTrue();
            var first = services.ImportComposite(source);
            first.Succeeded.ShouldBeTrue(string.Join("；", first.Failures));
            targetDocument = first.TargetPath;
            targetDocument.ShouldNotBeNull();

            // 改内容后以同名再导入，并明确选「替换」。
            services.SaveComposite(source, "新内容", ["s1"]).Succeeded.ShouldBeTrue();
            var conflicts = services.GetImportConflicts(source);
            conflicts.ShouldHaveSingleItem();
            conflicts[0].TargetName.ShouldBe("pkg");

            var decisions = new Dictionary<string, ImportConflictChoice>(StringComparer.OrdinalIgnoreCase)
            {
                [conflicts[0].SourcePath] = ImportConflictChoice.Replace,
            };

            var replaced = services.ImportComposite(source, decisions);

            replaced.Succeeded.ShouldBeTrue(string.Join("；", replaced.Failures));
            replaced.TargetPath.ShouldBe(targetDocument, "替换就落在原来的目标上");
            File.Exists(targetDocument).ShouldBeTrue();

            var names = services.GetCompositeFiles()
                .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
                .ToList();
            names.ShouldNotContain("pkg-2", "替换不得留下带序号的副本");
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            if (targetDocument is { Length: > 0 } && Path.GetDirectoryName(targetDocument) is { Length: > 0 } directory)
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    [Fact]
    public void 导入同名组合_选择跳过应保留旧内容并标记为跳过()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var sourceRoot = Path.Combine(Path.GetTempPath(), "hyw-import-skip-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(sourceRoot, "pkg");
        _ = Directory.CreateDirectory(source);

        string? targetDocument = null;
        try
        {
            services.SaveComposite(source, "旧内容", ["s1"]).Succeeded.ShouldBeTrue();
            targetDocument = services.ImportComposite(source).TargetPath;
            targetDocument.ShouldNotBeNull();
            var beforeBytes = File.ReadAllBytes(targetDocument!);

            services.SaveComposite(source, "新内容", ["s1"]).Succeeded.ShouldBeTrue();
            var conflicts = services.GetImportConflicts(source);
            conflicts.ShouldHaveSingleItem();

            var decisions = new Dictionary<string, ImportConflictChoice>(StringComparer.OrdinalIgnoreCase)
            {
                [conflicts[0].SourcePath] = ImportConflictChoice.Skip,
            };

            var skipped = services.ImportComposite(source, decisions);

            skipped.Succeeded.ShouldBeTrue(string.Join("；", skipped.Failures));
            skipped.Skipped.ShouldBeTrue();
            skipped.TargetPath.ShouldBeNull("跳过时没有落地目标");
            File.ReadAllBytes(targetDocument!).ShouldBe(beforeBytes, "跳过必须保持旧内容字节不变");
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            if (targetDocument is { Length: > 0 } && Path.GetDirectoryName(targetDocument) is { Length: > 0 } directory)
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    [Fact]
    public void 导入_接受组合文件夹()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var root = Path.Combine(Path.GetTempPath(), "hyw-import-dir-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "pkg");
        _ = Directory.CreateDirectory(package);

        string? target = null;
        try
        {
            services.SaveComposite(package, "pkg", ["s1"]).Succeeded.ShouldBeTrue();

            var result = services.ImportComposite(package);
            result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));
            target = result.TargetPath;
            target.ShouldNotBeNull();
            File.Exists(target).ShouldBeTrue();
            Path.GetFileName(target).ShouldBe(StoragePaths.CompositeDocumentFileName);
        }
        finally
        {
            if (target is { Length: > 0 } && Path.GetDirectoryName(target) is { Length: > 0 } directory)
            {
                TryDeleteDirectory(directory);
            }

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

    [Fact]
    public void 删除与重命名_只允许默认目录且不覆盖已有文件()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var inside = NewDefaultFile();
        var outside = Path.Combine(Path.GetTempPath(), "hyw-delete-" + Guid.NewGuid().ToString("N") + ".json");
        var renameSource = NewDefaultFile();
        var collision = NewDefaultFile();

        try
        {
            File.WriteAllText(inside, "{}");
            File.WriteAllText(outside, "{}");
            File.WriteAllText(renameSource, "{}");
            File.WriteAllText(collision, "{}");

            // 删除：只允许默认目录中的 json
            services.DeleteCompositeFile(inside).Succeeded.ShouldBeTrue();
            File.Exists(inside).ShouldBeFalse();

            services.DeleteCompositeFile(outside).Succeeded.ShouldBeFalse("外部路径不得删除");
            File.Exists(outside).ShouldBeTrue();

            // 重命名：落在默认目录，且不覆盖已有文件
            var result = services.RenameCompositeFile(renameSource, "renamed");
            result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));
            result.TargetPath.ShouldNotBeNull();
            File.Exists(result.TargetPath).ShouldBeTrue();
            File.Exists(renameSource).ShouldBeFalse();

            services.RenameCompositeFile(collision, "renamed").Succeeded.ShouldBeFalse("同名不得覆盖");
            File.Exists(collision).ShouldBeTrue();
            File.Exists(result.TargetPath).ShouldBeTrue();
        }
        finally
        {
            TryDelete(inside);
            TryDelete(outside);
            TryDelete(renameSource);
            TryDelete(collision);
            TryDelete(Path.Combine(StoragePaths.CompositesRoot, "renamed.json"));
        }
    }

    private static string NewDefaultFile()
    {
        var directory = StoragePaths.CompositesRoot;
        _ = Directory.CreateDirectory(directory);
        return Path.Combine(directory, "missing-" + Guid.NewGuid().ToString("N") + ".json");
    }

    private static void TryDelete(string? path)
    {
        if (path is not { Length: > 0 })
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
