namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **适配件 → 框架**的回报面：就绪、连接状态、连接数、丢弃。
/// </summary>
/// <remarks>
/// <para>
/// 适配件只报告事实，不决定会话生命周期：打开/关闭由框架执行；状态怎么呈现由模块决定。
/// </para>
/// <para>
/// <b>线程</b>：这些方法可能从适配件的异步泵线程调用，实现必须线程安全且**不阻塞**；
/// 不要在这些方法里回调适配件自身。
/// </para>
/// </remarks>
public interface IExternalAccessReady
{
    /// <summary>报告"有数据可读"（框架据此唤醒实例循环并调用 <see cref="IExternalAccessAdapter.TryRead"/>）。</summary>
    void NotifyReady();

    /// <summary>报告"正在打开/正在连接"（状态变为 <see cref="ExternalAccessState.Opening"/>）。</summary>
    /// <param name="detail">可选说明（如目标地址）。</param>
    void SetConnecting(string? detail = null);

    /// <summary>报告"已就绪"（状态变为 <see cref="ExternalAccessState.Open"/>）。</summary>
    /// <param name="detail">可选说明（如实际绑定的地址 / 端口）。</param>
    void SetOpen(string? detail = null);

    /// <summary>报告失败（状态变为 <see cref="ExternalAccessState.Faulted"/>；<b>框架不自动重试</b>）。</summary>
    /// <param name="message">面向用户的具体原因。</param>
    void SetFaulted(string message);

    /// <summary>报告当前连接数（TCP 服务端等多连接会话用）。</summary>
    /// <param name="count">连接数。</param>
    void SetConnections(int count);

    /// <summary>报告适配件内部丢弃（如暂存队列满、数据报被截断）。</summary>
    /// <param name="units">丢弃的单元数。</param>
    /// <param name="bytes">丢弃的字节数。</param>
    void ReportDropped(long units, long bytes);
}
