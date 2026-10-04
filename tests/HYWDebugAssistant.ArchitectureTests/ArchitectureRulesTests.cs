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
/// <b>目标程序集缺失即失败</b>——禁止「无对象则跳过」：规则静默失效比误报更危险。
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

    /// <summary>
    /// **AR-17：用户数据路径必须只有一处来源**（P5/P6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 纪律：所有需要持久化的数据**全部进 data**。落盘路径若散落多处（组合根 / 服务 / 设置 / 读取点…）
    /// ⇒ 「用户数据在哪」就没有单一答案（`architecture/10` 甚至并存两套互相矛盾的包根说法）。
    /// </para>
    /// <para>
    /// 本用例把这条纪律变成**可执行检查**：`settings.json` 这个文件名**只允许**出现在路径解析器里
    /// （`StoragePaths`）—— 谁再在别处拼一份，这里当场红。
    /// </para>
    /// </remarks>
    [Fact]
    public void AR17_用户数据文件名必须只有一处来源()
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
                if (line.Contains("\"settings.json\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber} 自己拼了设置文件名");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "用户数据路径只能从 `StoragePaths` 取（AR-17）：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// **AR-18：模块的窗口尺寸与端口默认位置只有包内 `view.json` 一个来源**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 口径：默认尺寸 / 最小尺寸 / 最大尺寸**全部进包内 `view.json`**，「网络标签在哪条边」这类位置也都可改
    /// ⇒ **json 为唯一来源**，代码里不留 `Edge` / `AnchorDraggable` 这类声明。
    /// </para>
    /// <para>
    /// 本用例把这条纪律变成**可执行检查**：模块源码里再出现尺寸常量或端口位置常量，这里当场红
    /// （框架侧的 `PortDeclaration.Edge` / `AnchorDraggable` 与 `ViewDeclaration.PreferredWidth` / `PreferredHeight`
    /// 已经删掉 —— 这些标识只剩"包内 json"一条通路）。
    /// </para>
    /// </remarks>
    [Fact]
    public void AR18_模块尺寸与端口位置必须只来自包内view_json()
    {
        string[] forbidden =
        [
            "PreferredWidth",
            "PreferredHeight",
            "AnchorDraggable",
            "Edge = Sdk.Ports.PortEdge.",
            "Edge = PortEdge.",
        ];

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (!file.Contains($"{Separator}Modules{Separator}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
            "模块的尺寸 / 端口默认位置只能写在包内 `view.json` 里（AR-18）："
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
}
