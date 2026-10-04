using HYWDebugAssistant.Tests.Reference.Stubs;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// 判据项目里**桩模块的包内声明**：数据面每个口都要有自己的位置，模块写的数据源也要登记。
/// </summary>
/// <remarks>
/// 桩模块走的是"直接注册一个 <c>ModuleDefinition</c>"这条近路，包目录只能由测试自己给；
/// 声明不合法（缺端口位置或缺源段）时**整份被拒** ⇒ 端口几何停在哨兵值、订阅缓冲也不会建立，
/// 于是链路判据看到的是"什么都没发生"。所以这里的表是判据能否成立的前提，不是可选的装饰。
/// </remarks>
internal static class TestViewPackages
{
    private static readonly Dictionary<Type, string> Table = new()
    {
        [typeof(RateSourceStubModule)] = Make(
            "ref-rate-source", [("out", "right")], [("value", "F64"), ("tick", "U64")]),
        [typeof(RejectCountingSenderStubModule)] = Make(
            "ref-reject-sender", [("out", "right")], [("sent", "U64"), ("rejected", "U64")]),
        [typeof(JudgementSinkStubModule)] = Make(
            "ref-judgement-sink", [("in", "left")],
            [("count", "U64"), ("passed", "U64"), ("failed", "U64"), ("deviation", "F64")]),
        [typeof(FusionStubModule)] = Make(
            "ref-fusion", [("in1", "left"), ("in2", "left"), ("out", "right")],
            [("fused", "F64"), ("n1", "U64"), ("n2", "U64")]),
        [typeof(AcceptanceCriteriaTests.HealthyGeneratorModule)] = Make(
            "ref-healthy", [("out", "right")], [("value", "F64")]),
        [typeof(AcceptanceCriteriaTests.FaultyGeneratorModule)] = Make(
            "ref-faulty", [("out", "right")], []),
        [typeof(AcceptanceCriteriaTests.CountingSinkModule)] = Make(
            "ref-counting-sink", [("in", "left")], []),
        [typeof(AcceptanceCriteriaTests.NewShapeSourceModule)] = Make(
            "ref-new-shape", [("out", "right")], [("value", "U32")]),
        [typeof(AcceptanceCriteriaTests.NewShapeSinkModule)] = Make(
            "ref-new-shape-sink", [("in", "left")], []),
    };

    /// <summary>取某入口类型的夹具包目录。</summary>
    /// <param name="entryType">模块入口类型。</param>
    public static string For(Type entryType) => Table[entryType];

    /// <summary>写一份只有端口位置与数据源、没有控件的声明，返回包目录。</summary>
    private static string Make(
        string name,
        IReadOnlyList<(string Port, string Edge)> ports,
        IReadOnlyList<(string Name, string ValueType)> sources)
    {
        var portItems = string.Join(", ", ports.Select(p => $$"""{ "name": "{{p.Port}}", "edge": "{{p.Edge}}" }"""));
        var sourceItems = string.Join(", ", sources.Select(s => $$"""{ "name": "{{s.Name}}", "kind": "Value", "valueType": "{{s.ValueType}}" }"""));

        var directory = Path.Combine(Path.GetTempPath(), $"hyw-reference-package-{name}");
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "view.json"),
            $$"""
            {
              "version": 1,
              "frame": { "ports": [ {{portItems}} ] },
              "sources": [ {{sourceItems}} ],
              "views": []
            }
            """);
        return directory;
    }
}
