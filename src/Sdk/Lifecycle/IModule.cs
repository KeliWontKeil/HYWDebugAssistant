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
/// <b>线程</b>：钩子由框架在**该实例的专属线程**上调用；模块不得自建线程。
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

    /// <summary>移除前调用：释放模块**内部**资源（句柄、文件、缓冲等）。</summary>
    void OnRemoved()
    {
    }
}

/// <summary>自发活动的形式（可选接口 + **框架承载**；只用于非 IO 活动）。</summary>
public enum ActivityKind
{
    /// <summary>周期活动（框架按 <see cref="IActivitySource.Interval"/> 触发）。</summary>
    Periodic = 0,

    /// <summary>事件驱动（由模块在收到投递时自行推进，框架只给线程）。</summary>
    EventDriven = 1,
}

/// <summary>
/// 可选能力：模块需要**非 IO 的周期活动**时实现该接口（如周期采样、定时发送、批处理）。
/// </summary>
/// <remarks>
/// <para>
/// 框架为该实例分配专属线程并调用 <see cref="RunActivity"/>；实现内部可以阻塞、循环，
/// 但必须响应 <see cref="IModuleContext.StopSignal"/>（收到停止信号后尽快返回）。
/// </para>
/// <para>
/// <b>外部 IO 不在这里</b>：串口 / TCP / UDP 等外部标准由外部接入的适配件实现，框架驱动会话；
/// 模块不因外部 IO 建活动体。有活动体的模块在外部接入上走**读取面**，没有活动体的模块由框架回调投递。
/// </para>
/// <para>模块**不得自建线程或定时器**（框架承担线程分配，模块只在该线程上做自发活动）。</para>
/// </remarks>
public interface IActivitySource
{
    /// <summary>活动形式。</summary>
    ActivityKind Kind { get; }

    /// <summary>周期活动的间隔（<see cref="ActivityKind.Periodic"/> 时有效）。</summary>
    TimeSpan Interval { get; }

    /// <summary>活动体：由框架在该实例的专属线程上调用。</summary>
    void RunActivity(IModuleContext context);
}
