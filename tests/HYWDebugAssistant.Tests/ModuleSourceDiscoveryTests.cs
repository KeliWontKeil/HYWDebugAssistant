using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-17：**模块来源根的发现规则**——多根 · 顺序 · 重复 · 重扫 · 诊断。
/// </summary>
/// <remarks>
/// 不依赖参考模块：用**临时目录 + 最小模块包**（module.json + 一个真实可加载的程序集）驱动真实扫描路径。
/// 这里刻意复用 <c>Sdk</c> 程序集作为"实现程序集"（含一个 IModule 实现），避免引入测试专用模块项目。
/// </remarks>
public class ModuleSourceDiscoveryTests
{
    [Fact]
    public void 多根扫描_应按顺序装入并给出逐根诊断()
    {
        using var fixture = new SourceFixture();
        var first = fixture.CreateRoot("root-a");
        var second = fixture.CreateRoot("root-b");
        SourceFixture.CreatePackage(first, "hyw", "alpha", "1.0.0", FixtureModuleType.Alpha);
        SourceFixture.CreatePackage(second, "hyw", "beta", "1.0.0", FixtureModuleType.Beta);

        var installed = fixture.Services.ScanModuleSources([first, second]);

        installed.ShouldBe(2);
        var sources = fixture.Services.GetModuleSources();
        sources.Count.ShouldBe(2);
        sources[0].Directory.ShouldBe(first);
        sources[0].PackagesFound.ShouldBe(1);
        sources[0].FailedPackages.ShouldBe(0);
        sources[0].Messages.ShouldContain(m => m.Contains("alpha"));

        fixture.Services.GetModuleCatalog().Select(c => c.ModuleId)
            .ShouldBe(["hyw/alpha/1.0.0", "hyw/beta/1.0.0"], ignoreOrder: true);
    }

    [Fact]
    public void 不存在的根_应记录为跳过而不是失败()
    {
        using var fixture = new SourceFixture();
        var missing = Path.Combine(fixture.BaseDirectory, "does-not-exist");

        fixture.Services.ScanModuleSources([missing]).ShouldBe(0);

        var source = fixture.Services.GetModuleSources().ShouldHaveSingleItem();
        source.Exists.ShouldBeFalse();
        source.FailedPackages.ShouldBe(0);
        source.Messages.ShouldContain(m => m.Contains("目录不存在"));
    }

    [Fact]
    public void 相同版本重复_应先到者胜且只记一次提示()
    {
        using var fixture = new SourceFixture();
        var first = fixture.CreateRoot("root-a");
        var second = fixture.CreateRoot("root-b");
        SourceFixture.CreatePackage(first, "hyw", "alpha", "1.0.0", FixtureModuleType.Alpha);
        SourceFixture.CreatePackage(second, "hyw", "alpha", "1.0.0", FixtureModuleType.Alpha);

        fixture.Services.ScanModuleSources([first, second]).ShouldBe(1, "第二个同 ID 同版本包被拒绝");

        fixture.Services.GetModuleCatalog().ShouldHaveSingleItem().SourceDirectory.ShouldBe(first, "先到者胜");
        fixture.Services.GetModuleSources()[1].FailedPackages.ShouldBe(1);
    }

    [Fact]
    public void 不同版本_应共存且目录里两行都在()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        SourceFixture.CreatePackage(root, "hyw", "multi", "1.0.0", FixtureModuleType.MultiV1);
        SourceFixture.CreatePackage(root, "hyw", "multi", "2.0.0", FixtureModuleType.MultiV2);

        fixture.Services.ScanModuleSources([root]).ShouldBe(2);

