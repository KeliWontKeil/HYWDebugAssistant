using System.Diagnostics;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// 外部接入的框架级用例：声明 → catalog 解析 → 配置登记 → 会话开关 → 数据回调 → 失败 → 生命周期。
/// </summary>
/// <remarks>
/// 用假 catalog/适配件与无活动体模块驱动；不碰真实端口/套接字，判据只看框架行为。
/// </remarks>
public sealed class ExternalAccessRuntimeTests
{
    [Fact]
    public void 声明外部接入_应登记参数类型并创建会话()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);

        var access = instance.ExternalAccesses.ShouldHaveSingleItem();
        access.Name.ShouldBe(FakeAccessModule.AccessName);
        access.StandardId.ShouldBe(FakeAccessModule.StandardId);
        catalog.Standards.ShouldHaveSingleItem();

        var typeName = typeof(FakeAccessParameters).FullName!;
        instance.Context.ConfigurationSnapshot.ContainsKey(typeName).ShouldBeTrue(
            "catalog 给出的参数类型必须进实例配置快照（配置表单与持久化据此复用）");

        instance.Context.Access.GetParametersType(FakeAccessModule.AccessName)
            .ShouldBe(typeof(FakeAccessParameters), "模块骨架要能经 GetParametersType 反射配置字段");

        instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName).State
            .ShouldBe(ExternalAccessState.Closed);

        var services = new RuntimeServices(runtime);
        services.TryGetConfigurationSchema("m1", out var schema).ShouldBeTrue();
        schema!.Fields.Any(field => field.Name == nameof(FakeAccessParameters.Value))
            .ShouldBeTrue("catalog 参数类型的字段应出现在配置 Schema 里（参数表单复用配置面）");
    }

    [Fact]
    public void 打开命令应开会话_外部数据应经回调到模块_关闭应关会话()
    {
        using var runtime = NewRuntime(out var catalog);
        var module = AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Open, "会话应打开");
        snapshot().Endpoint.ShouldNotBeNull("打开后快照必须带端点事实");
        adapter.OpenCount.ShouldBe(1);

        adapter.Publish([1, 2, 3]);

        WaitFor(() => module.Received.Count == 1, "外部数据应经回调到模块");
        module.Received[0].ShouldBe(new byte[] { 1, 2, 3 });

        Send(runtime, "close");

        WaitFor(() => snapshot().State == ExternalAccessState.Closed, "关闭后应回到 Closed");
        adapter.CloseCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void 打开失败应转Issue且不自动重试_再次请求才重开()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        adapter.ThrowOnOpen = true;
        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Faulted, "打开失败应进入 Faulted");
        snapshot().Failure!.Kind.ShouldBe(ExternalAccessFailureKind.OpenFailed);
        runtime.Issues.Count.ShouldBeGreaterThan(0, "失败应转成 Issue");
        runtime.Issues.Snapshot().ShouldContain(
            entry => entry.Issue.Severity == IssueSeverity.Warning,
            "外部接入失败是可恢复的运行状态，不得占错误角标");

        Thread.Sleep(150);
        adapter.OpenCount.ShouldBe(1, "框架不得自动重试");

        adapter.ThrowOnOpen = false;
        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Open, "用户再次请求应重开");
        snapshot().Failure.ShouldBeNull("打开成功必须清掉上一次失败");
        adapter.OpenCount.ShouldBe(2);
    }

    [Fact]
    public void 对端断开等外部故障_应降级为警告而不是错误()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        adapter.Fault(new ExternalAccessFailure(
            ExternalAccessFailureKind.PeerClosed,
            new ExternalAccessEndpoint("127.0.0.1", 1),
            Detail: "对端已关闭连接"));

        WaitFor(() => runtime.Issues.Count > 0, "外部故障应转成 Issue");
        snapshot().Failure!.Kind.ShouldBe(ExternalAccessFailureKind.PeerClosed);
        var issue = runtime.Issues.Snapshot()[^1];
        issue.Issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Issue.Category.ShouldBe(IssueCategory.CommunicationError);
    }

    [Fact]
    public void 禁用应关会话_启用应按意图重开()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        Apply(runtime, new SetModuleEnabledOperation("m1", false));
        WaitFor(() => snapshot().Suspended && snapshot().State == ExternalAccessState.Closed, "禁用应关会话");

        Apply(runtime, new SetModuleEnabledOperation("m1", true));
        WaitFor(() => !snapshot().Suspended && snapshot().State == ExternalAccessState.Open, "启用应重开");

        adapter.OpenCount.ShouldBe(2);
        adapter.CloseCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void 参数变更应重开会话并拿到新参数()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        Apply(runtime, new UpdateConfigurationsOperation(
            "m1",
            [new FakeAccessParameters { Value = 42 }]));

        WaitFor(
            () => adapter.OpenCount == 2 && snapshot().State == ExternalAccessState.Open,
            "参数变更应关闭并用新参数重开");

        adapter.OpenedWith.ShouldNotBeNull();
        adapter.OpenedWith!.Value.ShouldBe(42);
    }

    [Fact]
    public void 写失败应计入丢弃且不抛到模块()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out var instance);
        var adapter = catalog.Adapter;
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        adapter.WriteOk = false;

        instance.Context.Access.Write(FakeAccessModule.AccessName, [1, 2, 3, 4])
            .ShouldBeTrue();

        WaitFor(() => snapshot().DroppedBytes >= 4, "写失败应按字节计入丢弃");
    }

    [Fact]
    public void 会话队列满时应丢旧并计数_目标缓冲不足应丢弃不截断()
    {
        var queue = new AccessInboundBuffer(2);

        queue.TryEnqueue([1]);
        queue.TryEnqueue([2, 2]);
        queue.TryEnqueue([3, 3, 3]);

        queue.Count.ShouldBe(2);
        queue.Dropped.ShouldBe(1);
        queue.DroppedBytes.ShouldBe(1);

        var destination = new byte[4];
        queue.TryDequeue(destination, out var length).ShouldBeTrue();
        length.ShouldBe(2);
        destination[..2].ShouldBe(new byte[] { 2, 2 });

        // 目标缓冲不足：该单元被丢弃并计数，绝不截断交付。
        var tooSmall = new byte[1];
        queue.TryDequeue(tooSmall, out _).ShouldBeFalse();
        queue.Dropped.ShouldBe(2);
        queue.DroppedBytes.ShouldBe(4);
    }

    [Fact]
    public void 移除实例应关闭会话()
    {
        using var runtime = NewRuntime(out var catalog);
        AddModule(runtime, out _);
        var adapter = catalog.Adapter;

        Send(runtime, "open");
        WaitFor(() => adapter.OpenCount == 1, "先打开");

        Apply(runtime, new RemoveModuleOperation("m1"));

        WaitFor(() => adapter.CloseCount >= 1, "移除应关闭会话");
        runtime.ExternalAccessHost.ActiveInstanceCount.ShouldBe(0);
    }

    [Fact]
    public void 激活中途失败_应关闭已创建的适配器()
    {
        var first = new FakeExternalAccessAdapter();
        var catalog = new TwoStandardCatalog(
            new ExternalAccessDescriptor("std-a", typeof(FakeAccessParameters), () => first),
            new ExternalAccessDescriptor(
                "std-b",
                typeof(FakeAccessParameters),
                static () => throw new InvalidOperationException("工厂失败")));

        using var host = new ExternalAccessHost(catalog: catalog);
        var declarations = new[]
        {
            new ExternalAccessDeclaration("a", "std-a"),
            new ExternalAccessDeclaration("b", "std-b"),
        };

        host.TryActivate("i1", declarations, _ => new FakeAccessParameters(), out var failure)
            .ShouldBeFalse();

        failure.ShouldNotBeNull();
        failure.ShouldNotBeNullOrWhiteSpace();
        first.CloseCount.ShouldBe(1, "中途失败时已创建的适配器必须被关闭，不能留给 GC");
    }

    [Fact]
    public void 参数未登记_应给出种类与类型名()
    {
        var session = new ExternalAccessSession(
            new ExternalAccessDeclaration("fake", FakeAccessModule.StandardId),
            new FakeExternalAccessAdapter(),
            typeof(FakeAccessParameters),
            _ => null,
            new AccessInboundBuffer(4),
            TimeSpan.FromSeconds(1),
            signal: null);

        session.RequestOpen();
        _ = session.Pump();

        var snapshot = session.GetSnapshot();
        snapshot.State.ShouldBe(ExternalAccessState.Faulted);
        snapshot.Failure!.Kind.ShouldBe(ExternalAccessFailureKind.ParametersNotRegistered);
        snapshot.Failure.Detail.ShouldBe(nameof(FakeAccessParameters));

        var failures = new List<AccessFailure>();
        session.DrainFailures(failures);
        failures.ShouldNotBeEmpty("会话自身失败仍要转成 Issue");
    }

    [Fact]
    public void 未打开时写入_只计丢弃不留下失败()
    {
        var session = new ExternalAccessSession(
            new ExternalAccessDeclaration("fake", FakeAccessModule.StandardId),
            new FakeExternalAccessAdapter(),
            typeof(FakeAccessParameters),
            _ => new FakeAccessParameters(),
            new AccessInboundBuffer(4),
            TimeSpan.FromSeconds(1),
            signal: null);

        session.Write([1, 2]).ShouldBeFalse();

        var snapshot = session.GetSnapshot();
        snapshot.State.ShouldBe(ExternalAccessState.Closed);
        snapshot.Failure.ShouldBeNull("未打开时写入是正常丢弃，不应留下失败");

        var failures = new List<AccessFailure>();
        session.DrainFailures(failures);
        failures.ShouldBeEmpty("未打开时写入不应刷 Issue");
    }

    private static ModuleRuntime NewRuntime(out FakeExternalAccessCatalog catalog)
    {
        catalog = new FakeExternalAccessCatalog();
        return new ModuleRuntime(
            carrierOptions: new CarrierOptions
            {
                QueueCapacity = 16,
                StopTimeout = TimeSpan.FromSeconds(2),
                IdleWait = TimeSpan.FromMilliseconds(1),
            },
            catalog: catalog);
    }

    private static FakeAccessModule AddModule(ModuleRuntime runtime, out ModuleInstance instance)
    {
        var result = runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(
            "m1",
            FakeModuleDefinition.Create(typeof(FakeAccessModule)))));

        result.Applied.ShouldBeTrue(string.Join("；", result.Failures));
        runtime.Content.TryGetInstance("m1", out var found).ShouldBeTrue();
        instance = found!;
        return (FakeAccessModule)instance.Module;
    }

    private static void Send(ModuleRuntime runtime, string command) =>
        Apply(runtime, new SendCommandOperation("m1", command));

    private static void Apply(ModuleRuntime runtime, ChangeOperation operation)
    {
        var result = runtime.Apply(ChangeSet.OfOne(operation));
        result.Applied.ShouldBeTrue(string.Join("；", result.Failures));
    }

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException(message);
            }

            Thread.Sleep(10);
        }
    }

    /// <summary>测试用目录：一组显式 descriptor（用于覆盖激活中途失败的清理路径）。</summary>
    private sealed class TwoStandardCatalog(params ExternalAccessDescriptor[] standards) : IExternalAccessCatalog
    {
        public IReadOnlyList<ExternalAccessDescriptor> Standards { get; } = standards;

        public bool TryResolve(string standardId, out ExternalAccessDescriptor descriptor)
        {
            descriptor = null!;

            foreach (var candidate in Standards)
            {
                if (string.Equals(candidate.StandardId, standardId, StringComparison.Ordinal))
                {
                    descriptor = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
