using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Persistence;
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
                  "schemaVersion": 3,
                  "name": "missing",
                  "instances": [
                    { "instanceId": "m1", "moduleId": "hyw/missing/1.0.0", "enabled": true }
                  ],
                  "networks": [],
                  "layout": {}
                }
                """);

            var file = services.GetCompositeFiles().Single(f => f.Path == path);
            file.ModuleCount.ShouldBe(1);
            file.ReadError.ShouldBeNull();
            file.MissingModules.ShouldNotBeNull().ShouldContain("hyw/missing/1.0.0");
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
    public void 删除_只允许默认目录中的json()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var inside = NewDefaultFile();
        var outside = Path.Combine(Path.GetTempPath(), "hyw-delete-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            File.WriteAllText(inside, "{}");
            File.WriteAllText(outside, "{}");

            services.DeleteCompositeFile(inside).Succeeded.ShouldBeTrue();
            File.Exists(inside).ShouldBeFalse();

            services.DeleteCompositeFile(outside).Succeeded.ShouldBeFalse("外部路径不得删除");
            File.Exists(outside).ShouldBeTrue();
        }
        finally
        {
            TryDelete(inside);
            TryDelete(outside);
        }
    }

    [Fact]
    public void 重命名_只允许默认目录_不覆盖已有文件()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var first = NewDefaultFile();
        var second = NewDefaultFile();

        try
        {
            File.WriteAllText(first, "{}");
            File.WriteAllText(second, "{}");

            var result = services.RenameCompositeFile(first, "renamed");
            result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));
            result.TargetPath.ShouldNotBeNull();
            File.Exists(result.TargetPath).ShouldBeTrue();
            File.Exists(first).ShouldBeFalse();

            services.RenameCompositeFile(second, "renamed").Succeeded.ShouldBeFalse("同名不得覆盖");
            File.Exists(second).ShouldBeTrue();
        }
        finally
        {
            TryDelete(first);
            TryDelete(second);
            TryDelete(Path.Combine(StoragePaths.CompositesRoot, "renamed.json"));
        }
    }

    [Fact]
    public void 封面_设置后随重命名移动_删除时一起删除()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var json = NewDefaultFile();
        var image = Path.Combine(Path.GetTempPath(), "hyw-cover-" + Guid.NewGuid().ToString("N") + ".png");
        string? renamed = null;

        try
        {
            File.WriteAllText(json, "{}");
            File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47]);

            services.SetCompositeCover(json, image).Succeeded.ShouldBeTrue();
            var cover = CompositeFileStore.CoverPathFor(json);
            File.Exists(cover).ShouldBeTrue();
            services.GetCompositeFiles().Single(file => file.Path == json)
                .PreviewImagePath.ShouldBe(cover);

            var rename = services.RenameCompositeFile(json, "cover-renamed-" + Guid.NewGuid().ToString("N"));
            rename.Succeeded.ShouldBeTrue(string.Join("；", rename.Failures));
            renamed = rename.TargetPath;
            renamed.ShouldNotBeNull();
            var renamedCover = CompositeFileStore.CoverPathFor(renamed!);
            File.Exists(renamedCover).ShouldBeTrue("封面应随组合同步重命名");
            File.Exists(cover).ShouldBeFalse();

            services.DeleteCompositeFile(renamed!).Succeeded.ShouldBeTrue();
            File.Exists(renamedCover).ShouldBeFalse("删除组合时应删除封面");
        }
        finally
        {
            TryDelete(json);
            TryDelete(image);
            TryDelete(renamed);
            TryDelete(CompositeFileStore.CoverPathFor(json));
            if (renamed is { Length: > 0 })
            {
                TryDelete(CompositeFileStore.CoverPathFor(renamed));
            }
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
