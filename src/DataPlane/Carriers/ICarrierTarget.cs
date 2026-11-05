using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;

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
/// <para>承载者只做三件事：调度投递、承载 step/投递循环、有界停止。</para>
/// </remarks>
public interface ICarrierTarget
{
    /// <summary>实例 ID（诊断与 Issue 归属用）。</summary>
    string InstanceId { get; }

    /// <summary>该实例的接收口名集合（入队单元按口名路由）。</summary>
    IReadOnlyList<string> ReceivePortNames { get; }

    /// <summary>取某个接收口的处理；未注册该口时返回 <see langword="false"/>。</summary>
    bool TryGetReceiveHandler(string portName, out ReceiveHandler handler);

    /// <summary>是否使用 step 模型（框架调用 <see cref="OnStep"/>，而不是模块自建循环）。</summary>
    bool HasStepModel => false;

    /// <summary>端口/外部接入的数据消费方式。</summary>
    ReceiveMode ReceiveMode => ReceiveMode.Push;

    /// <summary>收件箱里是否有待处理命令/输入。</summary>
    bool HasPendingInbox => false;

    /// <summary>
    /// 是否有"口队列之外"的辅助工作源（外部接入会话）。
    /// </summary>
    bool HasAuxiliaryWork => false;

    /// <summary>
    /// 推进辅助工作源（应用会话开关、把就绪数据取进队列）。
    /// </summary>
    /// <returns>这一拍是否做了工作。</returns>
    /// <remarks>由承载者的循环调用；实现必须非阻塞或只有有界等待。</remarks>
    bool PumpAuxiliary() => false;

    /// <summary>
    /// **下一条外部探活需求的到期时间**（离现在多久；<see langword="null"/> = 没有）。
    /// </summary>
    /// <remarks>
    /// 它**不承担数据延迟**：数据到达不依赖它。模块自己的周期由 <c>IModuleTimer</c> 的
    /// posted Tick 驱动，不走本属性。
    /// </remarks>
    TimeSpan? NextWakeupDue => null;

    /// <summary>一步：处理本拍合并后的工作原因（由框架在实例线程调用）。</summary>
    /// <param name="reason">本拍的工作原因（可多位合并）。</param>
    void OnStep(StepReason reason)
    {
    }

    /// <summary>报告承载循环的异常（由承载者的异常边界调用，**不抛出**）。</summary>
    void ReportFailure(Exception exception);
}
