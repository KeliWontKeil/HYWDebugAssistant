using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Lifecycle;

/// <summary>
/// 模块实现基元：**接口实现**（不继承框架基类）。
/// </summary>
/// <remarks>
/// <para>六个生命周期钩子对应框架状态机 <c>Registered → Active ⇄ Disabled → Removed</c>（主文档 §8.4）：</para>
/// <list type="table">
///   <item><term>OnRegistered</term><description>实例创建、成员实例化完成后——在此**声明口与视图**；不得假定数据已在流动</description></item>
///   <item><term>OnActivated</term><description>进入 Active（加入即运行）——开始业务活动</description></item>
///   <item><term>OnDisabled</term><description>进入 Disabled（保留实例/拓扑/配置、暂停数据流）——内部处置**自决**</description></item>
///   <item><term>OnEnabled</term><description>Disabled → Active——恢复业务活动</description></item>
///   <item><term>OnConfigurationChanged</term><description>配置更新通知——热生效或请求重建由模块自决</description></item>
///   <item><term>OnRemoved</term><description>移除前——**释放模块内部资源**（框架已先清连接/绑定与成员资源）</description></item>
/// </list>
/// <para>
/// <b>异常纪律</b>：钩子中的异常不得逸出边界——框架受控捕获并转 Issue（AC-2）。
/// <b>线程</b>：<c>OnRegistered</c> 在实例线程创建前由创建方（管理线程）调用；其余五个钩子由框架在
/// **该实例的专属线程**上调用，与 <c>OnStep</c>、接收回调同线程。模块不得自建线程。
/// </para>
/// </remarks>
public interface IModule
{
    /// <summary>实例创建后调用一次：在此声明口与视图（<see cref="IModuleContext.Declaration"/>）。</summary>
    void OnRegistered(IModuleContext context)
    {
    }

    /// <summary>进入 <c>Active</c>：开始业务活动（若需要）。</summary>
    void OnActivated()
    {
    }

    /// <summary>进入 <c>Disabled</c>：保留实例与配置、暂停数据流；内部处置自决。</summary>
    void OnDisabled()
    {
    }

    /// <summary>由 <c>Disabled</c> 恢复为 <c>Active</c>。</summary>
    void OnEnabled()
    {
    }

    /// <summary>配置发生变更（框架通知；热生效或请求重建由模块自决）。</summary>
    void OnConfigurationChanged()
    {
    }

    /// <summary>移除前、实例线程停止前调用：释放模块**内部**资源（句柄、文件、缓冲等）。</summary>
    void OnRemoved()
    {
    }
}

/// <summary>一步被唤醒的原因（可合并：一拍里既收到命令又有数据只调一次 <c>OnStep</c>）。</summary>
[Flags]
public enum StepReason
{
    /// <summary>实例刚激活：做一次上下文相关初始化。</summary>
    Start = 1,

    /// <summary>端口有数据可处理（<see cref="ReceiveMode.Pull"/> 时由模块自取）。</summary>
    Data = 2,

    /// <summary>外部会话有变化（就绪数据 / 开关 / 故障 / 探活）。</summary>
    Session = 4,

    /// <summary>收件箱里有命令。</summary>
    Command = 8,

    /// <summary>收件箱里有输入值。</summary>
    Input = 16,
}

/// <summary>端口/外部接入的数据消费方式（每个模块统一一种，避免两个消费者）。</summary>
public enum ReceiveMode
{
    /// <summary>框架在实例线程把数据交给接收回调（默认）。</summary>
    Push = 0,

    /// <summary>数据留在队列里，模块在 <c>OnStep</c> 中自取。</summary>
    Pull = 1,
}

/// <summary>
/// 可选能力：模块需要自发活动（周期 / 事件）时实现该接口。
/// </summary>
/// <remarks>
/// <para>
/// 框架为该实例分配专属线程，并在**工作出现时**调用一次 <see cref="OnStep"/>；
/// 模块只描述"我这一步要做什么"，**不拥有循环**。
/// </para>
/// <para>
/// <b>外部 IO 不在这里</b>：串口 / TCP / UDP 等外部标准由外部接入的适配件实现，框架驱动会话；
/// 模块不因外部 IO 建循环。step 模块在外部接入上走**读取面**，没有 step 的模块由框架回调投递。
/// </para>
/// <para>
/// 模块**不得自建线程或系统定时器**；需要周期时使用
/// <see cref="HYWDebugAssistant.Sdk.Context.IModuleContext.Timers"/> 创建的 <c>IModuleTimer</c>。
/// </para>
/// </remarks>
public interface IActivitySource
{
    /// <summary>端口/外部接入的数据消费方式（默认由框架回调投递）。</summary>
    ReceiveMode Receive => ReceiveMode.Push;

    /// <summary>一步：处理本拍合并后的工作原因（由框架在实例线程调用）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="reason">本拍的工作原因（可多位合并）。</param>
    void OnStep(IModuleContext context, StepReason reason);
}
