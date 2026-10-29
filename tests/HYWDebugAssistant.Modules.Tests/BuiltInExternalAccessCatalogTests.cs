using System.Reflection;
using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **内置外部标准目录的一致性**：四个标准可解析、descriptor 的参数类型与适配器一致、
/// 视图字段名常量都能在对应参数类型上找到同名属性。
/// </summary>
/// <remarks>
/// 这些是框架侧所有权的一部分（模块只声明标准标识）：任何一处漂移都会让模块视图静默空白，
/// 所以用一致性判据钉住，而不是依赖运行期才发现。
/// </remarks>
public sealed class BuiltInExternalAccessCatalogTests
{
    private static readonly BuiltInExternalAccessCatalog Catalog = new();

    private static readonly (string StandardId, Type ParametersType, string[] FieldNames)[] Standards =
    [
        (ExternalAccessStandardIds.Serial, typeof(SerialConfig),
            [ExternalAccessFieldNames.PortName, ExternalAccessFieldNames.BaudRate, ExternalAccessFieldNames.DataBits,
             ExternalAccessFieldNames.StopBits, ExternalAccessFieldNames.Parity]),
        (ExternalAccessStandardIds.TcpClient, typeof(TcpClientConfig),
            [ExternalAccessFieldNames.RemoteHost, ExternalAccessFieldNames.RemotePort,
             ExternalAccessFieldNames.ConnectTimeout, ExternalAccessFieldNames.NoDelay]),
        (ExternalAccessStandardIds.TcpServer, typeof(TcpServerConfig),
            [ExternalAccessFieldNames.LocalAddress, ExternalAccessFieldNames.LocalPort,
             ExternalAccessFieldNames.MaxConnections, ExternalAccessFieldNames.NoDelay]),
        (ExternalAccessStandardIds.Udp, typeof(UdpConfig),
            [ExternalAccessFieldNames.LocalAddress, ExternalAccessFieldNames.LocalPort,
             ExternalAccessFieldNames.RemoteAddress, ExternalAccessFieldNames.RemotePort,
             ExternalAccessFieldNames.Broadcast]),
    ];

    /// <summary>四个标准行（xUnit MemberData）。</summary>
    public static IEnumerable<object[]> StandardRows =>
        Standards.Select(row => new object[] { row.StandardId, row.ParametersType, row.FieldNames });

    [Fact]
    public void 内置目录_应登记四个标准且标识唯一()
    {
        var ids = Catalog.Standards.Select(descriptor => descriptor.StandardId).ToArray();

        ids.ShouldBe(
            [
                ExternalAccessStandardIds.Serial,
                ExternalAccessStandardIds.TcpClient,
                ExternalAccessStandardIds.TcpServer,
                ExternalAccessStandardIds.Udp,
            ],
            ignoreOrder: true);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Length, "标准标识必须唯一");
    }

    [Theory]
    [MemberData(nameof(StandardRows))]
    public void 标准应可解析_参数类型应与适配器一致(string standardId, Type parametersType, string[] fieldNames)
    {
        Catalog.TryResolve(standardId, out var descriptor).ShouldBeTrue($"标准应已登记：{standardId}");

        descriptor.StandardId.ShouldBe(standardId);
        descriptor.ParametersType.ShouldBe(parametersType);

        var adapter = descriptor.CreateAdapter();
        adapter.ParametersType.ShouldBe(
            parametersType,
            $"descriptor 的参数类型与适配器不一致：{standardId}");

        foreach (var fieldName in fieldNames)
        {
            parametersType
                .GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
                .ShouldNotBeNull($"字段名常量 '{fieldName}' 在 {parametersType.Name} 上找不到同名属性");
        }
    }

    [Fact]
    public void 字段名常量_应全部映射到参数类型()
    {
        var declared = typeof(ExternalAccessFieldNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var mapped = Standards
            .SelectMany(row => row.FieldNames)
            .ToHashSet(StringComparer.Ordinal);

        declared.ShouldBe(mapped, ignoreOrder: true, "新增字段名常量时必须同时映射到对应参数类型");
    }

    [Fact]
    public void 未知标准标识_应返回false()
    {
        Catalog.TryResolve("not-a-standard", out _).ShouldBeFalse();
        Catalog.TryResolve(string.Empty, out _).ShouldBeFalse();
    }
}
