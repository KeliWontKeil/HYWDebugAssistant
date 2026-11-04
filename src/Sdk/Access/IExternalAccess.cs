using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **模块侧的接入存取面**：双向数据 + 打开/关闭请求 + 状态/计量快照。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>双向</b>：<see cref="TryRead"/> / <see cref="SetReceiveHandler"/> 收，<see cref="Write"/> 发；</item>
///   <item><b>模块只发请求</b>：<see cref="RequestOpen"/> / <see cref="RequestClose"/> 不直接操作连接，
///     由框架在实例循环里执行并持有会话；</item>
///   <item><b>两种消费方式由模块的 <c>ReceiveMode</c> 唯一决定</b>：Push 设回调、由实例循环投递；
///     Pull 在 <c>OnStep(Session)</c> 里 <see cref="TryRead"/>。<b>不能同时有两个消费者</b>；</item>
///   <item><b>状态不做事</b>事件面：<see cref="GetSnapshot"/> 是唯一事实源。</item>
/// </list>
/// </remarks>
public interface IExternalAccess
{
    /// <summary>请求打开一条接入（模块只发请求；实际执行在框架的实例循环里）。</summary>
    /// <param name="accessName">声明过的接入名。</param>
    /// <returns>名字是否已声明（不表示已经打开）。</returns>
    bool RequestOpen(string accessName);

    /// <summary>请求关闭一条接入。</summary>
    /// <param name="accessName">声明过的接入名。</param>
    /// <returns>名字是否已声明。</returns>
    bool RequestClose(string accessName);

    /// <summary>
    /// 主动取一个数据单元（非阻塞；无数据时返回 <see langword="false"/>）。
    /// </summary>
    /// <remarks>适用于 Pull 模式；**设了接收回调的会话不应再调本方法**（两个消费者）。</remarks>
    /// <param name="accessName">接入名。</param>
    /// <param name="destination">目标缓冲；小于单元长度 ⇒ 丢弃并计数。</param>
    /// <param name="length">实际字节数。</param>
    bool TryRead(string accessName, Span<byte> destination, out int length);

    /// <summary>
    /// 设接收回调（Push 模式）：框架在实例线程上把数据单元交给它；与 <see cref="TryRead"/> 互斥。
    /// </summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="handler">处理委托（生命周期仅限本次调用；需要留存时自行拷贝）。</param>
    void SetReceiveHandler(string accessName, ReceiveHandler handler);

    /// <summary>发送一个数据单元（有界等待；返回 <see langword="false"/> 表示未发出并已计入丢弃）。</summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="payload">有效字节（不含应用组帧）。</param>
    bool Write(string accessName, ReadOnlySpan<byte> payload);

    /// <summary>取当前快照（未知接入名 ⇒ <see cref="ExternalAccessSnapshot.None"/>）。</summary>
    /// <param name="accessName">接入名。</param>
    ExternalAccessSnapshot GetSnapshot(string accessName);
    /// <summary>
    /// 清空该接入的计数（收 / 发 / 丢弃 / 被拒）。
    /// </summary>
    /// <remarks>
    /// 只清计数：**不清队列数据、不清活动连接数、不清故障态**——清计数是排障动作，不是复位会话。
    /// </remarks>
    /// <param name="accessName">接入名。</param>
    /// <returns>接入名是否已声明（未知接入 ⇒ <see langword="false"/>，什么也不做）。</returns>
    bool ClearCounters(string accessName);

    /// <summary>
    /// 该接入的参数类型（框架按标准标识登记；未知接入 / 尚未接线 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <remarks>
    /// 模块骨架用它反射配置字段名，保留"会话打开/故障时才允许编辑配置"的既有行为；
    /// 参数类型本身留在框架侧，模块不引用它。默认实现返回 <see langword="null"/>（未接线面）。
    /// </remarks>
    /// <param name="accessName">接入名。</param>
    Type? GetParametersType(string accessName) => null;
}
