using HYWDebugAssistant.FrameworkUi.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **退出协调器**的行为：询问 / 保存 / 记住策略 / 关闭顺序 / 重入与幂等。
/// </summary>
public sealed class ShellExitCoordinatorTests
{
    [Fact]
    public async Task Save策略_只保存一次并关闭窗口_不询问()
    {
        var prompt = new FakePrompt();
        var saver = new FakeSaver();
        var policy = new FakePolicy { Current = "Save" };
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        prompt.Calls.ShouldBe(0, "记住的 Save 策略不再询问");
        saver.Calls.ShouldBe(1);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
        coordinator.CanClose.ShouldBeTrue();
        coordinator.State.ShouldBe(ShellExitState.Closing);
    }

    [Fact]
    public async Task Discard策略_不保存并直接关闭()
    {
        var prompt = new FakePrompt();
        var saver = new FakeSaver();
        var policy = new FakePolicy { Current = "Discard" };
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        prompt.Calls.ShouldBe(0);
        saver.Calls.ShouldBe(0);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
        coordinator.CanClose.ShouldBeTrue();
    }

    [Fact]
    public async Task 询问后取消_不保存不关闭且回到空闲()
    {
        var prompt = new FakePrompt { Result = new WorkspaceCloseResult(WorkspaceCloseChoice.Cancel, false) };
        var saver = new FakeSaver();
        var policy = new FakePolicy();
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        prompt.Calls.ShouldBe(1);
        saver.Calls.ShouldBe(0);
        closedModules.ShouldBe(0);
        closedShell.ShouldBe(0);
        coordinator.CanClose.ShouldBeFalse();
        coordinator.State.ShouldBe(ShellExitState.Idle);
    }

    [Fact]
    public async Task 询问后选择保存_保存一次并关闭()
    {
        var prompt = new FakePrompt { Result = new WorkspaceCloseResult(WorkspaceCloseChoice.Save, false) };
        var saver = new FakeSaver();
        var policy = new FakePolicy();
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        prompt.Calls.ShouldBe(1);
        saver.Calls.ShouldBe(1);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
    }

    [Fact]
    public async Task 保存失败_不关闭且回到空闲()
    {
        var prompt = new FakePrompt { Result = new WorkspaceCloseResult(WorkspaceCloseChoice.Save, false) };
        var saver = new FakeSaver { Result = false };
        var policy = new FakePolicy();
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        saver.Calls.ShouldBe(1);
        closedModules.ShouldBe(0);
        closedShell.ShouldBe(0);
        coordinator.State.ShouldBe(ShellExitState.Idle);
    }

    [Fact]
    public async Task 勾选记住_写一次策略且失败不阻止退出()
    {
        var prompt = new FakePrompt { Result = new WorkspaceCloseResult(WorkspaceCloseChoice.Discard, Remember: true) };
        var saver = new FakeSaver();
        var policy = new FakePolicy { RememberResult = false };
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync();

        policy.Remembered.ShouldBe(["Discard"]);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
    }

    [Fact]
    public async Task 重入_并发请求只询问一次()
    {
        var gate = new TaskCompletionSource<WorkspaceCloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new FakePrompt { Gate = gate };
        var saver = new FakeSaver();
        var policy = new FakePolicy();
        var coordinator = New(prompt, saver, policy, () => { }, () => { });

        var first = coordinator.RequestAsync();
        var second = coordinator.RequestAsync();

        await second;
        prompt.Calls.ShouldBe(1, "询问进行中时新的退出请求必须直接返回");

        gate.SetResult(new WorkspaceCloseResult(WorkspaceCloseChoice.Cancel, false));
        await first;

        coordinator.State.ShouldBe(ShellExitState.Idle);
    }

    [Fact]
    public void OnAppExit_重复调用只收尾一次()
    {
        var teardown = 0;
        var coordinator = New(new FakePrompt(), new FakeSaver(), new FakePolicy(), () => { }, () => { }, () => teardown++);

        coordinator.OnAppExit();
        coordinator.OnAppExit();

        teardown.ShouldBe(1);
        coordinator.State.ShouldBe(ShellExitState.Exited);
    }

