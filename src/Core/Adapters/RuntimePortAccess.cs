using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Hosting.Context;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;

namespace HYWDebugAssistant.Core.Adapters;

/// <summary>
/// 运行时的投递面实现：替换 Hosting 的占位投递面（M1-4 占位 → 这里接真实绑定与承载队列）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>发送</b>：走**绑定投递**（数据直通目标队列，Core 不经手数据）；</item>
///   <item><b>接收</b>：从**该实例的承载队列**取（主动接收模式）；</item>
///   <item><b>订阅写入</b>：写该实例的订阅缓冲（模块线程写、UI 线程读，Q4-4）。</item>
/// </list>
/// </remarks>
internal sealed class RuntimePortAccess : IPortAccess
{
    private readonly ModuleRuntime _runtime;
    private readonly string _instanceId;
    private readonly ModuleContext _context;

    internal RuntimePortAccess(ModuleRuntime runtime, string instanceId, ModuleContext context)
    {
        _runtime = runtime;
        _instanceId = instanceId;
        _context = context;
    }

    /// <inheritdoc />
    public bool TrySend(string portName, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        return _runtime.Deliver(_instanceId, portName, payload);
    }

    /// <inheritdoc />
    public void SetReceiveHandler(string portName, ReceiveHandler handler) =>
        _context.RegisterReceiveHandler(portName, handler);

    /// <inheritdoc />
    public bool TryReceive([NotNullWhen(true)] out PayloadBuffer? buffer, [NotNullWhen(true)] out string? sourcePort) =>
        _runtime.TryReceive(_instanceId, out buffer, out sourcePort);

    /// <inheritdoc />
    public void ReleaseBuffer(PayloadBuffer buffer) => _runtime.ReleaseBuffer(buffer);

    /// <inheritdoc />
    public void Publish(string dataSourceName, ReadOnlySpan<byte> value) =>
        _runtime.Publish(_instanceId, dataSourceName, value);
}
