using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Access;

/// <summary>
/// **框架内置的外部标准目录**：四个标准（串口 / TCP 客户端 / TCP 服务端 / UDP）的 descriptor。
/// </summary>
/// <remarks>
/// <para>
/// 只有组合根（<c>App</c>）构造并注入本目录；模块只声明标准标识，不接触适配器与参数类型。
/// </para>
/// <para>
/// <see cref="ExternalAccessDescriptor.CreateAdapter"/> **每次调用都新建实例**：
/// 同一标准的多个接入/多实例会话各自独立，互不共享状态。
/// </para>
/// </remarks>
public sealed class BuiltInExternalAccessCatalog : IExternalAccessCatalog
{
    private readonly ExternalAccessDescriptor[] _standards;

    /// <summary>创建内置目录。</summary>
    public BuiltInExternalAccessCatalog()
    {
        _standards =
        [
            new ExternalAccessDescriptor(
                ExternalAccessStandardIds.Serial,
                AccessText.Get("access.standard.serial"),
                typeof(SerialConfig),
                static () => new SerialAccessAdapter()),
            new ExternalAccessDescriptor(
                ExternalAccessStandardIds.TcpClient,
                AccessText.Get("access.standard.tcpClient"),
                typeof(TcpClientConfig),
                static () => new TcpClientAccessAdapter()),
            new ExternalAccessDescriptor(
                ExternalAccessStandardIds.TcpServer,
                AccessText.Get("access.standard.tcpServer"),
                typeof(TcpServerConfig),
                static () => new TcpServerAccessAdapter()),
            new ExternalAccessDescriptor(
                ExternalAccessStandardIds.Udp,
                "UDP",
                typeof(UdpConfig),
                static () => new UdpAccessAdapter()),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<ExternalAccessDescriptor> Standards => _standards;

    /// <inheritdoc />
    public bool TryResolve(string standardId, out ExternalAccessDescriptor descriptor)
    {
        descriptor = null!;

        if (string.IsNullOrWhiteSpace(standardId))
        {
            return false;
        }

        foreach (var candidate in _standards)
        {
            if (string.Equals(candidate.StandardId, standardId, StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        return false;
    }
}
