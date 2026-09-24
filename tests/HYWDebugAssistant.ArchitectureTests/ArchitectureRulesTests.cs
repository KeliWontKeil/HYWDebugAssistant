using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.ArchitectureTests;

/// <summary>
/// 架构约束检查：<b>AR-1 ~ AR-16</b>（唯一来源：docs/architecture/06-test-architecture.md §6.3）。
/// <para>
/// 依赖类（AR-1~AR-12）：读取**已编译程序集的 AssemblyRef 表**（覆盖间接依赖，比源码扫描可靠）。
/// 源码类（AR-13~AR-16）：源码扫描（命名空间一致性、业务词汇、排除清单、公开签名）。
/// </para>
/// <para>
/// <b>目标程序集缺失即失败</b>——禁止"无对象则跳过"：规则静默失效比误报更危险（ADR-0032 §6.3）。
/// 因此本测试要求产品程序集已构建（<c>build.ps1</c> 的顺序已满足）。
/// </para>
/// </summary>
public class ArchitectureRulesTests
{
    private const string SdkName = "HYWDebugAssistant.Sdk";
    private const string ModulesPrefix = "HYWDebugAssistant.Modules.";

    /// <summary>AR-3~AR-11：各框架程序集**允许直接引用**的框架内程序集（穷举，来自 docs/architecture/01 §1.3）。</summary>
    private static readonly Dictionary<string, string[]> AllowedFrameworkRefs = new(StringComparer.Ordinal)
    {
        [SdkName] = [],                                                                     // AR-9：零框架依赖
        // SDK 测试套件（T6）：与 Sdk 同源的零框架依赖（模块作者在自己的测试项目里引用它）
        ["HYWDebugAssistant.Sdk.Testing"] = [SdkName],
        ["HYWDebugAssistant.Infrastructure"] = [],                                          // AR-10：不引框架程序集
        ["HYWDebugAssistant.Hosting"] = [SdkName, "HYWDebugAssistant.Infrastructure"],       // AR-4
        ["HYWDebugAssistant.DataPlane"] = [SdkName, "HYWDebugAssistant.Infrastructure"],     // AR-3：不得反向依赖 L2/L4/L5
        ["HYWDebugAssistant.Core"] = [SdkName, "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Hosting", "HYWDebugAssistant.DataPlane", "HYWDebugAssistant.Infrastructure"], // AR-5（含实现自身公开面 Core.Api）
        ["HYWDebugAssistant.Core.Api"] = [SdkName],                                          // AR-6
        ["HYWDebugAssistant.Ui"] = [SdkName, "HYWDebugAssistant.Core.Api"],                  // AR-7
        ["HYWDebugAssistant.FrameworkUi"] = [SdkName, "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Ui"], // AR-8
        // AR-11：App 是**唯一白名单**（组合根，同时可见 L4 内部与 L5）
        ["HYWDebugAssistant.App"] =
        [
            SdkName, "HYWDebugAssistant.Infrastructure", "HYWDebugAssistant.Hosting", "HYWDebugAssistant.DataPlane",
            "HYWDebugAssistant.Core", "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Ui", "HYWDebugAssistant.FrameworkUi",
        ],
    };

    /// <summary>AR-5 加强：允许引用 UI 框架（Avalonia）的程序集。</summary>
    private static readonly string[] UiLayers =
        ["HYWDebugAssistant.Ui", "HYWDebugAssistant.FrameworkUi", "HYWDebugAssistant.App"];

    /// <summary>AR-13：框架核心层不得出现的具体业务词汇（P1/P2/P13、ADR-0003）。</summary>
    private static readonly string[] BusinessWords =
        ["Uart", "UART", "Foc", "FOC", "Plotter", "SerialPort", "Modbus", "CanBus", "串口", "波特率", "电机"];

    private static readonly string[] BusinessLayers =
        ["HYWDebugAssistant.Core", "HYWDebugAssistant.Hosting", "HYWDebugAssistant.DataPlane"];

