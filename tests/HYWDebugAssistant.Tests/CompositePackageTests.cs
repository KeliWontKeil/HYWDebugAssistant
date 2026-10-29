using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>组合件包：per-composite 文件夹、固定封面名、模块壁纸资产。</summary>
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
}
