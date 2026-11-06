using HYWDebugAssistant.Core.Adapters;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 声明面的一致性判据：端口形状与类型形状、外部接入的两种消费方式。
/// </summary>
/// <remarks>
/// 两条都属于"框架只对账、不解释语义"的机械判定：它比的是模块自己的声明，不是框架的类型系统。
/// 同标识多形状的冲突判定由 <c>DataTypeCompatibilityTests.重复声明同标识</c> 与 SDK 自检覆盖。
/// </remarks>
public sealed class DeclarationConsistencyTests
{
    [Fact]
    public void 端口形状与所引用类型形状不一致_实例化时应报错()
    {
        using var runtime = new ModuleRuntime();
        Add(runtime, "m1", "shape-mismatch", typeof(PortShapeMismatchModule));

        // 判据取"归属 + 类别 + 严重度 + 端口名"，不钉文案（文案是资源表的事）。
        var shapeIssues = runtime.Issues.Snapshot()
            .Where(entry => entry.ObjectPath == "module:m1")
            .Where(entry => entry.Issue.Category == IssueCategory.InterfaceError)
            .ToList();

        shapeIssues.ShouldContain(entry => entry.Issue.Message.Contains("out", StringComparison.Ordinal));
        shapeIssues.ShouldAllBe(entry => entry.Issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void 外部接入_两种消费方式互斥_后一次被拒并只报一次()
    {
        var buffer = new byte[16];

        // 方向一：先设回调 ⇒ 主动读取被拒
        var issues = new IssueCenter();
        var access = new RuntimeExternalAccess(new ExternalAccessHost(), "i1", issues);

        access.SetReceiveHandler("a", _ => { });
        access.TryRead("a", buffer, out _).ShouldBeFalse();

        var reported = issues.Snapshot()
            .Count(entry => entry.Issue.Category == IssueCategory.InterfaceError);
        reported.ShouldBe(1);

        // 方向二：先主动读取 ⇒ 设回调被拒
        var issues2 = new IssueCenter();
        var access2 = new RuntimeExternalAccess(new ExternalAccessHost(), "i2", issues2);

        access2.TryRead("a", buffer, out _).ShouldBeFalse();
        access2.SetReceiveHandler("a", _ => { });

        issues2.Snapshot()
            .Count(entry => entry.Issue.Category == IssueCategory.InterfaceError)
            .ShouldBe(1);
    }

    private static void Add(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, Definition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static ModuleDefinition Definition(string name, Type entryType) =>
        new(
            ModuleId.Create("test", name),
            name,
            name,
            [],
            ModuleCapability.None,
            [],
            participatesInDataFlow: true,
            entryType,
            packageDirectory: string.Empty,
            assemblyPath: string.Empty);

    /// <summary>声明一个类型，口却用另一种形状 —— 正是"传输与计量各读一份"的错位。</summary>
    public sealed class PortShapeMismatchModule : IModule
    {
        private static readonly ContractShape Declared = ContractShape.OfStruct(
            ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
            ("data", ContractShape.OfBytes(16)));

        private static readonly ContractShape PortShape = ContractShape.OfStruct(
            ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
            ("data", ContractShape.OfBytes(32)));

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/raw", Declared));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, PortShape)
            {
                TypeId = "test/raw",
            });
        }
    }

}
