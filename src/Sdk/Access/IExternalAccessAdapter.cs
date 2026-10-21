namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **适配件**：懂某个外部标准的实现件（开 / 关 / 读 / 写 / 就绪）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它不认识应用契约</b>：只交原始字节块；组帧、拆帧、类型语义在模块侧；</item>
///   <item><b>它不持有队列、不做投递</b>：每会话队列、回调、状态机、Issue 归框架；</item>
///   <item><b>协议词与第三方依赖只在这里</b>：框架层不认识串口 / TCP / UDP；</item>
///   <item><b>不自动重试</b>：失败就报告 <see cref="IExternalAccessReady.SetFaulted"/>，等用户再次请求；</item>
///   <item><b>线程</b>：<see cref="Open"/> 可以启动自己的异步泵，但必须响应 <see cref="Close"/>；
///     <see cref="TryRead"/> / <see cref="Write"/> 由框架在实例线程上调用，必须非阻塞或有界。</item>
/// </list>
/// </remarks>
public interface IExternalAccessAdapter
{
    /// <summary>参数类型（框架把它登记进实例配置快照 ⇒ 配置表单自动出现、随组合件保存）。</summary>
    Type ParametersType { get; }

    /// <summary>
    /// 打开会话：按参数启动外部标准；就绪时调用 <see cref="IExternalAccessReady.SetOpen"/>，
    /// 有数据时调用 <see cref="IExternalAccessReady.NotifyReady"/>。
    /// </summary>
    /// <param name="parameters">
    /// 该参数类型的**当前配置对象**（框架每次打开都取最新值；适配件应拷贝所需值，不长期持有可变对象）。
    /// </param>
    /// <param name="ready">回报面（就绪 / 状态 / 连接数 / 丢弃）。</param>
    void Open(object parameters, IExternalAccessReady ready);

    /// <summary>取一个已就绪的数据单元（非阻塞；无数据时返回 <see langword="false"/>）。</summary>
    /// <param name="destination">目标缓冲（长度至少为声明允许的最大单元；不足 ⇒ 返回 <see langword="false"/>）。</param>
    /// <param name="length">实际字节数。</param>
    bool TryRead(Span<byte> destination, out int length);

    /// <summary>发送一个数据单元（有界等待；返回 <see langword="false"/> 表示未发出）。</summary>
    /// <param name="payload">有效字节（不含任何应用组帧）。</param>
    bool Write(ReadOnlySpan<byte> payload);

    /// <summary>关闭会话：取消泵、释放句柄；实现必须在自己给出的期限内返回。</summary>
    /// <param name="timeout">关闭的有界等待时限。</param>
    void Close(TimeSpan timeout);
}
