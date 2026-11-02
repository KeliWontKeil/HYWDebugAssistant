using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单 7：**每模块一线程**在 100 / 300 / 1000 个活跃实例下的线程内存占用与建立开销。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>前提：**活跃模块通常 ≤ 50，最多数十**（`04a` Q4-1）——1000 是**压力上界**，用于量化线性代价；</item>
///   <item>测的是"加入 N 个实例"的耗时与内存增量（含线程创建）；工作集是进程级近似值，只作量级参考；</item>
///   <item>数值**只记录、不设阈值**：若未来出现数百活跃模块，按 `04a` §2.5 形态 B 演进（投递语义不变）。</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class ThreadScalingBenchmarks
{
    /// <summary>活跃实例数。</summary>
    [Params(100, 300, 1000)]
    public int Instances { get; set; }

    [Benchmark]
    public long AddInstancesAndMeasure()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Register(services);

        var workingSetBefore = Environment.WorkingSet;
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < Instances; i++)
        {
            if (!services.AddModule($"m{i}", "bench/idle").Succeeded)
            {
                throw new InvalidOperationException("加入实例失败");
            }
        }

        watch.Stop();
        var managedAfter = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetAfter = Environment.WorkingSet;

        Console.WriteLine(
            $"[{Instances} 实例] 建立 {watch.ElapsedMilliseconds} ms · "
            + $"工作集增量 {(workingSetAfter - workingSetBefore) / 1024d / 1024d:F1} MB · "
            + $"托管堆增量 {(managedAfter - managedBefore) / 1024d:F0} KB · "
            + $"托管堆/实例 {(managedAfter - managedBefore) / (double)Instances / 1024d:F1} KB");

        return watch.ElapsedMilliseconds;
    }

    private static void Register(RuntimeServices services)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("bench", "idle"),
            "IdleBenchModule",
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            typeof(IdleBenchModule),
            string.Empty,
            string.Empty);

        if (!services.Registry.TryRegister(definition, out _))
        {
            throw new InvalidOperationException("注册基准模块失败");
        }
    }

    /// <summary>空转模块：只声明一个口、没有任何活动（纯"占一个专属线程"）。</summary>
    [ModuleDefinition("bench", "idle")]
    public sealed class IdleBenchModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }
}
