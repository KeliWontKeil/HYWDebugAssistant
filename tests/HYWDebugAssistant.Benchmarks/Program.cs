using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// Gate B 数值实测入口（`build.ps1 -RunBenchmarks`）。数值**只记录、不设阈值**。
/// </summary>
/// <remarks>
/// 首轮实测使用**简版 job**（少量预热与迭代）以便在可接受时间内拿到量级结论；
/// 需要更稳的数值时可去掉 <see cref="FirstRunConfig"/> 改用默认 job（`--job default`）。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new FirstRunConfig());
        return 0;
    }
}

/// <summary>首轮实测配置：短 job + 内存诊断（每单元分配字节数是清单第 4 项）。</summary>
/// <remarks>
/// `<c>ManualConfig</c>` 的构造函数给的是**空配置**（不含任何列提供者/导出器/日志器），
/// 必须显式补齐——否则摘要表会打印成 "There are no columns to show"（数值拿不到）。
/// <para>
/// **输出去处固定在 `artifacts\benchmarks\`**：BenchmarkDotNet 默认丢一个
/// `BenchmarkDotNet.Artifacts\` 到当前工作目录，等于在仓库根另开一个临时根；
/// 临时产物只许落在 `artifacts\`（见 dev/workflow.md 的登记表与 tools/audit-temp-artifacts.ps1）。
/// </para>
/// </remarks>
internal sealed class FirstRunConfig : ManualConfig
{
    /// <summary>创建配置。</summary>
    public FirstRunConfig()
    {
        _ = AddJob(Job.ShortRun
            .WithId("M1 首轮")
            .WithWarmupCount(1)
            .WithIterationCount(3));

        _ = AddColumnProvider(DefaultColumnProviders.Instance);
        _ = AddDiagnoser(MemoryDiagnoser.Default);
        _ = AddLogger(ConsoleLogger.Default);

        // Markdown 表格最便于回写文档；同时**保留结果文件**（artifacts\benchmarks\results\*.md）。
        _ = AddExporter(MarkdownExporter.GitHub);

        _ = WithArtifactsPath(Path.Combine(FindRepoRoot(), "artifacts", "benchmarks"));
    }

    /// <summary>从程序目录向上找解决方案根（结果不写进仓库根，也不写进程序输出目录）。</summary>
    /// <remarks>
    /// 与截图工具的仓库根定位同一判据。
    /// 找不到就抛错：静默回落到"当前目录/默认路径"会让临时产物又一次跑出 artifacts。
    /// </remarks>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any() || directory.EnumerateFiles("*.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录（未找到 *.slnx/*.sln），无法确定基准输出去处");
    }
}
