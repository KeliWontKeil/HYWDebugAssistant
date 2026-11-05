using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 生命周期钩子的线程归属与 <c>Disabled</c> 暂停语义：
/// 除 <c>OnRegistered</c> 外都在实例线程执行；禁用期间不再调用 <c>OnStep</c>。
/// </summary>
public class LifecycleThreadTests
{
    [Fact]
    public void 钩子线程_除Registered外应在实例线程_且Activated先于Start()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        Register(services);
        services.AddModule("probe", "test/lifecycle-probe").Succeeded.ShouldBeTrue();

        var probe = Probe(runtime);
        probe.Activated.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("OnActivated 应执行");
        probe.Started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("首个 OnStep(Start) 应执行");

        var registered = probe.Event("Registered");
        var activated = probe.Event("Activated");
        var start = probe.StepEvent(StepReason.Start);

        registered.ThreadId.ShouldNotBe(start.ThreadId, "OnRegistered 在实例线程创建前执行");
        activated.ThreadId.ShouldBe(start.ThreadId, "OnActivated 必须在实例线程执行");
        activated.Sequence.ShouldBeLessThan(start.Sequence, "OnActivated 必须先于首个 OnStep(Start)");

        services.RemoveModule("probe").Succeeded.ShouldBeTrue();
        probe.Removed.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("OnRemoved 应执行");
        probe.Event("Removed").ThreadId.ShouldBe(start.ThreadId, "OnRemoved 必须在实例线程、停止前执行");
    }

    [Fact]
    public void 禁用_不调用OnStep_启用后恢复()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        Register(services);
        services.AddModule("probe", "test/lifecycle-probe").Succeeded.ShouldBeTrue();

        var probe = Probe(runtime);
        probe.Started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        var before = probe.StepCount;

        services.SetModuleEnabled("probe", false).Succeeded.ShouldBeTrue();
        probe.Disabled.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("OnDisabled 应执行");
        probe.Event("Disabled").ThreadId.ShouldBe(probe.StepEvent(StepReason.Start).ThreadId);

        services.SendCommand("probe", LifecycleProbeModule.ProbeCommand).Succeeded.ShouldBeTrue();
        services.SubmitInput("probe", LifecycleProbeModule.ProbeInput, "x").Succeeded.ShouldBeTrue();
        Thread.Sleep(100);
        probe.StepCount.ShouldBe(before, "禁用期间命令/输入不得推进 OnStep");

        services.SetModuleEnabled("probe", true).Succeeded.ShouldBeTrue();
        probe.Enabled.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("OnEnabled 应执行");
        probe.Event("Enabled").ThreadId.ShouldBe(probe.StepEvent(StepReason.Start).ThreadId);

        services.SubmitInput("probe", LifecycleProbeModule.ProbeInput, "y").Succeeded.ShouldBeTrue();
        WaitUntil(() => probe.StepCount > before, TimeSpan.FromSeconds(2)).ShouldBeTrue("启用后应恢复推进");
    }

    [Fact]
    public void 禁用中移除_OnRemoved应在实例线程且按时停止()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        Register(services);
        services.AddModule("probe", "test/lifecycle-probe").Succeeded.ShouldBeTrue();

        var probe = Probe(runtime);
        probe.Started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        var stepThread = probe.StepEvent(StepReason.Start).ThreadId;

        services.SetModuleEnabled("probe", false).Succeeded.ShouldBeTrue();
        probe.Disabled.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();

        services.RemoveModule("probe").Succeeded.ShouldBeTrue();
        probe.Removed.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("禁用状态下移除也应执行 OnRemoved");
        probe.Event("Removed").ThreadId.ShouldBe(stepThread);
        runtime.Content.TryGetInstance("probe", out _).ShouldBeFalse();
    }

    [Fact]
    public void 配置变更通知_应在实例线程()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        Register(services);
        services.AddModule("probe", "test/lifecycle-probe").Succeeded.ShouldBeTrue();

        var probe = Probe(runtime);
        probe.Started.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        var stepThread = probe.StepEvent(StepReason.Start).ThreadId;

        runtime.Apply(ChangeSet.OfOne(new UpdateConfigurationOperation(
            "probe",
            new LifecycleProbeConfig { Value = 1 }))).Applied.ShouldBeTrue();

        probe.ConfigurationChanged.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("OnConfigurationChanged 应执行");
        probe.Event("ConfigurationChanged").ThreadId.ShouldBe(stepThread);
    }

    private static void Register(RuntimeServices services)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("test", "lifecycle-probe"),
            nameof(LifecycleProbeModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            typeof(LifecycleProbeModule),
            string.Empty,
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
    }

    private static LifecycleProbeModule Probe(ModuleRuntime runtime)
    {
        runtime.Content.TryGetInstance("probe", out var instance).ShouldBeTrue();
        return (LifecycleProbeModule)instance!.Module;
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>探针：记录每个生命周期钩子的线程与顺序。</summary>
    [ModuleDefinition("test", "lifecycle-probe")]
    public sealed class LifecycleProbeModule : IModule, IActivitySource
    {
        public const string ProbeInput = "probe-input";
        public const string ProbeCommand = "probe-command";

        private readonly Lock _gate = new();
        private readonly List<HookEvent> _events = [];
        private long _sequence;
        private int _stepCount;

        public ManualResetEventSlim Activated { get; } = new(false);
        public ManualResetEventSlim Started { get; } = new(false);
        public ManualResetEventSlim Disabled { get; } = new(false);
        public ManualResetEventSlim Enabled { get; } = new(false);
        public ManualResetEventSlim ConfigurationChanged { get; } = new(false);
        public ManualResetEventSlim Removed { get; } = new(false);

        public int StepCount => Volatile.Read(ref _stepCount);

        public HookEvent Event(string name)
        {
            lock (_gate)
            {
                return _events.Last(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            }
        }

        public HookEvent StepEvent(StepReason reason)
        {
            lock (_gate)
            {
                return _events.Last(item => item.Name.StartsWith("Step:", StringComparison.Ordinal)
                    && reason.ToString().Length > 0
                    && item.Name.Contains(reason.ToString(), StringComparison.Ordinal));
            }
        }

        public void OnRegistered(IModuleContext context)
        {
            Record("Registered");
            context.Declaration.DeclareInput(new InputDeclaration(ProbeInput));
            context.Declaration.DeclareCommand(ProbeCommand, "probe");
        }

        public void OnActivated()
        {
            Record("Activated");
            Activated.Set();
        }

        public void OnDisabled()
        {
            Record("Disabled");
            Disabled.Set();
        }

        public void OnEnabled()
        {
            Record("Enabled");
            Enabled.Set();
        }

        public void OnConfigurationChanged()
        {
            Record("ConfigurationChanged");
            ConfigurationChanged.Set();
        }

        public void OnRemoved()
        {
            Record("Removed");
            Removed.Set();
        }

        public ReceiveMode Receive => ReceiveMode.Push;

        public void OnStep(IModuleContext context, StepReason reason)
        {
            while (context.Inbox.TryDequeueCommand(out _))
            {
            }

            while (context.Inbox.TryDequeueInput(out _, out _))
            {
            }

            Record($"Step:{reason}");
            _ = Interlocked.Increment(ref _stepCount);
            Started.Set();
        }

        private void Record(string name)
        {
            lock (_gate)
            {
                _events.Add(new HookEvent(name, Environment.CurrentManagedThreadId, _sequence++));
            }
        }
    }

    public sealed class LifecycleProbeConfig
    {
        public int Value { get; set; }
    }

    public readonly record struct HookEvent(string Name, int ThreadId, long Sequence);
}
