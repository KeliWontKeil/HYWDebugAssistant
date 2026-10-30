namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **可选能力**：外部接入的适配件愿意接受"写操作最多等多久"的设置。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是独立接口</b>：不是每条接入都有可设的写超时（有的适配件自己管超时、有的底层没有这个概念）。
/// 把它塞进 <see cref="IExternalAccessAdapter"/> 会让每个适配件都被迫实现一个空方法。
/// </para>
/// <para>
/// <b>谁调用</b>：框架在会话打开与参数变更时下发一次（值来自承载参数，属应用级设置）；
/// 适配件自己决定落到哪个底层开关上（传输的写超时、发送超时或等价设施）。
/// </para>
/// <para>
/// <b>纪律</b>：实现**不抛、不阻塞**——它在会话的状态推进路径上被调用，抛异常会打断打开/重开。
/// </para>
/// </remarks>
public interface IExternalAccessWriteTimeout
{
    /// <summary>设置写操作的超时（适配件尽力落实；无法落实时静默保持原行为）。</summary>
    /// <param name="timeout">写超时（大于 0；下发方保证不传无限等待）。</param>
    void SetWriteTimeout(TimeSpan timeout);
}
