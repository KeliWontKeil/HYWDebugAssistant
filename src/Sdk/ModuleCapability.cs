namespace HYWDebugAssistant.Sdk;

/// <summary>
/// 模块声明的**能力需求**（加载时透明提示，不拦截——主文档 §12「轻量透明模型」）。
/// </summary>
/// <remarks>
/// 该枚举只用于**告知用户**模块会触碰什么资源；v1 不做权限拦截、不做沙箱。
/// </remarks>
[Flags]
public enum ModuleCapability
{
    /// <summary>无特殊能力需求。</summary>
    None = 0,

    /// <summary>网络访问（TCP/UDP 等）。</summary>
    Network = 1 << 0,

    /// <summary>串口访问。</summary>
    SerialPort = 1 << 1,

    /// <summary>文件读写（限制在用户选择的路径内）。</summary>
    File = 1 << 2,

    /// <summary>进程/本地 IPC。</summary>
    Process = 1 << 3,
}
