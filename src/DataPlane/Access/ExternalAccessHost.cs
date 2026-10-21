using System.Collections.Concurrent;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>
/// 外部接入宿主：创建 / 驱动 / 关闭**每个实例的全部会话**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>归属</b>：L3 数据面持有会话（队列、状态机、计数）；实例的存在性由 L2/L4 决定；</item>
///   <item><b>不报 Issue</b>：失败以 <see cref="AccessFailure"/> 返回，由 L4 转成 Issue（保持本层只依赖 L1）；</item>
///   <item><b>不新增线程</b>：控制与读取都在实例循环里执行；<c>SignalResolver</c> 把"有活干"投回承载者；</item>
///   <item><b>队列容量/丢旧策略/停止超时</b>复用 <see cref="CarrierOptions"/>，修改只影响新建会话。</item>
/// </list>
/// </remarks>
public sealed class ExternalAccessHost : IDisposable
{
    private readonly ConcurrentDictionary<string, AccessInstance> _instances = new(StringComparer.Ordinal);
    private CarrierOptions _options;
    private bool _disposed;

    /// <summary>创建宿主。</summary>
    /// <param name="options">承载参数（队列容量、丢旧策略、停止超时）。</param>
    public ExternalAccessHost(CarrierOptions? options = null) =>
        _options = options ?? new CarrierOptions();

    /// <summary>当前承载参数（只影响新建会话）。</summary>
    public CarrierOptions Options => _options;

    /// <summary>
    /// 把某实例的"有活干"投给承载者；由 L4 接到 <c>CarrierHost</c>（实例没有承载者时返回 <see langword="null"/>）。
    /// </summary>
    public Func<string, Action?>? SignalResolver { get; set; }

