using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-17 缺陷回归：**刷新不得重建列表**（否则悬停等瞬时视觉状态每帧被重置 → 列表"乱闪"）。
/// </summary>
/// <remarks>
/// 缺陷复盘：面板按刷新率（默认 60 Hz）驱动刷新，而实现里每帧 <c>Clear()</c> + 重新 <c>Add</c>，
/// 于是列表项容器每秒被重建 60 次 —— 鼠标悬停时表现为**所有选项乱闪**。
/// 断言方式：**数据未变时，行实例必须保持同一个对象**（引用相等即"没有被重建"）。
/// </remarks>
public class PanelRefreshStabilityTests
{
    [Fact]
    public void 模块清单_连续刷新不得重建行实例()
    {
        using var harness = new ShellHarness();
        harness.Register("stability-sender", typeof(StabilitySenderModule));

        harness.Shell.Catalog.Refresh();
        var first = harness.Shell.Catalog.Catalog.ToList();
        first.ShouldNotBeEmpty();

        // 模拟连续 5 帧刷新（数据未变）
        for (var i = 0; i < 5; i++)
        {
            harness.Shell.Catalog.Refresh();
        }

        var after = harness.Shell.Catalog.Catalog.ToList();
        after.Count.ShouldBe(first.Count);
        for (var i = 0; i < first.Count; i++)
        {
            ReferenceEquals(first[i], after[i])
                .ShouldBeTrue("数据未变时行实例必须复用 —— 否则列表项容器每帧重建（悬停状态被重置 → 乱闪）");
        }
    }

    [Fact]
    public void 来源诊断_连续刷新不得重建行实例()
    {
        using var harness = new ShellHarness();

        harness.Shell.Catalog.Refresh();
        var first = harness.Shell.Catalog.Sources.ToList();

        for (var i = 0; i < 5; i++)
        {
            harness.Shell.Catalog.Refresh();
        }

        var after = harness.Shell.Catalog.Sources.ToList();
        after.Count.ShouldBe(first.Count);
        for (var i = 0; i < first.Count; i++)
        {
            ReferenceEquals(first[i], after[i]).ShouldBeTrue("来源诊断行必须复用（同一原因）");
        }
    }

    [Fact]
    public void Issue列表_连续刷新不得重建行实例()
    {
        using var harness = new ShellHarness();
        harness.Runtime.Issues.Report("module:a", new HYWDebugAssistant.Sdk.Reporting.IssueReport(
            HYWDebugAssistant.Sdk.Reporting.IssueCategory.CompositionError,
            HYWDebugAssistant.Sdk.Reporting.IssueSeverity.Warning,
            "测试问题"));

        harness.Shell.Issues.Refresh();
        var first = harness.Shell.Issues.Issues.ToList();
        first.ShouldNotBeEmpty();

        for (var i = 0; i < 5; i++)
        {
            harness.Shell.Issues.Refresh();
        }

        var after = harness.Shell.Issues.Issues.ToList();
        for (var i = 0; i < first.Count; i++)
        {
            ReferenceEquals(first[i], after[i]).ShouldBeTrue("Issue 行必须复用（同一原因）");
        }
    }

    [Fact]
    public void 拓扑诊断_连续刷新不得重建行实例()
    {
        using var harness = new ShellHarness();
        harness.Register("stability-sender", typeof(StabilitySenderModule));
        harness.Register("stability-receiver", typeof(StabilityReceiverModule));

        harness.Services.AddModule("s1", "test/stability-sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("r1", "test/stability-receiver/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();

        harness.Shell.Topology.Refresh();
        var first = harness.Shell.Topology.Rows.ToList();
        first.ShouldNotBeEmpty();

        for (var i = 0; i < 5; i++)
        {
            harness.Shell.Topology.Refresh();
        }

        var after = harness.Shell.Topology.Rows.ToList();
        for (var i = 0; i < first.Count; i++)
        {
            ReferenceEquals(first[i], after[i]).ShouldBeTrue("拓扑行必须复用（同一原因）");
        }
    }

    [Fact]
    public void 数据变化时_仍应更新列表内容()
    {
        using var harness = new ShellHarness();

        harness.Shell.Catalog.Refresh();
        var before = harness.Shell.Catalog.Catalog.Count;

        // 新装入一个模块包 → 列表必须更新（增量同步不能"该变不变"）
        harness.Register("extra", typeof(StabilitySenderModule));
        harness.Shell.Catalog.Refresh();

        harness.Shell.Catalog.Catalog.Count.ShouldBe(before + 1);
    }

    private sealed class ShellHarness : IDisposable
    {
        public ShellHarness()
        {
            Runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();

            Services = new HYWDebugAssistant.Core.Services.RuntimeServices(Runtime);
            Shell = new ShellViewModel(Services, Services);
        }

        public HYWDebugAssistant.Core.Runtime.ModuleRuntime Runtime { get; }

        public HYWDebugAssistant.Core.Services.RuntimeServices Services { get; }

        public ShellViewModel Shell { get; }

        public void Register(string name, Type entryType)
        {
            var definition = new HYWDebugAssistant.Hosting.Registry.ModuleDefinition(
                HYWDebugAssistant.Sdk.ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                HYWDebugAssistant.Sdk.ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        public void Dispose() => Runtime.Dispose();
    }

    /// <summary>稳定性测试用模块（发送口）。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "stability-sender", "1.0.0")]
    public sealed class StabilitySenderModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            context.Declaration.DeclarePort(new HYWDebugAssistant.Sdk.Ports.PortDeclaration(
                "out",
                HYWDebugAssistant.Sdk.Ports.PortDirection.Send,
                HYWDebugAssistant.Sdk.Contracts.ContractShape.OfPrimitive(
                    HYWDebugAssistant.Sdk.Contracts.ContractPrimitive.F32)));
    }

    /// <summary>稳定性测试用模块（接收口）。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "stability-receiver", "1.0.0")]
    public sealed class StabilityReceiverModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            context.Declaration.DeclarePort(new HYWDebugAssistant.Sdk.Ports.PortDeclaration(
                "in",
                HYWDebugAssistant.Sdk.Ports.PortDirection.Receive,
                HYWDebugAssistant.Sdk.Contracts.ContractShape.OfPrimitive(
                    HYWDebugAssistant.Sdk.Contracts.ContractPrimitive.F32)));
    }
}
