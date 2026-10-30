using System.Buffers.Binary;
using System.Reflection;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// **外部接入模块的共用骨架**：声明标准 <c>RawBytes</c> 契约、两个口、角色、显示源、命令、接入；
/// 负责双向数据搬运、状态灯、状态文字与计数发布。
/// </summary>
/// <remarks>
/// <para>
/// 它是**模块侧帮助类，不是框架件**：只使用 <see cref="IModuleContext"/> 的公开面；
/// 四个模块继承它，只提供 <see cref="Spec"/>、<see cref="Describe"/>，
/// 以及可选的 <see cref="DescribePressure"/>（"跟不上"时用模块自己的话再说一句）。
/// </para>
/// <para>
/// <b>驱动方式</b>：模块不实现 <see cref="IActivitySource"/>，由实例循环把命令 / 数据 / 状态推给
/// <see cref="IModuleInteractionSink"/>；四个模块因此不轮询、不建 IO 线程。
/// </para>
/// <para>
/// <b>数据路径</b>：外部原始字节 → 组 <c>{len,data}</c> → <c>rx</c> 口；
/// <c>tx</c> 口 → 按 <c>len</c> 取有效字节 → 写回外部。
/// </para>
/// <para>
/// <b>计数只呈现框架事实</b>：<c>drop</c> 读数、饱和、积压全部读框架的会话/队列快照，
/// 模块**不另存一份**——两份账迟早对不上，而"哪个是真的"永远说不清。
/// 模块自己看见的丢弃（非法帧）走 <see cref="IPortAccess.ReportLoss"/>。
/// </para>
/// </remarks>
public abstract class ExternalAccessModule : IModule, IModuleInteractionSink
{
    /// <summary>没有流动状态时用的空值（<see cref="DescribePressure"/> 的 <c>in</c> 参数）。</summary>
    private static readonly PortFlowState NoFlow = PortFlowState.None;

    /// <summary>丢失提示的时间窗：最近一次丢弃超过它就不再顶掉连接句（累计数字仍保留）。</summary>
    private static readonly TimeSpan LossHintWindow = TimeSpan.FromSeconds(10);

    private IModuleContext? _context;
    private byte[] _frame = [];
    private string? _lastStateText;
    private string? _lastLinkRole;
    private string[] _configurationFields = [];
    private bool _configurationFieldsResolved;
    private bool? _lastCanOpen;
    private bool? _lastCanClose;
    private bool? _lastCanEdit;

    /// <summary>本模块的共用骨架参数（接入名 / 标准标识 / 口 / 显示源 / 命令 / 容量）。</summary>
    protected abstract AccessModuleSpec Spec { get; }

    /// <summary>把会话快照翻译成一句人类可读的状态（模块自己的文案）。</summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    protected abstract string Describe(ExternalAccessSnapshot snapshot);

    /// <summary>
    /// 状态行要说的**"压力事实"**（由骨架按优先级选出，模块只负责用**自己的表**把它写成一句话）。
    /// </summary>
    /// <remarks>
    /// "先报故障、再报接收跟不上、最后报发送跟不上"这个顺序只在骨架里写一次；
    /// 模块只决定"这句话怎么写"——四个模块各有一套自己的文案。
    /// </remarks>
    protected enum PressureKind
    {
        /// <summary>没有压力（状态行回到 <see cref="Describe"/> 的连接句）。</summary>
        None = 0,

        /// <summary>
        /// 接收跟不上：外部来的数据在会话队列里积压
        /// （<see cref="ExternalAccessSnapshot.ReceiveSaturated"/>）。
        /// </summary>
        ReceiveSaturated = 1,

        /// <summary>发送跟不上：<c>tx</c> 口队列饱和（写外部太慢，框架投进来的单元积压）。</summary>
        SendSaturated = 2,

        /// <summary>
        /// 接收侧**已经丢了单元**（会话轴接收丢弃 &gt; 0，且最近一次丢弃在提示时间窗内）。
        /// </summary>
        ReceiveLost = 3,

        /// <summary>
        /// 发送侧**已经丢了单元**（写外部失败，或 <c>tx</c> 口队列丢弃，且最近一次丢弃在提示时间窗内）。
        /// </summary>
        SendLost = 4,

        /// <summary>发送积压（设备写缓冲）里还有待写单元：报出**当前积压数量**（还没丢）。</summary>
        SendBacklogged = 5,
    }