    /// <summary>更新承载参数（只影响新建会话；既有会话保持创建时容量）。</summary>
    /// <param name="options">新参数。</param>
    public void UpdateOptions(CarrierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// 为一个实例创建全部会话。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="declarations">该实例声明的接入（名字已由 L2 校验；这里再做一次防御）。</param>
    /// <param name="parametersProvider">按声明取当前参数对象。</param>
    /// <param name="failure">失败原因（成功为 <see langword="null"/>）。</param>
    /// <returns>是否创建成功；没有任何声明时也返回 <see langword="true"/>（不登记空实例）。</returns>
    public bool TryActivate(
        string instanceId,
        IReadOnlyList<ExternalAccessDeclaration> declarations,
        Func<ExternalAccessDeclaration, object?> parametersProvider,
        out string? failure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(parametersProvider);

        failure = null;

        if (declarations.Count == 0)
        {
            return true;
        }

        var closeTimeout = HalfOfStopTimeout(_options.StopTimeout);
        var sessions = new Dictionary<string, ExternalAccessSession>(declarations.Count, StringComparer.Ordinal);

        foreach (var declaration in declarations)
        {
            if (declaration is null || string.IsNullOrWhiteSpace(declaration.Name))
            {
                failure = "接入声明的名字为空";
                return false;
            }

            var session = new ExternalAccessSession(
                declaration,
                parametersProvider,
                new AccessInboundBuffer(_options.QueueCapacity, _options.DropPolicy),
                closeTimeout,
                () => SignalResolver?.Invoke(instanceId)?.Invoke());

            if (!sessions.TryAdd(declaration.Name, session))
            {
                failure = $"接入名重复：{declaration.Name}";
                return false;
            }
        }

        var instance = new AccessInstance(sessions);

        if (!_instances.TryAdd(instanceId, instance))
        {
            failure = $"实例已有外部接入会话：{instanceId}";
            return false;
        }

        return true;
    }

    /// <summary>请求打开一条接入（只置意图；实际执行在实例循环的 <see cref="Pump"/> 里）。</summary>
    public bool RequestOpen(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session) && Run(session, static s => s.RequestOpen());

    /// <summary>请求关闭一条接入。</summary>
    public bool RequestClose(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session) && Run(session, static s => s.RequestClose());

    /// <summary>设接收回调（无活动体模块的回调投递）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="accessName">接入名。</param>
    /// <param name="handler">处理委托（与口的接收处理同一委托类型）。</param>
    public void SetReceiveHandler(string instanceId, string accessName, ReceiveHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (TryGetSession(instanceId, accessName, out var session))
        {
            session.SetReceiveHandler(handler);
        }
    }

    /// <summary>置实例暂停（Disabled）：会话关闭但保留"想要打开"的意图。</summary>
    public void SetSuspended(string instanceId, bool suspended)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            foreach (var session in instance.Sessions.Values)
            {
                session.SetSuspended(suspended);
            }
        }
    }

    /// <summary>参数变更：下一拍关闭并用新参数重开。</summary>
    public void NotifyConfigurationChanged(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            foreach (var session in instance.Sessions.Values)
            {
                session.NotifyConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// 推进一个实例的全部会话（应用开关/重启/停用，并把适配件就绪的数据取进队列）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="failures">本次产生的失败（调用方转 Issue）。</param>
    /// <returns>这一拍是否做了工作。</returns>
    public bool Pump(string instanceId, out IReadOnlyList<AccessFailure> failures)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            failures = [];
            return false;
        }

        var list = new List<AccessFailure>();
        var didWork = false;

        foreach (var session in instance.Sessions.Values)
        {
            didWork |= session.Pump();
            session.DrainFailures(list);
        }

        failures = list;
        return didWork;
    }

    /// <summary>把队列里的单元交给已设的接收回调（无活动体模块的回调投递）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>是否投递了数据。</returns>
    public bool DrainToHandlers(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return false;
        }

        var didWork = false;
        foreach (var session in instance.Sessions.Values)
        {
            if (session.HasReceiveHandler)
            {
                didWork |= session.DrainToHandler();
            }
        }

        return didWork;
    }

    /// <summary>主动取一个数据单元（读取面；回调模式下返回 <see langword="false"/>）。</summary>
    public bool TryRead(string instanceId, string accessName, Span<byte> destination, out int length)
    {
        length = 0;
        return TryGetSession(instanceId, accessName, out var session)
            && session.TryRead(destination, out length);
    }

    /// <summary>写一个数据单元。</summary>
    public bool Write(string instanceId, string accessName, ReadOnlySpan<byte> payload) =>
        TryGetSession(instanceId, accessName, out var session) && session.Write(payload);

    /// <summary>取快照（未知实例/接入 ⇒ <see cref="ExternalAccessSnapshot.None"/>）。</summary>
    public ExternalAccessSnapshot GetSnapshot(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session)
            ? session.GetSnapshot()
            : ExternalAccessSnapshot.None;

    /// <summary>取走一个实例的全部待上报失败。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>失败列表（无 ⇒ 空）。</returns>
    public IReadOnlyList<AccessFailure> DrainFailures(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return [];
        }

        var list = new List<AccessFailure>();
        foreach (var session in instance.Sessions.Values)
        {
            session.DrainFailures(list);
        }

        return list;
    }

    /// <summary>关闭并移除一个实例的全部会话（移除/退出时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="failures">关闭过程中的失败。</param>
    /// <returns>是否存在该实例。</returns>
    public bool TryDeactivate(string instanceId, out IReadOnlyList<AccessFailure> failures)
    {
        failures = [];

        if (!_instances.TryRemove(instanceId, out var instance))
        {
            return false;
        }

        var list = new List<AccessFailure>();
        var timeout = HalfOfStopTimeout(_options.StopTimeout);

        foreach (var session in instance.Sessions.Values)
        {
            _ = session.CloseFromHost(timeout);
            session.DrainFailures(list);
        }

        failures = list;
        return true;
    }

    /// <summary>当前有会话的实例数（诊断用）。</summary>
    public int ActiveInstanceCount => _instances.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var instanceId in _instances.Keys.ToArray())
        {
            _ = TryDeactivate(instanceId, out _);
        }
    }

    private bool TryGetSession(string instanceId, string accessName, out ExternalAccessSession session)
    {
        session = null!;

        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(accessName))
        {
            return false;
        }

        return _instances.TryGetValue(instanceId, out var instance)
            && instance.Sessions.TryGetValue(accessName, out session!);
    }

    private static bool Run(ExternalAccessSession session, Action<ExternalAccessSession> action)
    {
        action(session);
        return true;
    }

    private static TimeSpan HalfOfStopTimeout(TimeSpan timeout)
    {
        var ticks = Math.Max(timeout.Ticks / 2, TimeSpan.FromMilliseconds(100).Ticks);
        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>一个实例的会话集合（创建后不变）。</summary>
    private sealed class AccessInstance
    {
        public AccessInstance(IReadOnlyDictionary<string, ExternalAccessSession> sessions)
        {
            ArgumentNullException.ThrowIfNull(sessions);
            Sessions = sessions;
        }

        public IReadOnlyDictionary<string, ExternalAccessSession> Sessions { get; }
    }
}
