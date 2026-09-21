using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.ArchitectureTests;

/// <summary>
/// 架构约束检查（ADR-0025 §2，自研源码/项目文件扫描，零外部依赖）。
/// 规则：
///   A1 Core 角色项目不得依赖 UI（Avalonia）
///   A2 模块项目之间不得相互引用（仅经 Modules.Sdk 契约）—— 顶层 P12
///   A3 UI 角色项目不得引用具体模块实现（只允许 Modules.Sdk）
///   A4 命名空间应与所在目录结构一致
///   A5 Core 角色项目内不得出现具体业务词汇（P1/P2/P13）
///   A6 不得引用 ADR-0024 排除清单中的第三方组件（许可合规）
/// 说明：分层项目尚未建立时，A1/A2/A3/A5 无对象可查，将标记为跳过（不影响通过）。
/// </summary>
public class ArchitectureRulesTests
{
    // ---------------- 基础设施 ----------------

    private static readonly string RepoRoot = FindRepoRoot();

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

    private static List<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

    private static List<string> ProjectFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

    /// <summary>从源码文件路径推断所属项目名（src 下第一层目录）。</summary>
    private static string ProjectNameOf(string filePath)
    {
        var rel = Path.GetRelativePath(Path.Combine(RepoRoot, "src"), filePath);
        var segments = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[0] : string.Empty;
    }

    private enum Role { App, Core, Sdk, Module, Ui, Other }

    private static Role RoleOf(string projectName)
    {
        if (projectName.EndsWith(".Modules.Sdk", StringComparison.Ordinal))
        {
            return Role.Sdk;
        }

        if (projectName.EndsWith(".Core", StringComparison.Ordinal))
        {
            return Role.Core;
        }

        if (projectName.Contains(".Modules.", StringComparison.Ordinal))
        {
            return Role.Module;
        }

        if (projectName.EndsWith(".UI", StringComparison.Ordinal)
            || projectName.EndsWith(".Ui", StringComparison.Ordinal))
        {
            return Role.Ui;
        }

        if (projectName.EndsWith(".App", StringComparison.Ordinal))
        {
            return Role.App;
        }

        return Role.Other;
    }

    private static readonly string[] ForbiddenNamespaces =
    {
        "AvaloniaUI.DiagnosticsSupport", "Avalonia.Accelerate", "DevExpress", "Telerik",
        "Actipro", "Syncfusion", "MediatR", "AutoMapper", "FluentAssertions", "Moq"
    };

    private static readonly string[] CoreBusinessWords =
    {
        "Uart", "Foc", "Plotter", "SerialPort", "Modbus", "CanBus"
    };

    private static readonly Regex NamespaceRegex = new(@"^\s*namespace\s+([A-Za-z_][\w\.]*)\s*[;{]", RegexOptions.Multiline);
    private static readonly Regex UsingRegex = new(@"^\s*using\s+(?:static\s+)?([A-Za-z_][\w\.]*)\s*;", RegexOptions.Multiline);
    private static readonly Regex ProjectRefRegex = new(@"<ProjectReference\s+Include=""(?<path>[^""]+)""", RegexOptions.IgnoreCase);

    // ---------------- A4：命名空间与目录一致 ----------------

    [Fact]
    public void A4_命名空间应与目录结构一致()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles())
        {
            var projectName = ProjectNameOf(file);
            var fileDir = Path.GetDirectoryName(file)!;
            var projectDir = Path.Combine(RepoRoot, "src", projectName);
            var subPath = Path.GetRelativePath(projectDir, fileDir);
            var expectedPrefix = subPath == "."
                ? projectName
                : projectName + "." + subPath.Replace(Path.DirectorySeparatorChar, '.');

            var text = File.ReadAllText(file);
            var match = NamespaceRegex.Match(text);
            if (!match.Success)
            {
                continue; // 无命名空间声明（如纯 global 文件）→ 跳过
            }

            var actual = match.Groups[1].Value;
            if (!actual.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetRelativePath(RepoRoot, file)}：命名空间 '{actual}' 与目录期望前缀 '{expectedPrefix}' 不一致");
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    // ---------------- A6：许可合规（禁止被排除的第三方组件） ----------------

    [Fact]
    public void A6_不得引用被排除的第三方组件()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in UsingRegex.Matches(text))
            {
                var ns = m.Groups[1].Value;
                if (ForbiddenNamespaces.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot, file)}：引用了禁止的命名空间 '{ns}'（ADR-0024 排除清单）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    // ---------------- A1/A5：Core 角色约束 ----------------

    [Fact]
    public void A1_Core角色项目不得依赖UI或业务词汇()
    {
        var coreFiles = SourceFiles().Where(f => RoleOf(ProjectNameOf(f)) == Role.Core).ToList();
        if (coreFiles.Count == 0)
        {
            return; // 分层尚未落地 → 跳过
        }

        var violations = new List<string>();
        foreach (var file in coreFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in UsingRegex.Matches(text))
            {
                var ns = m.Groups[1].Value;
                if (ns.StartsWith("Avalonia", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot, file)}：Core 不得依赖 UI（'{ns}'）");
                }
            }

            foreach (var word in CoreBusinessWords)
            {
                if (text.Contains(word, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot, file)}：Core 内出现业务词汇 '{word}'（P1/P2/P13）；如属误报请加 ARCH-EXCEPTION 说明");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    // ---------------- A2：模块之间不得相互引用（P12） ----------------

    [Fact]
    public void A2_模块项目之间不得相互引用()
    {
        var projectRefs = ProjectFiles()
            .Select(p => new
            {
                Project = Path.GetFileNameWithoutExtension(p),
                Refs = ProjectRefRegex.Matches(File.ReadAllText(p))
                    .Select(m => Path.GetFileNameWithoutExtension(m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)))
                    .ToList()
            })
            .Where(x => RoleOf(x.Project) == Role.Module)
            .ToList();

        if (projectRefs.Count == 0)
        {
            return; // 尚无模块项目 → 跳过
        }

        var violations = new List<string>();
        foreach (var entry in projectRefs)
        {
            foreach (var target in entry.Refs.Where(r => RoleOf(r) == Role.Module))
            {
                violations.Add($"模块项目 '{entry.Project}' 直接引用了另一个模块 '{target}'（应由 Interface 契约组合，P12）");
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    // ---------------- A3：UI 不得引用具体模块实现 ----------------

    [Fact]
    public void A3_UI角色项目不得引用具体模块实现()
    {
        var uiProjects = ProjectFiles().Where(p => RoleOf(Path.GetFileNameWithoutExtension(p)) == Role.Ui).ToList();
        if (uiProjects.Count == 0)
        {
            return; // 分层尚未落地 → 跳过
        }

        var violations = new List<string>();
        foreach (var proj in uiProjects)
        {
            var projName = Path.GetFileNameWithoutExtension(proj);
            foreach (Match m in ProjectRefRegex.Matches(File.ReadAllText(proj)))
            {
                var target = Path.GetFileNameWithoutExtension(m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar));
                if (RoleOf(target) == Role.Module)
                {
                    violations.Add($"UI 项目 '{projName}' 引用了具体模块 '{target}'（应仅依赖 Modules.Sdk 契约）");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }
}
