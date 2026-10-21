using HYWDebugAssistant.Tests.Reference.Stubs;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// 判据项目里**桩模块的包目录**：框架不再从包目录读取界面/几何事实，这里只提供一个存在的目录。
/// </summary>
/// <remarks>
/// 桩模块的端口边、显示源等声明全部在各自的 <c>OnRegistered</c> 里（代码是唯一来源）；
/// 包目录只用于满足 <c>ModuleDefinition</c> 的路径参数。**不要在这里写任何 <c>view.json</c>**：
/// 那会让一个已经被删除的声明面在测试夹具里复活。
/// </remarks>
internal static class TestViewPackages
{
    private static readonly Dictionary<Type, string> Table = new()
    {
        [typeof(RateSourceStubModule)] = Make("ref-rate-source"),
        [typeof(RejectCountingSenderStubModule)] = Make("ref-reject-sender"),
        [typeof(JudgementSinkStubModule)] = Make("ref-judgement-sink"),
        [typeof(FusionStubModule)] = Make("ref-fusion"),
        [typeof(AcceptanceCriteriaTests.HealthyGeneratorModule)] = Make("ref-healthy"),
        [typeof(AcceptanceCriteriaTests.FaultyGeneratorModule)] = Make("ref-faulty"),
        [typeof(AcceptanceCriteriaTests.CountingSinkModule)] = Make("ref-counting-sink"),
        [typeof(AcceptanceCriteriaTests.NewShapeSourceModule)] = Make("ref-new-shape"),
        [typeof(AcceptanceCriteriaTests.NewShapeSinkModule)] = Make("ref-new-shape-sink"),
    };

    /// <summary>取某入口类型的夹具包目录。</summary>
    /// <param name="entryType">模块入口类型。</param>
    public static string For(Type entryType) => Table[entryType];

    /// <summary>创建一个空包目录。</summary>
    private static string Make(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hyw-reference-package-{name}");
        _ = Directory.CreateDirectory(directory);
        return directory;
    }
}
