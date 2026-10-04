namespace HYWDebugAssistant.Hosting.Instances;

/// <summary>
/// 模块实例的**框架层状态机**（权威）。
/// </summary>
/// <remarks>
/// <code>Registered → Active ⇄ Disabled → Removed</code>
/// <para>
/// 基线草案中的 <c>Created/Initialized</c> 属"加入"的转换过程、<c>Stopping/Stopped/Disposed</c> 属"移除"的转换过程，
/// 均为过程而非稳态；<c>Faulted/Recovering</c> **不作为框架状态**（软件异常走异常报告 → Issue）。
/// </para>
/// </remarks>
public enum ModuleInstanceState
{
    /// <summary>已加入（实例与成员已创建）——不占线程。</summary>
    Registered = 0,

    /// <summary>参与数据流（加入即运行）。</summary>
    Active = 1,

    /// <summary>保留实例/拓扑/配置、暂停数据流。</summary>
    Disabled = 2,

    /// <summary>已移除（**不可逆**；撤销由 UI 撤销栈负责）。</summary>
    Removed = 3,
}
