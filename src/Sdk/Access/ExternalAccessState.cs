namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 一条外部接入会话的**框架状态**（框架维护，模块与适配件只读）。
/// </summary>
/// <remarks>
/// 只有三件事会改变它：用户的打开/关闭请求、实例状态（Active / Disabled / Removed）、
/// 适配件的状态回报。**框架不自动重试**：失败就是 <see cref="Faulted"/>，用户再次请求才会回到
/// <see cref="Closed"/> 并重新打开。
/// </remarks>
public enum ExternalAccessState
{
    /// <summary>未打开（初始态、正常关闭后的稳定态）。</summary>
    Closed = 0,

    /// <summary>正在打开（适配件已接受请求，尚未报告就绪）。</summary>
    Opening = 1,

    /// <summary>已打开（适配件报告就绪，可读可写）。</summary>
    Open = 2,

    /// <summary>正在关闭（框架已发出关闭，等待适配件退出）。</summary>
    Closing = 3,

    /// <summary>故障（打开失败、连接断开、读写失败；需用户再次请求）。</summary>
    Faulted = 4,
}
