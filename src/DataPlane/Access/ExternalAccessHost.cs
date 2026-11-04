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
    private readonly IExternalAccessCatalog? _catalog;
    private CarrierOptions _options;
    private bool _disposed;

    /// <summary>创建宿主。</summary>
    /// <param name="options">承载参数（队列容量、丢旧策略、停止超时）。</param>
    /// <param name="catalog">
    /// 外部标准目录；<see langword="null"/> ⇒ 无法按标准标识解析适配器与参数类型
    /// （有声明时激活失败并给出原因）。
    /// </param>
    public ExternalAccessHost(CarrierOptions? options = null, IExternalAccessCatalog? catalog = null)
    {
        _options = options ?? new CarrierOptions();
        _catalog = catalog;
    }

    /// <summary>当前承载参数（只影响新建会话）。</summary>
    public CarrierOptions Options => _options;

    /// <summary>
    /// 把某实例的"有活干"投给承载者；由 L4 接到 <c>CarrierHost</c>（实例没有承载者时返回 <see langword="null"/>）。
    /// </summary>
    public Func<string, Action?>? SignalResolver { get; set; }

    /// <summary>
    /// 更新承载参数（由应用级设置驱动）：保存参数，并把可即时改变的部分下发给既有会话。
    /// </summary>
    /// <remarks>
    /// 队列容量与满时策略只影响**新建**会话（既有会话保持创建时的容量）；
    /// 饱和判据、积压上限与写超时对既有会话即时生效。
    /// </remarks>
    /// <param name="options">新参数。</param>
    /// <summary>把承载参数里的通信项转成适配件可消费的参数。</summary>
    private static ExternalAccessTuning TuningOf(CarrierOptions options) => new(
        options.AdapterBufferBytes,
        options.AccessCloseTimeout,
        options.DeviceLostMisses,
        options.LivenessInterval);

    public void UpdateOptions(CarrierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        foreach (var instance in _instances.Values)
        {
            foreach (var session in instance.Sessions.Values)
            {
                session.UpdateFlowPolicy(options.HintDepth, options.HintHold, options.MaxBacklogAge);
                session.UpdateWriteTimeout(options.ExternalWriteTimeout);
                session.UpdateTuning(TuningOf(options));
            }
        }
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

        var sessions = new Dictionary<string, ExternalAccessSession>(declarations.Count, StringComparer.Ordinal);
        var created = new List<ExternalAccessSession>(declarations.Count);
        var closeTimeout = HalfOfStopTimeout(_options.StopTimeout);
        var catalog = _catalog;

        // 中途失败必须**先释放已创建的会话/适配器**再返回：不能把半成品留给 GC。
        string? error = null;

        void Fail(string message)
        {
            foreach (var session in created)
            {
                session.Discard();
            }

            error = message;
        }

        if (catalog is null)
        {
            failure = DataPlaneText.Get("dataplane.access.catalogMissing");
            return false;
        }

        if (error is null)
        {
            foreach (var declaration in declarations)
            {
                if (declaration is null || string.IsNullOrWhiteSpace(declaration.Name))
                {
                    Fail(DataPlaneText.Get("dataplane.access.nameEmpty"));
                    break;
                }

                if (!catalog.TryResolve(declaration.StandardId, out var descriptor) || descriptor is null)
                {
                    Fail(DataPlaneText.Get("dataplane.access.unknownStandard", declaration.StandardId, declaration.Name));
                    break;
                }

                if (descriptor.ParametersType is null)
                {
                    Fail(DataPlaneText.Get("dataplane.access.parametersTypeEmpty", declaration.StandardId, declaration.Name));
                    break;
                }

                IExternalAccessAdapter adapter;
                try
                {
                    adapter = descriptor.CreateAdapter();
                }
                catch (Exception ex)
                {
                    Fail(DataPlaneText.Get("dataplane.access.createAdapterFailed", declaration.StandardId, ex.GetType().Name, ex.Message));
                    break;
                }

                if (adapter is null)
                {
                    Fail(DataPlaneText.Get("dataplane.access.adapterFactoryReturnedNull", declaration.StandardId));
                    break;
                }

                var session = new ExternalAccessSession(
                    declaration,
                    adapter,
                    descriptor.ParametersType,
                    parametersProvider,
                    new AccessInboundBuffer(
                        _options.QueueCapacity,
                        _options.DropPolicy,
                        _options.HintDepth,
                        _options.HintHold,
                        _options.MaxBacklogAge),
                    closeTimeout,
                    () => SignalResolver?.Invoke(instanceId)?.Invoke(),
                    _options.ExternalWriteTimeout,
                    tuning: TuningOf(_options),
                    backlog: new SendBacklog(
                        _options.SendBacklogBytes,
                        _options.MaxBacklogAge,
                        _options.HintDepth,
                        _options.HintHold));

                created.Add(session);

                if (!sessions.TryAdd(declaration.Name, session))
                {
                    Fail(DataPlaneText.Get("dataplane.access.duplicateName", declaration.Name));
                    break;
                }
            }
        }

        if (error is null)
        {
            var instance = new AccessInstance(sessions);

            if (!_instances.TryAdd(instanceId, instance))
            {
                Fail(DataPlaneText.Get("dataplane.access.sessionAlreadyExists", instanceId));
            }
        }

        failure = error;
        return error is null;
    }

    /// <summary>取一条接入的参数类型（未知实例/接入 ⇒ <see langword="null"/>）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="accessName">接入名。</param>
    public Type? GetParametersType(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session) ? session.ParametersType : null;

    /// <summary>请求打开一条接入（只置意图；实际执行在实例循环的 <see cref="Pump"/> 里）。</summary>
    public bool RequestOpen(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session) && Run(session, static s => s.RequestOpen());

    /// <summary>请求关闭一条接入。</summary>
    public bool RequestClose(string instanceId, string accessName) =>
        TryGetSession(instanceId, accessName, out var session) && Run(session, static s => s.RequestClose());

    /// <summary>清空一条接入的计数（未知实例/接入 ⇒ <see langword="false"/>）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="accessName">接入名。</param>
    public bool ClearCounters(string instanceId, string accessName)
    {
        if (!TryGetSession(instanceId, accessName, out var session))
        {
            return false;
        }

        session.ClearCounters();
        return true;
    }

    /// <summary>设接收回调（投递循环承载的模块的回调投递）。</summary>
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

    /// <summary>
    /// 取某实例"最晚多久必须回来推进一次"（离现在的时间；没有周期需求 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <remarks>
    /// 只有**没有事件也会变化**的会话需要它（设备探活）；其余会话的推进都由信号触发。
    /// 返回的是最早到期的那一条，实例循环据此决定"兜底等待"的上限。
    /// </remarks>
    public TimeSpan? NextWakeup(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
        {
            return null;
        }

        TimeSpan? due = null;

        foreach (var session in instance.Sessions.Values)
        {
            if (session.NextLivenessDue is not { } candidate)
            {
                continue;
            }

            if (due is null || candidate < due.Value)
            {
                due = candidate;
            }
        }

        return due;
    }

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

        // 多会话**并行**收尾（总时长取最大值而不是求和）。
        var closes = new List<Task>(instance.Sessions.Count);

        foreach (var session in instance.Sessions.Values)
        {
            closes.Add(Task.Run(() => _ = session.CloseFromHost(timeout)));
        }

        if (closes.Count > 0)
        {
            _ = Task.WaitAll([.. closes], timeout);
        }

        foreach (var session in instance.Sessions.Values)
        {
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
