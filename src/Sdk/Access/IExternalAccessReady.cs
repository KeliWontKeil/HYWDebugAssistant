using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **适配件 → 框架**的回报面：就绪、连接状态、连接数、丢弃。
/// </summary>
/// <remarks>
/// <para>
/// 适配件只报告事实，不决定会话生命周期：打开/关闭由框架执行；状态怎么呈现由模块决定。
/// 因此这里给的是**端点与失败事实**，没有面向用户的句子。
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
    /// <param name="endpoint">可选端点事实（如目标地址 / 计划绑定的本地端点）。</param>
    void SetConnecting(ExternalAccessEndpoint? endpoint = null);

    /// <summary>报告"已就绪"（状态变为 <see cref="ExternalAccessState.Open"/>）。</summary>
    /// <param name="endpoint">端点事实（如实际绑定的地址 / 端口、连接的目标）。</param>
    void SetOpen(ExternalAccessEndpoint? endpoint = null);

    /// <summary>报告失败（状态变为 <see cref="ExternalAccessState.Faulted"/>；<b>框架不自动重试</b>）。</summary>
    /// <param name="failure">结构化失败事实（种类 + 端点 / 原始值 / 底层细节）。</param>
    void SetFaulted(ExternalAccessFailure failure);

    /// <summary>报告当前连接数（TCP 服务端等多连接会话用）。</summary>
    /// <param name="count">连接数。</param>
    void SetConnections(int count);

    /// <summary>报告适配件内部丢弃（如暂存队列满、数据报被截断）。</summary>
    /// <param name="units">丢弃的单元数。</param>
    /// <param name="bytes">丢弃的字节数。</param>
    void ReportDropped(long units, long bytes);

    /// <summary>
    /// 报告一次**带原因**的丢弃（默认实现丢弃原因、等价于 <see cref="ReportDropped(long, long)"/>）。
    /// </summary>
    /// <param name="reason">丢弃原因（会话轴词表；典型是 <see cref="DropReason.AdapterOverflow"/>）。</param>
    /// <param name="units">丢弃的单元数。</param>
    /// <param name="bytes">丢弃的字节数。</param>
    /// <remarks>
    /// <b>默认实现是为了不打断既有适配件</b>：它们只报数量，原因由框架按调用点归类即是事实；
    /// 能分辨原因的适配件覆盖它，就能让诊断区分"适配件自己溢出"与"会话队列满"。
    /// <para>单位与字节口径与两参数重载**完全一致**，不因多了原因而改变。</para>
    /// </remarks>
    void ReportDropped(DropReason reason, long units, long bytes) => ReportDropped(units, bytes);
}
