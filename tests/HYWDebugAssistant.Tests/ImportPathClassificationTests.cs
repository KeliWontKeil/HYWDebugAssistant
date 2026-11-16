using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>拖入路径分类：模块包 / 组合包 / 未知，只读判断。</summary>
public class ImportPathClassificationTests
{
    [Fact]
    public void 按目录内容分类导入路径()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        var root = Path.Combine(Path.GetTempPath(), "hyw-classify-" + Guid.NewGuid().ToString("N"));
        var composite = Path.Combine(root, "composite-pkg");
        var module = Path.Combine(root, "module-pkg");
        var parent = Path.Combine(root, "parent");
        var unknown = Path.Combine(root, "unknown");

        _ = Directory.CreateDirectory(composite);
        _ = Directory.CreateDirectory(module);
        _ = Directory.CreateDirectory(Path.Combine(parent, "child"));
        _ = Directory.CreateDirectory(unknown);

        try
        {
            File.WriteAllText(Path.Combine(composite, "composite.json"), "{}");
            File.WriteAllText(Path.Combine(module, "module.json"), "{}");
            File.WriteAllText(Path.Combine(parent, "child", "module.json"), "{}");

            services.ClassifyImportPath(composite).ShouldBe(ImportPathKind.CompositePackage);
            services.ClassifyImportPath(module).ShouldBe(ImportPathKind.ModulePackage);
            services.ClassifyImportPath(parent).ShouldBe(ImportPathKind.ModulePackage);
            services.ClassifyImportPath(unknown).ShouldBe(ImportPathKind.None);
            services.ClassifyImportPath(string.Empty).ShouldBe(ImportPathKind.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