        fixture.Services.GetModuleCatalog().Select(c => c.ModuleId)
            .ShouldBe(["hyw/multi/1.0.0", "hyw/multi/2.0.0"], ignoreOrder: true);
    }

    [Fact]
    public void 重扫_应能发现新增的包()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        SourceFixture.CreatePackage(root, "hyw", "first", "1.0.0", FixtureModuleType.First);
        fixture.Services.ScanModuleSources([root]).ShouldBe(1);

        // 新增一个包后再扫（等价于「立即重扫」）
        SourceFixture.CreatePackage(root, "hyw", "second", "1.0.0", FixtureModuleType.Second);
        fixture.Services.ScanModuleSources([root]).ShouldBe(1, "只新增一个包");
        fixture.Services.GetModuleCatalog().Count.ShouldBe(2);
    }

    [Fact]
    public void 重扫命令_应经设置里的根列表并给出结果()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        SourceFixture.CreatePackage(root, "hyw", "from-settings", "1.0.0", FixtureModuleType.FromSettings);

        fixture.Services.UpdateSettings(new ApplicationSettings
        {
            ModuleSourceDirectories = [root],
        }).Succeeded.ShouldBeTrue();

        var result = fixture.Services.RescanModuleSources();

        result.Succeeded.ShouldBeTrue();
        result.Notices.ShouldNotBeEmpty();
        fixture.Services.GetModuleCatalog().ShouldHaveSingleItem().ModuleId.ShouldBe("hyw/from-settings/1.0.0");
    }

    /// <summary>
    /// **重扫不得丢掉内置包目录**：重加载之后，模块来源目录列表不得被清空。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>真因</b>：**「扫哪些根」有两个来源** —— 启动路径（组合根）扫 `内置 modules\ + 设置里的根`；
    /// 而 `RescanModuleSources` 只读 `设置里的根`，而内置目录**刻意不出现在设置里**（它「不可移除、不显示为
    /// 可编辑项」）⇒ **没有自配来源根**时（默认情形）重扫的根列表是**空的** ⇒ 扫描结果表被覆盖成空表
    /// ⇒ 「模块来源目录」列表**清空**，且**什么都没重扫**。
    /// </para>
    /// <para>
    /// <b>本用例为什么抓得住</b>：只注入「内置目录」、**不配任何用户根**（默认现场），然后调重扫 ⇒
    /// 内置目录必须仍在诊断表里、且其中的包仍在清单里（漏掉任一条都会让表变空）。
    /// 另：本方法**从不删磁盘文件** —— 「消失的只是列表内容」这一点也由「目录里文件仍在」钉住。
    /// </para>
    /// </remarks>
    [Fact]
    public void 重扫命令_必须保住内置包目录()
    {
        using var fixture = new SourceFixture();
        var builtIn = fixture.CreateRoot("built-in");

        // 注意：包清单里的 (author,name,version) 必须与入口类型的 [ModuleDefinition] 一致（加载器的校验点）
        SourceFixture.CreatePackage(builtIn, "hyw", "from-settings", "1.0.0", FixtureModuleType.FromSettings);

        // 现场 = 只有内置目录，用户一个来源根都没配
        fixture.Services.BuiltInModuleDirectory = builtIn;
        fixture.Services.ScanModuleSources(fixture.Services.ResolveScanRoots()).ShouldBe(1);
        fixture.Services.GetModuleSources().ShouldHaveSingleItem().Directory.ShouldBe(builtIn);

        var result = fixture.Services.RescanModuleSources();

        result.Succeeded.ShouldBeTrue();
        fixture.Services.GetModuleSources().ShouldHaveSingleItem().Directory.ShouldBe(
            builtIn,
            "内置包目录必须仍在扫描结果里（旧实现：根列表为空 ⇒ 结果表被清空 ⇒ 界面上「目录消失」）");
        fixture.Services.GetModuleCatalog().ShouldContain(
            e => e.ModuleId == "hyw/from-settings/1.0.0",
            "内置目录里的包必须仍在可用模块清单里");

        // 重扫绝不删磁盘文件（"消失的只是列表内容"）
        File.Exists(Path.Combine(builtIn, "from-settings-1.0.0", "module.json")).ShouldBeTrue();
    }

    [Fact]
    public void 缺少实现程序集的包_应失败并计入诊断()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        var package = Path.Combine(root, "broken");
        _ = Directory.CreateDirectory(package);
        File.WriteAllText(
            Path.Combine(package, "module.json"),
            """{"id":"hyw/broken/1.0.0","entryAssembly":"Nope.dll","entryType":"Nope.Type","frameworkCompatibility":">=1.0.0 <2.0.0"}""");

        fixture.Services.ScanModuleSources([root]).ShouldBe(0);

        var source = fixture.Services.GetModuleSources().ShouldHaveSingleItem();
        source.PackagesFound.ShouldBe(1);
        source.FailedPackages.ShouldBe(1);
        source.Messages.ShouldContain(m => m.Contains("broken") && m.Contains("失败"));
    }



    /// <summary>临时根目录 + 真实服务面的测试夹具。</summary>
    private sealed class SourceFixture : IDisposable
    {
        public SourceFixture()
        {
            BaseDirectory = Path.Combine(Path.GetTempPath(), "hyw-src-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(BaseDirectory);

            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
        }

        public string BaseDirectory { get; }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        /// <summary>建一个来源根目录。</summary>
        public string CreateRoot(string name)
        {
            var root = Path.Combine(BaseDirectory, name);
            _ = Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// 在给定根下造一个**最小模块包**：module.json + 把**测试程序集**复制过来当"实现程序集"。
        /// </summary>
        /// <remarks>
        /// 用真实程序集是为了走**真实的加载路径**（<c>Assembly.LoadFrom</c> + 入口类型与清单一致性校验）；
        /// 入口类型见本文件末尾的 <see cref="FixtureModuleType"/> —— 其 <c>[ModuleDefinition]</c> 标识
        /// 必须与清单一致（这正是加载器的校验点之一）。
        /// </remarks>
        public static void CreatePackage(string root, string author, string name, string version, Type entryType)
        {
            var package = Path.Combine(root, $"{name}-{version}");   // 包目录名可自由：扫描按 module.json 识别
            _ = Directory.CreateDirectory(package);

            var testAssemblyPath = typeof(SourceFixture).Assembly.Location;
            var assemblyName = Path.GetFileName(testAssemblyPath);
            File.Copy(testAssemblyPath, Path.Combine(package, assemblyName), overwrite: true);

            File.WriteAllText(
                Path.Combine(package, "module.json"),
                $$"""
                {
                  "id": "{{author}}/{{name}}/{{version}}",
                  "entryAssembly": "{{assemblyName}}",
                  "entryType": "{{entryType.FullName}}",
                  "frameworkCompatibility": ">=1.0.0 <2.0.0",
                  "dependsOn": [],
                  "resources": []
                }
                """);
        }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(BaseDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// 模块来源发现测试用的**入口类型集合**（标识必须与 <c>module.json</c> 完全一致）。
/// </summary>
/// <remarks>
/// 每个类型对应一个"模块包身份"：同名的不同版本需要不同标识 → 用不同类型区分。
/// 这些类型不参与任何真实运行，只用于"能被加载器加载"。
/// </remarks>
public static class FixtureModuleType
{
    /// <summary><c>hyw/alpha/1.0.0</c>（多根场景与重复包场景共用）。</summary>
    public static readonly Type Alpha = typeof(AlphaModule);

    /// <summary><c>hyw/beta/1.0.0</c>。</summary>
    public static readonly Type Beta = typeof(BetaModule);

    /// <summary><c>hyw/multi/1.0.0</c>。</summary>
    public static readonly Type MultiV1 = typeof(MultiModuleV1);

    /// <summary><c>hyw/multi/2.0.0</c>。</summary>
    public static readonly Type MultiV2 = typeof(MultiModuleV2);

    /// <summary><c>hyw/first/1.0.0</c>。</summary>
    public static readonly Type First = typeof(FirstModule);

    /// <summary><c>hyw/second/1.0.0</c>。</summary>
    public static readonly Type Second = typeof(SecondModule);

    /// <summary><c>hyw/from-settings/1.0.0</c>。</summary>
    public static readonly Type FromSettings = typeof(FromSettingsModule);

    /// <summary><c>hyw/alpha/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "alpha", "1.0.0", DisplayName = "夹具 alpha")]
    public sealed class AlphaModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/beta/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "beta", "1.0.0", DisplayName = "夹具 beta")]
    public sealed class BetaModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/multi/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "multi", "1.0.0", DisplayName = "夹具 multi v1")]
    public sealed class MultiModuleV1 : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/multi/2.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "multi", "2.0.0", DisplayName = "夹具 multi v2")]
    public sealed class MultiModuleV2 : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/first/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "first", "1.0.0", DisplayName = "夹具 first")]
    public sealed class FirstModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/second/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "second", "1.0.0", DisplayName = "夹具 second")]
    public sealed class SecondModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/from-settings/1.0.0</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "from-settings", "1.0.0", DisplayName = "夹具 from-settings")]
    public sealed class FromSettingsModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;
}