    [Fact]
    public async Task 系统关机_不弹窗且静默保存后关闭()
    {
        var prompt = new FakePrompt();
        var saver = new FakeSaver();
        var policy = new FakePolicy();
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync(isOsShutdown: true);

        prompt.Calls.ShouldBe(0);
        saver.Calls.ShouldBe(1);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
    }

    [Fact]
    public async Task 系统关机且策略为丢弃_不保存也继续关闭()
    {
        var prompt = new FakePrompt();
        var saver = new FakeSaver();
        var policy = new FakePolicy { Current = "Discard" };
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.RequestAsync(isOsShutdown: true);

        prompt.Calls.ShouldBe(0);
        saver.Calls.ShouldBe(0);
        closedModules.ShouldBe(1);
        closedShell.ShouldBe(1);
    }

    [Fact]
    public async Task 系统关机_保存失败也继续关闭()
    {
        var saver = new FakeSaver { Result = false };
        var closedShell = 0;
        var coordinator = New(new FakePrompt(), saver, new FakePolicy(), () => { }, () => closedShell++);

        await coordinator.RequestAsync(isOsShutdown: true);

        saver.Calls.ShouldBe(1);
        closedShell.ShouldBe(1);
    }

    [Fact]
    public async Task 直接关闭_不询问不保存且重入只执行一次()
    {
        var prompt = new FakePrompt();
        var saver = new FakeSaver();
        var policy = new FakePolicy();
        var closedModules = 0;
        var closedShell = 0;

        var coordinator = New(prompt, saver, policy, () => closedModules++, () => closedShell++);

        await coordinator.CloseWithoutSavingAsync();
        await coordinator.CloseWithoutSavingAsync();

        prompt.Calls.ShouldBe(0);
        saver.Calls.ShouldBe(0);
        closedModules.ShouldBe(1, "重入必须直接返回");
        closedShell.ShouldBe(1);
        coordinator.CanClose.ShouldBeTrue();
        coordinator.State.ShouldBe(ShellExitState.Closing);
    }

    [Fact]
    public async Task 询问抛异常_回到空闲且不关闭()
    {
        var prompt = new FakePrompt { Throw = true };
        var closedShell = 0;
        var coordinator = New(prompt, new FakeSaver(), new FakePolicy(), () => { }, () => closedShell++);

        await coordinator.RequestAsync();

        closedShell.ShouldBe(0);
        coordinator.State.ShouldBe(ShellExitState.Idle);
    }

    private static ShellExitCoordinator New(
        IExitPrompt prompt,
        IExitSaver saver,
        IExitPolicy policy,
        Action closeModuleWindows,
        Action closeShell,
        Action? teardown = null) =>
        new(prompt, saver, policy, closeModuleWindows, closeShell, teardown ?? (() => { }), () => 3);

    private sealed class FakePrompt : IExitPrompt
    {
        public int Calls { get; private set; }

        public int LastModuleCount { get; private set; }

        public WorkspaceCloseResult Result { get; init; } = new(WorkspaceCloseChoice.Cancel, false);

        public bool Throw { get; init; }

        public TaskCompletionSource<WorkspaceCloseResult>? Gate { get; init; }

        public async Task<WorkspaceCloseResult> AskAsync(int moduleCount)
        {
            Calls++;
            LastModuleCount = moduleCount;

            if (Throw)
            {
                throw new InvalidOperationException("假询问：失败");
            }

            return Gate is null ? Result : await Gate.Task;
        }
    }

    private sealed class FakeSaver : IExitSaver
    {
        public int Calls { get; private set; }

        public bool Result { get; init; } = true;

        public Task<bool> SaveAsync()
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePolicy : IExitPolicy
    {
        public string Current { get; init; } = "Ask";

        public bool RememberResult { get; init; } = true;

        public List<string> Remembered { get; } = [];

        public Task<bool> RememberAsync(string policy)
        {
            Remembered.Add(policy);
            return Task.FromResult(RememberResult);
        }
    }
}
