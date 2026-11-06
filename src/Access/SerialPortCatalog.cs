using System.IO.Ports;

namespace HYWDebugAssistant.Access;

/// <summary>
/// **串口目录的进程级缓存**：端口枚举只在这里做，供配置下拉、探活共用。
/// </summary>
/// <remarks>
/// <para>
/// 枚举有系统调用开销，且热插拔瞬间可能抛异常；调用方（界面线程 / 实例线程）不应各自重复枚举。
/// 本类把枚举节流到 <see cref="RefreshInterval"/>，端口集合变化时递增 <see cref="Revision"/>，
/// 让配置 Schema 缓存知道该重建。
/// </para>
/// <para>
/// 缓存只改变"多久看一次系统"，不改变"哪些端口真实存在"：枚举结果按名称排序后整体替换。
/// </para>
/// </remarks>
internal static class SerialPortCatalog
{
    private static readonly Lock Gate = new();

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private static string[] _ports = [];
    private static long _revision;
    private static long _lastRefreshTicks = long.MinValue;

    /// <summary>当前端口快照（只读；最多 500 ms 过期）。</summary>
    public static IReadOnlyList<string> Ports
    {
        get
        {
            RefreshIfDue();
            return _ports;
        }
    }

    /// <summary>端口集合版本；集合内容变化时递增。</summary>
    public static long Revision
    {
        get
        {
            RefreshIfDue();
            return _revision;
        }
    }

    /// <summary>某个端口当前是否仍在系统中（不区分大小写）。</summary>
    /// <param name="portName">端口名（如 <c>COM3</c>）。</param>
    public static bool Contains(string portName)
    {
        if (string.IsNullOrEmpty(portName))
        {
            return false;
        }

        var ports = Ports;
        for (var index = 0; index < ports.Count; index++)
        {
            if (string.Equals(ports[index], portName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RefreshIfDue()
    {
        var now = Environment.TickCount64;

        lock (Gate)
        {
            if (_lastRefreshTicks != long.MinValue
                && now - _lastRefreshTicks < (long)RefreshInterval.TotalMilliseconds)
            {
                return;
            }

            _lastRefreshTicks = now;

            string[] next;
            try
            {
                next = SerialPort.GetPortNames();
            }
            catch (Exception)
            {
                // 热插拔/驱动异常时按“当前没有端口”处理；调用方不应因为枚举失败而崩。
                next = [];
            }

            Array.Sort(next, StringComparer.OrdinalIgnoreCase);

            if (!_ports.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
            {
                _ports = next;
                _revision++;
            }
        }
    }
}
