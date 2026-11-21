using System.Net;
using System.Net.Sockets;

namespace HYWDebugAssistant.Access;

/// <summary>
/// **本机 IPv4 地址目录的进程级缓存**：TCP 服务端/UDP 的本地地址下拉共用。
/// </summary>
/// <remarks>
/// <para>
/// 动态选项必须是环境的**当前完整快照**：网卡禁用/拔掉后地址消失，已选地址不能继续留在列表里。
/// 枚举按 <see cref="RefreshInterval"/> 节流；集合变化时递增 <see cref="Revision"/>，让配置 Schema 缓存重建。
/// </para>
/// <para>
/// <c>0.0.0.0</c> 与回环地址是本机永远可用的绑定地址，始终保留；枚举失败也保留这两项。
/// </para>
/// </remarks>
internal static class LocalAddressCatalog
{
    private const string AnyAddress = "0.0.0.0";
    private const string LoopbackAddress = "127.0.0.1";

    private static readonly Lock Gate = new();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private static string[] _addresses = [AnyAddress, LoopbackAddress];
    private static long _revision;
    private static long _lastRefreshTicks = long.MinValue;

    /// <summary>当前可用地址快照（只读；最多 500 ms 过期）。</summary>
    public static IReadOnlyList<string> Addresses
    {
        get
        {
            RefreshIfDue();
            return _addresses;
        }
    }

    /// <summary>地址集合版本；集合内容变化时递增。</summary>
    public static long Revision
    {
        get
        {
            RefreshIfDue();
            return _revision;
        }
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

            var next = new List<string> { AnyAddress, LoopbackAddress };

            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = address.ToString();
                    if (!next.Contains(text, StringComparer.Ordinal))
                    {
                        next.Add(text);
                    }
                }
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                // 环境取不到就只剩“全部网卡 + 回环”；调用方不应因为枚举失败而崩。
            }

            // 前两项顺序固定；网卡地址排序，避免枚举顺序变化造成无意义的版本抖动。
            var tail = next.Skip(2).OrderBy(text => text, StringComparer.Ordinal);
            var ordered = next.Take(2).Concat(tail).ToArray();

            if (!_addresses.SequenceEqual(ordered, StringComparer.Ordinal))
            {
                _addresses = ordered;
                _revision++;
            }
        }
    }
}
