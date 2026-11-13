using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.ArchitectureTests;

/// <summary>
/// 架构约束检查：<b>AR-0 ~ AR-22</b>（规则集的权威定义见 dev/constraints.md）。
/// <para>
/// 依赖类（AR-1~AR-12）：读取**已编译程序集的 AssemblyRef 表**（覆盖间接依赖，比源码扫描可靠）。
/// 源码类（AR-0、AR-13~AR-19）：源码扫描（项目清单、命名空间一致性、业务词汇、排除清单、公开签名、数据路径、模块几何、注释纪律）。
/// </para>
/// <para>
/// <b>目标程序集缺失即失败</b>——禁止「无对象则跳过」：规则静默失效比误报更危险。
/// 因此本测试要求产品程序集已构建（<c>build.ps1</c> 的顺序已满足）。
/// </para>
/// </summary>
public class ArchitectureRulesTests
{
    private const string SdkName = "HYWDebugAssistant.Sdk";
    private const string SdkUiName = "HYWDebugAssistant.Sdk.Ui";
    private const string SdkAccessKitName = "HYWDebugAssistant.Sdk.AccessKit";
    private const string AccessName = "HYWDebugAssistant.Access";
    private const string AppName = "HYWDebugAssistant.App";
    private const string ModulesPrefix = "HYWDebugAssistant.Modules.";

    /// <summary>AR-3~AR-11：各框架程序集**允许直接引用**的框架内程序集（穷举）。</summary>
    private static readonly Dictionary<string, string[]> AllowedFrameworkRefs = new(StringComparer.Ordinal)
    {
        [SdkName] = [],                                                                     // AR-9：零框架依赖
        // 模块界面契约与控件库：只依赖 Sdk（Avalonia 由包引用带入，属外部框架，不在这张表里判）
        [SdkUiName] = [SdkName],
        // 模块侧外部接入骨架：只依赖契约层（宿主随附，模块包不带）
        [SdkAccessKitName] = [SdkName],
        // SDK 测试套件（T6）：与 Sdk 同源的零框架依赖（模块作者在自己的测试项目里引用它）
        ["HYWDebugAssistant.Sdk.Testing"] = [SdkName],
        // 外部接入层（**框架扩展**，不是独立层）：只依赖契约层；只有 App 可引用（AR-3/AR-12）
        [AccessName] = [SdkName],
        ["HYWDebugAssistant.Infrastructure"] = [],                                          // AR-10：不引框架程序集
        ["HYWDebugAssistant.Hosting"] = [SdkName, "HYWDebugAssistant.Infrastructure"],       // AR-4
        ["HYWDebugAssistant.DataPlane"] = [SdkName, "HYWDebugAssistant.Infrastructure"],     // AR-3：不得反向依赖 L2/L4/L5
        ["HYWDebugAssistant.Core"] = [SdkName, "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Hosting", "HYWDebugAssistant.DataPlane", "HYWDebugAssistant.Infrastructure"], // AR-5（含实现自身公开面 Core.Api）
        ["HYWDebugAssistant.Core.Api"] = [SdkName],                                          // AR-6
        ["HYWDebugAssistant.Ui"] = [SdkName, SdkUiName, "HYWDebugAssistant.Core.Api"],       // AR-7
        ["HYWDebugAssistant.FrameworkUi"] = [SdkName, SdkUiName, "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Ui"], // AR-8（Sdk.Ui 只为 {loc:Tr}）
        // AR-11：App 是**唯一白名单**（组合根，同时可见 L4 内部与 L5）
        ["HYWDebugAssistant.App"] =
        [
            SdkName, SdkUiName, SdkAccessKitName, "HYWDebugAssistant.Infrastructure", "HYWDebugAssistant.Hosting",
            "HYWDebugAssistant.DataPlane", "HYWDebugAssistant.Core", "HYWDebugAssistant.Core.Api", "HYWDebugAssistant.Ui",
            "HYWDebugAssistant.FrameworkUi", AccessName,
        ],
    };

    /// <summary>AR-5 加强：允许引用 UI 框架（Avalonia）的程序集。</summary>
    private static readonly string[] UiLayers =
        ["HYWDebugAssistant.Ui", "HYWDebugAssistant.FrameworkUi", "HYWDebugAssistant.App", SdkUiName];

