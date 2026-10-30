namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **适配件可选能力：探活**——"这个外部设备现在还在吗"。
/// </summary>
/// <remarks>
/// <para>
/// 只给**没有连接语义**的接入用（串口等：设备拔出后端口只是从系统里消失，读泵收不到任何事件）。
/// 有连接语义的接入（TCP 客户端 / 服务端）靠连接本身感知对端，不需要实现本接口；
/// UDP 无连接语义、没有设备移除可探，也不需要实现。
/// </para>
/// <para>
/// 会话在实例线程上按拍调用：实现必须**非阻塞**、**不抛**，判定失联时自行调用
/// <see cref="IExternalAccessReady.SetFaulted"/>。传输的释放由会话在同一线程上做，
/// 实现不得在探活路径上关闭 / 释放传输（否则会与读泵和写路径交叉）。
/// </para>
/// </remarks>
public interface IExternalAccessLiveness
{
    /// <summary>探活（会话打开期间按拍调用；非阻塞；失联时自行 SetFaulted）。</summary>
    void CheckLiveness();
}