    /// <summary>
    /// 把**压力事实**写成一句模块自己的话（默认 <see langword="null"/> ⇒ 状态行回落到 <see cref="Describe"/>）。
    /// </summary>
    /// <param name="kind">骨架选定的压力种类（<see cref="PressureKind.None"/> 不会传进来）。</param>
    /// <param name="sendFlow">
    /// 本模块 <c>tx</c> 口的流动状态；取不到时为 <see langword="default"/>（深度与计数全 0）。
    /// </param>
    /// <param name="snapshot">该接入的当前快照（接收侧的积压深度在这里）。</param>
    /// <returns>供状态行显示的一句话；<see langword="null"/> ⇒ 不接管。</returns>
    protected virtual string? DescribePressure(
        PressureKind kind,
        in PortFlowState sendFlow,
        ExternalAccessSnapshot snapshot) => null;

    /// <summary>
    /// 把结构化端点拼成 <c>address[:port]</c>（只搬运事实，不含界面词）。
    /// </summary>
    /// <param name="endpoint">端点；空 ⇒ 空串。</param>
    /// <remarks>
    /// 拼法只有这一处：模块的句子把结果当 <c>{0}</c> 用；没有端口的接入（串口）只出现地址。
    /// </remarks>
    protected static string EndpointText(ExternalAccessEndpoint? endpoint) =>
        endpoint is { } value
            ? value.Port is { } port
                ? $"{value.Address}:{port}"
                : value.Address
            : string.Empty;

    /// <summary>
    /// 状态灯的外观角色（相对色；默认按会话状态映射）。
    /// </summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    protected virtual string? LinkRole(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.Suspended)
        {
            return ViewInkRoles.Idle;
        }

