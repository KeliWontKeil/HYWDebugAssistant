using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// **显示缓冲注册表**：按实例/源名持有文本日志的写者与读者共享的那一份缓冲。
/// </summary>
/// <remarks>
/// <para>
/// 它有自己的锁，与输入/页签状态（<see cref="ViewStateStore"/>）分离：
/// 写者是模块实例线程、读者是 UI 与模块保存路径，两者都不该被输入编辑的锁挡住。
/// </para>
/// <para>
/// 缓冲随实例移除一起丢弃；显示内容**不持久化**（重启即空）。
/// </para>
/// </remarks>
internal sealed class DisplayRegistry
{
    private readonly Dictionary<string, Dictionary<string, DisplayBuffer>> _instances =
        new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    /// <summary>取某实例/源名的缓冲（首次调用即创建）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>缓冲。</returns>
    public DisplayBuffer Display(string instanceId, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var displays))
            {
                displays = new Dictionary<string, DisplayBuffer>(StringComparer.Ordinal);
                _instances[instanceId] = displays;
            }

            if (!displays.TryGetValue(sourceName, out var buffer))
            {
                buffer = new DisplayBuffer();
                displays[sourceName] = buffer;
            }

            return buffer;
        }
    }

    /// <summary>尝试取已存在的缓冲（不创建）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="buffer">缓冲；不存在时为 <see langword="null"/>。</param>
    /// <returns>是否存在。</returns>
    public bool TryGet(string instanceId, string sourceName, out DisplayBuffer? buffer)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sourceName))
        {
            buffer = null;
            return false;
        }

        lock (_gate)
        {
            if (_instances.TryGetValue(instanceId, out var displays)
                && displays.TryGetValue(sourceName, out var found))
            {
                buffer = found;
                return true;
            }

            buffer = null;
            return false;
        }
    }

    /// <summary>移除某实例的全部显示缓冲。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void RemoveInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        lock (_gate)
        {
            _ = _instances.Remove(instanceId);
        }
    }
}
