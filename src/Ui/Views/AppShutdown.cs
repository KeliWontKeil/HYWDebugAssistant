namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **应用收尾的一次性闸门**：真正的退出收尾只允许执行一遍。
/// </summary>
/// <remarks>
/// <para>
/// 退出的"请求"与"决定"是两件事：前者可能被取消，后者才是终局。收尾动作（释放窗口管理与服务容器）
/// 一旦发生就不可逆，因此调用方可能从多个路径到达时，用一次原子交换把执行次数收敛为一次。
/// </para>
/// <para>
/// 闸门先翻转、再执行动作：即使动作抛异常，后续调用也不会重试（重试只会产生第二次副作用）。
/// </para>
/// </remarks>
public sealed class AppShutdown
{
    private readonly Action _action;
    private int _done;

    /// <summary>创建收尾闸门。</summary>
    /// <param name="action">真正退出时执行一次的收尾动作。</param>
    public AppShutdown(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _action = action;
    }

    /// <summary>收尾动作是否已被取走执行（无论它是否执行成功）。</summary>
    public bool IsDone => Volatile.Read(ref _done) != 0;

    /// <summary>执行收尾；重复调用无效果。</summary>
    public void Run()
    {
        if (Interlocked.Exchange(ref _done, 1) == 0)
        {
            _action();
        }
    }
}
