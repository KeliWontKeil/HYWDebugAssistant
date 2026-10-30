namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 外部接入失败的**机器可读种类**：框架只报种类与事实，句子由模块自己的表生成。
/// </summary>
/// <remarks>
/// 种类是模块显示文案的判据，但**按模块裁剪**：模块只为自己真的会产生的种类给句子，其余一律走
/// 该模块自己的未知回退。新增种类必须同时确认生产者，并按需补上对应模块的表项。
/// </remarks>
public enum ExternalAccessFailureKind
{
    /// <summary>未知（没有更具体的分类）。</summary>
    Unknown = 0,

    /// <summary>框架不变量：参数类型不匹配、参数未登记、会话内部错误。</summary>
    FrameworkInternal,

    /// <summary>参数类型没有登记进实例配置快照。</summary>
    ParametersNotRegistered,

    /// <summary>地址为空。</summary>
    AddressEmpty,

    /// <summary>地址不合法。</summary>
    AddressInvalid,

    /// <summary>不允许使用通配地址。</summary>
    WildcardAddressNotAllowed,

    /// <summary>端口超出有效范围。</summary>
    PortOutOfRange,

    /// <summary>端口已被占用。</summary>
    PortInUse,

    /// <summary>连接超时。</summary>
    ConnectTimeout,

    /// <summary>对端拒绝连接。</summary>
    ConnectionRefused,

    /// <summary>对端已关闭连接。</summary>
    PeerClosed,

    /// <summary>外部设备已断开 / 不在系统中（串口等无连接语义的接入）。</summary>
    DeviceLost,

    /// <summary>打开失败。</summary>
    OpenFailed,

    /// <summary>监听失败。</summary>
    ListenFailed,

    /// <summary>绑定失败。</summary>
    BindFailed,

    /// <summary>接受连接失败。</summary>
    AcceptFailed,

    /// <summary>读取失败。</summary>
    ReadFailed,

    /// <summary>写入失败。</summary>
    WriteFailed,

    /// <summary>没有选择端口（串口）。</summary>
    PortNotSelected,
}
