using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// **模块级测试上下文里的外部接入面**：脚本化数据、记录请求与写入，不拉起框架。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="QueueReceived"/> 把数据留给读取面；<see cref="Deliver"/> 直接交给已设的接收回调；</item>
///   <item><see cref="OpenRequests"/> / <see cref="CloseRequests"/> 记录模块发出的请求；</item>
///   <item><see cref="Written"/> 记录模块写出的字节；<see cref="WriteSucceeds"/> 可模拟写失败；</item>
///   <item><see cref="SetSnapshot"/> 预置状态/计量；<c>GetSnapshot</c> 原样返回。</item>
/// </list>
/// </remarks>
public sealed class TestExternalAccess : IExternalAccess
{
    private readonly Dictionary<string, Queue<byte[]>> _inbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReceiveHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExternalAccessSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _parametersTypes = new(StringComparer.Ordinal);

    /// <summary>模块发出的打开请求（按顺序）。</summary>
    public List<string> OpenRequests { get; } = [];

    /// <summary>模块发出的关闭请求（按顺序）。</summary>
    public List<string> CloseRequests { get; } = [];

    /// <summary>模块写出的数据单元（接入名 → 字节）。</summary>
    public List<(string AccessName, byte[] Payload)> Written { get; } = [];

    /// <summary>写是否成功（默认成功；设为 <see langword="false"/> 模拟发送失败）。</summary>
    public bool WriteSucceeds { get; set; } = true;

    /// <summary>把数据留给读取面（模块调 <c>TryRead</c> 时取走）。</summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="payload">字节。</param>
    public void QueueReceived(string accessName, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessName);

        if (!_inbound.TryGetValue(accessName, out var queue))
        {
            queue = new Queue<byte[]>();
            _inbound[accessName] = queue;
        }

        queue.Enqueue(payload.ToArray());
    }

    /// <summary>
    /// 模拟一次回调投递：已设接收回调 ⇒ 直接交给它；否则留给读取面。
    /// </summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="payload">字节。</param>
    public void Deliver(string accessName, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessName);

        if (_handlers.TryGetValue(accessName, out var handler))
        {
            handler(payload);
            return;
        }

        QueueReceived(accessName, payload);
    }

    /// <summary>预置一条接入的状态/计量快照（没有预置时是 <see cref="ExternalAccessSnapshot.None"/>）。</summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="snapshot">快照。</param>
    public void SetSnapshot(string accessName, ExternalAccessSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessName);
        _snapshots[accessName] = snapshot;
    }

    /// <inheritdoc />
    public bool RequestOpen(string accessName)
    {
        OpenRequests.Add(accessName);
        return true;
    }

    /// <inheritdoc />
    public bool RequestClose(string accessName)
    {
        CloseRequests.Add(accessName);
        return true;
    }

    /// <inheritdoc />
    public bool TryRead(string accessName, Span<byte> destination, [NotNullWhen(true)] out int length)
    {
        length = 0;

        if (!_inbound.TryGetValue(accessName, out var queue) || queue.Count == 0)
        {
            return false;
        }

        var payload = queue.Dequeue();
        if (payload.Length > destination.Length)
        {
            // 目标缓冲不足 ⇒ 丢弃（与框架同口径：不截断、不抛）。
            return false;
        }

        payload.CopyTo(destination);
        length = payload.Length;
        return true;
    }

    /// <inheritdoc />
    public void SetReceiveHandler(string accessName, ReceiveHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[accessName] = handler;
    }

    /// <inheritdoc />
    public bool Write(string accessName, ReadOnlySpan<byte> payload)
    {
        Written.Add((accessName, payload.ToArray()));
        return WriteSucceeds;
    }

    /// <summary>预置一条接入的参数类型（模拟框架按标准目录登记的类型）。</summary>
    /// <param name="accessName">接入名。</param>
    /// <param name="parametersType">参数类型。</param>
    public void SetParametersType(string accessName, Type parametersType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessName);
        ArgumentNullException.ThrowIfNull(parametersType);
        _parametersTypes[accessName] = parametersType;
    }

    /// <inheritdoc />
    public ExternalAccessSnapshot GetSnapshot(string accessName) =>
        _snapshots.TryGetValue(accessName, out var snapshot) ? snapshot : ExternalAccessSnapshot.None;

    /// <inheritdoc />
    public Type? GetParametersType(string accessName) =>
        _parametersTypes.TryGetValue(accessName, out var parametersType) ? parametersType : null;
}