        return snapshot.State switch
        {
            ExternalAccessState.Open => ViewInkRoles.Success,
            ExternalAccessState.Opening or ExternalAccessState.Closing => ViewInkRoles.Warning,
            ExternalAccessState.Faulted => ViewInkRoles.Danger,
            _ => ViewInkRoles.Idle,
        };
    }

    /// <summary>
    /// 非标准命令的落点（四个模块只有打开/关闭；需要更多命令时覆盖）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="commandName">命令名。</param>
    protected virtual void OnUnknownCommand(IModuleContext context, string commandName)
    {
        _ = context;
        _ = commandName;
    }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        var spec = Spec;

        if (spec.Capacity is < 1 or > BytesShape.MaxLength)
        {
            throw new InvalidOperationException(
                $"RawBytes 字节区容量必须在 1–{BytesShape.MaxLength} 之间（当前 {spec.Capacity}）");
        }

        _frame = new byte[RawBytesFraming.FrameSize(spec.Capacity)];

        var shape = RawBytesFraming.Shape(spec.Capacity);
        var type = new DataTypeRef(RawBytesFraming.TypeId, RawBytesFraming.Version);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesFraming.TypeId,
            RawBytesFraming.Version,
            shape)
        {
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(spec.RxPort, PortDirection.Send, shape)
        {
            Type = type,
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclarePort(new PortDeclaration(spec.TxPort, PortDirection.Receive, shape)
        {
            Type = type,
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            spec.RoleId,
            type,
            spec.TxPort,
            spec.RxPort,
            spec.RoleDisplayName));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        if (spec.ConnCountSource is { Length: > 0 } conn)
        {
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(conn, ViewDataSourceKind.Value, ContractPrimitive.U32));
        }

        context.Declaration.DeclareCommand(spec.OpenCommand, spec.OpenTitle);
        context.Declaration.DeclareCommand(spec.CloseCommand, spec.CloseTitle);
        context.Declaration.DeclareCommand(spec.ClearCountersCommand, spec.ClearCountersTitle);

        // 模块只声明"接入名 + 标准标识"；适配器与参数类型都由框架按标准创建/登记。
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(
            spec.AccessName,
            spec.StandardId)
        {
            MaxUnitBytes = spec.Capacity,
        });

        // tx 口的接收处理在注册期登记（占位投递面会把它存进同一个处理器表）。
        context.Ports.SetReceiveHandler(spec.TxPort, OnPortData);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is null)
        {
            return;
        }

        // 此时外部接入面已由 L4 挂上：登记回调，数据由实例循环投递。
        _context.Access.SetReceiveHandler(Spec.AccessName, OnAccessData);
        PublishStatus();
    }

    /// <inheritdoc />
    public void OnCommand(string commandName)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        var snapshot = _context.Access.GetSnapshot(spec.AccessName);

        if (string.Equals(commandName, spec.OpenCommand, StringComparison.Ordinal))
        {
            if (!snapshot.Suspended
                && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted)
            {
                _context.Access.RequestOpen(spec.AccessName);
            }

            return;
        }

        if (string.Equals(commandName, spec.CloseCommand, StringComparison.Ordinal))
        {
            if (!snapshot.Suspended
                && snapshot.State is ExternalAccessState.Open or ExternalAccessState.Opening)
            {
                _context.Access.RequestClose(spec.AccessName);
            }

            return;
        }

        if (string.Equals(commandName, spec.ClearCountersCommand, StringComparison.Ordinal))
        {
            // 清计数是排障动作：队列数据、连接数与故障态都由会话保留。
            // 模块没有自己的丢弃累计可清——所有计数都在框架的会话/队列账本里，清的就是那一份。
            _ = _context.Access.ClearCounters(spec.AccessName);
            PublishStatus();
            return;
        }

        OnUnknownCommand(_context, commandName);
    }

    /// <inheritdoc />
    public void OnInput(string sourceName, string text)
    {
        _ = sourceName;
        _ = text;
    }

    /// <inheritdoc />
    public void OnWork() => PublishStatus();

    /// <summary>外部数据到达（实例线程）：组帧并从 <c>rx</c> 口发出。</summary>
    /// <param name="payload">原始字节（≤ 容量）。</param>
    private void OnAccessData(ReadOnlySpan<byte> payload)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        if (payload.Length > spec.Capacity)
        {
            // 防御式：框架已按声明上限（MaxUnitBytes = 容量）把超长单元挡在会话队列外。
            // 这里**不记账**——同一个事实记两遍，就再也没有"哪个数是真的"了。
            return;
        }

        var frame = _frame;
        if (frame.Length != RawBytesFraming.FrameSize(spec.Capacity))
        {
            return;
        }

        RawBytesFraming.WriteFrame(payload, frame, spec.Capacity);

        // 未入网 / 队列满 / 无承载都是**合法状态或框架已记过的事实**：
        // 流轴（绑定）与队列轴各自有账，模块不再替它们记一遍。
        _ = _context.Ports.TrySend(spec.RxPort, frame);
    }

    /// <summary>口数据到达（实例线程）：按 <c>len</c> 取有效字节并写回外部。</summary>
    /// <param name="payload">定长载荷。</param>
    private void OnPortData(ReadOnlySpan<byte> payload)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        if (!RawBytesFraming.TryReadContent(payload, spec.Capacity, out var content))
        {
            // 非法帧是**模块自己看见**的事实（框架只按契约尺寸投递，不解释内容）：
            // 走模块上报口，让它落进该口的原因账本，而不是模块自己攒一个计数器。
            _context.Ports.ReportLoss(spec.TxPort, DropReason.ModuleInternal, 1, payload.Length);
            return;
        }

        if (content.Length == 0)
        {
            return;
        }

        _ = _context.Access.Write(spec.AccessName, content);
    }

    private void PublishStatus()
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        var snapshot = _context.Access.GetSnapshot(spec.AccessName);

        var canOpen = !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted;
        var canClose = !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Open or ExternalAccessState.Opening;
        var canEdit = !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted;

        PublishEnabled(spec.OpenCommand, canOpen, ref _lastCanOpen);
        PublishEnabled(spec.CloseCommand, canClose, ref _lastCanClose);

        if (_lastCanEdit != canEdit)
        {
            _lastCanEdit = canEdit;
            foreach (var field in ResolveConfigurationFields())
            {
                _context.Appearance.SetEnabled(field, canEdit);
            }
        }

        var text = DescribeStatus(snapshot);
        if (!string.Equals(text, _lastStateText, StringComparison.Ordinal))
        {
            _context.Display.SetText(spec.StateSource, text);
            _lastStateText = text;
        }

        var role = LinkRole(snapshot);
        if (!string.Equals(role, _lastLinkRole, StringComparison.Ordinal))
        {
            _context.Appearance.SetOutlineRole(spec.LinkSource, role);
            _lastLinkRole = role;
        }

        // **单一来源**：会话轴的有效字节（模块不再自建"口侧丢弃字节"这第二份账）
        var dropped = Math.Max(0, snapshot.DroppedBytes);

        PublishU64(_context, spec.RxCountSource, ToUInt64(snapshot.ReceivedBytes));
        PublishU64(_context, spec.TxCountSource, ToUInt64(snapshot.SentBytes));
        PublishU64(_context, spec.DropCountSource, ToUInt64(dropped));

        if (spec.ConnCountSource is { Length: > 0 } conn)
        {
            PublishU32(_context, conn, (uint)Math.Max(0, snapshot.ActiveConnections));
        }
    }

    /// <summary>
    /// 选出这一拍状态行要显示的那一句（**优先级只在这里定义一次**）。
    /// </summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    /// <remarks>
    /// 顺序：① 非打开态 ⇒ 故障 / 开合句（故障压过一切）；
    /// ② 接收侧**已经丢了**（最近 10 s）⇒ 接收已丢弃；③ 发送侧**已经丢了**（写失败 / tx 队列丢，最近 10 s）
    /// ⇒ 发送已丢弃；④ 接收侧饱和；⑤ <c>tx</c> 口饱和；⑥ 连接句。
    /// <see cref="DescribePressure"/> 返回 <see langword="null"/>（模块没写压力句）⇒ 逐级回落。
    /// </remarks>
    private string DescribeStatus(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.State != ExternalAccessState.Open)
        {
            return Describe(snapshot);
        }

        var hasSendFlow = TryGetSendFlow(out var sendFlow);

        if (snapshot.ReceiveDroppedUnits > 0
            && IsRecent(snapshot.LastDropAt)
            && DescribePressure(PressureKind.ReceiveLost, in sendFlow, snapshot) is { } lostReceive)
        {
            return lostReceive;
        }

        if ((snapshot.SendFailedUnits > 0
                || (hasSendFlow && sendFlow.DroppedUnits > 0 && IsRecent(sendFlow.SinceLastDrop))
                || ((snapshot.SendBacklogEvictedUnits > 0 || snapshot.SendBacklogExpiredUnits > 0)
                    && IsRecent(snapshot.LastDropAt)))
            && DescribePressure(PressureKind.SendLost, in sendFlow, snapshot) is { } lostSend)
        {
            return lostSend;
        }

        if (snapshot.SendBacklogDepth > 0
            && DescribePressure(PressureKind.SendBacklogged, in sendFlow, snapshot) is { } backlogged)
        {
            return backlogged;
        }

        if (snapshot.ReceiveSaturated
            && DescribePressure(PressureKind.ReceiveSaturated, in sendFlow, snapshot) is { } receive)
        {
            return receive;
        }

        if (hasSendFlow
            && sendFlow.Saturated
            && DescribePressure(PressureKind.SendSaturated, in sendFlow, snapshot) is { } send)
        {
            return send;
        }

        return Describe(snapshot);
    }

    /// <summary>最近一次丢弃是否还在提示时间窗内（绝对时间）。</summary>
    private static bool IsRecent(DateTimeOffset? at) =>
        at is { } value && DateTimeOffset.UtcNow - value <= LossHintWindow;

    /// <summary>最近一次丢弃是否还在提示时间窗内（相对时长）。</summary>
    private static bool IsRecent(TimeSpan? since) =>
        since is { } value && value <= LossHintWindow;

    /// <summary>取本模块 <c>tx</c> 口的流动状态（口没有队列时为 <see langword="false"/> 且给空值）。</summary>
    /// <param name="state">流动状态。</param>
    private bool TryGetSendFlow(out PortFlowState state)
    {
        state = NoFlow;

        return _context is not null && _context.Ports.TryGetFlowState(Spec.TxPort, out state);
    }

    /// <summary>
    /// 从框架登记的**参数类型**反射配置字段名（用于会话打开/故障时禁用编辑）。
    /// </summary>
    /// <remarks>
    /// 参数类型在模块注册时可能还没挂上（外部接入面尚未接线）⇒ 返回空并保持未解析，
    /// 下一次状态发布继续尝试；这不是错误。
    /// </remarks>
    private string[] ResolveConfigurationFields()
    {
        if (_configurationFieldsResolved || _context is null)
        {
            return _configurationFields;
        }

        if (_context.Access.GetParametersType(Spec.AccessName) is not { } parametersType)
        {
            return _configurationFields;
        }

        _configurationFieldsResolved = true;
        _configurationFields = parametersType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ConfigurationFieldAttribute>() is not null)
            .Select(property => property.Name)
            .ToArray();

        return _configurationFields;
    }

    private void PublishEnabled(string source, bool enabled, ref bool? previous)
    {
        if (previous == enabled)
        {
            return;
        }

        previous = enabled;
        _context?.Appearance.SetEnabled(source, enabled);
    }

    private static void PublishU64(IModuleContext context, string source, ulong value)
    {
        Span<byte> payload = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        context.Ports.Publish(source, payload);
    }

    private static void PublishU32(IModuleContext context, string source, uint value)
    {
        Span<byte> payload = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value);
        context.Ports.Publish(source, payload);
    }

    private static ulong ToUInt64(long value) => value <= 0 ? 0 : (ulong)value;
}
