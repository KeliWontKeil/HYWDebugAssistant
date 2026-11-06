using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 组合文件检查/导入：默认目录扫描、缺失模块标注、导入复制与同名唯一化。
/// </summary>
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
    public void 导入_应复制进默认目录并复用相同内容_不同内容唯一命名()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var firstSource = Path.Combine(Path.GetTempPath(), "hyw-import-a-" + Guid.NewGuid().ToString("N"), "same.json");
        var secondSource = Path.Combine(Path.GetTempPath(), "hyw-import-b-" + Guid.NewGuid().ToString("N"), "same.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(firstSource)!);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(secondSource)!);

        string? target = null;
        string? secondTarget = null;
        try
        {
            services.SaveComposite(firstSource, "same", ["s1"]).Succeeded.ShouldBeTrue();
            services.SaveComposite(secondSource, "same", ["s1"]).Succeeded.ShouldBeTrue();

            var first = services.ImportCompositeFile(firstSource);
            first.Succeeded.ShouldBeTrue(string.Join("；", first.Failures));
            target = first.TargetPath;
            target.ShouldNotBeNull();
            Path.GetDirectoryName(target!).ShouldBe(Path.GetFullPath(StoragePaths.CompositesRoot));
            File.Exists(target).ShouldBeTrue();

            var duplicate = services.ImportCompositeFile(firstSource);
            duplicate.Succeeded.ShouldBeTrue();
            duplicate.TargetPath.ShouldBe(target, "内容相同应复用，不生成副本");

            var different = services.ImportCompositeFile(secondSource);
            different.Succeeded.ShouldBeTrue(string.Join("；", different.Failures));
            secondTarget = different.TargetPath;
            secondTarget.ShouldNotBeNull();
            secondTarget.ShouldNotBe(target, "同名但内容不同必须唯一命名");
            File.Exists(secondTarget).ShouldBeTrue();
        }
        finally
        {
            TryDelete(firstSource);
            TryDelete(secondSource);
            TryDelete(target);
            TryDelete(secondTarget);
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
