using System.Diagnostics;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// 外部接入的框架级用例：声明 → 配置登记 → 会话开关 → 数据回调 → 失败 → 生命周期。
/// </summary>
/// <remarks>
/// 用假适配件与无活动体模块驱动；不碰真实端口/套接字，判据只看框架行为。
/// </remarks>
public sealed class ExternalAccessRuntimeTests
{
    [Fact]
    public void 声明外部接入_应登记参数类型并创建会话()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);

        var access = instance.ExternalAccesses.ShouldHaveSingleItem();
        access.Name.ShouldBe(FakeAccessModule.AccessName);
        _ = module;

        var typeName = typeof(FakeAccessParameters).FullName!;
        instance.Context.ConfigurationSnapshot.ContainsKey(typeName).ShouldBeTrue(
            "适配件交出的参数类型必须进实例配置快照（配置表单与持久化据此复用）");

        instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName).State
            .ShouldBe(ExternalAccessState.Closed);

        var services = new RuntimeServices(runtime);
        services.TryGetConfigurationSchema("m1", out var schema).ShouldBeTrue();
        schema!.Fields.Any(field => field.Name == nameof(FakeAccessParameters.Value))
            .ShouldBeTrue("适配件参数类型的字段应出现在配置 Schema 里（参数表单复用配置面）");
    }

    [Fact]
    public void 打开命令应开会话_外部数据应经回调到模块_关闭应关会话()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Open, "会话应打开");
        module.Adapter.OpenCount.ShouldBe(1);

        module.Adapter.Publish([1, 2, 3]);

        WaitFor(() => module.Received.Count == 1, "外部数据应经回调到模块");
        module.Received[0].ShouldBe(new byte[] { 1, 2, 3 });

        Send(runtime, "close");

        WaitFor(() => snapshot().State == ExternalAccessState.Closed, "关闭后应回到 Closed");
        module.Adapter.CloseCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void 打开失败应转Issue且不自动重试_再次请求才重开()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        module.Adapter.ThrowOnOpen = true;
        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Faulted, "打开失败应进入 Faulted");
        runtime.Issues.Count.ShouldBeGreaterThan(0, "失败应转成 Issue");

        Thread.Sleep(150);
        module.Adapter.OpenCount.ShouldBe(1, "框架不得自动重试");

        module.Adapter.ThrowOnOpen = false;
        Send(runtime, "open");

        WaitFor(() => snapshot().State == ExternalAccessState.Open, "用户再次请求应重开");
        module.Adapter.OpenCount.ShouldBe(2);
    }

    [Fact]
    public void 禁用应关会话_启用应按意图重开()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        Apply(runtime, new SetModuleEnabledOperation("m1", false));
        WaitFor(() => snapshot().Suspended && snapshot().State == ExternalAccessState.Closed, "禁用应关会话");

        Apply(runtime, new SetModuleEnabledOperation("m1", true));
        WaitFor(() => !snapshot().Suspended && snapshot().State == ExternalAccessState.Open, "启用应重开");

        module.Adapter.OpenCount.ShouldBe(2);
        module.Adapter.CloseCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void 参数变更应重开会话并拿到新参数()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        Apply(runtime, new UpdateConfigurationsOperation(
            "m1",
            [new FakeAccessParameters { Value = 42 }]));

        WaitFor(
            () => module.Adapter.OpenCount == 2 && snapshot().State == ExternalAccessState.Open,
            "参数变更应关闭并用新参数重开");

        module.Adapter.OpenedWith.ShouldNotBeNull();
        module.Adapter.OpenedWith!.Value.ShouldBe(42);
    }

    [Fact]
    public void 写失败应计入丢弃且不抛到模块()
    {
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out var instance);
        var snapshot = () => instance.Context.Access.GetSnapshot(FakeAccessModule.AccessName);

        Send(runtime, "open");
        WaitFor(() => snapshot().State == ExternalAccessState.Open, "先打开");

        module.Adapter.WriteOk = false;

        instance.Context.Access.Write(FakeAccessModule.AccessName, [1, 2, 3, 4])
            .ShouldBeFalse();

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
        using var runtime = NewRuntime();
        var module = AddModule(runtime, out _);

        Send(runtime, "open");
        WaitFor(() => module.Adapter.OpenCount == 1, "先打开");

        Apply(runtime, new RemoveModuleOperation("m1"));

        WaitFor(() => module.Adapter.CloseCount >= 1, "移除应关闭会话");
        runtime.ExternalAccessHost.ActiveInstanceCount.ShouldBe(0);
    }

    private static ModuleRuntime NewRuntime() => new(
        carrierOptions: new CarrierOptions
        {
            QueueCapacity = 16,
            StopTimeout = TimeSpan.FromSeconds(2),
            IdleWait = TimeSpan.FromMilliseconds(1),
        });

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
}
