using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.DataPlane.Carriers;

/// <summary>
/// 可承载目标：数据面**对模块实例的最小视图**（依赖倒置）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不让数据面直接引用 Hosting 的 <c>ModuleInstance</c>：分层禁令要求
/// <b>L3 只依赖 L1 + L0</b>（AR-3）——数据面不得反向依赖 L2。
/// 因此宿主侧（L2/L4）通过实现本接口把实例交给数据面承载；
/// 由 **L4（Core）** 提供适配器（Core 同时引用 L2 与 L3）。
/// </para>
/// <para>承载者只做三件事：调度投递、承载活动、驱动停止信号。</para>
/// </remarks>
public interface ICarrierTarget
{
    /// <summary>实例 ID（诊断与 Issue 归属用）。</summary>
    string InstanceId { get; }

    /// <summary>该实例的接收口名集合（入队单元按口名路由）。</summary>
    IReadOnlyList<string> ReceivePortNames { get; }

    /// <summary>取某个接收口的处理；未注册该口时返回 <see langword="false"/>。</summary>
    bool TryGetReceiveHandler(string portName, out ReceiveHandler handler);

    /// <summary>是否实现自发活动（<c>IActivitySource</c>）——决定承载模式。</summary>
    bool HasActivity { get; }

    /// <summary>
    /// 无活动体时，是否有"口队列之外"的辅助工作源（外部接入会话 / 命令输入收件箱）。
    /// </summary>
    /// <remarks>有活动体时恒为 <see langword="false"/>：实例线程归活动体，框架不投递辅助工作。</remarks>
    bool HasAuxiliaryWork => false;

    /// <summary>
    /// 推进辅助工作源（应用会话开关、把就绪数据取进队列、把命令/输入交给模块回调）。
    /// </summary>
    /// <returns>这一拍是否做了工作。</returns>
    /// <remarks>仅在没有活动体时由承载者的投递循环调用；实现必须非阻塞或只有有界等待。</remarks>
    bool PumpAuxiliary() => false;

    /// <summary>本拍工作处理完（用于模块侧的 <c>OnWork</c>；只提示去读快照，不携带状态值）。</summary>
    void NotifyWorkCompleted()
    {
    }

    /// <summary>运行模块活动体（由承载者在**该实例的专属线程**上调用；实现内部可阻塞）。</summary>
    void RunActivity();

    /// <summary>停止信号（承载者请求停止时置位；模块活动体应据此尽快退出）。</summary>
    IStopSignal StopSignal { get; }

    /// <summary>
    /// 请求停止：承载者在**停止协议开始时**调用（模块活动体据此退出）。
    /// </summary>
    /// <remarks>与 <see cref="StopSignal"/> 配对：前者置位、后者查询（模块侧只读）。</remarks>
    void RequestStop();

    /// <summary>报告模块活动体的异常（由承载者的异常边界调用，**不抛出**）。</summary>
    void ReportActivityFailure(Exception exception);
}
