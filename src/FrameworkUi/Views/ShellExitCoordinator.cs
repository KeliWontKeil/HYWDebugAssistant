namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>退出询问面：问一次「保存 / 不保存 / 取消」。</summary>
public interface IExitPrompt
{
    /// <summary>问用户是否保存工作区。</summary>
    /// <param name="moduleCount">当前模块数（用于说明）。</param>
    Task<WorkspaceCloseResult> AskAsync(int moduleCount);
}

/// <summary>退出保存面：成功返回 <see langword="true"/>；失败已就地报告。</summary>
public interface IExitSaver
{
    /// <summary>保存工作区。</summary>
    Task<bool> SaveAsync();
}

/// <summary>退出策略面：当前策略与「下次不再提醒」的持久化。</summary>
public interface IExitPolicy
{
    /// <summary>当前策略（<c>Save</c> / <c>Discard</c>；其余 = 每次询问）。</summary>
    string Current { get; }

    /// <summary>持久化本次选择；失败已就地报告。</summary>
    /// <param name="policy">要记住的策略。</param>
    Task<bool> RememberAsync(string policy);
}

/// <summary>退出协调器的状态。</summary>
public enum ShellExitState
{
    /// <summary>空闲（可以接受新的退出请求）。</summary>
    Idle = 0,

    /// <summary>正在询问。</summary>
    Prompting,

    /// <summary>正在保存。</summary>
    Saving,

    /// <summary>正在关闭窗口。</summary>
    Closing,

    /// <summary>应用已退出（收尾已执行）。</summary>
    Exited,
}

/// <summary>
/// **应用退出的唯一出口**：询问 / 保存 / 记住策略 / 关模块窗 / 关外壳 / 收尾，按一次顺序执行。
/// </summary>
/// <remarks>
/// <para>
/// 平台可能在多处发起关闭：标题栏 X、菜单退出、系统关机。它们都必须落到同一个入口，
/// 否则「先关子窗还是先问用户」这类顺序问题会在每条路径上各写一遍。
/// </para>
/// <para>
/// 重入只做一次：非空闲状态的新请求直接返回；询问或保存抛异常时回到空闲且**不关闭**。
/// 「记住」失败只提示、不阻止退出。
/// </para>
/// </remarks>
public sealed class ShellExitCoordinator
{
    private readonly IExitPrompt _prompt;
    private readonly IExitSaver _saver;
    private readonly IExitPolicy _policy;
    private readonly Action _closeModuleWindows;
    private readonly Action _closeShell;
    private readonly Action _teardown;
    private readonly Func<int> _moduleCount;
    private bool _exited;

    /// <summary>创建退出协调器。</summary>
    /// <param name="prompt">询问面。</param>
    /// <param name="saver">保存面。</param>
    /// <param name="policy">策略面。</param>
    /// <param name="closeModuleWindows">关闭全部模块窗口。</param>
    /// <param name="closeShell">关闭外壳窗口（此后平台才会真正退出）。</param>
    /// <param name="teardown">终局收尾（只执行一次）。</param>
    /// <param name="moduleCount">当前模块数（可空 ⇒ 询问时给 0）。</param>
    public ShellExitCoordinator(
        IExitPrompt prompt,
        IExitSaver saver,
        IExitPolicy policy,
        Action closeModuleWindows,
        Action closeShell,
        Action teardown,
        Func<int>? moduleCount = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(saver);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(closeModuleWindows);
        ArgumentNullException.ThrowIfNull(closeShell);
        ArgumentNullException.ThrowIfNull(teardown);

        _prompt = prompt;
        _saver = saver;
        _policy = policy;
        _closeModuleWindows = closeModuleWindows;
        _closeShell = closeShell;
        _teardown = teardown;
        _moduleCount = moduleCount ?? (static () => 0);
    }

    /// <summary>当前状态。</summary>
    public ShellExitState State { get; private set; } = ShellExitState.Idle;

    /// <summary>外壳是否已被允许关闭（关闭询问已经完成）。</summary>
    public bool CanClose { get; private set; }

    /// <summary>请求退出；非空闲时直接返回（重入只问一次）。</summary>
    /// <param name="isOsShutdown">是否由操作系统关机触发（不弹窗、静默保存、失败也继续）。</param>
    public async Task RequestAsync(bool isOsShutdown = false)
    {
        if (State != ShellExitState.Idle)
        {
            return;
        }

        State = ShellExitState.Prompting;

        bool proceed;

        try
        {
            proceed = await DecideAsync(isOsShutdown);
        }
        catch (Exception)
        {
            // 询问/保存的异常不得让窗口停在半路：回到空闲，保持原状让用户重试。
            State = ShellExitState.Idle;
            return;
        }

        if (!proceed)
        {
            return;
        }

        try
        {
            CloseNow();
        }
        catch (Exception)
        {
            State = ShellExitState.Idle;
        }
    }

    /// <summary>不询问、不保存、不记住，直接关闭（供已经自行完成保存的路径使用）。</summary>
    public Task CloseWithoutSavingAsync()
    {
        if (State != ShellExitState.Idle)
        {
            return Task.CompletedTask;
        }

        try
        {
            CloseNow();
        }
        catch (Exception)
        {
            State = ShellExitState.Idle;
        }

        return Task.CompletedTask;
    }

    /// <summary>应用真正的退出事件：收尾只执行一次。</summary>
    public void OnAppExit()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        CanClose = true;
        State = ShellExitState.Exited;
        _teardown();
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>按策略决定是否继续；返回 <see langword="false"/> 时状态已回到空闲。</summary>
    private async Task<bool> DecideAsync(bool isOsShutdown)
    {
        var policy = _policy.Current;

        if (isOsShutdown)
        {
            // 系统关机：不打扰用户；保存失败也只能继续（进程马上就不在了）。
            if (!IsDiscard(policy))
            {
                State = ShellExitState.Saving;
                _ = await _saver.SaveAsync();
            }

            return true;
        }

        if (IsSave(policy))
        {
            return await SaveAsync();
        }

        if (IsDiscard(policy))
        {
            return true;
        }

        var result = await _prompt.AskAsync(_moduleCount());

        if (result.Choice == WorkspaceCloseChoice.Cancel)
        {
            State = ShellExitState.Idle;
            return false;
        }

        if (result.Choice == WorkspaceCloseChoice.Save && !await SaveAsync())
        {
            return false;
        }

        if (result.Remember)
        {
            var remembered = result.Choice == WorkspaceCloseChoice.Save ? "Save" : "Discard";

            try
            {
                _ = await _policy.RememberAsync(remembered);
            }
            catch (Exception)
            {
                // 记住失败只影响下次是否再问，不应阻止退出。
            }
        }

        return true;
    }

    /// <summary>保存一次；失败回到空闲并返回 <see langword="false"/>。</summary>
    private async Task<bool> SaveAsync()
    {
        State = ShellExitState.Saving;

        if (await _saver.SaveAsync())
        {
            return true;
        }

        State = ShellExitState.Idle;
        return false;
    }

    /// <summary>关闭顺序：模块窗先关（视图随窗口摘除），再放行外壳。</summary>
    private void CloseNow()
    {
        State = ShellExitState.Closing;
        _closeModuleWindows();
        CanClose = true;
        _closeShell();
    }

    private static bool IsSave(string policy) => string.Equals(policy, "Save", StringComparison.Ordinal);

    private static bool IsDiscard(string policy) => string.Equals(policy, "Discard", StringComparison.Ordinal);
}
