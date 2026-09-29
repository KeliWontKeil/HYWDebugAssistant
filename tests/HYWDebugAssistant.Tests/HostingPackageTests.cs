using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3 的一部分（M1-4）：模块包加载（显式加载 + 校验）与模块注册表（多版本共存）。
/// </summary>
public class HostingPackageTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>业务模块 文本发送器 的构建输出目录（= 它的模块包目录）。</summary>
    /// <remarks>载体说明：原参考桩模块 `data-generator` 已按 2026-09 用户裁定删除 ⇒ 换成本项目的正式模块。</remarks>
    private static string TextSenderPackage => Path.Combine(
        RepoRoot, "src", "Modules", "Business", "TextSender", "bin", Configuration, "net10.0");

    [Fact]
    public void 加载业务模块包_应成功并解析出标识与入口类型()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var package = result.Package!;
        package.Id.ToString().ShouldBe("hyw/text-sender/1.0.0");
        package.EntryType.Name.ShouldBe("TextSenderModule");
        package.DefinitionAttribute.DisplayName.ShouldBe("文本发送器");
        File.Exists(package.AssemblyPath).ShouldBeTrue();
    }

    [Fact]
    public void 加载不存在的目录_应失败并给出可读原因()
    {
        var result = ModulePackageLoader.Load(Path.Combine(Path.GetTempPath(), "hyw-nonexistent-" + Guid.NewGuid().ToString("N")));

        result.Succeeded.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
        result.FailureReason!.ShouldContain("包目录不存在");
    }

    [Fact]
    public void 清单标识非法_应失败()
    {
        using var temp = new TempPackage(TextSenderPackage, manifestId: "NotAValidId");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason!.ShouldContain("模块标识不合法");
    }

    [Fact]
    public void 清单与代码声明不一致_应拒绝加载()
    {
        // 清单写 2.0.0，而代码声明是 1.0.0 → 拒绝（避免同名歧义）
        using var temp = new TempPackage(TextSenderPackage, manifestId: "hyw/text-sender/2.0.0");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason!.ShouldContain("不一致");
    }

    [Fact]
    public void 注册表_重复注册同版本应被拒绝()
    {
        var definition = DefinitionFromRealPackage();
        var registry = new ModuleRegistry();

        registry.TryRegister(definition, out var firstFailure).ShouldBeTrue(firstFailure);
        registry.TryRegister(definition, out var secondFailure).ShouldBeFalse();
        secondFailure!.ShouldContain("已注册");
    }

    [Fact]
    public void 注册表_多版本应可共存且可按名解析()
    {
        var registry = new ModuleRegistry();
        var v1 = DefinitionFromRealPackage();
        var v2 = new ModuleDefinition(
            ModuleId.Create("hyw", "text-sender", "2.0.0"),
            v1.DisplayName, v1.Description, v1.Tags, v1.Capabilities, v1.DependsOn,
            v1.FrameworkCompatibility, v1.ParticipatesInDataFlow, v1.EntryType, v1.PackageDirectory, v1.AssemblyPath);

        registry.TryRegister(v1, out _).ShouldBeTrue();
        registry.TryRegister(v2, out _).ShouldBeTrue();

        registry.Definitions.Count.ShouldBe(2);
        registry.ResolveVersions("hyw", "text-sender").Count.ShouldBe(2);

        registry.TryResolve(ModuleId.Create("hyw", "text-sender", "2.0.0"), out var resolved).ShouldBeTrue();
        resolved!.Id.Version.ShouldBe("2.0.0");
    }

    [Fact]
    public void 注册表_可注销()
    {
        var definition = DefinitionFromRealPackage();
        var registry = new ModuleRegistry();
        registry.TryRegister(definition, out _).ShouldBeTrue();

        registry.TryUnregister(definition.Id).ShouldBeTrue();
        registry.Definitions.ShouldBeEmpty();
        registry.TryResolve(definition.Id, out _).ShouldBeFalse();
    }

    private static ModuleDefinition DefinitionFromRealPackage()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);
        result.Succeeded.ShouldBeTrue(result.FailureReason);
        return ModuleDefinition.FromPackage(result.Package!);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未能定位解决方案根目录（未找到 *.slnx/*.sln）");
    }

    /// <summary>
    /// 临时包：复制真实包的输出文件到临时目录，并可按需篡改清单内容（用于验证拒绝路径）。
    /// </summary>
    private sealed class TempPackage : IDisposable
    {
        public TempPackage(string sourceDirectory, string manifestId)
        {
            Directory = Path.Combine(Path.GetTempPath(), "hyw-pkg-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            foreach (var file in System.IO.Directory.EnumerateFiles(sourceDirectory)
                         .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)), overwrite: true);
            }

            var manifest = $$"""
            {
              "id": "{{manifestId}}",
              "entryAssembly": "HYWDebugAssistant.Modules.TextSender.dll",
              "entryType": "HYWDebugAssistant.Modules.TextSender.TextSenderModule"
            }
            """;

            File.WriteAllText(Path.Combine(Directory, "module.json"), manifest);
        }

        public string Directory { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 经 LoadFrom 加载的程序集会锁定其文件（进程退出前无法删除）；
                // 清理失败不影响测试结论，临时目录由系统回收。
            }
        }
    }
}
