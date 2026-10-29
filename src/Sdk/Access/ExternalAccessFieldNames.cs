namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **外部标准配置字段的名字**：与框架侧参数类型的属性名逐字一致。
/// </summary>
/// <remarks>
/// <para>
/// 模块视图只通过 <c>{x:Static}</c> 引用这些常量（不写字符串字面量），但**不引用参数类型本身**——
/// 参数类型是框架侧的实现，模块包里没有它。
/// </para>
/// <para>
/// 改名时先断模块编译，而不是等运行期界面空白；框架侧参数类型的改名必须同步这里。
/// </para>
/// </remarks>
public static class ExternalAccessFieldNames
{
    /// <summary>串口：端口名。</summary>
    public const string PortName = nameof(PortName);

    /// <summary>串口：波特率。</summary>
    public const string BaudRate = nameof(BaudRate);

    /// <summary>串口：数据位。</summary>
    public const string DataBits = nameof(DataBits);

    /// <summary>串口：停止位。</summary>
    public const string StopBits = nameof(StopBits);

    /// <summary>串口：校验位。</summary>
    public const string Parity = nameof(Parity);

    /// <summary>TCP 客户端：远端主机。</summary>
    public const string RemoteHost = nameof(RemoteHost);

    /// <summary>TCP 客户端 / UDP：远端端口。</summary>
    public const string RemotePort = nameof(RemotePort);

    /// <summary>TCP 客户端：连接超时（秒）。</summary>
    public const string ConnectTimeout = nameof(ConnectTimeout);

    /// <summary>TCP：禁用 Nagle。</summary>
    public const string NoDelay = nameof(NoDelay);

    /// <summary>TCP 服务端 / UDP：本地地址。</summary>
    public const string LocalAddress = nameof(LocalAddress);

    /// <summary>TCP 服务端 / UDP：本地端口。</summary>
    public const string LocalPort = nameof(LocalPort);

    /// <summary>TCP 服务端：最大连接数。</summary>
    public const string MaxConnections = nameof(MaxConnections);

    /// <summary>UDP：默认收件人地址。</summary>
    public const string RemoteAddress = nameof(RemoteAddress);

    /// <summary>UDP：广播。</summary>
    public const string Broadcast = nameof(Broadcast);
}