    /// <summary>AR-13：框架核心层不得出现的具体业务词汇（P1/P2/P13）。</summary>
    private static readonly string[] BusinessWords =
        ["Uart", "UART", "Foc", "FOC", "Plotter", "SerialPort", "Modbus", "CanBus", "串口", "波特率", "电机"];

    private static readonly string[] BusinessLayers =
        ["HYWDebugAssistant.Core", "HYWDebugAssistant.Hosting", "HYWDebugAssistant.DataPlane"];

    /// <summary>AR-15：排除清单（第三方组件命名空间前缀）。</summary>
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
        new(@"<ProjectReference\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase);

    /// <summary>从 ProjectReference 的属性串里取 Include 路径。</summary>
    private static readonly Regex ProjectRefIncludeRegex =
        new(@"Include=""(?<path>[^""]+)""", RegexOptions.IgnoreCase);

    /// <summary>AR-17 / AR-25：用户数据文件名——只允许出现在路径解析器里。</summary>
    private static readonly string[] UserDataFileNames =
        ["\"settings.json\"", "\"workspace.json\"", "\"restart.json\""];

    /// <summary>从 C# 源码行里取字符串字面量（AR-25 的路径段检查用）。</summary>
    private static readonly Regex StringLiteralRegex =
        new(@"""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

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
            // 模块侧外部接入骨架（宿主随附）：只依赖 Sdk，模块包不带
            SdkAccessKitName,
            // 模块界面契约与控件库（模块自带视图引用的那一层；只依赖 Sdk + Avalonia）
            SdkUiName,
            // T6 契约测试套件（M1-12 加入；与 Sdk 同源的零框架依赖，供模块作者在自己的测试项目里引用）
            "HYWDebugAssistant.Sdk.Testing",
            // 外部接入层（**框架扩展**：适配器 + 参数 + 内置标准目录）；只有 App 引用
            AccessName,
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
                if (!string.Equals(reference, SdkName, StringComparison.Ordinal)
                    && !string.Equals(reference, SdkUiName, StringComparison.Ordinal)
                    && !string.Equals(reference, SdkAccessKitName, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{module.ProjectName} 引用了 {reference}；模块**只允许**引用 {SdkName} / {SdkUiName} / {SdkAccessKitName}（AR-1），" +
                        "不得引用 Access（框架扩展）或其它模块（AR-2 / P12）");
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
                    violations.Add($"{assembly.ProjectName} 引用了 {reference}；允许集合为：{permitted}（dev/constraints.md）");
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
                // AR-1 / AR-2：契约层 + 视图控件库 + 外部接入骨架；Access（框架扩展）不得被模块引用
                allowed = [SdkName, SdkUiName, SdkAccessKitName];
            }
            else if (!AllowedFrameworkRefs.TryGetValue(projectName, out allowed))
            {
                continue;
            }

            var references = ProjectRefRegex.Matches(File.ReadAllText(csproj))
                // `ReferenceOutputAssembly=false` 只表达**构建顺序**（例如应用要等模块包归拢后再镜像），
                // 不产生程序集引用 ⇒ 不构成编译期依赖，AR-1~AR-12 的判定只看真实引用。
                .Where(reference => !reference.Groups["attrs"].Value.Contains(
                    "ReferenceOutputAssembly=\"false\"", StringComparison.OrdinalIgnoreCase))
                .Select(reference => ProjectRefIncludeRegex.Match(reference.Groups["attrs"].Value))
                .Where(include => include.Success)
                .Select(include => Path.GetFileNameWithoutExtension(
                    include.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(reference => !string.Equals(reference, projectName, StringComparison.Ordinal))
                .ToList();

            foreach (var reference in references)
            {
                if (!allowed.Contains(reference, StringComparer.Ordinal))
                {
                    var permitted = allowed.Length == 0 ? "无（本层不得引用任何框架项目）" : string.Join(", ", allowed);
                    violations.Add(
                        $"{Path.GetRelativePath(RepoRoot, csproj)} 引用了 {reference}；允许集合为：{permitted}（dev/constraints.md）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// **AR-3 / AR-12：`Access` 是框架扩展，只允许组合根（App）引用**。
    /// </summary>
    [Fact]
    public void AR3_AR12_Access只允许被App引用()
    {
        var violations = new List<string>();

        foreach (var assembly in ProductAssemblies().Where(a => !string.Equals(a.ProjectName, AppName, StringComparison.Ordinal)))
        {
            EnsureBuilt(assembly);
            if (ReferencedAssemblies(assembly.DllPath).Contains(AccessName))
            {
                violations.Add(
                    $"{assembly.ProjectName} 引用了 {AccessName}；只有 {AppName} 可引用（AR-3 / AR-12）");
            }
        }

        var csprojFiles = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Separator}obj{Separator}") && !p.Contains($"{Separator}bin{Separator}"));

        foreach (var csproj in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            if (string.Equals(projectName, AppName, StringComparison.Ordinal))
            {
                continue;
            }

            var references = ProjectRefRegex.Matches(File.ReadAllText(csproj))
                .Select(match => ProjectRefIncludeRegex.Match(match.Groups["attrs"].Value))
                .Where(include => include.Success)
                .Select(include => Path.GetFileNameWithoutExtension(
                    include.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)));

            if (references.Contains(AccessName, StringComparer.Ordinal))
            {
                violations.Add(
                    $"{Path.GetRelativePath(RepoRoot, csproj)} 引用了 {AccessName}；只有 {AppName} 可引用（AR-3 / AR-12）");
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

            // **模块也允许引用 Avalonia**：模块自带视图（XAML）就是它的界面，
            // 而界面只能是控件 —— 这是"界面在模块里"，不是"框架层依赖 UI 框架"。
            if (uiReferences.Count > 0
                && !UiLayers.Contains(assembly.ProjectName, StringComparer.Ordinal)
                && !assembly.ProjectName.StartsWith(ModulesPrefix, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{assembly.ProjectName} 引用了 UI 框架（{string.Join(", ", uiReferences)}）；" +
                    $"仅 {string.Join(" / ", UiLayers)} 与模块可引用（AR-5 / AR-9 / ADR-0026 C1-3）");
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

    /// <summary>
    /// **AR-17：用户数据路径必须只有一处来源**（P5/P6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 纪律：设置 / 工作区 / 重启快照的文件名只能从路径解析器取。落盘路径若散落多处
    /// ⇒「用户数据在哪」就没有单一答案。
    /// </para>
    /// <para>
    /// 本用例把这条纪律变成**可执行检查**：这三个文件名**只允许**出现在路径解析器里
    /// （`StoragePaths`）—— 谁再在别处拼一份，这里当场红。
    /// </para>
    /// </remarks>
    [Fact]
    public void AR17_用户数据文件名必须只有一处来源()
    {
        var offenders = FindUserDataFileNameOffenders();

        offenders.ShouldBeEmpty(
            "用户数据路径只能从 `StoragePaths` 取（AR-17）：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-18：模块尺寸与端口几何都只来自视图 XAML 声明**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 口径：视图根 `.axaml` 同时声明初始内容尺寸（`InitialWidth/InitialHeight`）与运行期最小内容尺寸
    /// （`MinWidth/MinHeight`），两者语义独立；`[ModuleDefinition]` 不再声明 Window/Min 尺寸；
    /// 端口声明（名称/方向/类型/边/偏移）写在 `ModuleView.Ports` 里，C# 不再 `DeclarePort`。
    /// </para>
    /// <para>
    /// 本用例把纪律变成可执行检查：模块视图根缺 `MinWidth/MinHeight` 即红；
    /// 模块 `[ModuleDefinition]` 再出现 Window/Min 尺寸也红；
    /// 模块源码里出现"从界面数据文件读几何"的通路（`ViewFrame` / `ViewWindowDeclaration` / `view.json`）也红；
    /// 模块 C# 再出现 `DeclarePort(` 也红（旧模块兼容路径只允许测试夹具/外部模块使用）。
    /// </para>
    /// </remarks>
    [Fact]
    public void AR18_模块尺寸与端口几何必须来自视图XAML声明()
    {
        string[] forbidden = ["ViewFrame", "ViewWindowDeclaration", "view.json", "frame.ports", "DeclarePort("];

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (!file.Contains($"{Separator}Modules{Separator}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            // 反例：模块 [ModuleDefinition] 再声明窗口尺寸 = 第二份尺寸事实。
            if (text.Contains("[ModuleDefinition(", StringComparison.Ordinal))
            {
                foreach (var token in new[] { "WindowWidth", "WindowHeight", "MinWidth", "MinHeight" })
                {
                    if (text.Contains(token, StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)} 的 [ModuleDefinition] 出现 '{token}'"
                            + "（窗口/内容尺寸唯一事实是视图根 XAML 的 MinWidth/MinHeight）");
                    }
                }
            }

            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (var token in forbidden)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 出现 '{token}'（几何只能来自视图声明）");
                    }
                }
            }
        }

        // 正向：每个模块视图根 XAML 必须同时声明初始尺寸与运行期最小尺寸（两个独立事实）。
        var moduleRoot = Path.Combine(RepoRoot, "src", "Modules");
        if (Directory.Exists(moduleRoot))
        {
            foreach (var file in Directory.EnumerateFiles(moduleRoot, "*.axaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Separator}obj{Separator}") || file.Contains($"{Separator}bin{Separator}"))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (!text.Contains("<ui:ModuleView", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!text.Contains("MinWidth=", StringComparison.Ordinal)
                    || !text.Contains("MinHeight=", StringComparison.Ordinal)
                    || !text.Contains("InitialWidth=", StringComparison.Ordinal)
                    || !text.Contains("InitialHeight=", StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(RepoRoot, file)} 的视图根没有同时声明 "
                        + "InitialWidth/InitialHeight 与 MinWidth/MinHeight"
                        + "（初始尺寸与运行期最小尺寸是两个独立事实）");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "模块的尺寸与端口几何只能来自视图 XAML（AR-18）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-19：源码注释只承载"为什么 / 不变量 / 坑"**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 注释是给读者理解**当前**代码用的：说清为什么这么写、必须守住什么不变量、哪里踩过坑就够了。
    /// 时间标记、决定编号、逐字引语、变更叙述、测试统计与仓库内路径清单都另有唯一落点，
    /// 抄进注释只会多出一份会过期的事实（同一件事两处各写一遍，改一次要改两处）。
    /// </para>
    /// <para>
    /// 例外：确有必要保留的注释，在同行或紧邻上一行写 `ARCH-EXCEPTION` 加理由，并到例外登记处登记；
    /// 本用例只认这个标记，不做人工判断。
    /// </para>
    /// </remarks>
    [Fact]
    public void AR19_源码注释必须只承载为什么_不得携带日期与决定编号与用户原话与读数()
    {
        var forbidden = new (string Label, Regex Pattern)[]
        {
            ("日期", new Regex(@"\d{4}-\d{2}")),
            ("决定编号", new Regex(@"decisions\.md|ADR-\d|(?<![.\d])7\.\d{2}(?![.\d])")),
            ("用户原话", new Regex(@"用户原话|用户裁定|用户裁决|用户明示|用户要求|用户清单|用户追问")),
            ("变更叙述", new Regex(@"原先|此前|曾经|旧版|当时|本来|一度|历史记录")),
            ("测试读数", new Regex(@"\d+\s*/\s*\d+\s*(通过|失败)|(通过|失败)\s*[：:]\s*\d+|0\s*警告|警告\s*0")),
            ("仓库内路径清单", new Regex(@"(src|tests)[\\/]")),
        };

        const int reportLimit = 60;
        var offenders = new List<string>();
        var total = 0;
        var roots = new[] { Path.Combine(RepoRoot, "src"), Path.Combine(RepoRoot, "tests") };

        foreach (var file in roots.Where(Directory.Exists)
                     .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
                     .Where(p => !p.Contains($"{Separator}obj{Separator}") && !p.Contains($"{Separator}bin{Separator}")))
        {
            var lines = File.ReadAllLines(file);
            var inBlockComment = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                var isComment = inBlockComment
                    || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal);

                // 注释边界只在"去掉字符串与字符字面量之后"的文本上判定：字面量里的 `/*`
                // （例如 "*.slnx/*.sln"）不是注释起点，误判会让整段代码被当成注释。
                var code = StripLiterals(line);
                if (code.Contains("/*", StringComparison.Ordinal) && !code.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }
                else if (inBlockComment && code.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }

                if (!isComment || line.Contains("ARCH-EXCEPTION", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (label, pattern) in forbidden)
                {
                    if (!pattern.IsMatch(line))
                    {
                        continue;
                    }

                    total++;
                    if (offenders.Count < reportLimit)
                    {
                        offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1} 注释里有{label}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"注释只写为什么 / 不变量 / 坑（AR-19），共 {total} 处（先列前 {reportLimit} 处）；"
            + "例外请写 ARCH-EXCEPTION 标记并登记："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-20：界面描述文件禁止复活**。
    /// </summary>
    /// <remarks>
    /// 模块界面 = 模块自己的 XAML + 代码声明。任何 `view.json` / `view.schema.json` 之类
    /// "第二份界面事实"都不允许再进仓库 —— 它一旦存在，测试与文档就会再次围着它转。
    /// </remarks>
    [Fact]
    public void AR20_界面描述文件禁止复活()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "src"),
            Path.Combine(RepoRoot, "tests"),
            Path.Combine(RepoRoot, ".dsh"),
        };

        var offenders = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Separator}bin{Separator}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Separator}obj{Separator}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Separator}artifacts{Separator}", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return string.Equals(name, "view.json", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".view.json", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("view.schema", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(RepoRoot, path))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "界面只能由模块 XAML + 代码声明决定；界面描述文件一经出现就会变成第二份事实源（AR-20）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-21：Sdk.Ui 只许有控件 / 契约 / 主题**。
    /// </summary>
    /// <remarks>
    /// 控件库一旦重新长出"按 kind 派发的描述表 / 界面 schema / 节点树遍历"，
    /// 就等于把刚删掉的声明面换了张皮搬回来。字段编辑器按 <c>ConfigFieldKind</c> 选编辑器
    /// 属控件行为，不在禁令内。
    /// </remarks>
    [Fact]
    public void AR21_控件库不得重新长出界面描述面()
    {
        string[] forbidden =
        [
            "ViewDeclarationReader", "ViewSchema", "ViewDocument", "ViewElementKind",
            "ControlCatalog", "ViewFrame", "ViewWindowDeclaration",

            // 视觉结构必须住在 ControlTheme 的 XAML 里；控件代码只许创建"编辑器种类"这类行为件。
            "new Grid", "new TextBlock", "new Ellipse",
        ];

        var offenders = new List<string>();

        foreach (var file in SourceFiles(SdkUiName))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)} 出现 '{token}'");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Sdk.Ui 只许有控件 / 契约 / 主题；不得重新长出界面描述面（AR-21）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-22：模块 XAML 的绑定键只许经 <c>{x:Static}</c>**。
    /// </summary>
    /// <remarks>
    /// <c>Source="text1"</c> 这类字面量在编译期完全合法，写错只会在运行期变成空白或"点了没反应"。
    /// 键必须来自模块常量（<c>{x:Static views:XxxViewKeys.Yyy}</c>）——错一个字符就是编译错误。
    /// </remarks>
    [Fact]
    public void AR22_模块XAML的绑定键必须来自x_Static常量()
    {
        var keyProperty = new Regex(
            @"\b(?:Source|Field|CommandName|StateKey)\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.Compiled);

        var offenders = new List<string>();
        var modulesRoot = Path.Combine(RepoRoot, "src", "Modules");

        foreach (var file in Directory.EnumerateFiles(modulesRoot, "*.axaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Separator}bin{Separator}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Separator}obj{Separator}", StringComparison.OrdinalIgnoreCase)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (Match match in keyProperty.Matches(line))
                {
                    var value = match.Groups["value"].Value.Trim();

                    if (!value.StartsWith("{x:Static", StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 的键是字面量「{value}」（应改为 {{x:Static …}}）");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "模块 XAML 的 Source / Field / CommandName / StateKey 必须引用模块常量（AR-22）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-23：模块包不得自行调度或调用平台专有 API**。
    /// </summary>
    /// <remarks>
    /// 模块的周期用 <c>IModuleContext.Timers</c> 创建 <c>IModuleTimer</c>，框架计时服务负责调度并把 Tick
    /// 投回实例线程；自建 Timer、Sleep、DispatcherTimer、P/Invoke 仍会破坏"全局唯一计时机制"并让模块包无法跨平台。
    /// </remarks>
    [Fact]
    public void AR23_模块包不得使用定时器或平台专有调度()
    {
        string[] forbidden =
        [
            "System.Threading.Timer", "System.Timers", "new Timer(", "DispatcherTimer",
            "PeriodicTimer", "Task.Delay", "Thread.Sleep", "Thread.SpinWait",
            "DllImport", "LibraryImport", "OperatingSystem.IsWindows",
            "winmm", "user32", "kernel32",
        ];

        var offenders = new List<string>();
        var modulesRoot = Path.Combine(RepoRoot, "src", "Modules");

        foreach (var file in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Separator}obj{Separator}", StringComparison.OrdinalIgnoreCase)
                         && !p.Contains($"{Separator}bin{Separator}", StringComparison.OrdinalIgnoreCase)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (var token in forbidden)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 出现 '{token}'");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "模块必须用 IModuleContext.Timers 创建定时器；不得自建 Timer、Sleep、DispatcherTimer 或平台 API（AR-23）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-24：全 8 模块包不得包含宿主提供的程序集**（Sdk / Sdk.Ui / Sdk.AccessKit / Access /
    /// Avalonia / System.IO.Ports / SkiaSharp / HarfBuzzSharp / MicroCom.Runtime / *.deps.json）。
    /// </summary>
    [Fact]
    public void AR24_模块包不得包含宿主提供程序集()
    {
        var packageRoot = Path.Combine(RepoRoot, "artifacts", "modules", Configuration);
        Directory.Exists(packageRoot).ShouldBeTrue(
            $"模块包未归拢：{Path.GetRelativePath(RepoRoot, packageRoot)}（请先运行 build.ps1）");

        var sourceManifests = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Modules"), "module.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Separator}bin{Separator}") && !path.Contains($"{Separator}obj{Separator}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        sourceManifests.ShouldNotBeEmpty("源码模块清单为空（模块包门禁无法对账）");

        var expectedIds = sourceManifests
            .Select(ReadModuleId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var packages = Directory
            .EnumerateDirectories(packageRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        packages.Count.ShouldBe(
            expectedIds.Length,
            $"归拢目录应有 {expectedIds.Length} 个包，实际 {packages.Count}（{packageRoot}）");

        var actualIds = new List<string>(packages.Count);
        var violations = new List<string>();

        foreach (var package in packages)
        {
            var relative = Path.GetRelativePath(RepoRoot, package);
            var manifestPath = Path.Combine(package, "module.json");
            File.Exists(manifestPath).ShouldBeTrue($"模块包缺少 module.json：{relative}");
            actualIds.Add(ReadModuleId(manifestPath));

            foreach (var file in Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (IsHostProvidedFile(fileName))
                {
                    violations.Add($"{relative} 含宿主提供程序集 {fileName}（AR-24）");
                }
            }
        }

        actualIds.OrderBy(id => id, StringComparer.Ordinal).ToArray().ShouldBe(expectedIds);
        violations.ShouldBeEmpty(
            "模块包必须只含模块自有文件（AR-24）："
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// **AR-8 加强：FrameworkUi 的 XAML 只允许用 `loc` 前缀引用 `Sdk.Ui`**。
    /// </summary>
    /// <remarks>
    /// 外壳可以用文本扩展（`{loc:Tr}`，现已住 `Sdk.Ui`），但**不得顺手用模块控件库**：
    /// 别的前缀（如 `xmlns:ui="using:HYWDebugAssistant.Sdk.Ui…"`）会让外壳依赖模块控件，
    /// 把模块 UI 的演进绑到框架外壳上。
    /// </remarks>
    [Fact]
    public void AR8_FrameworkUi的XAML只允许loc前缀引用SdkUi()
    {
        var offenders = new List<string>();
        var root = Path.Combine(RepoRoot, "src", "FrameworkUi");

        foreach (var file in Directory
                     .EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Separator}bin{Separator}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Separator}obj{Separator}", StringComparison.OrdinalIgnoreCase)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (Match match in Regex.Matches(
                             line,
                             "xmlns:(?<prefix>[A-Za-z0-9_.-]+)=\"using:HYWDebugAssistant\\.Sdk\\.Ui[^\"]*\""))
                {
                    if (!string.Equals(match.Groups["prefix"].Value, "loc", StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} "
                            + $"xmlns:{match.Groups["prefix"].Value}=…Sdk.Ui…");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "FrameworkUi 的 XAML 只允许用 `loc` 前缀引用 Sdk.Ui（文本扩展）；不得使用模块控件库（AR-8）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-25：旧布局的根字面量与资源路径段必须只有一处来源**。
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item><b>旧根字面量</b>：`"framework"` / `"theme-seeds"` 不得重新出现在源码里
    ///     （模块包的清单字段不受影响）；</item>
    ///   <item><b>用户数据文件名</b>：设置 / 工作区 / 重启快照三个文件名只允许出现在路径解析器
    ///     （与 AR-17 同一扫描，文件名清单已扩到三个）；</item>
    ///   <item><b>资源路径段</b>：`resources` / `seeds` 作为路径段只允许出现在路径解析器、
    ///     投放器、设置服务的种子装配处与资源布局（`ResourceLayout`）；设计时桩 `EmptyRuntime` 是例外——
    ///     它刻意不引 Infrastructure，只用字面量给设计预览一份路径。</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void AR25_旧根字面量与资源路径段必须只有一处来源()
    {
        // ① 旧根字面量
        string[] oldRootTokens = ["\"framework\"", "\"framework\\", "\"framework/", "theme-seeds"];
        string[] scannedExtensions = [".cs", ".axaml", ".json", ".csproj", ".props", ".targets"];
        var oldRootOffenders = new List<string>();

        foreach (var file in Directory
                     .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Separator}bin{Separator}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Separator}obj{Separator}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => scannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                foreach (var token in oldRootTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        oldRootOffenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 出现旧根字面量 {token}");
                    }
                }
            }
        }

        oldRootOffenders.ShouldBeEmpty(
            "旧布局的根目录字面量不得复活（AR-25）：" + Environment.NewLine + string.Join(Environment.NewLine, oldRootOffenders));

        // ② 用户数据文件名（与 AR-17 同一扫描：三个名字只允许出现在路径解析器）
        var fileNameOffenders = FindUserDataFileNameOffenders();
        fileNameOffenders.ShouldBeEmpty(
            "用户数据文件名只允许出现在 `StoragePaths`（AR-17 / AR-25）："
            + Environment.NewLine + string.Join(Environment.NewLine, fileNameOffenders));

        // ③ resources / seeds 作为路径段
        string[] pathSegments = ["resources", "seeds"];
        string[] allowedSuffixes =
        [
            Path.Combine("Infrastructure", "Storage", "StoragePaths.cs"),
            Path.Combine("Core", "Seeding", "SeedService.cs"),
            Path.Combine("Core", "Settings", "SettingsService.cs"),
            // 资源布局：`resources/<kind>` 是它的唯一关注点（文本与图标共用的根解析）
            Path.Combine("Sdk", "Resources", "ResourceLayout.cs"),
            // 设计时桩：Ui 不得引 Infrastructure，故只用字面量拼给设计预览的路径
            Path.Combine("Ui", "Design", "EmptyRuntime.cs"),
        ];

        var segmentOffenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (allowedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lineNumber = 0;
            var inBlockComment = false;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();

                if (inBlockComment)
                {
                    if (trimmed.Contains("*/", StringComparison.Ordinal)) { inBlockComment = false; }
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    if (!trimmed.Contains("*/", StringComparison.Ordinal)) { inBlockComment = true; }
                    continue;
                }

                foreach (Match match in StringLiteralRegex.Matches(line))
                {
                    var parts = match.Groups["value"].Value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var segment in pathSegments)
                    {
                        if (parts.Contains(segment, StringComparer.Ordinal))
                        {
                            segmentOffenders.Add(
                                $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 字符串字面量里出现资源路径段 '{segment}'");
                        }
                    }
                }
            }
        }

        segmentOffenders.ShouldBeEmpty(
            "`resources` / `seeds` 作为路径段只允许在 `StoragePaths`、`SeedService`、`SettingsService`、"
            + "`ResourceLayout` 与设计时桩 `EmptyRuntime`（AR-25）：" + Environment.NewLine + string.Join(Environment.NewLine, segmentOffenders));
    }

    /// <summary>AR-17 / AR-25 ②：扫描"自己拼了用户数据文件名"的位置。</summary>
    private static List<string> FindUserDataFileNameOffenders()
    {
        var allowed = Path.Combine("Infrastructure", "Storage", "StoragePaths.cs");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (file.EndsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNumber++;
                if (UserDataFileNames.Any(name => line.Contains(name, StringComparison.Ordinal)))
                {
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 自己拼了用户数据文件名");
                }
            }
        }

        return offenders;
    }

    /// <summary>读取 module.json 的 id（AR-24 对账）。</summary>
    private static string ReadModuleId(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.TryGetProperty("id", out var id)
            && id.GetString() is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"module.json 缺少 id：{manifestPath}");
    }

    /// <summary>AR-24：判断文件名是否是宿主提供、不得进包的程序集。</summary>
    private static bool IsHostProvidedFile(string fileName) =>
        string.Equals(fileName, "HYWDebugAssistant.Sdk.dll", StringComparison.Ordinal)
        || string.Equals(fileName, "HYWDebugAssistant.Sdk.pdb", StringComparison.Ordinal)
        || fileName.StartsWith("HYWDebugAssistant.Sdk.Ui.", StringComparison.Ordinal)
        || fileName.StartsWith("HYWDebugAssistant.Sdk.AccessKit.", StringComparison.Ordinal)
        || fileName.StartsWith("HYWDebugAssistant.Access.", StringComparison.Ordinal)
        || fileName.StartsWith("Avalonia", StringComparison.Ordinal)
        || fileName.StartsWith("System.IO.Ports", StringComparison.Ordinal)
        || fileName.StartsWith("SkiaSharp", StringComparison.Ordinal)
        || fileName.StartsWith("HarfBuzzSharp", StringComparison.Ordinal)
        || fileName.StartsWith("MicroCom.Runtime", StringComparison.Ordinal)
        || fileName.EndsWith(".deps.json", StringComparison.Ordinal);

    /// <summary>
    /// 去掉字符串与字符字面量的内容，只留代码骨架。
    /// </summary>
    /// <remarks>
    /// 注释边界不能靠字面量里的字符判断：`"*.slnx/*.sln"` 里的 `/*` 不是块注释起点，
    /// 误判会让后面的整段代码都被当成注释。
    /// </remarks>
    private static string StripLiterals(string line)
    {
        var buffer = new char[line.Length];
        var length = 0;
        var inString = false;
        var inChar = false;
        var verbatim = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inString)
            {
                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }

                    continue;
                }

                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                verbatim = i > 0 && line[i - 1] == '@';
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            buffer[length++] = c;
        }

        return new string(buffer, 0, length);
    }

    /// <summary>
    /// AR-26：控件不得直接接触配置、视图状态、提交或字段写回 API。
    /// 控件只允许经 <c>IModuleViewContext</c> 的值契约（ReadValue / CommitValue / RegisterViewDefault）
    /// 与显示契约取数；任何后端调用都应被这一层拦下。
    /// </summary>
    [Fact]
    public void AR26_控件不得直接调用值后端()
    {
        var controlsDir = Path.Combine(RepoRoot, "src", "Sdk.Ui", "Controls");
        Directory.Exists(controlsDir).ShouldBeTrue($"缺少控件目录：{controlsDir}");

        var forbidden = new[]
        {
            "SubmitInput(",
            "ReadInputText(",
            "WriteInputText(",
            "UpdateField(",
            "ReadField(",
            "IConfigurationAccess",
            "IViewStateReader",
        };

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(controlsDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "DesignModuleViewContext.cs", StringComparison.Ordinal))
            {
                // 设计期上下文是 IModuleViewContext 的实现，不是控件。
                continue;
            }

            // ModuleLabel / ModuleNumeric 读配置字段标题是显示契约，不是字段编辑器后端。
            var allowReadField = string.Equals(name, "ModuleTextControls.cs", StringComparison.Ordinal);

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                foreach (var token in forbidden)
                {
                    if (allowReadField && string.Equals(token, "ReadField(", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{name}:{index + 1}: {token}");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "控件不得直接读配置/视图状态或提交值；请改走 IModuleViewContext 的值契约。违规：\n"
            + string.Join("\n", violations));
    }
}
