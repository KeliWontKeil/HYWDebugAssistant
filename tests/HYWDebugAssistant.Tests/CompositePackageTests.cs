using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>组合件包：per-composite 文件夹、固定封面名、模块壁纸资产。</summary>
[Collection(nameof(FileSystemImportTests))]
public class CompositePackageTests
{
    [Fact]
    public void 保存包_应写composite_json与assets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-package-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var runtime = new ModuleRuntime();
            CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
            var services = new RuntimeServices(runtime);
            var png = new byte[] { 1, 2, 3, 4, 5 };

            services.SetModuleWallpaper("s1", png).Succeeded.ShouldBeTrue();
            services.SaveComposite(directory, "示例包").Succeeded.ShouldBeTrue();

            var document = Path.Combine(directory, "composite.json");
            File.Exists(document).ShouldBeTrue();
            var json = File.ReadAllText(document);
            json.ShouldContain("presentation");
            json.ShouldContain("assets/");

            var assets = Path.Combine(directory, "assets");
            Directory.Exists(assets).ShouldBeTrue();
            Directory.GetFiles(assets, "*.png").ShouldHaveSingleItem();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 保存包_重新加载应恢复实例图片资产()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-package-" + Guid.NewGuid().ToString("N"));

        try
        {
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("s1", bytes).Succeeded.ShouldBeTrue();
                services.SaveComposite(directory, "示例包").Succeeded.ShouldBeTrue();
            }

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(directory).Succeeded.ShouldBeTrue();
                var restored = services.GetModuleWallpaper("s1");
                restored.ShouldNotBeNull();
                restored!.Png.SequenceEqual(bytes).ShouldBeTrue("图片资产应随组合件包恢复");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 保存扁平json_也应写assets并恢复图片资产()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-flat-" + Guid.NewGuid().ToString("N"));
        var document = Path.Combine(directory, "flat.json");

        try
        {
            _ = Directory.CreateDirectory(directory);
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 5, 6, 7, 8 };
            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("s1", bytes).Succeeded.ShouldBeTrue();
                services.SaveComposite(document, "扁平示例").Succeeded.ShouldBeTrue();
            }

            Directory.Exists(Path.Combine(directory, "assets")).ShouldBeTrue();

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(document).Succeeded.ShouldBeTrue();
                services.GetModuleWallpaper("s1")!.Png.SequenceEqual(bytes).ShouldBeTrue();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 导入带图片的组合件包_应复制图片资产并可加载()
    {
        var source = Path.Combine(Path.GetTempPath(), "hyw-import-" + Guid.NewGuid().ToString("N"));
        string? importedDirectory = null;

        try
        {
            var image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleViewImage("s1", image, new HYWDebugAssistant.Core.Api.ViewImageCrop(0, 0, 1, 1))
                    .Succeeded.ShouldBeTrue();
                services.SaveComposite(source, "带图包").Succeeded.ShouldBeTrue();
            }

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                var result = services.ImportComposite(source);
                result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));
                result.TargetPath.ShouldNotBeNull();

                importedDirectory = Path.GetDirectoryName(result.TargetPath!);
                importedDirectory.ShouldNotBeNull();
                Directory.GetFiles(Path.Combine(importedDirectory!, "assets"), "*.jpg").ShouldHaveSingleItem();
            }

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(importedDirectory!).Succeeded.ShouldBeTrue();
                services.GetModuleViewImage("s1")!.Bytes.SequenceEqual(image).ShouldBeTrue(
                    "导入包时应连同非 PNG 图片资产一起复制");
            }
        }
        finally
        {
            if (importedDirectory is { Length: > 0 } && Directory.Exists(importedDirectory))
            {
                Directory.Delete(importedDirectory, recursive: true);
            }

            if (Directory.Exists(source))
            {
                Directory.Delete(source, recursive: true);
            }
        }
    }
}
