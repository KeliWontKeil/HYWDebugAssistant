using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Packages;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-17：**模块来源根的发现规则**——多根 · 顺序 · 重扫 · 诊断 · 外部导入。
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
        SourceFixture.CreatePackage(first, "hyw", "alpha", FixtureModuleType.Alpha);
        SourceFixture.CreatePackage(second, "hyw", "beta", FixtureModuleType.Beta);

        var installed = fixture.Services.ScanModuleSources([first, second]);

        installed.ShouldBe(2);
        var sources = fixture.Services.GetModuleSources();
        sources.Count.ShouldBe(2);
        sources[0].Directory.ShouldBe(first);
        sources[0].PackagesFound.ShouldBe(1);
        sources[0].FailedPackages.ShouldBe(0);
        sources[0].Messages.ShouldContain(m => m.Contains("alpha"));

        fixture.Services.GetModuleCatalog().Select(c => c.ModuleId)
            .ShouldBe(["hyw/alpha", "hyw/beta"], ignoreOrder: true);
        fixture.Services.GetModuleCatalog().ShouldBeSameAs(
            fixture.Services.GetModuleCatalog(),
            "目录未重扫时应复用同一份快照，避免 UI 每帧重建");
    }

    [Fact]
    public void 重扫_应能发现新增的包()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        SourceFixture.CreatePackage(root, "hyw", "first", FixtureModuleType.First);
        fixture.Services.ScanModuleSources([root]).ShouldBe(1);

        // 新增一个包后再扫（等价于「立即重扫」）
        SourceFixture.CreatePackage(root, "hyw", "second", FixtureModuleType.Second);
        fixture.Services.ScanModuleSources([root]).ShouldBe(1, "只新增一个包");
        fixture.Services.GetModuleCatalog().Count.ShouldBe(2);
        fixture.Runtime.Issues.Recent(50)
            .Count(entry => entry.Issue.Severity == Sdk.Reporting.IssueSeverity.Error)
            .ShouldBe(0, "已载入/内容变化的包不应产生 Error（内容变化只是 Warning + 需重启）");
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
        SourceFixture.CreatePackage(builtIn, "hyw", "from-settings", FixtureModuleType.FromSettings);

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
            e => e.ModuleId == "hyw/from-settings",
            "内置目录里的包必须仍在可用模块清单里");

        // 重扫绝不删磁盘文件（"消失的只是列表内容"）
        File.Exists(Path.Combine(builtIn, "from-settings", "module.json")).ShouldBeTrue();
    }

    [Fact]
    public void 外部导入_应复制到外部目录并标注来源()
    {
        using var fixture = new SourceFixture();
        var builtIn = fixture.CreateRoot("built-in");
        var external = fixture.CreateRoot("external");
        var source = fixture.CreateRoot("source");
        SourceFixture.CreatePackage(source, "hyw", "alpha", FixtureModuleType.Alpha);
        File.WriteAllBytes(Path.Combine(source, "alpha", "preview.png"), [0x89, 0x50, 0x4E, 0x47]);

        fixture.Services.BuiltInModuleDirectory = builtIn;
        fixture.Services.ExternalModuleDirectory = external;

        var result = fixture.Services.ImportModuleDirectory(source);

        result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));
        var entry = fixture.Services.GetModuleCatalog().ShouldHaveSingleItem();
        entry.ModuleId.ShouldBe("hyw/alpha");
        entry.IsExternal.ShouldBeTrue("导入包位于 modules/external，必须标注外部");
        entry.PreviewImagePath.ShouldNotBeNull();
        File.Exists(entry.PreviewImagePath).ShouldBeTrue("包内 preview.png 应作为模块封面路径");
        Directory.Exists(Path.Combine(external, "alpha")).ShouldBeTrue("包应复制进默认外部目录");
    }

    [Fact]
    public void 外部导入_同ID应拒绝且不复制()
    {
        using var fixture = new SourceFixture();
        var builtIn = fixture.CreateRoot("built-in");
        var external = fixture.CreateRoot("external");
        var first = fixture.CreateRoot("source-a");
        var second = fixture.CreateRoot("source-b");
        SourceFixture.CreatePackage(first, "hyw", "alpha", FixtureModuleType.Alpha);
        SourceFixture.CreatePackage(second, "hyw", "alpha", FixtureModuleType.Alpha);

        fixture.Services.BuiltInModuleDirectory = builtIn;
        fixture.Services.ExternalModuleDirectory = external;
        fixture.Services.ImportModuleDirectory(first).Succeeded.ShouldBeTrue();

        var duplicate = fixture.Services.ImportModuleDirectory(second);

        duplicate.Succeeded.ShouldBeFalse("同 ID 同版本不允许静默覆盖");
        Directory.GetDirectories(external).Length.ShouldBe(1, "被拒绝的包不得复制");
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
            """{"id":"hyw/broken","entryAssembly":"Nope.dll","entryType":"Nope.Type","dependsOn":"not-an-array"}""");

        fixture.Services.ScanModuleSources([root]).ShouldBe(0);

        var source = fixture.Services.GetModuleSources().ShouldHaveSingleItem();
        source.PackagesFound.ShouldBe(1);
        source.FailedPackages.ShouldBe(1);
        source.Messages.ShouldNotBeEmpty("导入失败必须有一句可定位的说明");
    }

    /// <summary>
    /// **只换封面不算"内容变化"**：封面是展示资源，不承载代码 —— 换一张图不该要求重启应用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定依据是"包根文件在进程启动后被改过"。封面若参与这个判定，
    /// 那么"甲方要求换个图"就会变成"请重启应用"，而重启之后唯一的差别还是那张图。
    /// </para>
    /// <para>
    /// 本用例同时钉住**反面**：改程序集仍然算内容变化。否则"排除封面"很容易被写成
    /// "干脆不看修改时间"，那会让真正的代码更新静默失效（改了代码没生效，比重启更糟）。
    /// </para>
    /// </remarks>
    [Fact]
    public void 只换封面_不算内容变化不要求重启()
    {
        using var fixture = new SourceFixture();
        var root = fixture.CreateRoot("root-a");
        SourceFixture.CreatePackage(root, "hyw", "alpha", FixtureModuleType.Alpha);

        // 重扫只扫"设置里的根 + 内置目录" ⇒ 把临时根当内置目录（否则重扫扫的是空列表，
        // 断言会**静默成立** —— 那种"绿得没意义"的用例比没有更糟）。
        fixture.Services.BuiltInModuleDirectory = root;

        var package = Path.Combine(root, "alpha");
        fixture.Services.ScanModuleSources([root]).ShouldBe(1);

        // 把包根文件的修改时间统一压到"进程启动之前"：这一包在判定里就是"没重新构建过"。
        var before = DateTime.UtcNow.AddMinutes(-10);
        foreach (var file in Directory.EnumerateFiles(package, "*", SearchOption.TopDirectoryOnly))
        {
            File.SetLastWriteTimeUtc(file, before);
        }

        var unchanged = fixture.Services.RescanModuleSources();
        unchanged.Succeeded.ShouldBeTrue();
        fixture.Services.GetModuleSources()
            .ShouldAllBe(source => source.ChangedPackages == 0, "全部文件都是旧的 ⇒ 没有内容变化");

        // 只换封面（其余文件保持旧的修改时间）：仍不该被判成"内容已变化"。
        var cover = Path.Combine(package, ModulePackageLayout.CoverFileName);
        File.WriteAllBytes(cover, [0x89, 0x50, 0x4E, 0x47]);
        File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddMinutes(10));

        var afterCover = fixture.Services.RescanModuleSources();
        afterCover.Succeeded.ShouldBeTrue();
        fixture.Services.GetModuleSources()
            .ShouldAllBe(source => source.ChangedPackages == 0, "换封面不是内容变化");
        fixture.Services.GetModuleCatalog()
            .ShouldContain(entry => entry.ModuleId == "hyw/alpha" && !entry.NeedsRestart,
                "只换封面的包不该要求重启应用");

        // 反面：改程序集仍然算内容变化（排除封面不能变成"不看修改时间"）。
        var assembly = Directory.EnumerateFiles(package, "*.dll", SearchOption.TopDirectoryOnly).First();
        File.SetLastWriteTimeUtc(assembly, DateTime.UtcNow.AddMinutes(10));

        var afterAssembly = fixture.Services.RescanModuleSources();
        afterAssembly.Succeeded.ShouldBeTrue();
        fixture.Services.GetModuleSources()
            .ShouldContain(source => source.ChangedPackages == 1, "代码更新必须仍然要求重启");
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
        public static void CreatePackage(string root, string author, string name, Type entryType)
        {
            var package = Path.Combine(root, name);   // 包目录名可自由：扫描按 module.json 识别
            _ = Directory.CreateDirectory(package);

            var testAssemblyPath = typeof(FixtureModuleType).Assembly.Location;
            var assemblyName = Path.GetFileName(testAssemblyPath);
            File.Copy(testAssemblyPath, Path.Combine(package, assemblyName), overwrite: true);

            File.WriteAllText(
                Path.Combine(package, "module.json"),
                $$"""
                {
                  "id": "{{author}}/{{name}}",
                  "entryAssembly": "{{assemblyName}}",
                  "entryType": "{{entryType.FullName}}",
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