    /// <summary>AR-15：ADR-0024 排除清单（第三方组件命名空间前缀）。</summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "AvaloniaUI.DiagnosticsSupport", "Avalonia.Accelerate", "Avalonia.Diagnostics",
        "DevExpress", "Telerik", "Actipro", "Syncfusion",
        "MediatR", "AutoMapper", "FluentAssertions", "Moq", "Serilog",
    ];

    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Configuration = DetectConfiguration();

    private static readonly Regex NamespaceRegex =
        new(@"^\s*namespace\s+([A-Za-z_][\w\.]*)\s*[;{]", RegexOptions.Multiline);

    private static readonly Regex UsingRegex =
        new(@"^\s*using\s+(?:static\s+)?([A-Za-z_][\w\.]*)\s*;", RegexOptions.Multiline);

    private static readonly Regex ProjectRefRegex =
        new(@"<ProjectReference\s+Include=""(?<path>[^""]+)""", RegexOptions.IgnoreCase);

    // ---------------------------------------------------------------- 基础设施

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

    private static string DetectConfiguration() =>
        AppContext.BaseDirectory.Contains($"{Separator}Release{Separator}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    private sealed record ProductAssembly(string ProjectName, string ProjectDir, string DllPath);

    /// <summary>src 下的全部产品项目（项目名 = csproj 文件名 = 程序集名）。</summary>
    private static List<ProductAssembly> ProductAssemblies() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Separator}obj{Separator}") && !p.Contains($"{Separator}bin{Separator}"))
            .Select(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                var dir = Path.GetDirectoryName(p)!;
                return new ProductAssembly(name, dir, Path.Combine(dir, "bin", Configuration, "net10.0", name + ".dll"));
            })
            .OrderBy(a => a.ProjectName, StringComparer.Ordinal)
            .ToList();

    private static void EnsureBuilt(ProductAssembly assembly)
    {
        File.Exists(assembly.DllPath).ShouldBeTrue(
            $"未找到已编译的程序集：{assembly.DllPath.Replace(RepoRoot, ".")}" +
            "（AR 规则要求产品程序集已构建；请先运行 build.ps1 —— 缺失即失败，不跳过）");
    }

    /// <summary>读取已编译程序集的 AssemblyRef 表（间接依赖也可见）。</summary>
    private static HashSet<string> ReferencedAssemblies(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.AssemblyReferences)
        {
            result.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
        }

        return result;
    }

    /// <summary>是否为框架内程序集（含模块程序集）；测试/基准项目不计入。</summary>
    private static bool IsFrameworkOrModule(string name) =>
        name.StartsWith("HYWDebugAssistant.", StringComparison.Ordinal)
        && !name.StartsWith("HYWDebugAssistant.Tests", StringComparison.Ordinal)
        && !name.StartsWith("HYWDebugAssistant.ArchitectureTests", StringComparison.Ordinal)
        && !name.StartsWith("HYWDebugAssistant.Benchmarks", StringComparison.Ordinal);

    private static List<string> SourceFiles(params string[] projectNames)
    {
        string[] roots;
        if (projectNames.Length == 0)
        {
            roots = [Path.Combine(RepoRoot, "src")];
        }
        else
        {
            var all = ProductAssemblies();
            roots = projectNames
                .Select(n => all.FirstOrDefault(a => a.ProjectName == n)?.ProjectDir)
                .Where(d => d is not null)
                .Select(d => d!)
                .ToArray();
        }

        return roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Separator}obj{Separator}") && !p.Contains($"{Separator}bin{Separator}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------- 结构自检

    [Fact]
    public void AR0_产品项目清单应与七层结构一致()
    {
        var expected = new[]
        {
            "HYWDebugAssistant.Sdk",
            // T6 契约测试套件（M1-12 加入；与 Sdk 同源的零框架依赖，供模块作者在自己的测试项目里引用）
            "HYWDebugAssistant.Sdk.Testing",
            "HYWDebugAssistant.Infrastructure", "HYWDebugAssistant.Hosting",
            "HYWDebugAssistant.DataPlane", "HYWDebugAssistant.Core", "HYWDebugAssistant.Core.Api",
            "HYWDebugAssistant.Ui", "HYWDebugAssistant.FrameworkUi", "HYWDebugAssistant.App",
        };

        var actual = ProductAssemblies()
            .Where(a => !a.ProjectName.StartsWith(ModulesPrefix, StringComparison.Ordinal))
            .Select(a => a.ProjectName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        actual.ShouldBe(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    // ---------------------------------------------------------------- 依赖类 AR-1 ~ AR-12

    [Fact]
    public void AR1_AR2_模块程序集只允许引用Sdk且不得互引()
    {
        var modules = ProductAssemblies()
            .Where(a => a.ProjectName.StartsWith(ModulesPrefix, StringComparison.Ordinal))
            .ToList();

        modules.ShouldNotBeEmpty("src/Modules 下未发现模块项目（参考桩模块应已随骨架建立）");

        var violations = new List<string>();
        foreach (var module in modules)
        {
            EnsureBuilt(module);
            foreach (var reference in ReferencedAssemblies(module.DllPath).Where(IsFrameworkOrModule))
            {
                if (!string.Equals(reference, SdkName, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{module.ProjectName} 引用了 {reference}；模块**只允许**引用 {SdkName}（AR-1），" +
                        "模块之间不得互相引用（AR-2 / P12）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR3_AR12_框架程序集依赖方向必须符合七层禁令()
    {
        var violations = new List<string>();

        foreach (var assembly in ProductAssemblies().Where(a => AllowedFrameworkRefs.ContainsKey(a.ProjectName)))
        {
            EnsureBuilt(assembly);
            var allowed = AllowedFrameworkRefs[assembly.ProjectName];

            foreach (var reference in ReferencedAssemblies(assembly.DllPath))
            {
                if (!IsFrameworkOrModule(reference))
                {
                    continue;
                }

                if (reference.StartsWith(ModulesPrefix, StringComparison.Ordinal))
                {
                    violations.Add($"{assembly.ProjectName} 引用了模块程序集 {reference}（AR-12：框架不得在编译期依赖模块）");
                    continue;
                }

                if (!allowed.Contains(reference, StringComparer.Ordinal))
                {
                    var permitted = allowed.Length == 0 ? "无（本层不得引用任何框架程序集）" : string.Join(", ", allowed);
                    violations.Add($"{assembly.ProjectName} 引用了 {reference}；允许集合为：{permitted}（docs/architecture/01 §1.3）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR3_AR11_项目文件引用必须符合依赖方向()
    {
        // AssemblyRef 只反映"实际用到的类型"；项目引用关系必须单独检查，
        // 否则"引用了但不允许、只是暂时没用"的违规会漏检（空项目阶段尤其明显）。
        var violations = new List<string>();

        var csprojFiles = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Separator}obj{Separator}") && !p.Contains($"{Separator}bin{Separator}"));

        foreach (var csproj in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);

            string[]? allowed;
            if (projectName.StartsWith(ModulesPrefix, StringComparison.Ordinal))
            {
                allowed = [SdkName]; // AR-1 / AR-2
            }
            else if (!AllowedFrameworkRefs.TryGetValue(projectName, out allowed))
            {
                continue;
            }

            var references = ProjectRefRegex.Matches(File.ReadAllText(csproj))
                .Select(m => Path.GetFileNameWithoutExtension(m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(r => !string.Equals(r, projectName, StringComparison.Ordinal))
                .ToList();

            foreach (var reference in references)
            {
                if (!allowed.Contains(reference, StringComparer.Ordinal))
                {
                    var permitted = allowed.Length == 0 ? "无（本层不得引用任何框架项目）" : string.Join(", ", allowed);
                    violations.Add(
                        $"{Path.GetRelativePath(RepoRoot, csproj)} 引用了 {reference}；允许集合为：{permitted}（docs/architecture/01 §1.3）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR5_AR9_仅UI层可引用Avalonia且其他层不得依赖UI框架()
    {
        var violations = new List<string>();

        foreach (var assembly in ProductAssemblies())
        {
            EnsureBuilt(assembly);
            var uiReferences = ReferencedAssemblies(assembly.DllPath)
                .Where(r => r.StartsWith("Avalonia", StringComparison.Ordinal))
                .ToList();

            if (uiReferences.Count > 0 && !UiLayers.Contains(assembly.ProjectName, StringComparer.Ordinal))
            {
                violations.Add(
                    $"{assembly.ProjectName} 引用了 UI 框架（{string.Join(", ", uiReferences)}）；" +
                    $"仅 {string.Join(" / ", UiLayers)} 可引用（AR-5 / AR-9 / ADR-0026 C1-3）");
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR9_Sdk必须零依赖()
    {
        var sdk = ProductAssemblies().Single(a => a.ProjectName == SdkName);
        EnsureBuilt(sdk);

        var violations = ReferencedAssemblies(sdk.DllPath)
            .Where(IsFrameworkOrModule)
            .Select(r => $"{SdkName} 引用了 {r}；契约层必须**零框架依赖**（AR-9 / ADR-0026 C1-4）")
            .ToList();

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR6_AR16_CoreApi只引Sdk且公开签名不得出现框架内部类型()
    {
        const string coreApi = "HYWDebugAssistant.Core.Api";
        var project = ProductAssemblies().Single(a => a.ProjectName == coreApi);

        // 依赖面（AR-6）
        EnsureBuilt(project);
        var references = ReferencedAssemblies(project.DllPath)
            .Where(IsFrameworkOrModule)
            .Where(r => !string.Equals(r, SdkName, StringComparison.Ordinal))
            .Select(r => $"{coreApi} 引用了 {r}；只允许引用 {SdkName}（AR-6）")
            .ToList();
        references.ShouldBeEmpty(string.Join(Environment.NewLine, references));

        // 公开面（AR-16）：public 成员签名中不得出现框架内部类型
        var internalPrefixes = new[]
        {
            "HYWDebugAssistant.Core.", "HYWDebugAssistant.Hosting.",
            "HYWDebugAssistant.DataPlane.", "HYWDebugAssistant.Infrastructure.",
        };

        var signatureViolations = new List<string>();
        foreach (var file in SourceFiles(coreApi))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                if (!line.Contains("public ", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var prefix in internalPrefixes)
                {
                    if (line.Contains(prefix, StringComparison.Ordinal))
                    {
                        signatureViolations.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 公开签名中出现内部类型 {prefix}*（AR-16）");
                    }
                }
            }
        }

        signatureViolations.ShouldBeEmpty(string.Join(Environment.NewLine, signatureViolations));
    }

    // ---------------------------------------------------------------- 源码类 AR-13 ~ AR-15

    [Fact]
    public void AR13_框架核心层不得出现具体业务词汇()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles([.. BusinessLayers]))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (var word in BusinessWords)
                {
                    if (line.Contains(word, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 出现业务词汇 '{word}'（AR-13 / P1·P2·P13）；" +
                            "如属误报请按 implementation/01 §8 登记例外");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR14_命名空间应与项目名加目录一致()
    {
        var violations = new List<string>();
        var projects = ProductAssemblies();

        foreach (var file in SourceFiles())
        {
            var project = projects
                .Where(p => file.StartsWith(p.ProjectDir + Separator, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.ProjectDir.Length)
                .FirstOrDefault();

            if (project is null)
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var match = NamespaceRegex.Match(text);
            if (!match.Success)
            {
                continue; // 无命名空间声明（如纯 global 文件）→ 跳过
            }

            var subPath = Path.GetRelativePath(project.ProjectDir, Path.GetDirectoryName(file)!);
            var expected = subPath == "."
                ? project.ProjectName
                : project.ProjectName + "." + subPath.Replace(Separator, ".").Replace('\\', '.');

            var actual = match.Groups[1].Value;
            if (!actual.StartsWith(expected, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{Path.GetRelativePath(RepoRoot, file)}：命名空间 '{actual}' 与期望前缀 '{expected}' 不一致（AR-14）");
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AR15_不得引用被排除的第三方组件()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                var match = UsingRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var ns = match.Groups[1].Value;
                if (ForbiddenNamespaces.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 引用了禁止的命名空间 '{ns}'（AR-15 / ADR-0024 排除清单）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }
}
